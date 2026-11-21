using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.GoodsIssues.Dtos;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.SalesReturns.Dtos;
using OISM.Application.Features.StockCounts.Dtos;
using OISM.Application.Features.StockTransfers.Dtos;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Upgrade 14 (OISMS-66-U14-A): Integration tests trên PostgreSQL thật cho:
/// - AC-01 (Q2): Cashier đọc phiếu xuất của đơn mình qua GET /api/pos/orders/{orderId}/goods-issues; đơn Cashier khác hoặc tenant khác -> 404 GOODS_ISSUE_NOT_FOUND
/// - AC-02 (Q3): Cashier luôn nhận UnitCost/TotalCost = null ở mọi dòng và tổng
/// - AC-03 (Q7): Staff nhận null mọi trường giá nhập/giá vốn ở GET chi tiết phiếu nhập, chuyển kho, kiểm kê, trả hàng, phiếu điều chỉnh; Owner nhận đủ
/// - AC-04 (Q9): GET /api/inventory/adjustments/{id} (Owner,Staff), tenant theo JWT, tenant khác 404, có số phiếu, lý do, chiều, dòng SKU, đơn vị gốc, người lập
/// - AC-05 (Q9): DTO chi tiết có đủ trường in bổ sung (tên người lập/duyệt/nhận, NCC, tên đơn vị, số chứng từ liên quan)
/// </summary>
[Collection("PostgresIntegrationTests")]
public class PostgresU14PrintDocumentApiIntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private Guid _tenantAId;
    private Guid _tenantBId;
    private Guid _branchAId;
    private Guid _warehouseA1Id;
    private Guid _warehouseA2Id;
    private Guid _supplierAId;
    private Guid _skuAId;
    private string _skuACode = string.Empty;
    private decimal _retailPriceA;

    private string _cashier2Email = "cashier2@shopa.local";
    private Guid _cashier1OrderId;
    private Guid _cashier1GoodsIssueId;
    private Guid _cashier2OrderId;
    private Guid _tenantBOrderId;

    private Guid _goodsReceiptId;
    private Guid _stockTransferId;
    private Guid _stockCountId;
    private Guid _salesReturnId;
    private Guid _adjustmentId;

    private sealed class TestTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    public PostgresU14PrintDocumentApiIntegrationTests(WebApplicationFactoryWithPostgres factory)
    {
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
        await SeedTestDataAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private (AppDbContext Context, TestTenantService TenantService) CreateDb(Guid? tenantId = null)
    {
        var ts = new TestTenantService();
        if (tenantId.HasValue)
        {
            ts.SetTenant(tenantId.Value);
        }
        var ctx = PostgresTestEnvironment.CreateRuntimeDbContext(ts);
        return (ctx, ts);
    }

    private async Task<string> LoginAsAsync(string email, string password)
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var res = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = password
        });
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        return auth!.AccessToken;
    }

    private async Task SeedTestDataAsync()
    {
        var (seedCtx, _) = CreateDb(null);
        await using (seedCtx)
        {
            await OismsDatabaseSeeder.SeedAsync(seedCtx, PostgresTestEnvironment.AppPassword);
        }

        _tenantAId = OismsDatabaseSeeder.TenantAId;
        _tenantBId = OismsDatabaseSeeder.TenantBId;

        Guid ownerAId;
        await using (var db = CreateDb(_tenantAId).Context)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == _tenantAId);
            _branchAId = branch.Id;

            var whs = await db.Warehouses.Where(w => w.TenantId == _tenantAId && w.BranchId == _branchAId).ToListAsync();
            _warehouseA1Id = whs[0].Id;
            if (whs.Count > 1)
            {
                _warehouseA2Id = whs[1].Id;
            }
            else
            {
                var wh2 = new Warehouse
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantAId,
                    BranchId = _branchAId,
                    Name = "Kho Phụ U14",
                    CreatedAt = DateTimeOffset.UtcNow
                };
                db.Warehouses.Add(wh2);
                await db.SaveChangesAsync();
                _warehouseA2Id = wh2.Id;
            }

            var supplier = await db.Suppliers.FirstAsync(s => s.TenantId == _tenantAId);
            _supplierAId = supplier.Id;

            var sku = await db.ProductVariants.FirstAsync(v => v.TenantId == _tenantAId && v.IsActive);
            _skuAId = sku.Id;
            _skuACode = sku.SKU;
            _retailPriceA = sku.RetailPrice;

            var owner = await db.Users.FirstAsync(u => u.TenantId == _tenantAId && u.Role == "Owner");
            ownerAId = owner.Id;

            // Tạo Cashier 2 cùng Tenant A để test phân quyền sở hữu đơn (AC-01)
            var cashier1 = await db.Users.FirstAsync(u => u.TenantId == _tenantAId && u.Role == "Cashier");
            var cashier2 = new User
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantAId,
                Email = _cashier2Email,
                DisplayName = "Thu ngân 2 U14",
                Role = "Cashier",
                PasswordHash = cashier1.PasswordHash,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Users.Add(cashier2);
            await db.SaveChangesAsync();
        }

        // Nhập 100 sản phẩm vào Kho A1 qua PurchaseOrderSeed (WAC = 20,000)
        var (ctxA, tsA) = CreateDb(_tenantAId);
        await using (ctxA)
        {
            await PriceSeed.SyncFromLegacyColumnsAsync(ctxA);
            await PurchaseOrderSeed.ReceiveStockAsync(ctxA, tsA, _branchAId, _warehouseA1Id, _supplierAId, ownerAId, (_skuAId, 100m, 20000m));
            _goodsReceiptId = await ctxA.GoodsReceipts.Where(g => g.TenantId == _tenantAId).Select(g => g.Id).FirstAsync();
        }

        // Nhập 50 sản phẩm vào Tenant B để tạo đơn Tenant B
        var (ctxB, tsB) = CreateDb(_tenantBId);
        await using (ctxB)
        {
            await PriceSeed.SyncFromLegacyColumnsAsync(ctxB);
            var branchB = await ctxB.Branches.FirstAsync(b => b.TenantId == _tenantBId);
            var whB = await ctxB.Warehouses.FirstAsync(w => w.TenantId == _tenantBId && w.BranchId == branchB.Id);
            var supB = await ctxB.Suppliers.FirstAsync(s => s.TenantId == _tenantBId);
            var skuB = await ctxB.ProductVariants.FirstAsync(v => v.TenantId == _tenantBId && v.IsActive);
            var ownerB = await ctxB.Users.FirstAsync(u => u.TenantId == _tenantBId && u.Role == "Owner");
            await PurchaseOrderSeed.ReceiveStockAsync(ctxB, tsB, branchB.Id, whB.Id, supB.Id, ownerB.Id, (skuB.Id, 50m, 15000m));

            var ownerBToken = await LoginAsAsync("owner@shopb.local", PostgresTestEnvironment.AppPassword);
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerBToken);
            var posBRes = await _client.PostAsJsonAsync("/api/pos/checkout", new PosCheckoutRequestDto
            {
                BranchId = branchB.Id,
                RequestKey = Guid.NewGuid(),
                PaymentMethod = "Cash",
                AmountGiven = skuB.RetailPrice,
                Items = new List<OrderItemRequestDto>
                {
                    new() { SkuId = skuB.Id, SkuCode = skuB.SKU, Quantity = 1m, UnitPrice = skuB.RetailPrice }
                }
            });
            posBRes.StatusCode.Should().Be(HttpStatusCode.OK, await posBRes.Content.ReadAsStringAsync());
            var posBData = await posBRes.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);
            _tenantBOrderId = posBData!.OrderId;
        }

        // Cashier 1 checkout tạo Order 1
        var cashier1Token = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashier1Token);
        var pos1Res = await _client.PostAsJsonAsync("/api/pos/checkout", new PosCheckoutRequestDto
        {
            BranchId = _branchAId,
            RequestKey = Guid.NewGuid(),
            PaymentMethod = "Cash",
            AmountGiven = _retailPriceA * 2m,
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = _skuAId, SkuCode = _skuACode, Quantity = 2m, UnitPrice = _retailPriceA }
            }
        });
        pos1Res.StatusCode.Should().Be(HttpStatusCode.OK, await pos1Res.Content.ReadAsStringAsync());
        var pos1Data = await pos1Res.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);
        _cashier1OrderId = pos1Data!.OrderId;

        await using (var db = CreateDb(_tenantAId).Context)
        {
            _cashier1GoodsIssueId = await db.GoodsIssues
                .Where(g => g.OrderId == _cashier1OrderId)
                .Select(g => g.Id)
                .FirstAsync();
        }

        // Cashier 2 checkout tạo Order 2
        var cashier2Token = await LoginAsAsync(_cashier2Email, PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashier2Token);
        var pos2Res = await _client.PostAsJsonAsync("/api/pos/checkout", new PosCheckoutRequestDto
        {
            BranchId = _branchAId,
            RequestKey = Guid.NewGuid(),
            PaymentMethod = "Cash",
            AmountGiven = _retailPriceA,
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = _skuAId, SkuCode = _skuACode, Quantity = 1m, UnitPrice = _retailPriceA }
            }
        });
        pos2Res.StatusCode.Should().Be(HttpStatusCode.OK, await pos2Res.Content.ReadAsStringAsync());
        var pos2Data = await pos2Res.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);
        _cashier2OrderId = pos2Data!.OrderId;

        // Owner A tạo các chứng từ kho để test AC-03, AC-04, AC-05
        var ownerAToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerAToken);

        // 1. SalesReturn (Confirmed)
        Guid orderItemId;
        await using (var db = CreateDb(_tenantAId).Context)
        {
            orderItemId = await db.OrderItems.Where(i => i.OrderId == _cashier1OrderId).Select(i => i.Id).FirstAsync();
        }
        var srRes = await _client.PostAsJsonAsync("/api/sales-returns", new CreateSalesReturnDto
        {
            RequestKey = Guid.NewGuid(),
            OrderId = _cashier1OrderId,
            WarehouseId = _warehouseA1Id,
            Reason = "Trả hàng U14",
            Items = new List<CreateSalesReturnItemDto>
            {
                new() { OrderItemId = orderItemId, Quantity = 1m, RefundAmount = _retailPriceA }
            }
        });
        srRes.StatusCode.Should().Be(HttpStatusCode.Created, await srRes.Content.ReadAsStringAsync());
        _salesReturnId = (await srRes.Content.ReadFromJsonAsync<SalesReturnResponseDto>(_jsonOptions))!.Id;
        var srConfirmRes = await _client.PostAsJsonAsync($"/api/sales-returns/{_salesReturnId}/confirm", new ConfirmSalesReturnRequestDto { RefundMethod = "Cash" });
        srConfirmRes.StatusCode.Should().Be(HttpStatusCode.OK, await srConfirmRes.Content.ReadAsStringAsync());

        // 2. StockTransfer (Draft -> InTransit -> Received)
        var stRes = await _client.PostAsJsonAsync("/api/stock-transfers", new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = _branchAId,
            ToBranchId = _branchAId,
            FromWarehouseId = _warehouseA1Id,
            ToWarehouseId = _warehouseA2Id,
            Note = "Chuyển kho U14",
            Items = new List<StockTransferItemRequestDto> { new() { SkuId = _skuAId, Quantity = 2m } }
        });
        stRes.StatusCode.Should().Be(HttpStatusCode.Created, await stRes.Content.ReadAsStringAsync());
        _stockTransferId = (await stRes.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions))!.Id;
        var stDispatch = await _client.PostAsJsonAsync($"/api/stock-transfers/{_stockTransferId}/dispatch", new DispatchStockTransferRequestDto());
        stDispatch.StatusCode.Should().Be(HttpStatusCode.OK, await stDispatch.Content.ReadAsStringAsync());
        var stReceive = await _client.PostAsJsonAsync($"/api/stock-transfers/{_stockTransferId}/receive", new ReceiveStockTransferRequestDto());
        stReceive.StatusCode.Should().Be(HttpStatusCode.OK, await stReceive.Content.ReadAsStringAsync());

        // 3. StockCount (Draft -> Record -> Submit -> Confirm -> sinh InventoryAdjustment)
        var scRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = _branchAId,
            WarehouseId = _warehouseA1Id,
            Reason = "Kiểm kê U14"
        });
        scRes.StatusCode.Should().Be(HttpStatusCode.Created, await scRes.Content.ReadAsStringAsync());
        _stockCountId = (await scRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions))!.Id;

        var recRes = await _client.PostAsJsonAsync($"/api/stock-counts/{_stockCountId}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto>
            {
                new() { SkuId = _skuAId, CountedQuantity = 90m, AdjustmentUnitCost = 20000m }
            }
        });
        recRes.StatusCode.Should().Be(HttpStatusCode.OK, await recRes.Content.ReadAsStringAsync());

        var subRes = await _client.PostAsJsonAsync($"/api/stock-counts/{_stockCountId}/submit", new SubmitStockCountRequestDto());
        subRes.StatusCode.Should().Be(HttpStatusCode.OK, await subRes.Content.ReadAsStringAsync());

        var confRes = await _client.PostAsJsonAsync($"/api/stock-counts/{_stockCountId}/confirm", new ConfirmStockCountRequestDto { Note = "Duyệt kiểm kê U14" });
        confRes.StatusCode.Should().Be(HttpStatusCode.OK, await confRes.Content.ReadAsStringAsync());
        var confirmedCount = await confRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
        _adjustmentId = confirmedCount!.GeneratedAdjustmentId!.Value;
    }

    [Fact]
    public async Task AC01_AC02_Cashier_GetPosOrderGoodsIssues_OwnOrderSucceeds_CostNull_OtherCashierOrTenant404()
    {
        var cashier1Token = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashier1Token);

        // 1. Đơn của chính Cashier 1 -> 200 OK, giá vốn bị che null (AC-01, AC-02)
        var ownRes = await _client.GetAsync($"/api/pos/orders/{_cashier1OrderId}/goods-issues");
        ownRes.StatusCode.Should().Be(HttpStatusCode.OK, await ownRes.Content.ReadAsStringAsync());
        var issues = await ownRes.Content.ReadFromJsonAsync<List<GoodsIssueDto>>(_jsonOptions);
        issues.Should().NotBeNullOrEmpty();
        foreach (var issue in issues!)
        {
            issue.TotalCost.Should().BeNull("Cashier không được nhận TotalCost (AC-02)");
            issue.Items.Should().NotBeEmpty();
            foreach (var item in issue.Items)
            {
                item.UnitCost.Should().BeNull("Cashier không được nhận UnitCost (AC-02)");
                item.TotalCost.Should().BeNull("Cashier không được nhận TotalCost ở từng dòng (AC-02)");
            }
        }

        // 2. Đơn của Cashier 2 cùng tenant -> 404 GOODS_ISSUE_NOT_FOUND (AC-01)
        var otherCashierRes = await _client.GetAsync($"/api/pos/orders/{_cashier2OrderId}/goods-issues");
        otherCashierRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using (var doc = JsonDocument.Parse(await otherCashierRes.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("errorCode").GetString().Should().Be("GOODS_ISSUE_NOT_FOUND");
        }

        // 3. Đơn của Tenant B -> 404 GOODS_ISSUE_NOT_FOUND (AC-01)
        var otherTenantRes = await _client.GetAsync($"/api/pos/orders/{_tenantBOrderId}/goods-issues");
        otherTenantRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using (var doc = JsonDocument.Parse(await otherTenantRes.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("errorCode").GetString().Should().Be("GOODS_ISSUE_NOT_FOUND");
        }

        // 4. Cashier gọi endpoint Admin GoodsIssuesController -> vẫn bị chặn 403 Forbidden (Q2)
        var adminGiRes = await _client.GetAsync($"/api/goods-issues/{_cashier1GoodsIssueId}");
        adminGiRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var adminByOrderRes = await _client.GetAsync($"/api/orders/{_cashier1OrderId}/goods-issues");
        adminByOrderRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 5. Owner gọi endpoint POS -> 200 OK và thấy đủ giá vốn
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var ownerRes = await _client.GetAsync($"/api/pos/orders/{_cashier1OrderId}/goods-issues");
        ownerRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var ownerIssues = await ownerRes.Content.ReadFromJsonAsync<List<GoodsIssueDto>>(_jsonOptions);
        ownerIssues!.Should().NotBeEmpty();
        ownerIssues[0].TotalCost.Should().NotBeNull().And.BeGreaterThan(0);
        ownerIssues[0].Items[0].UnitCost.Should().NotBeNull().And.BeGreaterThan(0);
    }

    [Fact]
    public async Task AC03_AC05_DetailApis_MaskCostForStaff_ShowFullForOwner_AndIncludePrintFields()
    {
        // 1. Staff đọc 5 API chi tiết: mọi trường giá nhập/giá vốn phải là null (AC-03)
        var staffToken = await LoginAsAsync("staff@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);

        // Phiếu nhập
        var grStaffRes = await _client.GetAsync($"/api/goods-receipts/{_goodsReceiptId}");
        grStaffRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var grStaff = await grStaffRes.Content.ReadFromJsonAsync<GoodsReceiptResponseDto>(_jsonOptions);
        grStaff!.TotalAmount.Should().BeNull();
        grStaff.SupplierName.Should().NotBeNullOrWhiteSpace();
        grStaff.PurchaseOrderNumber.Should().NotBeNullOrWhiteSpace();
        grStaff.CreatedByName.Should().NotBeNullOrWhiteSpace();
        grStaff.Items.Should().NotBeEmpty();
        grStaff.Items.Should().AllSatisfy(i =>
        {
            i.UnitPrice.Should().BeNull();
            i.LineTotal.Should().BeNull();
            i.BaseUnitName.Should().NotBeNullOrWhiteSpace();
        });

        // Phiếu chuyển kho
        var stStaffRes = await _client.GetAsync($"/api/stock-transfers/{_stockTransferId}");
        stStaffRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var stStaff = await stStaffRes.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);
        stStaff!.CreatedByName.Should().NotBeNullOrWhiteSpace();
        stStaff.DispatchedByName.Should().NotBeNullOrWhiteSpace();
        stStaff.ReceivedByName.Should().NotBeNullOrWhiteSpace();
        stStaff.Items.Should().NotBeEmpty();
        stStaff.Items.Should().AllSatisfy(i =>
        {
            i.UnitCost.Should().BeNull();
            i.BaseUnitName.Should().NotBeNullOrWhiteSpace();
        });

        // Phiếu kiểm kê
        var scStaffRes = await _client.GetAsync($"/api/stock-counts/{_stockCountId}");
        scStaffRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var scStaff = await scStaffRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
        scStaff!.CreatedByName.Should().NotBeNullOrWhiteSpace();
        scStaff.SubmittedByName.Should().NotBeNullOrWhiteSpace();
        scStaff.ConfirmedByName.Should().NotBeNullOrWhiteSpace();
        scStaff.GeneratedAdjustmentNumber.Should().NotBeNullOrWhiteSpace();
        scStaff.Items.Should().NotBeEmpty();
        scStaff.Items.Should().AllSatisfy(i =>
        {
            i.AdjustmentUnitCost.Should().BeNull();
            i.BaseUnitName.Should().NotBeNullOrWhiteSpace();
        });

        // Phiếu trả hàng
        var srStaffRes = await _client.GetAsync($"/api/sales-returns/{_salesReturnId}");
        srStaffRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var srStaff = await srStaffRes.Content.ReadFromJsonAsync<SalesReturnResponseDto>(_jsonOptions);
        srStaff!.CreatedByName.Should().NotBeNullOrWhiteSpace();
        srStaff.Items.Should().NotBeEmpty();
        srStaff.Items.Should().AllSatisfy(i =>
        {
            i.OriginalUnitCost.Should().BeNull();
            i.BaseUnitName.Should().NotBeNullOrWhiteSpace();
        });

        // Phiếu điều chỉnh
        var adjStaffRes = await _client.GetAsync($"/api/inventory/adjustments/{_adjustmentId}");
        adjStaffRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var adjStaff = await adjStaffRes.Content.ReadFromJsonAsync<InventoryAdjustmentDto>(_jsonOptions);
        adjStaff!.TotalCost.Should().BeNull();
        adjStaff.Items.Should().NotBeEmpty();
        adjStaff.Items.Should().AllSatisfy(i =>
        {
            i.UnitCost.Should().BeNull();
            i.TotalCost.Should().BeNull();
        });

        // 2. Owner đọc 5 API chi tiết: nhận đủ giá nhập/giá vốn (> 0)
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var grOwner = await (await _client.GetAsync($"/api/goods-receipts/{_goodsReceiptId}")).Content.ReadFromJsonAsync<GoodsReceiptResponseDto>(_jsonOptions);
        grOwner!.TotalAmount.Should().NotBeNull().And.BeGreaterThan(0);
        grOwner.Items[0].UnitPrice.Should().NotBeNull().And.BeGreaterThan(0);

        var stOwner = await (await _client.GetAsync($"/api/stock-transfers/{_stockTransferId}")).Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);
        stOwner!.Items[0].UnitCost.Should().NotBeNull().And.BeGreaterThan(0);

        var scOwner = await (await _client.GetAsync($"/api/stock-counts/{_stockCountId}")).Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
        scOwner!.Items[0].AdjustmentUnitCost.Should().NotBeNull().And.BeGreaterThan(0);

        var srOwner = await (await _client.GetAsync($"/api/sales-returns/{_salesReturnId}")).Content.ReadFromJsonAsync<SalesReturnResponseDto>(_jsonOptions);
        srOwner!.Items[0].OriginalUnitCost.Should().NotBeNull().And.BeGreaterThan(0);

        var adjOwner = await (await _client.GetAsync($"/api/inventory/adjustments/{_adjustmentId}")).Content.ReadFromJsonAsync<InventoryAdjustmentDto>(_jsonOptions);
        adjOwner!.TotalCost.Should().NotBeNull().And.BeGreaterThan(0);
        adjOwner.Items[0].UnitCost.Should().NotBeNull().And.BeGreaterThan(0);
    }

    [Fact]
    public async Task AC04_GetInventoryAdjustmentById_ReturnsFields_TenantIsolated_404OtherTenant_403Cashier()
    {
        var staffToken = await LoginAsAsync("staff@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);

        var res = await _client.GetAsync($"/api/inventory/adjustments/{_adjustmentId}");
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var adj = await res.Content.ReadFromJsonAsync<InventoryAdjustmentDto>(_jsonOptions);
        adj.Should().NotBeNull();
        adj!.Number.Should().NotBeNullOrWhiteSpace();
        adj.Reason.Should().NotBeNullOrWhiteSpace();
        adj.Direction.Should().Be("OUT");
        adj.CreatedByName.Should().NotBeNullOrWhiteSpace();
        adj.StockCountId.Should().Be(_stockCountId);
        adj.StockCountNumber.Should().NotBeNullOrWhiteSpace();
        adj.Items.Should().NotBeEmpty();
        adj.Items[0].SkuCode.Should().Be(_skuACode);
        adj.Items[0].BaseUnitName.Should().NotBeNullOrWhiteSpace();
        adj.Items[0].Direction.Should().Be("OUT");

        // Cashier bị từ chối 403
        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        var cashierRes = await _client.GetAsync($"/api/inventory/adjustments/{_adjustmentId}");
        cashierRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Tenant B truy cập phiếu điều chỉnh của Tenant A -> 404 ADJUSTMENT_NOT_FOUND
        var ownerBToken = await LoginAsAsync("owner@shopb.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerBToken);
        var crossTenantRes = await _client.GetAsync($"/api/inventory/adjustments/{_adjustmentId}");
        crossTenantRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var doc = JsonDocument.Parse(await crossTenantRes.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("ADJUSTMENT_NOT_FOUND");
    }
}
