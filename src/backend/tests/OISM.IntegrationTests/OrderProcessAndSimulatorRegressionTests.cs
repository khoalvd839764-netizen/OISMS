using OISM.Application.Features.Webhooks.Dtos;
using OISM.Application.Features.Webhooks.Services;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Inventory.Services;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Orders.Services;
using OISM.Application.Features.Simulator.Dtos;
using OISM.Application.Features.Simulator.Services;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Bộ kiểm thử tích hợp hồi quy trên PostgreSQL thật cho S3-BE2-T01 (Review R1 Fix v1.1).
/// Kiểm chứng toàn bộ 7 tiêu chuẩn nghiệm thu R1-AC-01 .. R1-AC-07:
/// - F01 (R1-AC-01..04): POST /api/orders/{id}/process tuân thủ lifecycle Reserved -> Confirmed (không có 23514 / Processing),
///   idempotent retry, chặn invalid transition không confirm dở dang, concurrency locking an toàn.
/// - F06 (R1-AC-05..07): Ingest qua SimulatorService tuân thủ ShopIntegration validation (không bypass),
///   sử dụng WebhookCanonicalSignatureHelper SHA-256 từ chối payload mismatch (cùng tổng tiền khác items 1x100 vs 2x50),
///   idempotent replay chính xác, và cách ly đa chi nhánh / đa tenant.
/// </summary>
public class OrderProcessAndSimulatorRegressionTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class TestTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    public OrderProcessAndSimulatorRegressionTests(WebApplicationFactoryWithPostgres factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(Guid TenantId, string AccessToken, Guid BranchId, Guid WarehouseId, Guid SkuId, string SkuCode, decimal RetailPrice, Guid UserId)> SetupTenantAndStockAsync(decimal initialStock = 10m)
    {
        _client.DefaultRequestHeaders.Authorization = null;

        // 1. Seed cơ sở dữ liệu với OismsDatabaseSeeder
        await using (var seedDb = PostgresTestEnvironment.CreateRuntimeDbContext(new TestTenantService()))
        {
            await OismsDatabaseSeeder.SeedAsync(seedDb, PostgresTestEnvironment.AppPassword);
        }

        var tenantId = OismsDatabaseSeeder.TenantAId;
        var tenantService = new TestTenantService { TenantId = tenantId };

        // 2. Đăng nhập lấy Token bằng tài khoản Owner của Tenant A
        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "owner@shopa.local",
            Password = PostgresTestEnvironment.AppPassword
        });
        loginRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var authData = await loginRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        var token = authData!.AccessToken;

        // 3. Khởi tạo tồn kho ban đầu cho SKU
        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService);
        var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
        var warehouse = await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branch.Id);
        var supplier = await db.Suppliers.FirstAsync(s => s.TenantId == tenantId);
        var user = await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local");
        var sku = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);

        if (initialStock > 0)
        {
            var receiptsService = new GoodsReceiptService(db, tenantService, new WacCalculator());
            var receipt = await receiptsService.CreateReceiptAsync(new CreateGoodsReceiptDto
            {
                BranchId = branch.Id,
                WarehouseId = warehouse.Id,
                SupplierId = supplier.Id,
                Items = new List<CreateGoodsReceiptItemDto>
                {
                    new() { SkuId = sku.Id, Quantity = initialStock, UnitPrice = 100m }
                }
            }, user.Id);
            await receiptsService.ConfirmReceiptAsync(receipt.Id, user.Id);
        }

        return (tenantId, token, branch.Id, warehouse.Id, sku.Id, sku.SKU, sku.RetailPrice, user.Id);
    }

    [Fact]
    public async Task R1_AC01_Process_WhenOrderReserved_ConfirmsOrderAndDeductsInventory_AndReturns200()
    {
        // Arrange
        var (tenantId, token, branchId, _, skuId, skuCode, retailPrice, _) = await SetupTenantAndStockAsync(10m);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Tạo đơn hàng Admin (Reserved, quantity = 2)
        var createRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new() { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2, UnitPrice = retailPrice } }
        });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
        created.Should().NotBeNull();
        created!.Status.Should().Be("Reserved");

        // Act: POST /api/orders/{id}/process
        var processRes = await _client.PostAsync($"/api/orders/{created.Id}/process", null);
        var processBody = await processRes.Content.ReadAsStringAsync();

        // Assert: HTTP 200 OK, không có lỗi 23514 hay 500
        processRes.StatusCode.Should().Be(HttpStatusCode.OK, $"Expected 200 OK but got {processRes.StatusCode}. Body: {processBody}");
        processBody.Should().NotContain("ck_orders_status_values");

        var processedOrder = await processRes.Content.ReadFromJsonAsync<OrderDetailDto>(_jsonOptions);
        processedOrder.Should().NotBeNull();
        processedOrder!.Status.Should().Be("Confirmed");

        // Kiểm tra cơ sở dữ liệu PostgreSQL
        var tenantService = new TestTenantService { TenantId = tenantId };
        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService);

        var persistedOrder = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == created.Id);
        persistedOrder.Status.Should().Be("Confirmed");

        var balance = await db.InventoryBalances.AsNoTracking().SingleAsync(b => b.BranchId == branchId && b.SkuId == skuId);
        balance.OnHand.Should().Be(8m, "Tồn thực tế phải bị trừ 2 từ 10 xuống 8");
        balance.Reserved.Should().Be(0m, "Tồn giữ chỗ phải được giải phóng về 0");

        var outTransactions = await db.InventoryTransactions.AsNoTracking().CountAsync(t => t.Type == "OUT" && t.TenantId == tenantId);
        outTransactions.Should().Be(1, "Phải ghi nhận đúng 1 ledger giao dịch OUT cho việc xuất hàng");
    }

    [Fact]
    public async Task R1_AC02_Process_OnCancelledOrCompletedOrder_ReturnsControlled409_AndDoesNotMutateInventory()
    {
        // Arrange
        var (tenantId, token, branchId, _, skuId, skuCode, retailPrice, _) = await SetupTenantAndStockAsync(10m);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Tạo đơn hàng và hủy đơn
        var createRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new() { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2, UnitPrice = retailPrice } }
        });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);

        var cancelRes = await _client.PostAsJsonAsync($"/api/orders/{created!.Id}/cancel", new CancelOrderRequestDto
        {
            Reason = "Khách yêu cầu hủy"
        });
        cancelRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Lấy snapshot tồn kho sau khi hủy (OnHand=10, Reserved=0, outRows=0)
        var tenantService = new TestTenantService { TenantId = tenantId };
        await using (var dbBefore = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService))
        {
            var balBefore = await dbBefore.InventoryBalances.AsNoTracking().SingleAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            balBefore.OnHand.Should().Be(10m);
            balBefore.Reserved.Should().Be(0m);
        }

        // Act: POST /api/orders/{id}/process trên đơn đã Cancelled
        var processRes = await _client.PostAsync($"/api/orders/{created.Id}/process", null);

        // Assert: HTTP 409 Conflict với INVALID_STATE_TRANSITION, không được phép chuyển trạng thái dở dang
        processRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using (var dbAfter = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService))
        {
            var orderAfter = await dbAfter.Orders.AsNoTracking().SingleAsync(o => o.Id == created.Id);
            orderAfter.Status.Should().Be("Cancelled");

            var balAfter = await dbAfter.InventoryBalances.AsNoTracking().SingleAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            balAfter.OnHand.Should().Be(10m, "Tồn OnHand không được bị thay đổi");
            balAfter.Reserved.Should().Be(0m, "Tồn Reserved không được bị thay đổi");

            var outRows = await dbAfter.InventoryTransactions.AsNoTracking().CountAsync(t => t.Type == "OUT" && t.TenantId == tenantId);
            outRows.Should().Be(0, "Không được sinh ra ledger xuất kho");
        }
    }

    [Fact]
    public async Task R1_AC03_Process_ReplayOnAlreadyConfirmedOrder_IsIdempotent_DoesNotDoubleDeduct()
    {
        // Arrange
        var (tenantId, token, branchId, _, skuId, skuCode, retailPrice, _) = await SetupTenantAndStockAsync(10m);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new() { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 3, UnitPrice = retailPrice } }
        });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);

        // First process: Reserved -> Confirmed
        var processRes1 = await _client.PostAsync($"/api/orders/{created!.Id}/process", null);
        processRes1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act: Replay process lần 2
        var processRes2 = await _client.PostAsync($"/api/orders/{created.Id}/process", null);

        // Assert: Replay phải trả 200 OK, trạng thái Confirmed, không double-apply
        processRes2.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail2 = await processRes2.Content.ReadFromJsonAsync<OrderDetailDto>(_jsonOptions);
        detail2!.Status.Should().Be("Confirmed");

        var tenantService = new TestTenantService { TenantId = tenantId };
        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService);

        var balance = await db.InventoryBalances.AsNoTracking().SingleAsync(b => b.BranchId == branchId && b.SkuId == skuId);
        balance.OnHand.Should().Be(7m, "OnHand chỉ bị trừ 1 lần (10 - 3 = 7)");
        balance.Reserved.Should().Be(0m);

        var outRows = await db.InventoryTransactions.AsNoTracking().CountAsync(t => t.Type == "OUT" && t.TenantId == tenantId);
        outRows.Should().Be(1, "Ledger OUT chỉ được ghi 1 lần duy nhất");
    }

    [Fact]
    public async Task R1_AC04_ConcurrentProcessCalls_AreSerialized_AndIdempotent()
    {
        // Arrange
        var (tenantId, token, branchId, _, skuId, skuCode, retailPrice, _) = await SetupTenantAndStockAsync(10m);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new() { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2, UnitPrice = retailPrice } }
        });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);

        // Act: Gửi 2 request process đồng thời
        var task1 = _client.PostAsync($"/api/orders/{created!.Id}/process", null);
        var task2 = _client.PostAsync($"/api/orders/{created.Id}/process", null);
        var results = await Task.WhenAll(task1, task2);

        // Assert: Cả 2 request đều thành công (hoặc 1 thành công 200 và 1 idempotent 200), không có crash 500
        results.All(r => r.StatusCode == HttpStatusCode.OK).Should().BeTrue("Các concurrent process requests đều phải kết thúc thành công với HTTP 200");

        var tenantService = new TestTenantService { TenantId = tenantId };
        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService);

        var balance = await db.InventoryBalances.AsNoTracking().SingleAsync(b => b.BranchId == branchId && b.SkuId == skuId);
        balance.OnHand.Should().Be(8m, "Tồn kho chỉ bị trừ đúng 2 đơn vị");
        balance.Reserved.Should().Be(0m);

        var outRows = await db.InventoryTransactions.AsNoTracking().CountAsync(t => t.Type == "OUT" && t.TenantId == tenantId);
        outRows.Should().Be(1, "Chỉ duy nhất 1 ledger OUT được ghi nhận");
    }

    [Fact]
    public async Task R1_AC05_Simulator_SameTotalChangedItems_Replay_RejectsWithPayloadMismatch409()
    {
        // Arrange
        var (tenantId, _, branchId, _, _, skuCode, _, _) = await SetupTenantAndStockAsync(10m);
        var tenantService = new TestTenantService { TenantId = tenantId };
        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService);

        // Cấu hình ShopIntegration hợp lệ
        var shop = new ShopIntegration
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            Channel = "Shopee",
            ExternalShopId = "SHOP-TEST-AC05",
            SecretHash = Guid.NewGuid().ToString("N"),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ShopIntegrations.Add(shop);
        await db.SaveChangesAsync();

        var simulator = new SimulatorService(db, tenantService, new OrderService(db, tenantService));

        var payload = new WebhookOrderPayloadDto
        {
            ExternalShopId = shop.ExternalShopId,
            ExternalOrderId = "ORD-EXT-AC05",
            BranchId = branchId,
            OrderTimestamp = DateTimeOffset.UtcNow,
            Items = new() { new() { SkuCode = skuCode, Quantity = 1, UnitPrice = 100m } }
        };

        // 1. Ingest lần đầu: 1 x 100 = 100 -> Thành công
        var firstOrder = await simulator.IngestWebhookOrderAsync("Shopee", payload, null);
        firstOrder.Should().NotBeNull();
        firstOrder.Status.Should().Be("Reserved");

        // 2. Replay đổi items: 2 x 50 = 100 (cùng tổng tiền, khác cấu trúc items)
        payload.Items = new() { new() { SkuCode = skuCode, Quantity = 2, UnitPrice = 50m } };

        // Act & Assert: Phải bị từ chối với lỗi WEBHOOK_PAYLOAD_MISMATCH (409)
        var exception = await Record.ExceptionAsync(async () =>
        {
            await simulator.IngestWebhookOrderAsync("Shopee", payload, null);
        });

        exception.Should().NotBeNull();
        exception.Should().BeOfType<OismException>();
        var oismEx = (OismException)exception!;
        oismEx.ErrorCode.Should().Be("WEBHOOK_PAYLOAD_MISMATCH");
        oismEx.StatusCode.Should().Be(409);

        // Kiểm tra dữ liệu trong DB vẫn giữ nguyên đơn hàng ban đầu
        var persistedOrder = await db.Orders.Include(o => o.Items).AsNoTracking().SingleAsync(o => o.Id == firstOrder.Id);
        persistedOrder.Items.Single().Quantity.Should().Be(1m);
        persistedOrder.Items.Single().UnitPrice.Should().Be(100m);

        var balance = await db.InventoryBalances.AsNoTracking().SingleAsync(b => b.BranchId == branchId && b.Sku!.SKU == skuCode);
        balance.Reserved.Should().Be(1m, "Giữ chỗ ban đầu vẫn là 1, không bị tăng thành 2");
    }

    [Fact]
    public async Task R1_AC06_Simulator_ExactReplay_ReturnsExistingOrder_WithoutIncreasingReservation()
    {
        // Arrange
        var (tenantId, _, branchId, _, _, skuCode, _, _) = await SetupTenantAndStockAsync(10m);
        var tenantService = new TestTenantService { TenantId = tenantId };
        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService);

        var shop = new ShopIntegration
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            Channel = "TikTok",
            ExternalShopId = "SHOP-TEST-AC06",
            SecretHash = Guid.NewGuid().ToString("N"),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ShopIntegrations.Add(shop);
        await db.SaveChangesAsync();

        var simulator = new SimulatorService(db, tenantService, new OrderService(db, tenantService));

        var payload = new WebhookOrderPayloadDto
        {
            ExternalShopId = shop.ExternalShopId,
            ExternalOrderId = "ORD-EXT-AC06",
            BranchId = branchId,
            OrderTimestamp = DateTimeOffset.UtcNow,
            Items = new() { new() { SkuCode = skuCode, Quantity = 2, UnitPrice = 150m } }
        };

        // Act 1: Ingest lần đầu
        var first = await simulator.IngestWebhookOrderAsync("TikTok", payload, null);
        first.Should().NotBeNull();

        // Act 2: Exact Replay cùng payload
        var replay = await simulator.IngestWebhookOrderAsync("TikTok", payload, null);

        // Assert: Trả về chính xác đơn hàng cũ, không tăng giữ chỗ
        replay.Should().NotBeNull();
        replay.Id.Should().Be(first.Id);

        var balance = await db.InventoryBalances.AsNoTracking().SingleAsync(b => b.BranchId == branchId && b.Sku!.SKU == skuCode);
        balance.Reserved.Should().Be(2m, "Tồn giữ chỗ vẫn là 2, không bị double reserve thành 4");
    }

    [Fact]
    public async Task R1_AC07_Simulator_SecurityGuards_EnforceShopIntegration()
    {
        // Arrange
        var (tenantId, _, branchId, _, _, skuCode, _, _) = await SetupTenantAndStockAsync(10m);
        var tenantService = new TestTenantService { TenantId = tenantId };
        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService);

        var activeShop = new ShopIntegration
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            Channel = "Lazada",
            ExternalShopId = "SHOP-ACTIVE",
            SecretHash = Guid.NewGuid().ToString("N"),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var inactiveShop = new ShopIntegration
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            Channel = "Lazada",
            ExternalShopId = "SHOP-INACTIVE",
            SecretHash = Guid.NewGuid().ToString("N"),
            IsActive = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.ShopIntegrations.AddRange(activeShop, inactiveShop);
        await db.SaveChangesAsync();

        var simulator = new SimulatorService(db, tenantService, new OrderService(db, tenantService));

        // Case 1: Unconfigured shop -> INTEGRATION_NOT_FOUND (404)
        var unconfPayload = new WebhookOrderPayloadDto
        {
            ExternalShopId = "SHOP-UNKNOWN-NOT-FOUND",
            ExternalOrderId = "ORD-TEST-1",
            BranchId = branchId,
            OrderTimestamp = DateTimeOffset.UtcNow,
            Items = new() { new() { SkuCode = skuCode, Quantity = 1, UnitPrice = 50m } }
        };
        var exUnconf = await Record.ExceptionAsync(async () => await simulator.IngestWebhookOrderAsync("Lazada", unconfPayload, null));
        exUnconf.Should().NotBeNull();
        exUnconf.Should().BeOfType<OismException>();
        ((OismException)exUnconf!).ErrorCode.Should().Be("INTEGRATION_NOT_FOUND");
        ((OismException)exUnconf!).StatusCode.Should().Be(404);

        // Case 2: Inactive shop -> UNAUTHORIZED (401)
        var inactivePayload = new WebhookOrderPayloadDto
        {
            ExternalShopId = inactiveShop.ExternalShopId,
            ExternalOrderId = "ORD-TEST-2",
            BranchId = branchId,
            OrderTimestamp = DateTimeOffset.UtcNow,
            Items = new() { new() { SkuCode = skuCode, Quantity = 1, UnitPrice = 50m } }
        };
        var exInactive = await Record.ExceptionAsync(async () => await simulator.IngestWebhookOrderAsync("Lazada", inactivePayload, null));
        exInactive.Should().NotBeNull();
        exInactive.Should().BeOfType<OismException>();
        ((OismException)exInactive!).ErrorCode.Should().Be("UNAUTHORIZED");
        ((OismException)exInactive!).StatusCode.Should().Be(401);

        // Case 3: Mismatched Branch -> BRANCH_MISMATCH (400)
        var mismatchedBranchId = Guid.NewGuid();
        var branchMismatchPayload = new WebhookOrderPayloadDto
        {
            ExternalShopId = activeShop.ExternalShopId,
            ExternalOrderId = "ORD-TEST-3",
            BranchId = mismatchedBranchId,
            OrderTimestamp = DateTimeOffset.UtcNow,
            Items = new() { new() { SkuCode = skuCode, Quantity = 1, UnitPrice = 50m } }
        };
        var exBranch = await Record.ExceptionAsync(async () => await simulator.IngestWebhookOrderAsync("Lazada", branchMismatchPayload, null));
        exBranch.Should().NotBeNull();
        exBranch.Should().BeOfType<OismException>();
        ((OismException)exBranch!).ErrorCode.Should().Be("BRANCH_MISMATCH");
        ((OismException)exBranch!).StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task R2_AC01_Batch_WithoutConfiguredIntegration_RejectsWithControlled404_NoShopOrOrderCreated()
    {
        // Arrange
        var (tenantId, token, branchId, _, _, skuCode, _, _) = await SetupTenantAndStockAsync(10m);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var tenantService = new TestTenantService { TenantId = tenantId };
        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService);

        var beforeIntegrations = await db.ShopIntegrations.CountAsync(x => x.Channel == "Shopee" && x.BranchId == branchId);
        beforeIntegrations.Should().Be(0, "Chưa có ShopIntegration nào được cấu hình cho branch/channel này");

        // Act: Gọi batch simulation khi chưa cấu hình ShopIntegration
        var batchRes = await _client.PostAsJsonAsync("/api/webhooks/simulator/batch", new SimulationBatchRequestDto
        {
            Channel = "Shopee",
            BranchId = branchId,
            SkuCode = skuCode,
            ConcurrencyCount = 1,
            QuantityPerOrder = 1,
            UnitPrice = 100m
        });

        // Assert: Bị từ chối có kiểm soát với HTTP 404 (INTEGRATION_NOT_FOUND)
        batchRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await batchRes.Content.ReadAsStringAsync();
        body.Should().Contain("INTEGRATION_NOT_FOUND");

        // Kiểm chứng không có side effect: không tạo shop, không tạo order, không giữ chỗ tồn kho
        var afterIntegrations = await db.ShopIntegrations.CountAsync(x => x.Channel == "Shopee" && x.BranchId == branchId);
        afterIntegrations.Should().Be(0, "Batch bị từ chối tuyệt đối không được tự ý INSERT ShopIntegration ngầm");

        var ordersCount = await db.Orders.CountAsync(x => x.TenantId == tenantId);
        ordersCount.Should().Be(0, "Không có đơn hàng nào được tạo khi batch bị từ chối");

        var balance = await db.InventoryBalances.AsNoTracking().SingleAsync(b => b.BranchId == branchId && b.Sku!.SKU == skuCode);
        balance.Reserved.Should().Be(0m, "Không phát sinh giữ chỗ tồn kho khi batch bị từ chối");
    }

    [Fact]
    public async Task R2_AC02_PredictableSourceSecret_CannotIngestAnonymousWebhook_Returns401_WhileValidSecretSucceeds()
    {
        // Arrange
        var (tenantId, _, branchId, _, _, skuCode, _, _) = await SetupTenantAndStockAsync(10m);
        var tenantService = new TestTenantService { TenantId = tenantId };
        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService);

        var validSecret = "my-custom-secure-secret-key-12345";
        var shop = new ShopIntegration
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            Channel = "Shopee",
            ExternalShopId = "SHOP-TEST-R2-AC02",
            SecretHash = WebhookSecretHasher.HashSecret(validSecret),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ShopIntegrations.Add(shop);
        await db.SaveChangesAsync();

        _client.DefaultRequestHeaders.Authorization = null;

        // Act 1: Thử gọi webhook ẩn danh bằng secret cố định lịch sử ('sim-batch-secret-key')
        var badRequest = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/simulator/Shopee")
        {
            Content = JsonContent.Create(new CanonicalWebhookPayloadDto
            {
                Channel = "Shopee",
                ExternalShopId = shop.ExternalShopId,
                ExternalOrderId = "ORD-ATTACK-01",
                OrderTimestamp = DateTimeOffset.UtcNow,
                TotalAmount = 100m,
                Items = new() { new() { SkuCode = skuCode, Quantity = 1, UnitPrice = 100m } }
            })
        };
        badRequest.Headers.Add("X-Webhook-Secret", "sim-batch-secret-key");
        var badResponse = await _client.SendAsync(badRequest);

        // Assert 1: Bị từ chối 401 Unauthorized, không có đơn nào được tạo
        badResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var attackOrderCount = await db.Orders.CountAsync(x => x.ExternalOrderId == "ORD-ATTACK-01");
        attackOrderCount.Should().Be(0, "Không có đơn hàng nào được tạo bằng secret cố định cũ");

        // Act 2: Gọi webhook bằng đúng secret riêng hợp lệ được cấu hình
        var goodRequest = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/simulator/Shopee")
        {
            Content = JsonContent.Create(new CanonicalWebhookPayloadDto
            {
                Channel = "Shopee",
                ExternalShopId = shop.ExternalShopId,
                ExternalOrderId = "ORD-VALID-01",
                OrderTimestamp = DateTimeOffset.UtcNow,
                TotalAmount = 100m,
                Items = new() { new() { SkuCode = skuCode, Quantity = 1, UnitPrice = 100m } }
            })
        };
        goodRequest.Headers.Add("X-Webhook-Secret", validSecret);
        var goodResponse = await _client.SendAsync(goodRequest);

        // Assert 2: Thành công 201 Created và đơn được lưu đúng
        goodResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var validOrder = await db.Orders.SingleOrDefaultAsync(x => x.ExternalOrderId == "ORD-VALID-01");
        validOrder.Should().NotBeNull();
        validOrder!.Status.Should().Be("Reserved");
    }

    [Fact]
    public async Task R2_AC03_Batch_WithValidIntegration_SucceedsAndDoesNotMutateCredentials()
    {
        // Arrange
        var (tenantId, token, branchId, _, _, skuCode, _, _) = await SetupTenantAndStockAsync(10m);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var tenantService = new TestTenantService { TenantId = tenantId };
        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService);

        var dedicatedSecretHash = WebhookSecretHasher.HashSecret("my-dedicated-secret-999");
        var shop = new ShopIntegration
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            Channel = "Shopee",
            ExternalShopId = "SHOP-TEST-R2-AC03",
            SecretHash = dedicatedSecretHash,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ShopIntegrations.Add(shop);
        await db.SaveChangesAsync();

        var initialIntegrationsCount = await db.ShopIntegrations.CountAsync(x => x.BranchId == branchId && x.TenantId == tenantId);

        // Act: Chạy batch simulation với cấu hình hợp lệ
        var batchRes = await _client.PostAsJsonAsync("/api/webhooks/simulator/batch", new SimulationBatchRequestDto
        {
            Channel = "Shopee",
            BranchId = branchId,
            SkuCode = skuCode,
            ConcurrencyCount = 2,
            QuantityPerOrder = 1,
            UnitPrice = 100m
        });

        // Assert: Thành công 200 OK
        batchRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await batchRes.Content.ReadFromJsonAsync<SimulationBatchResultDto>(_jsonOptions);
        result.Should().NotBeNull();
        result!.SuccessCount.Should().Be(2);

        // Kiểm tra credential của shop hoàn toàn không bị ghi đè hay thay đổi
        var reloadedShop = await db.ShopIntegrations.AsNoTracking().SingleAsync(x => x.Id == shop.Id);
        reloadedShop.SecretHash.Should().Be(dedicatedSecretHash, "SecretHash của gian hàng không bị thay đổi bởi batch");

        var finalIntegrationsCount = await db.ShopIntegrations.CountAsync(x => x.BranchId == branchId && x.TenantId == tenantId);
        finalIntegrationsCount.Should().Be(initialIntegrationsCount, "Batch không được tự ý thêm bất kỳ ShopIntegration nào khác");
    }

    [Fact]
    public async Task R2_AC04_SimulatorAdapter_DefaultTimestamp_ThrowsValidationError400_DoesNotPersistOrder()
    {
        // Arrange
        var (tenantId, _, branchId, _, _, skuCode, _, _) = await SetupTenantAndStockAsync(10m);
        var tenantService = new TestTenantService { TenantId = tenantId };
        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService);

        var shop = new ShopIntegration
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            Channel = "Shopee",
            ExternalShopId = "SHOP-TEST-R2-AC04",
            SecretHash = Guid.NewGuid().ToString("N"),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ShopIntegrations.Add(shop);
        await db.SaveChangesAsync();

        var simulator = new SimulatorService(db, tenantService, new OrderService(db, tenantService));

        var payload = new WebhookOrderPayloadDto
        {
            ExternalShopId = shop.ExternalShopId,
            ExternalOrderId = "ORD-DEFAULT-TIMESTAMP-01",
            BranchId = branchId,
            OrderTimestamp = default(DateTimeOffset), // Timestamp mặc định không xác định
            Items = new() { new() { SkuCode = skuCode, Quantity = 1, UnitPrice = 100m } }
        };

        // Act: Adapter gọi Ingest với default timestamp
        var exception = await Record.ExceptionAsync(async () =>
        {
            await simulator.IngestWebhookOrderAsync("Shopee", payload, null);
        });

        // Assert: Phải bị từ chối với lỗi VALIDATION_ERROR (400)
        exception.Should().NotBeNull();
        exception.Should().BeOfType<OismException>();
        var oismEx = (OismException)exception!;
        oismEx.ErrorCode.Should().Be("VALIDATION_ERROR");
        oismEx.StatusCode.Should().Be(400);

        // Kiểm tra cơ sở dữ liệu: không lưu đơn, không giữ chỗ
        var persistedOrder = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.ExternalOrderId == payload.ExternalOrderId);
        persistedOrder.Should().BeNull("Đơn hàng có default timestamp không được lưu vào cơ sở dữ liệu");

        var balance = await db.InventoryBalances.AsNoTracking().SingleAsync(b => b.BranchId == branchId && b.Sku!.SKU == skuCode);
        balance.Reserved.Should().Be(0m, "Không phát sinh giữ chỗ tồn kho khi timestamp không hợp lệ");
    }

    [Fact]
    public async Task R2_AC05_CanonicalWebhook_MissingOrDefaultTimestamp_Returns400_DoesNotPersistOrder()
    {
        // Arrange
        var (tenantId, _, branchId, _, _, skuCode, _, _) = await SetupTenantAndStockAsync(10m);
        var tenantService = new TestTenantService { TenantId = tenantId };
        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService);

        var secret = "test-secret-r2-ac05";
        var shop = new ShopIntegration
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            Channel = "Shopee",
            ExternalShopId = "SHOP-TEST-R2-AC05",
            SecretHash = WebhookSecretHasher.HashSecret(secret),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ShopIntegrations.Add(shop);
        await db.SaveChangesAsync();

        _client.DefaultRequestHeaders.Authorization = null;

        // Case 1: Missing timestamp (null)
        var missingReq = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/simulator/Shopee")
        {
            Content = JsonContent.Create(new CanonicalWebhookPayloadDto
            {
                Channel = "Shopee",
                ExternalShopId = shop.ExternalShopId,
                ExternalOrderId = "ORD-CANONICAL-MISSING-TS",
                OrderTimestamp = null,
                TotalAmount = 100m,
                Items = new() { new() { SkuCode = skuCode, Quantity = 1, UnitPrice = 100m } }
            })
        };
        missingReq.Headers.Add("X-Webhook-Secret", secret);
        var missingRes = await _client.SendAsync(missingReq);

        missingRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var missingBody = await missingRes.Content.ReadAsStringAsync();
        missingBody.Should().Contain("VALIDATION_ERROR");

        // Case 2: Default timestamp (default(DateTimeOffset))
        var defaultReq = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/simulator/Shopee")
        {
            Content = JsonContent.Create(new CanonicalWebhookPayloadDto
            {
                Channel = "Shopee",
                ExternalShopId = shop.ExternalShopId,
                ExternalOrderId = "ORD-CANONICAL-DEFAULT-TS",
                OrderTimestamp = default(DateTimeOffset),
                TotalAmount = 100m,
                Items = new() { new() { SkuCode = skuCode, Quantity = 1, UnitPrice = 100m } }
            })
        };
        defaultReq.Headers.Add("X-Webhook-Secret", secret);
        var defaultRes = await _client.SendAsync(defaultReq);

        defaultRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var defaultBody = await defaultRes.Content.ReadAsStringAsync();
        defaultBody.Should().Contain("VALIDATION_ERROR");

        // Kiểm tra cơ sở dữ liệu không có đơn nào
        var count = await db.Orders.CountAsync(x => x.ExternalOrderId != null && x.ExternalOrderId.StartsWith("ORD-CANONICAL-"));
        count.Should().Be(0);
    }

    [Fact]
    public async Task R3_AC01_AC03_SimulatorAdapter_MissingTimestamp_TwoIndependentPayloads_BothRejected400_NoPersistOrMismatch()
    {
        // Arrange
        var (tenantId, _, branchId, _, _, skuCode, _, _) = await SetupTenantAndStockAsync(10m);
        var tenantService = new TestTenantService { TenantId = tenantId };
        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService);

        var shop = new ShopIntegration
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            Channel = "Shopee",
            ExternalShopId = "SHOP-R3-AC01-03",
            SecretHash = WebhookSecretHasher.HashSecret(Guid.NewGuid().ToString("N")),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ShopIntegrations.Add(shop);
        await db.SaveChangesAsync();

        var simulator = new SimulatorService(db, tenantService, new OrderService(db, tenantService));

        WebhookOrderPayloadDto CreateMissingTimestampPayload() => new()
        {
            ExternalShopId = shop.ExternalShopId,
            ExternalOrderId = "SAME-JSON-MISSING-TIME",
            BranchId = branchId,
            OrderTimestamp = null,
            Items = new() { new() { SkuCode = skuCode, Quantity = 1, UnitPrice = 100m } }
        };

        // Act 1: Lần đầu tiên với DTO instance thứ nhất (missing timestamp)
        var firstPayload = CreateMissingTimestampPayload();
        var firstEx = await Record.ExceptionAsync(async () =>
        {
            await simulator.IngestWebhookOrderAsync("Shopee", firstPayload, null);
        });

        // Assert 1: Bị từ chối VALIDATION_ERROR 400, không bị mutate đối số
        firstEx.Should().NotBeNull();
        firstEx.Should().BeOfType<OismException>();
        var firstOism = (OismException)firstEx!;
        firstOism.ErrorCode.Should().Be("VALIDATION_ERROR");
        firstOism.StatusCode.Should().Be(400);
        firstPayload.OrderTimestamp.Should().BeNull("Không được gán/mutate timestamp vào payload đầu vào");

        // Act 2: Bắn lại cùng dữ liệu với DTO instance thứ hai mới hoàn toàn (missing timestamp)
        var secondPayload = CreateMissingTimestampPayload();
        var secondEx = await Record.ExceptionAsync(async () =>
        {
            await simulator.IngestWebhookOrderAsync("Shopee", secondPayload, null);
        });

        // Assert 2: Cả 2 đều bị từ chối 400 VALIDATION_ERROR, tuyệt đối không bị persist lần 1 rồi báo mismatch 409 ở lần 2
        secondEx.Should().NotBeNull();
        secondEx.Should().BeOfType<OismException>();
        var secondOism = (OismException)secondEx!;
        secondOism.ErrorCode.Should().Be("VALIDATION_ERROR");
        secondOism.StatusCode.Should().Be(400);

        // Kiểm tra Canonical Ingress qua HTTP cũng trả 400 BadRequest
        _client.DefaultRequestHeaders.Authorization = null;
        var canonicalRes = await _client.PostAsJsonAsync("/api/webhooks/simulator/Shopee", new CanonicalWebhookPayloadDto
        {
            Channel = "Shopee",
            ExternalShopId = shop.ExternalShopId,
            ExternalOrderId = "CANONICAL-MISSING-TIME-R3",
            OrderTimestamp = null,
            TotalAmount = 100m,
            Items = new() { new() { SkuCode = skuCode, Quantity = 1, UnitPrice = 100m } }
        });
        canonicalRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Kiểm chứng tuyệt đối không có side effect nào trong DB
        var persistedCount = await db.Orders.AsNoTracking().CountAsync(x => x.ExternalOrderId == "SAME-JSON-MISSING-TIME" || x.ExternalOrderId == "CANONICAL-MISSING-TIME-R3");
        persistedCount.Should().Be(0, "Payload thiếu timestamp tuyệt đối không được lưu order vào database");

        var balance = await db.InventoryBalances.AsNoTracking().SingleAsync(b => b.BranchId == branchId && b.Sku!.SKU == skuCode);
        balance.Reserved.Should().Be(0m, "Không phát sinh bất kỳ giữ chỗ tồn kho nào");
    }

    [Fact]
    public async Task R3_AC02_SimulatorAdapter_TwoNewInstances_SameValidTimestamp_IdempotentReplayReturnsSameOrder()
    {
        // Arrange
        var (tenantId, _, branchId, _, _, skuCode, _, _) = await SetupTenantAndStockAsync(10m);
        var tenantService = new TestTenantService { TenantId = tenantId };
        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService);

        var shop = new ShopIntegration
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            Channel = "Shopee",
            ExternalShopId = "SHOP-R3-AC02",
            SecretHash = WebhookSecretHasher.HashSecret(Guid.NewGuid().ToString("N")),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ShopIntegrations.Add(shop);
        await db.SaveChangesAsync();

        var simulator = new SimulatorService(db, tenantService, new OrderService(db, tenantService));
        var eventTimestamp = DateTimeOffset.UtcNow;

        WebhookOrderPayloadDto CreateValidPayload() => new()
        {
            ExternalShopId = shop.ExternalShopId,
            ExternalOrderId = "ORD-IDEMPOTENT-DTO-NEW",
            BranchId = branchId,
            CustomerName = "Khách Hàng Thử Nghiệm",
            CustomerPhone = "0912345678",
            OrderTimestamp = eventTimestamp,
            Items = new() { new() { SkuCode = skuCode, Quantity = 2, UnitPrice = 150m } }
        };

        // Act 1: Lần đầu tiên từ instance 1
        var payloadInstance1 = CreateValidPayload();
        var firstResult = await simulator.IngestWebhookOrderAsync("Shopee", payloadInstance1, null);
        firstResult.Should().NotBeNull();
        firstResult.Status.Should().Be("Reserved");

        // Act 2: Replay cùng dữ liệu nhưng từ instance 2 mới độc lập (mô phỏng deserialize mới từ body JSON)
        var payloadInstance2 = CreateValidPayload();
        var secondResult = await simulator.IngestWebhookOrderAsync("Shopee", payloadInstance2, null);

        // Assert: Trả về chính xác cùng đơn hàng cũ, không lỗi signature mismatch
        secondResult.Should().NotBeNull();
        secondResult.Id.Should().Be(firstResult.Id);
        secondResult.Number.Should().Be(firstResult.Number);

        // Kiểm tra DB: Chỉ có đúng 1 order được lưu, Reserved đúng 2 (không bị double reserve thành 4)
        var orderCount = await db.Orders.CountAsync(o => o.ExternalOrderId == "ORD-IDEMPOTENT-DTO-NEW");
        orderCount.Should().Be(1);

        var balance = await db.InventoryBalances.AsNoTracking().SingleAsync(b => b.BranchId == branchId && b.Sku!.SKU == skuCode);
        balance.Reserved.Should().Be(2m, "Tồn giữ chỗ vẫn là 2, không bị double reserve khi replay qua instance mới");
    }

    [Fact]
    public async Task R3_AC04_SimulatorAdapter_NullItemInList_ThrowsControlledValidationError_NoNullReferenceExceptionOrSideEffect()
    {
        // Arrange
        var (tenantId, _, branchId, _, _, skuCode, _, _) = await SetupTenantAndStockAsync(10m);
        var tenantService = new TestTenantService { TenantId = tenantId };
        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService);

        var secret = "test-secret-r3-ac04";
        var shop = new ShopIntegration
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            Channel = "Shopee",
            ExternalShopId = "SHOP-R3-AC04",
            SecretHash = WebhookSecretHasher.HashSecret(secret),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.ShopIntegrations.Add(shop);
        await db.SaveChangesAsync();

        var simulator = new SimulatorService(db, tenantService, new OrderService(db, tenantService));
        var timestamp = DateTimeOffset.UtcNow;

        // Act 1: Simulator Adapter nhận payload có item mang giá trị null
        var adapterError = await Record.ExceptionAsync(async () =>
        {
            await simulator.IngestWebhookOrderAsync("Shopee", new WebhookOrderPayloadDto
            {
                ExternalShopId = shop.ExternalShopId,
                ExternalOrderId = "NULL-ITEM-ADAPTER-TEST",
                BranchId = branchId,
                OrderTimestamp = timestamp,
                Items = new List<WebhookOrderItemPayloadDto> { null! }
            }, null);
        });

        // Assert 1: Phải ném OismException 400 VALIDATION_ERROR (không phải NullReferenceException 500)
        adapterError.Should().NotBeNull();
        adapterError.Should().BeOfType<OismException>();
        var oismEx = (OismException)adapterError!;
        oismEx.ErrorCode.Should().Be("VALIDATION_ERROR");
        oismEx.StatusCode.Should().Be(400);

        // Act 2: Canonical Ingress nhận payload có item mang giá trị null
        _client.DefaultRequestHeaders.Authorization = null;
        var canonicalReq = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/simulator/Shopee")
        {
            Content = JsonContent.Create(new CanonicalWebhookPayloadDto
            {
                Channel = "Shopee",
                ExternalShopId = shop.ExternalShopId,
                ExternalOrderId = "NULL-ITEM-CANONICAL-TEST",
                OrderTimestamp = timestamp,
                TotalAmount = 100m,
                Items = new List<CanonicalWebhookItemDto> { null! }
            })
        };
        canonicalReq.Headers.Add("X-Webhook-Secret", secret);
        var canonicalRes = await _client.SendAsync(canonicalReq);

        // Assert 2: Canonical Ingress cũng trả 400 BadRequest
        canonicalRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var canonicalBody = await canonicalRes.Content.ReadAsStringAsync();
        canonicalBody.Should().Contain("VALIDATION_ERROR");

        // Kiểm tra cơ sở dữ liệu: không có order nào được persist, không có giữ chỗ tồn kho
        var persistedOrders = await db.Orders.AsNoTracking().CountAsync(x => x.ExternalOrderId == "NULL-ITEM-ADAPTER-TEST" || x.ExternalOrderId == "NULL-ITEM-CANONICAL-TEST");
        persistedOrders.Should().Be(0);

        var balance = await db.InventoryBalances.AsNoTracking().SingleAsync(b => b.BranchId == branchId && b.Sku!.SKU == skuCode);
        balance.Reserved.Should().Be(0m);
    }
}
