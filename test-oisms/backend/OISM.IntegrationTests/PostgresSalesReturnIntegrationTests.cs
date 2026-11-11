using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Reports.Dtos;
using OISM.Application.Features.SalesInvoices.Dtos;
using OISM.Application.Features.SalesReturns.Dtos;
using OISM.Domain.Common;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Kiểm chứng chức năng Backend lõi trả hàng (CS-01) trên PostgreSQL 16+ thật (database: oisms_test).
/// AC-07:
/// (a) xác nhận phiếu: OnHand chi nhánh và kho, dòng IN, BalanceAfter, WAC đúng công thức;
/// (b) hai phiếu Draft cùng dòng đơn có tổng vượt SL bán, xác nhận đồng thời: đúng 1 phiếu thành công, phiếu kia 422 RETURN_QUANTITY_EXCEEDED;
/// (c) replay Create cùng key: không tạo phiếu thứ hai; khác payload: 409;
/// (d) Confirm hai lần: chỉ có 1 dòng sổ;
/// (e) đơn bán theo đơn vị có FactorSnapshot = 24, trả 1 thì cộng 24 đơn vị gốc;
/// (f) tenant khác Get hoặc Confirm nhận 404;
/// (g) đơn sàn: nhập kho, refund NULL;
/// AC-06: Cashier gọi nhận 403.
/// </summary>
public class PostgresSalesReturnIntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public PostgresSalesReturnIntegrationTests(WebApplicationFactoryWithPostgres factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
        var context = PostgresTestEnvironment.CreateRuntimeDbContext(new TestTenantService());
        await using (context)
        {
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class TestTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private static (AppDbContext Context, TestTenantService TenantService) CreatePostgresDbContext(Guid? tenantId = null)
    {
        var tenantService = new TestTenantService();
        if (tenantId.HasValue)
        {
            tenantService.SetTenant(tenantId.Value);
        }
        var context = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService);
        return (context, tenantService);
    }

    private async Task<string> LoginAsync(string email)
    {
        using var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = PostgresTestEnvironment.AppPassword
        });
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>(_json);
        auth.Should().NotBeNull();
        return auth!.AccessToken;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string url,
        string token,
        object? body = null,
        Dictionary<string, string>? headers = null)
    {
        using var client = _factory.CreateClient();
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (headers != null)
        {
            foreach (var (k, v) in headers)
            {
                req.Headers.TryAddWithoutValidation(k, v);
            }
        }

        if (body != null)
        {
            req.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(req);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage res)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }

    private async Task<(Guid OrderId, Guid OrderItemId, Guid BranchId, Guid WarehouseId, Guid SkuId)> SeedOrderAsync(
        Guid tenantId,
        string channel = "POS",
        string status = OrderStatus.Completed,
        decimal quantity = 10m,
        decimal factorSnapshot = 1m,
        decimal unitPrice = 100_000m,
        decimal costPrice = 60_000m,
        decimal initialOnHand = 0m,
        decimal initialWac = 50_000m)
    {
        var (context, tenantService) = CreatePostgresDbContext(tenantId);
        await using (context)
        {
            using var _ = context.OpenPrivilegedScope();
            var branch = await context.Branches.FirstAsync(b => b.TenantId == tenantId);
            var warehouse = await context.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branch.Id);
            var sku = await context.ProductVariants.FirstAsync(s => s.TenantId == tenantId && s.IsActive);
            var user = await context.Users.FirstAsync(u => u.TenantId == tenantId && u.Role == "Owner");
            var supplier = await context.Suppliers.FirstAsync(s => s.TenantId == tenantId);

            if (initialOnHand > 0)
            {
                await PriceSeed.SyncFromLegacyColumnsAsync(context);
                await PurchaseOrderSeed.ReceiveStockAsync(
                    context,
                    tenantService,
                    branch.Id,
                    warehouse.Id,
                    supplier.Id,
                    user.Id,
                    (sku.Id, initialOnHand, initialWac));
            }

            var orderId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var order = new Order
            {
                Id = orderId,
                TenantId = tenantId,
                BranchId = branch.Id,
                Number = $"ORD-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..24],
                Channel = channel,
                Status = status,
                TotalAmount = quantity * unitPrice,
                CreatedBy = user.Id,
                ConfirmedBy = user.Id,
                ConfirmedAt = now,
                CompletedAt = status == OrderStatus.Completed ? now : null,
                ExternalShopId = channel is "Shopee" or "TikTok" or "Lazada" ? "SHOP_01" : null,
                ExternalOrderId = channel is "Shopee" or "TikTok" or "Lazada" ? $"EXT_{Guid.NewGuid():N}"[..20] : null,
                CreatedAt = now
            };

            var orderItemId = Guid.NewGuid();
            var orderItem = new OrderItem
            {
                Id = orderItemId,
                TenantId = tenantId,
                OrderId = orderId,
                SkuId = sku.Id,
                SkuCodeSnapshot = sku.SKU,
                SkuNameSnapshot = sku.SKU,
                Quantity = quantity,
                FactorSnapshot = factorSnapshot,
                UnitPrice = unitPrice,
                CostPrice = costPrice,
                PriceSource = channel is "Shopee" or "TikTok" or "Lazada" ? "Marketplace" : "Legacy",
                CreatedAt = now
            };

            order.Items.Add(orderItem);
            context.Orders.Add(order);

            await context.SaveChangesAsync();
            return (orderId, orderItemId, branch.Id, warehouse.Id, sku.Id);
        }
    }

    // =========================================================================
    // (a) Xác nhận phiếu: OnHand chi nhánh và kho, dòng IN, BalanceAfter, WAC đúng công thức
    // =========================================================================
    [Fact]
    public async Task A_Confirm_ShouldIncreaseOnHand_RecordLedgerIn_UpdateWacCorrectly()
    {
        var tenantId = OismsDatabaseSeeder.TenantAId;
        var token = await LoginAsync("owner@shopa.local");

        // Đơn hàng bán 5 cái, giá vốn 60_000, tồn cũ = 10, WAC cũ = 50_000
        var (orderId, orderItemId, branchId, warehouseId, skuId) = await SeedOrderAsync(
            tenantId, quantity: 5m, unitPrice: 100_000m, costPrice: 60_000m, initialOnHand: 10m, initialWac: 50_000m);

        // 1. Tạo phiếu trả 2 cái
        var reqKey = Guid.NewGuid();
        var createRes = await SendAsync(HttpMethod.Post, "/api/sales-returns", token,
            new CreateSalesReturnDto
            {
                OrderId = orderId,
                WarehouseId = warehouseId,
                Reason = "Khách trả 2 cái",
                Items = [new CreateSalesReturnItemDto { OrderItemId = orderItemId, Quantity = 2m }]
            },
            new() { ["Idempotency-Key"] = reqKey.ToString() });

        createRes.StatusCode.Should().Be(HttpStatusCode.Created, await createRes.Content.ReadAsStringAsync());
        var created = await createRes.Content.ReadFromJsonAsync<SalesReturnResponseDto>(_json);
        created.Should().NotBeNull();
        created!.Status.Should().Be(SalesReturnStatus.Draft);
        created.TotalRefundAmount.Should().Be(200_000m);

        // 2. Xác nhận phiếu trả
        var confirmRes = await SendAsync(HttpMethod.Post, $"/api/sales-returns/{created.Id}/confirm", token,
            new ConfirmSalesReturnRequestDto { RefundMethod = "Cash" });

        confirmRes.StatusCode.Should().Be(HttpStatusCode.OK, await confirmRes.Content.ReadAsStringAsync());
        var confirmed = await confirmRes.Content.ReadFromJsonAsync<SalesReturnResponseDto>(_json);
        confirmed.Should().NotBeNull();
        confirmed!.Status.Should().Be(SalesReturnStatus.Confirmed);
        confirmed.RefundMethod.Should().Be("Cash");
        confirmed.RefundedAt.Should().NotBeNull();
        confirmed.ConfirmedAt.Should().NotBeNull();

        // 3. Kiểm tra CSDL PostgreSQL: Tồn kho, WAC, Dòng sổ IN
        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branchBal = await db.InventoryBalances.FirstAsync(b => b.TenantId == tenantId && b.BranchId == branchId && b.SkuId == skuId);
            var whBal = await db.WarehouseInventoryBalances.FirstAsync(b => b.TenantId == tenantId && b.WarehouseId == warehouseId && b.SkuId == skuId);

            // OnHand tăng 2: 10 -> 12
            branchBal.OnHand.Should().Be(12m);
            whBal.OnHand.Should().Be(12m);

            // WAC = (10 * 50_000 + 2 * 60_000) / 12 = 620_000 / 12 = 51666.666667
            var expectedWac = Math.Round((10m * 50_000m + 2m * 60_000m) / 12m, 6, MidpointRounding.AwayFromZero);
            branchBal.AverageCost.Should().Be(expectedWac);

            // Dòng sổ IN trong inventory_transactions
            var returnItem = confirmed.Items.Single();
            var ledger = await db.InventoryTransactions.FirstAsync(t => t.TenantId == tenantId && t.SalesReturnItemId == returnItem.Id);
            ledger.Type.Should().Be("IN");
            ledger.Quantity.Should().Be(2m);
            ledger.UnitCost.Should().Be(60_000m);
            ledger.BalanceAfter.Should().Be(12m);
            ledger.SequenceNo.Should().Be(2); // ban đầu GoodsReceipt sequence 1 + return sequence 1 = 2
        }
    }

    // =========================================================================
    // (b) Hai phiếu Draft cùng dòng đơn có tổng vượt SL bán, xác nhận đồng thời:
    //     đúng 1 phiếu thành công, phiếu kia 422 RETURN_QUANTITY_EXCEEDED
    // =========================================================================
    [Fact]
    public async Task B_Concurrent_ConfirmTwoDraftsExceedingRemaining_ExactlyOneSucceeds_OtherGets422()
    {
        var tenantId = OismsDatabaseSeeder.TenantAId;
        var token = await LoginAsync("owner@shopa.local");

        // Đơn hàng bán 3 cái
        var (orderId, orderItemId, _, warehouseId, _) = await SeedOrderAsync(tenantId, quantity: 3m);

        // Tạo 2 phiếu Draft, mỗi phiếu trả 2 cái (tổng = 4 > 3 bán)
        var create1 = await SendAsync(HttpMethod.Post, "/api/sales-returns", token,
            new CreateSalesReturnDto
            {
                OrderId = orderId,
                WarehouseId = warehouseId,
                Reason = "Draft 1",
                Items = [new CreateSalesReturnItemDto { OrderItemId = orderItemId, Quantity = 2m }]
            },
            new() { ["Idempotency-Key"] = Guid.NewGuid().ToString() });
        create1.StatusCode.Should().Be(HttpStatusCode.Created);
        var ticket1 = (await create1.Content.ReadFromJsonAsync<SalesReturnResponseDto>(_json))!;

        var create2 = await SendAsync(HttpMethod.Post, "/api/sales-returns", token,
            new CreateSalesReturnDto
            {
                OrderId = orderId,
                WarehouseId = warehouseId,
                Reason = "Draft 2",
                Items = [new CreateSalesReturnItemDto { OrderItemId = orderItemId, Quantity = 2m }]
            },
            new() { ["Idempotency-Key"] = Guid.NewGuid().ToString() });
        create2.StatusCode.Should().Be(HttpStatusCode.Created);
        var ticket2 = (await create2.Content.ReadFromJsonAsync<SalesReturnResponseDto>(_json))!;

        // Xác nhận đồng thời 2 phiếu
        var task1 = SendAsync(HttpMethod.Post, $"/api/sales-returns/{ticket1.Id}/confirm", token, new ConfirmSalesReturnRequestDto { RefundMethod = "Cash" });
        var task2 = SendAsync(HttpMethod.Post, $"/api/sales-returns/{ticket2.Id}/confirm", token, new ConfirmSalesReturnRequestDto { RefundMethod = "Cash" });

        var responses = await Task.WhenAll(task1, task2);

        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var exceededCount = responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity);

        successCount.Should().Be(1, "Chỉ duy nhất một phiếu được xác nhận thành công.");
        exceededCount.Should().Be(1, "Phiếu còn lại phải bị từ chối 422 RETURN_QUANTITY_EXCEEDED.");

        var failedRes = responses.Single(r => r.StatusCode == HttpStatusCode.UnprocessableEntity);
        var errCode = await ErrorCodeAsync(failedRes);
        errCode.Should().Be("RETURN_QUANTITY_EXCEEDED");
    }

    // =========================================================================
    // (b2) Hủy và xác nhận cùng lúc một phiếu Draft: đúng một thao tác thành công,
    //      thao tác kia 409 INVALID_STATE_TRANSITION (không 500), sổ cái và trạng thái nhất quán
    // =========================================================================
    [Fact]
    public async Task B2_Concurrent_CancelAndConfirmSameDraft_OneWins_OtherGets409()
    {
        var tenantId = OismsDatabaseSeeder.TenantAId;
        var token = await LoginAsync("owner@shopa.local");
        var (orderId, orderItemId, _, warehouseId, _) = await SeedOrderAsync(tenantId, quantity: 3m);

        var create = await SendAsync(HttpMethod.Post, "/api/sales-returns", token,
            new CreateSalesReturnDto
            {
                OrderId = orderId,
                WarehouseId = warehouseId,
                Reason = "Race cancel/confirm",
                Items = [new CreateSalesReturnItemDto { OrderItemId = orderItemId, Quantity = 1m }]
            },
            new() { ["Idempotency-Key"] = Guid.NewGuid().ToString() });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var ticket = (await create.Content.ReadFromJsonAsync<SalesReturnResponseDto>(_json))!;

        var confirmTask = SendAsync(HttpMethod.Post, $"/api/sales-returns/{ticket.Id}/confirm", token, new ConfirmSalesReturnRequestDto { RefundMethod = "Cash" });
        var cancelTask = SendAsync(HttpMethod.Post, $"/api/sales-returns/{ticket.Id}/cancel", token);
        var responses = await Task.WhenAll(confirmTask, cancelTask);

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        var loser = responses.Single(r => r.StatusCode != HttpStatusCode.OK);
        loser.StatusCode.Should().Be(HttpStatusCode.Conflict, await loser.Content.ReadAsStringAsync());
        (await ErrorCodeAsync(loser)).Should().Be("INVALID_STATE_TRANSITION");

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var dbReturn = await db.SalesReturns.Include(r => r.Items).FirstAsync(r => r.Id == ticket.Id);
            var itemId = dbReturn.Items.Single().Id;
            var ledgerCount = await db.InventoryTransactions.CountAsync(t => t.TenantId == tenantId && t.SalesReturnItemId == itemId);
            if (dbReturn.Status == SalesReturnStatus.Confirmed)
            {
                ledgerCount.Should().Be(1);
            }
            else
            {
                dbReturn.Status.Should().Be(SalesReturnStatus.Cancelled);
                ledgerCount.Should().Be(0);
            }
        }
    }

    // =========================================================================
    // (c) Replay Create cùng key: không tạo phiếu thứ hai (200 OK); khác payload: 409 IDEMPOTENCY_PAYLOAD_MISMATCH
    // =========================================================================
    [Fact]
    public async Task C_CreateAsync_IdempotencyReplay_SamePayloadReturns200_DifferentReturns409()
    {
        var tenantId = OismsDatabaseSeeder.TenantAId;
        var token = await LoginAsync("owner@shopa.local");

        var (orderId, orderItemId, _, warehouseId, _) = await SeedOrderAsync(tenantId, quantity: 5m);

        var reqKey = Guid.NewGuid();
        var originalPayload = new CreateSalesReturnDto
        {
            OrderId = orderId,
            WarehouseId = warehouseId,
            Reason = "Lỗi sản phẩm",
            Note = "Ghi chú gốc",
            Items = [new CreateSalesReturnItemDto { OrderItemId = orderItemId, Quantity = 1m }]
        };

        // 1. Tạo mới -> 201 Created
        var res1 = await SendAsync(HttpMethod.Post, "/api/sales-returns", token, originalPayload, new() { ["Idempotency-Key"] = reqKey.ToString() });
        res1.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto1 = (await res1.Content.ReadFromJsonAsync<SalesReturnResponseDto>(_json))!;

        // 2. Replay cùng key và cùng payload -> 200 OK, trả lại phiếu cũ
        var res2 = await SendAsync(HttpMethod.Post, "/api/sales-returns", token, originalPayload, new() { ["Idempotency-Key"] = reqKey.ToString() });
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto2 = (await res2.Content.ReadFromJsonAsync<SalesReturnResponseDto>(_json))!;
        dto2.Id.Should().Be(dto1.Id);
        dto2.Number.Should().Be(dto1.Number);

        // Kiểm tra trong DB không có bản ghi thứ 2
        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var count = await db.SalesReturns.CountAsync(r => r.TenantId == tenantId && r.RequestKey == reqKey);
            count.Should().Be(1);
        }

        // 3. Replay cùng key nhưng khác payload (khác Reason) -> 409 IDEMPOTENCY_PAYLOAD_MISMATCH
        var differentPayload = new CreateSalesReturnDto
        {
            OrderId = orderId,
            WarehouseId = warehouseId,
            Reason = "Lý do khác",
            Note = "Ghi chú gốc",
            Items = [new CreateSalesReturnItemDto { OrderItemId = orderItemId, Quantity = 1m }]
        };
        var res3 = await SendAsync(HttpMethod.Post, "/api/sales-returns", token, differentPayload, new() { ["Idempotency-Key"] = reqKey.ToString() });
        res3.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var errCode = await ErrorCodeAsync(res3);
        errCode.Should().Be("IDEMPOTENCY_PAYLOAD_MISMATCH");
    }

    // =========================================================================
    // (d) Confirm hai lần: chỉ có 1 dòng sổ
    // =========================================================================
    [Fact]
    public async Task D_Confirm_Twice_ShouldBeIdempotent_AndNotCreateDuplicateLedger()
    {
        var tenantId = OismsDatabaseSeeder.TenantAId;
        var token = await LoginAsync("owner@shopa.local");

        var (orderId, orderItemId, branchId, warehouseId, skuId) = await SeedOrderAsync(tenantId, quantity: 5m, initialOnHand: 10m);

        var createRes = await SendAsync(HttpMethod.Post, "/api/sales-returns", token,
            new CreateSalesReturnDto
            {
                OrderId = orderId,
                WarehouseId = warehouseId,
                Reason = "Trả hàng test idempotent confirm",
                Items = [new CreateSalesReturnItemDto { OrderItemId = orderItemId, Quantity = 1m }]
            },
            new() { ["Idempotency-Key"] = Guid.NewGuid().ToString() });
        var ticket = (await createRes.Content.ReadFromJsonAsync<SalesReturnResponseDto>(_json))!;

        // Xác nhận lần 1 -> 200 OK
        var confirm1 = await SendAsync(HttpMethod.Post, $"/api/sales-returns/{ticket.Id}/confirm", token, new ConfirmSalesReturnRequestDto { RefundMethod = "Cash" });
        confirm1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Xác nhận lần 2 -> 200 OK (idempotent replay)
        var confirm2 = await SendAsync(HttpMethod.Post, $"/api/sales-returns/{ticket.Id}/confirm", token, new ConfirmSalesReturnRequestDto { RefundMethod = "Cash" });
        confirm2.StatusCode.Should().Be(HttpStatusCode.OK);

        // Kiểm tra DB: OnHand chỉ tăng 1 lần (10 -> 11) và chỉ có đúng 1 dòng sổ
        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branchBal = await db.InventoryBalances.FirstAsync(b => b.TenantId == tenantId && b.BranchId == branchId && b.SkuId == skuId);
            branchBal.OnHand.Should().Be(11m);

            var returnItem = ticket.Items.Single();
            var ledgerCount = await db.InventoryTransactions.CountAsync(t => t.TenantId == tenantId && t.SalesReturnItemId == returnItem.Id);
            ledgerCount.Should().Be(1, "Chỉ được tạo đúng 1 dòng sổ cái IN.");
        }
    }

    // =========================================================================
    // (e) Đơn bán theo đơn vị có FactorSnapshot = 24, trả 1 thì cộng 24 đơn vị gốc
    // =========================================================================
    [Fact]
    public async Task E_Confirm_MultiUnitOrder_Factor24_Returns1Unit_IncreasesBaseQtyBy24()
    {
        var tenantId = OismsDatabaseSeeder.TenantAId;
        var token = await LoginAsync("owner@shopa.local");

        // Đơn bán theo thùng (FactorSnapshot = 24), giá vốn gốc = 10_000 / cái, OnHand ban đầu = 10
        var (orderId, orderItemId, branchId, warehouseId, skuId) = await SeedOrderAsync(
            tenantId, quantity: 2m, factorSnapshot: 24m, unitPrice: 300_000m, costPrice: 10_000m, initialOnHand: 10m);

        // Trả 1 thùng
        var createRes = await SendAsync(HttpMethod.Post, "/api/sales-returns", token,
            new CreateSalesReturnDto
            {
                OrderId = orderId,
                WarehouseId = warehouseId,
                Reason = "Trả 1 thùng",
                Items = [new CreateSalesReturnItemDto { OrderItemId = orderItemId, Quantity = 1m }]
            },
            new() { ["Idempotency-Key"] = Guid.NewGuid().ToString() });
        var ticket = (await createRes.Content.ReadFromJsonAsync<SalesReturnResponseDto>(_json))!;

        // Xác nhận phiếu
        var confirmRes = await SendAsync(HttpMethod.Post, $"/api/sales-returns/{ticket.Id}/confirm", token, new ConfirmSalesReturnRequestDto { RefundMethod = "BankTransfer" });
        confirmRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Kiểm tra DB: OnHand tăng 24 cái (10 -> 34), dòng IN có quantity = 24
        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branchBal = await db.InventoryBalances.FirstAsync(b => b.TenantId == tenantId && b.BranchId == branchId && b.SkuId == skuId);
            var whBal = await db.WarehouseInventoryBalances.FirstAsync(b => b.TenantId == tenantId && b.WarehouseId == warehouseId && b.SkuId == skuId);

            branchBal.OnHand.Should().Be(34m, "Tồn chi nhánh phải tăng thêm 24 đơn vị gốc.");
            whBal.OnHand.Should().Be(34m, "Tồn kho phải tăng thêm 24 đơn vị gốc.");

            var returnItem = ticket.Items.Single();
            var ledger = await db.InventoryTransactions.FirstAsync(t => t.TenantId == tenantId && t.SalesReturnItemId == returnItem.Id);
            ledger.Type.Should().Be("IN");
            ledger.Quantity.Should().Be(24m, "Số lượng dòng sổ cái kho phải được quy đổi sang đơn vị gốc (1 * 24 = 24).");
            ledger.UnitCost.Should().Be(10_000m);
            ledger.BalanceAfter.Should().Be(34m);
        }
    }

    // =========================================================================
    // (f) Tenant khác Get hoặc Confirm nhận 404
    // =========================================================================
    [Fact]
    public async Task F_TenantIsolation_CrossTenantGetOrConfirm_Returns404()
    {
        var tenantAId = OismsDatabaseSeeder.TenantAId;
        var tokenA = await LoginAsync("owner@shopa.local");
        var tokenB = await LoginAsync("owner@shopb.local");

        // Tạo phiếu trả ở Tenant A
        var (orderId, orderItemId, _, warehouseId, _) = await SeedOrderAsync(tenantAId, quantity: 2m);
        var createRes = await SendAsync(HttpMethod.Post, "/api/sales-returns", tokenA,
            new CreateSalesReturnDto
            {
                OrderId = orderId,
                WarehouseId = warehouseId,
                Reason = "Phiếu của Tenant A",
                Items = [new CreateSalesReturnItemDto { OrderItemId = orderItemId, Quantity = 1m }]
            },
            new() { ["Idempotency-Key"] = Guid.NewGuid().ToString() });
        var ticketA = (await createRes.Content.ReadFromJsonAsync<SalesReturnResponseDto>(_json))!;

        // Tenant B gọi GET /api/sales-returns/{idA} -> 404
        var getRes = await SendAsync(HttpMethod.Get, $"/api/sales-returns/{ticketA.Id}", tokenB);
        getRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var getErr = await ErrorCodeAsync(getRes);
        getErr.Should().Be("SALES_RETURN_NOT_FOUND");

        // Tenant B gọi POST /api/sales-returns/{idA}/confirm -> 404
        var confirmRes = await SendAsync(HttpMethod.Post, $"/api/sales-returns/{ticketA.Id}/confirm", tokenB, new ConfirmSalesReturnRequestDto { RefundMethod = "Cash" });
        confirmRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var confirmErr = await ErrorCodeAsync(confirmRes);
        confirmErr.Should().Be("SALES_RETURN_NOT_FOUND");
    }

    // =========================================================================
    // (g) Đơn sàn (Shopee, TikTok, Lazada): nhập kho, refund NULL
    // =========================================================================
    [Fact]
    public async Task G_MarketplaceOrder_ReturnStockImported_RefundIsNull()
    {
        var tenantId = OismsDatabaseSeeder.TenantAId;
        var token = await LoginAsync("owner@shopa.local");

        // Đơn hàng Shopee, OnHand ban đầu = 5
        var (orderId, orderItemId, branchId, warehouseId, skuId) = await SeedOrderAsync(
            tenantId, channel: "Shopee", quantity: 2m, unitPrice: 200_000m, costPrice: 120_000m, initialOnHand: 5m);

        // Lập phiếu trả (đơn sàn không có hoàn tiền)
        var createRes = await SendAsync(HttpMethod.Post, "/api/sales-returns", token,
            new CreateSalesReturnDto
            {
                OrderId = orderId,
                WarehouseId = warehouseId,
                Reason = "Khách trả hàng trên sàn Shopee",
                Items = [new CreateSalesReturnItemDto { OrderItemId = orderItemId, Quantity = 1m }]
            },
            new() { ["Idempotency-Key"] = Guid.NewGuid().ToString() });

        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var ticket = (await createRes.Content.ReadFromJsonAsync<SalesReturnResponseDto>(_json))!;
        ticket.TotalRefundAmount.Should().Be(0m);
        ticket.Items.Single().RefundAmount.Should().Be(0m);

        // Xác nhận phiếu
        var confirmRes = await SendAsync(HttpMethod.Post, $"/api/sales-returns/{ticket.Id}/confirm", token, new ConfirmSalesReturnRequestDto { RefundMethod = "Cash" });
        confirmRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmed = (await confirmRes.Content.ReadFromJsonAsync<SalesReturnResponseDto>(_json))!;

        // Đơn sàn: refund_method = NULL, refunded_at = NULL
        confirmed.RefundMethod.Should().BeNull();
        confirmed.RefundedAt.Should().BeNull();
        confirmed.ConfirmedAt.Should().NotBeNull();

        // Báo cáo: đơn sàn không ghi hoàn tiền nhưng doanh thu vẫn bị trừ theo giá trị dòng trả (1 × 200k)
        var reportRes = await SendAsync(HttpMethod.Get, $"/api/reports/revenue-profit?branchId={branchId}&channel=Shopee", token);
        reportRes.StatusCode.Should().Be(HttpStatusCode.OK, await reportRes.Content.ReadAsStringAsync());
        var report = (await reportRes.Content.ReadFromJsonAsync<RevenueProfitReportDto>(_json))!;
        report.ReturnAmount.Should().Be(200_000m);
        report.NetRevenue.Should().Be(report.Revenue - 200_000m);
        report.ReturnCogs.Should().Be(120_000m);
        report.TotalGrossProfit.Should().Be(report.TotalNetRevenue - report.TotalCostOfGoodsSold);

        // Kiểm tra DB: OnHand tăng 1 (5 -> 6), dòng IN có ghi nhận
        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branchBal = await db.InventoryBalances.FirstAsync(b => b.TenantId == tenantId && b.BranchId == branchId && b.SkuId == skuId);
            branchBal.OnHand.Should().Be(6m);

            var returnItem = ticket.Items.Single();
            var ledger = await db.InventoryTransactions.FirstAsync(t => t.TenantId == tenantId && t.SalesReturnItemId == returnItem.Id);
            ledger.Type.Should().Be("IN");
            ledger.Quantity.Should().Be(1m);
            ledger.UnitCost.Should().Be(120_000m);

            var dbReturn = await db.SalesReturns.FirstAsync(r => r.TenantId == tenantId && r.Id == ticket.Id);
            dbReturn.RefundMethod.Should().BeNull();
            dbReturn.RefundedAt.Should().BeNull();
            dbReturn.Status.Should().Be(SalesReturnStatus.Confirmed);
        }
    }

    // =========================================================================
    // AC-06: Cashier gọi endpoint trả hàng thì nhận 403 Forbidden
    // =========================================================================
    [Fact]
    public async Task Rbac_CashierCallingSalesReturnsEndpoints_Receives403Forbidden()
    {
        var tenantId = OismsDatabaseSeeder.TenantAId;
        var cashierToken = await LoginAsync("cashier@shopa.local");

        var (orderId, orderItemId, _, warehouseId, _) = await SeedOrderAsync(tenantId, quantity: 2m);

        // 1. GET /api/orders/{id}/returnable-items -> 403
        var res1 = await SendAsync(HttpMethod.Get, $"/api/orders/{orderId}/returnable-items", cashierToken);
        res1.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 2. POST /api/sales-returns -> 403
        var res2 = await SendAsync(HttpMethod.Post, "/api/sales-returns", cashierToken,
            new CreateSalesReturnDto
            {
                OrderId = orderId,
                WarehouseId = warehouseId,
                Reason = "Thu ngân gọi",
                Items = [new CreateSalesReturnItemDto { OrderItemId = orderItemId, Quantity = 1m }]
            },
            new() { ["Idempotency-Key"] = Guid.NewGuid().ToString() });
        res2.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 3. GET /api/sales-returns -> 403
        var res3 = await SendAsync(HttpMethod.Get, "/api/sales-returns", cashierToken);
        res3.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 4. POST /api/sales-returns/{id}/confirm -> 403
        var res4 = await SendAsync(HttpMethod.Post, $"/api/sales-returns/{Guid.NewGuid()}/confirm", cashierToken, new ConfirmSalesReturnRequestDto { RefundMethod = "Cash" });
        res4.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 5. POST /api/sales-returns/{id}/cancel -> 403
        var res5 = await SendAsync(HttpMethod.Post, $"/api/sales-returns/{Guid.NewGuid()}/cancel", cashierToken);
        res5.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // =========================================================================
    // AC-04 (P2): Bán, trả rồi gọi báo cáo doanh thu thuần & truy vết trên PostgreSQL thật
    // =========================================================================
    [Fact]
    public async Task ReportAndTrace_AfterSalesReturn_ReflectsNetRevenueAndReturnLinesOnPostgres()
    {
        var tenantId = OismsDatabaseSeeder.TenantAId;
        var ownerToken = await LoginAsync("owner@shopa.local");
        var staffToken = await LoginAsync("staff@shopa.local");

        // Bán 10 cái @ 100k (Doanh thu = 1,000,000, Giá vốn = 600,000)
        var (orderId, orderItemId, branchId, warehouseId, skuId) = await SeedOrderAsync(
            tenantId, channel: "POS", quantity: 10m, unitPrice: 100_000m, costPrice: 60_000m);

        // 1. Tạo phiếu trả 3 cái (tiền hoàn = 300,000, giá vốn hàng trả = 180,000)
        var createRes = await SendAsync(
            HttpMethod.Post,
            "/api/sales-returns",
            ownerToken,
            new CreateSalesReturnDto
            {
                OrderId = orderId,
                WarehouseId = warehouseId,
                Reason = "Khách đổi ý một phần",
                Items = [new CreateSalesReturnItemDto { OrderItemId = orderItemId, Quantity = 3m }]
            },
            new() { ["Idempotency-Key"] = Guid.NewGuid().ToString() });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var ticket = await createRes.Content.ReadFromJsonAsync<SalesReturnResponseDto>(_json);
        ticket.Should().NotBeNull();

        // 2. Xác nhận phiếu trả
        var confirmRes = await SendAsync(
            HttpMethod.Post,
            $"/api/sales-returns/{ticket!.Id}/confirm",
            ownerToken,
            new ConfirmSalesReturnRequestDto { RefundMethod = "Cash" });
        confirmRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Gọi báo cáo Doanh thu - Lợi nhuận bằng Owner token
        var reportRes = await SendAsync(HttpMethod.Get, "/api/reports/revenue-profit", ownerToken);
        reportRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = await reportRes.Content.ReadFromJsonAsync<RevenueProfitReportDto>(_json);
        report.Should().NotBeNull();

        // Doanh thu gộp 1tr, trả 300k => thuần 700k
        report!.Revenue.Should().Be(1_000_000m);
        report.ReturnAmount.Should().Be(300_000m);
        report.NetRevenue.Should().Be(700_000m);
        report.TotalNetRevenue.Should().Be(700_000m);

        // Giá vốn gộp 600k, trả 180k => thuần 420k
        report.CostOfGoodsSold.Should().Be(600_000m);
        report.ReturnCogs.Should().Be(180_000m);
        report.NetCogs.Should().Be(420_000m);
        report.TotalCostOfGoodsSold.Should().Be(420_000m, "Total* là số thuần: TotalNetRevenue - TotalCostOfGoodsSold = TotalGrossProfit");

        // Lãi gộp thuần = 700k - 420k = 280k
        report.GrossProfit.Should().Be(280_000m);
        report.TotalGrossProfit.Should().Be(280_000m);
        report.GrossMarginPercentage.Should().Be(40m); // 280 / 700 * 100

        // Phân rã Kênh bán
        report.ByChannel.Should().Contain(c => c.Channel == "POS");
        var posChannel = report.ByChannel.First(c => c.Channel == "POS");
        posChannel.Revenue.Should().Be(1_000_000m);
        posChannel.ReturnAmount.Should().Be(300_000m);
        posChannel.NetRevenue.Should().Be(700_000m);
        posChannel.CostOfGoodsSold.Should().Be(600_000m);
        posChannel.ReturnCogs.Should().Be(180_000m);
        posChannel.NetCogs.Should().Be(420_000m);
        posChannel.GrossProfit.Should().Be(280_000m);

        // 4. Gọi báo cáo bằng Staff token: Ẩn giá vốn và lãi gộp theo ma trận RBAC
        var staffReportRes = await SendAsync(HttpMethod.Get, "/api/reports/revenue-profit", staffToken);
        staffReportRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var staffReport = await staffReportRes.Content.ReadFromJsonAsync<RevenueProfitReportDto>(_json);
        staffReport.Should().NotBeNull();
        staffReport!.Revenue.Should().Be(1_000_000m);
        staffReport.ReturnAmount.Should().Be(300_000m);
        staffReport.NetRevenue.Should().Be(700_000m);
        staffReport.CostOfGoodsSold.Should().BeNull();
        staffReport.TotalCostOfGoodsSold.Should().BeNull();
        staffReport.ReturnCogs.Should().BeNull();
        staffReport.NetCogs.Should().BeNull();
        staffReport.GrossProfit.Should().BeNull();
        staffReport.TotalGrossProfit.Should().BeNull();
        staffReport.GrossMarginPercentage.Should().BeNull();

        // 5. Kiểm tra chi tiết đơn hàng (GET /api/orders/{id}): returnedQuantity = 3
        var orderRes = await SendAsync(HttpMethod.Get, $"/api/orders/{orderId}", ownerToken);
        orderRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var orderDetail = await orderRes.Content.ReadFromJsonAsync<OrderResponseDto>(_json);
        orderDetail.Should().NotBeNull();
        var itemDetail = orderDetail!.Items.Single(i => i.Id == orderItemId);
        itemDetail.Quantity.Should().Be(10m);
        itemDetail.ReturnedQuantity.Should().Be(3m);

        // 6. Kiểm tra truy vết đơn hàng (GET /api/orders/{id}/trace): có phiếu trả và dòng sổ IN
        var traceRes = await SendAsync(HttpMethod.Get, $"/api/orders/{orderId}/trace", ownerToken);
        traceRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var trace = await traceRes.Content.ReadFromJsonAsync<DocumentTraceDto>(_json);
        trace.Should().NotBeNull();
        trace!.SalesReturns.Should().ContainSingle(r => r.Id == ticket.Id);
        var retTrace = trace.SalesReturns.Single(r => r.Id == ticket.Id);
        retTrace.Status.Should().Be("Confirmed");
        retTrace.TotalRefundAmount.Should().Be(300_000m);
        retTrace.Lines.Should().ContainSingle(l => l.Type == "IN" && l.Quantity == 3m);
        retTrace.Lines[0].UnitCost.Should().Be(60_000m);

        // 7. Dashboard summary
        var dashRes = await SendAsync(HttpMethod.Get, "/api/reports/dashboard-summary", ownerToken);
        dashRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var dash = await dashRes.Content.ReadFromJsonAsync<DashboardSummaryDto>(_json);
        dash.Should().NotBeNull();
        dash!.TotalRevenue.Should().Be(1_000_000m);
        dash.ReturnAmount.Should().Be(300_000m);
        dash.NetRevenue.Should().Be(700_000m);
        dash.ReturnCogs.Should().Be(180_000m);
        dash.NetCogs.Should().Be(420_000m);
        dash.TotalGrossProfit.Should().Be(280_000m);
        dash.GrossMarginPercentage.Should().Be(40m);
    }
}
