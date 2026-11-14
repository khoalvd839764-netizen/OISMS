using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Orders.Services;
using OISM.Domain.Common;
using OISM.Infrastructure.Jobs;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Kiểm thử tích hợp PostgreSQL thật cho Gói 3 Phase A (OISMS-59-G3A):
/// - AC-01: Bước nhảy trạng thái trái phép trả 409 INVALID_STATE_TRANSITION (Cancelled -> Confirmed, xác nhận lần hai).
/// - AC-04: Xác nhận và ReservationExpiryJob chạy song song trên cùng đơn hết hạn (>= 10 lần): đúng một bên thắng, Reserved giảm đúng 1 lần, không có dòng IN bù.
/// - AC-05: Đơn POS nháp do POST /api/inventory/reserve sinh ra hết hạn -> job giải phóng đúng số lượng, đơn sang Cancelled.
/// - AC-06: Đối soát sau vòng đời (tạo -> xác nhận -> hoàn tất; tạo -> hủy): tổng IN - OUT = OnHand; Reserved = tổng reservation Active.
/// </summary>
public class PostgresOrderLifecycleAtomicityTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public PostgresOrderLifecycleAtomicityTests(WebApplicationFactoryWithPostgres factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
        OrderService.ResetCursors();
        ReservationExpiryJob.ResetTenantCursor();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private class TestTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private (AppDbContext Context, TestTenantService TenantService) CreatePostgresDbContext(Guid? tenantId = null)
    {
        var tenantService = new TestTenantService { TenantId = tenantId };
        var context = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService);
        return (context, tenantService);
    }

    private async Task SeedDatabaseAsync()
    {
        var (context, _) = CreatePostgresDbContext(null);
        await using (context)
        {
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);
        }
    }

    private async Task<string> LoginAsAsync(string email, string password)
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = password
        });

        var body = await loginRes.Content.ReadAsStringAsync();
        loginRes.StatusCode.Should().Be(HttpStatusCode.OK, $"Đăng nhập thất bại cho user {email}: {body}");
        var authData = await loginRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        authData.Should().NotBeNull();
        return authData!.AccessToken;
    }

    private async Task<(Guid BranchId, Guid WarehouseId, Guid SkuId, string SkuCode, decimal RetailPrice)> SetupTenantASkuWithStockAsync(decimal stockQty, decimal unitCost = 100000m)
    {
        await SeedDatabaseAsync();

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
        string skuCode;
        decimal retailPrice;

        var (db, tenantService) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            warehouseId = (await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId)).Id;
            supplierId = (await db.Suppliers.FirstAsync(s => s.TenantId == tenantId)).Id;
            userId = (await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local")).Id;
            var sku = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = sku.Id;
            skuCode = sku.SKU;
            retailPrice = sku.RetailPrice;

            await PriceSeed.SyncFromLegacyColumnsAsync(db);
            await PurchaseOrderSeed.ReceiveStockAsync(db, tenantService, branchId, warehouseId, supplierId, userId, (skuId, stockQty, unitCost));
        }

        return (branchId, warehouseId, skuId, skuCode, retailPrice);
    }

    [Fact]
    public async Task AC01_IllegalStateTransitions_ConfirmCancelled_And_DoubleConfirm_ShouldReturn409InvalidStateTransition()
    {
        var (branchId, warehouseId, skuId, skuCode, retailPrice) = await SetupTenantASkuWithStockAsync(10m);
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // 1. Tạo đơn Admin -> Hủy (Cancelled) -> Thử xác nhận đơn Cancelled -> 409 INVALID_STATE_TRANSITION
        var createCancelledRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2m, UnitPrice = retailPrice }
            }
        });
        createCancelledRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var cancelledOrder = await createCancelledRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);

        var cancelRes = await _client.PostAsJsonAsync($"/api/orders/{cancelledOrder!.Id}/cancel", new CancelOrderRequestDto
        {
            Reason = "Hủy trước khi thử xác nhận trái phép"
        });
        cancelRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var confirmCancelledRes = await _client.PostAsJsonAsync($"/api/orders/{cancelledOrder.Id}/confirm", new ConfirmOrderRequestDto());
        confirmCancelledRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var confirmCancelledBody = await confirmCancelledRes.Content.ReadAsStringAsync();
        confirmCancelledBody.Should().Contain("INVALID_STATE_TRANSITION");

        // 2. Tạo đơn Admin -> Xác nhận lần 1 (Confirmed) -> Xác nhận lần 2 -> 409 INVALID_STATE_TRANSITION
        var createConfirmedRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2m, UnitPrice = retailPrice }
            }
        });
        createConfirmedRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var confirmedOrder = await createConfirmedRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);

        var firstConfirmRes = await _client.PostAsJsonAsync($"/api/orders/{confirmedOrder!.Id}/confirm", new ConfirmOrderRequestDto());
        firstConfirmRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondConfirmRes = await _client.PostAsJsonAsync($"/api/orders/{confirmedOrder.Id}/confirm", new ConfirmOrderRequestDto());
        secondConfirmRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var secondConfirmBody = await secondConfirmRes.Content.ReadAsStringAsync();
        secondConfirmBody.Should().Contain("INVALID_STATE_TRANSITION");

        // Đối soát CSDL: OnHand chỉ trừ đúng 2m (10 -> 8), Reserved = 0, sổ OUT chỉ có 1 dòng
        var (verifyDb, _) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
        await using (verifyDb)
        {
            var dbCancelled = await verifyDb.Orders.FirstAsync(o => o.Id == cancelledOrder.Id);
            dbCancelled.Status.Should().Be(OrderStatus.Cancelled);

            var dbConfirmed = await verifyDb.Orders.FirstAsync(o => o.Id == confirmedOrder.Id);
            dbConfirmed.Status.Should().Be(OrderStatus.Confirmed);

            var bBal = await verifyDb.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bBal.OnHand.Should().Be(8m);
            bBal.Reserved.Should().Be(0m);

            var whBal = await verifyDb.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == warehouseId && w.SkuId == skuId);
            whBal.OnHand.Should().Be(8m);
            whBal.Reserved.Should().Be(0m);

            var outRows = await verifyDb.InventoryTransactions.Where(t => t.Type == "OUT" && t.SkuId == skuId).ToListAsync();
            outRows.Should().HaveCount(1);
            outRows[0].Quantity.Should().Be(2m);
        }
    }

    [Fact]
    public async Task AC04_ConcurrentConfirmAndReservationExpiryJob_OnExpiredOrder_TenIterations_ExactlyOneWins()
    {
        const int iterations = 10;
        var (branchId, warehouseId, skuId, skuCode, retailPrice) = await SetupTenantASkuWithStockAsync(30m);
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);

        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();

        int initialInCount;
        var (initDb, _) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
        await using (initDb)
        {
            initialInCount = await initDb.InventoryTransactions.CountAsync(t => t.TenantId == OismsDatabaseSeeder.TenantAId && t.Type == "IN");
        }

        for (var i = 0; i < iterations; i++)
        {
            OrderService.ResetCursors();
            ReservationExpiryJob.ResetTenantCursor();

            using var setupClient = _factory.CreateClient();
            setupClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

            var createRes = await setupClient.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
            {
                BranchId = branchId,
                Channel = "Admin",
                RequestKey = Guid.NewGuid(),
                Items = new List<OrderItemRequestDto>
                {
                    new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2m, UnitPrice = retailPrice }
                }
            });
            createRes.StatusCode.Should().Be(HttpStatusCode.Created, $"Iteration {i}: tạo đơn thất bại");
            var createdOrder = await createRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
            createdOrder.Should().NotBeNull();

            // Đưa phiếu giữ chỗ về trạng thái đã hết hạn (giữ nguyên ràng buộc expires_at > created_at)
            var now = DateTimeOffset.UtcNow;
            var (expireDb, _) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
            await using (expireDb)
            {
                await expireDb.Database.ExecuteSqlRawAsync(
                    "UPDATE reservations SET created_at = {0}, expires_at = {1} WHERE order_id = {2};",
                    now.AddMinutes(-30),
                    now.AddMinutes(-5),
                    createdOrder!.Id);
            }

            // Chạy song song thật sự giữa Confirm HTTP và ReservationExpiryJob.ExecuteAsync
            var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            using var confirmClient = _factory.CreateClient();
            confirmClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

            var confirmTask = Task.Run(async () =>
            {
                await startGate.Task;
                return await confirmClient.PostAsJsonAsync($"/api/orders/{createdOrder!.Id}/confirm", new ConfirmOrderRequestDto());
            });

            var expiryTask = Task.Run(async () =>
            {
                await startGate.Task;
                var job = new ReservationExpiryJob(scopeFactory, NullLogger<ReservationExpiryJob>.Instance);
                return await job.ExecuteAsync(DateTimeOffset.UtcNow);
            });

            startGate.SetResult();
            await Task.WhenAll(confirmTask, expiryTask);

            var confirmRes = await confirmTask;
            var expiryCancelledCount = await expiryTask;

            var confirmWon = confirmRes.StatusCode == HttpStatusCode.OK ? 1 : 0;
            var expiryWon = expiryCancelledCount > 0 ? 1 : 0;

            (confirmWon + expiryWon).Should().Be(1, $"Iteration {i}: phải có đúng một bên thắng khi Confirm và ReservationExpiryJob chạy song song trên đơn đã hết hạn.");
            confirmRes.StatusCode.Should().Be(HttpStatusCode.Conflict, $"Iteration {i}: Confirm trên đơn đã hết hạn phải bị từ chối 409.");
            var confirmErrBody = await confirmRes.Content.ReadAsStringAsync();
            (confirmErrBody.Contains("RESERVATION_EXPIRED", StringComparison.Ordinal) ||
             confirmErrBody.Contains("INVALID_STATE_TRANSITION", StringComparison.Ordinal))
                .Should().BeTrue($"Iteration {i}: mã lỗi trả về phải là RESERVATION_EXPIRED hoặc INVALID_STATE_TRANSITION nhưng nhận được: {confirmErrBody}");

            var (verifyDb, _) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
            await using (verifyDb)
            {
                var dbOrder = await verifyDb.Orders.Include(o => o.Reservations).FirstAsync(o => o.Id == createdOrder!.Id);
                dbOrder.Status.Should().Be(OrderStatus.Cancelled, $"Iteration {i}: đơn hết hạn phải chuyển sang Cancelled.");
                dbOrder.Reservations.Should().AllSatisfy(r => r.Status.Should().Be(ReservationStatus.Expired));

                var bBal = await verifyDb.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
                bBal.OnHand.Should().Be(30m, $"Iteration {i}: OnHand không đổi.");
                bBal.Reserved.Should().Be(0m, $"Iteration {i}: Reserved phải giảm đúng một lần về 0.");

                var whBal = await verifyDb.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == warehouseId && w.SkuId == skuId);
                whBal.OnHand.Should().Be(30m);
                whBal.Reserved.Should().Be(0m);

                var currentInCount = await verifyDb.InventoryTransactions.CountAsync(t => t.TenantId == OismsDatabaseSeeder.TenantAId && t.Type == "IN");
                currentInCount.Should().Be(initialInCount, $"Iteration {i}: hủy/hết hạn tuyệt đối không được ghi dòng IN bù.");

                var outCount = await verifyDb.InventoryTransactions.CountAsync(t => t.TenantId == OismsDatabaseSeeder.TenantAId && t.Type == "OUT");
                outCount.Should().Be(0, $"Iteration {i}: không có bút toán OUT.");
            }
        }
    }

    [Fact]
    public async Task AC05_PosDraftFromReserveApi_Expires_JobReleasesStockAndCancelsOrder()
    {
        var (branchId, warehouseId, skuId, _, _) = await SetupTenantASkuWithStockAsync(10m);
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // 1. Gọi POST /api/inventory/reserve (luồng giữ chỗ kỹ thuật POS -> tạo Order POS ở trạng thái Draft)
        var reserveRes = await _client.PostAsJsonAsync("/api/inventory/reserve", new ReserveStockRequestDto
        {
            BranchId = branchId,
            WarehouseId = warehouseId,
            SkuId = skuId,
            Quantity = 3m,
            DurationMinutes = 15,
            RequestKey = Guid.NewGuid()
        });
        var reserveBody = await reserveRes.Content.ReadAsStringAsync();
        reserveRes.StatusCode.Should().Be(HttpStatusCode.OK, $"POST /api/inventory/reserve thất bại: {reserveBody}");

        var reserveDto = await reserveRes.Content.ReadFromJsonAsync<ReserveStockResponseDto>(_jsonOptions);
        reserveDto.Should().NotBeNull();
        reserveDto!.Status.Should().Be(ReservationStatus.Active);
        reserveDto.Quantity.Should().Be(3m);
        reserveDto.RemainingAvailableStock.Should().Be(7m);

        int txCountBeforeExpiry;
        var (preDb, _) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
        await using (preDb)
        {
            var draftOrder = await preDb.Orders.FirstAsync(o => o.Id == reserveDto.OrderId);
            draftOrder.Channel.Should().Be("POS");
            draftOrder.Status.Should().Be(OrderStatus.Draft, "Đơn giữ chỗ kỹ thuật POS phải giữ nguyên Draft để Order.CanCancel cho phép hết hạn.");

            var bBalBefore = await preDb.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bBalBefore.Reserved.Should().Be(3m);

            txCountBeforeExpiry = await preDb.InventoryTransactions.CountAsync(t => t.TenantId == OismsDatabaseSeeder.TenantAId);

            // 2. Đặt phiếu giữ chỗ quá hạn
            var now = DateTimeOffset.UtcNow;
            await preDb.Database.ExecuteSqlRawAsync(
                "UPDATE reservations SET created_at = {0}, expires_at = {1} WHERE id = {2};",
                now.AddMinutes(-30),
                now.AddMinutes(-5),
                reserveDto.ReservationId);
        }

        // 3. Chạy ReservationExpiryJob
        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        var job = new ReservationExpiryJob(scopeFactory, NullLogger<ReservationExpiryJob>.Instance);
        var cancelledCount = await job.ExecuteAsync(DateTimeOffset.UtcNow);
        cancelledCount.Should().Be(1);

        // 4. Đối soát DB: giải phóng đúng 3m, đơn sang Cancelled, không ghi thêm dòng sổ kho
        var (verifyDb, _) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
        await using (verifyDb)
        {
            var updatedOrder = await verifyDb.Orders.Include(o => o.Reservations).FirstAsync(o => o.Id == reserveDto.OrderId);
            updatedOrder.Status.Should().Be(OrderStatus.Cancelled);
            updatedOrder.CancelledAt.Should().NotBeNull();
            updatedOrder.Reservations.Should().ContainSingle(r => r.Id == reserveDto.ReservationId && r.Status == ReservationStatus.Expired);

            var bBalAfter = await verifyDb.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bBalAfter.OnHand.Should().Be(10m);
            bBalAfter.Reserved.Should().Be(0m);

            var whBalAfter = await verifyDb.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == warehouseId && w.SkuId == skuId);
            whBalAfter.OnHand.Should().Be(10m);
            whBalAfter.Reserved.Should().Be(0m);

            var txCountAfterExpiry = await verifyDb.InventoryTransactions.CountAsync(t => t.TenantId == OismsDatabaseSeeder.TenantAId);
            txCountAfterExpiry.Should().Be(txCountBeforeExpiry, "Giải phóng hết hạn không được ghi thêm bút toán sổ kho.");
        }
    }

    [Fact]
    public async Task AC06_ReconciliationAfterLifecycles_LedgerNetEqualsOnHand_AndReservedEqualsActiveSum()
    {
        var (branchId, warehouseId, skuId, skuCode, retailPrice) = await SetupTenantASkuWithStockAsync(10m, 100000m);
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // Vòng đời 1: Tạo -> Xác nhận -> Hoàn tất (2 đơn vị)
        var order1Res = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2m, UnitPrice = retailPrice }
            }
        });
        order1Res.StatusCode.Should().Be(HttpStatusCode.Created);
        var order1 = await order1Res.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);

        (await _client.PostAsJsonAsync($"/api/orders/{order1!.Id}/confirm", new ConfirmOrderRequestDto())).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.PostAsJsonAsync($"/api/orders/{order1.Id}/complete", new CompleteOrderRequestDto())).StatusCode.Should().Be(HttpStatusCode.OK);

        // Vòng đời 2: Tạo -> Hủy (3 đơn vị)
        var order2Res = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 3m, UnitPrice = retailPrice }
            }
        });
        order2Res.StatusCode.Should().Be(HttpStatusCode.Created);
        var order2 = await order2Res.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);

        (await _client.PostAsJsonAsync($"/api/orders/{order2!.Id}/cancel", new CancelOrderRequestDto { Reason = "Đối soát vòng đời hủy" })).StatusCode.Should().Be(HttpStatusCode.OK);

        // Đơn 3 giữ trạng thái Reserved (1 đơn vị) để kiểm chứng Reserved == SUM(Active) khi Active > 0
        var order3Res = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1m, UnitPrice = retailPrice }
            }
        });
        order3Res.StatusCode.Should().Be(HttpStatusCode.Created);
        var order3 = await order3Res.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);

        // Đối soát khi có cả đơn Completed, Cancelled và Reserved Active
        await AssertLedgerAndReservationReconciliationAsync(branchId, warehouseId, skuId, expectedOnHand: 8m, expectedReserved: 1m);

        // Hủy tiếp đơn 3 và đối soát lại khi Active == 0
        (await _client.PostAsJsonAsync($"/api/orders/{order3!.Id}/cancel", new CancelOrderRequestDto { Reason = "Giải phóng nốt đơn còn lại" })).StatusCode.Should().Be(HttpStatusCode.OK);

        await AssertLedgerAndReservationReconciliationAsync(branchId, warehouseId, skuId, expectedOnHand: 8m, expectedReserved: 0m);
    }

    private async Task AssertLedgerAndReservationReconciliationAsync(
        Guid branchId,
        Guid warehouseId,
        Guid skuId,
        decimal expectedOnHand,
        decimal expectedReserved)
    {
        var (verifyDb, _) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
        await using (verifyDb)
        {
            var ledgerRows = await verifyDb.InventoryTransactions
                .Where(t => t.TenantId == OismsDatabaseSeeder.TenantAId && t.BranchId == branchId && t.SkuId == skuId)
                .ToListAsync();

            var totalIn = ledgerRows.Where(t => t.Type == "IN").Sum(t => t.Quantity);
            var totalOut = ledgerRows.Where(t => t.Type == "OUT").Sum(t => t.Quantity);
            var ledgerNet = totalIn - totalOut;

            var bBal = await verifyDb.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            var whBal = await verifyDb.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == warehouseId && w.SkuId == skuId);

            ledgerNet.Should().Be(expectedOnHand);
            bBal.OnHand.Should().Be(ledgerNet, "Tổng IN - OUT trên sổ phải bằng OnHand chi nhánh.");
            whBal.OnHand.Should().Be(ledgerNet, "Tổng IN - OUT trên sổ phải bằng OnHand kho.");

            var activeBranchReservedSum = await verifyDb.Reservations
                .Where(r => r.TenantId == OismsDatabaseSeeder.TenantAId && r.BranchId == branchId && r.SkuId == skuId && r.Status == ReservationStatus.Active)
                .SumAsync(r => r.Quantity);

            var activeWarehouseReservedSum = await verifyDb.Reservations
                .Where(r => r.TenantId == OismsDatabaseSeeder.TenantAId && r.WarehouseId == warehouseId && r.SkuId == skuId && r.Status == ReservationStatus.Active)
                .SumAsync(r => r.Quantity);

            bBal.Reserved.Should().Be(expectedReserved);
            bBal.Reserved.Should().Be(activeBranchReservedSum, "Reserved chi nhánh phải bằng tổng reservation Active.");
            whBal.Reserved.Should().Be(activeWarehouseReservedSum, "Reserved kho phải bằng tổng reservation Active.");
            (bBal.OnHand - bBal.Reserved).Should().BeGreaterThanOrEqualTo(0m, "Available không được âm.");
        }
    }
}
