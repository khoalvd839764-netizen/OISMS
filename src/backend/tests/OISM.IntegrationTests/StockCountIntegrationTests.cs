using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Inventory.Services;
using OISM.Application.Features.StockCounts.Dtos;
using OISM.Application.Features.StockCounts.Services;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Bộ kiểm thử tích hợp thực thi trên cơ sở dữ liệu PostgreSQL 16+ thật (database: oisms_test).
/// Kiểm chứng toàn diện chức năng Kiểm kê kho và Cân bằng tồn (Task S3-BE1-T04B-STOCK-COUNT / v1.0):
/// AC-01: Chụp tồn sổ sách và tạo Draft (POST /api/stock-counts, 201 Created, snapshot on_hand & sequence, zero stock mutation).
/// AC-02: Idempotency và Replay theo RequestKey (replay 200 OK, payload mismatch 409, replay across status changes, multi-tenant, 20 concurrent unique numbers).
/// AC-03: Ghi nhận số lượng đếm (POST /api/stock-counts/{id}/record, Draft -> Counting, CountedQuantity = 0 vs omitted, invalid SKU 400, atomic batch, incomplete 400).
/// AC-04: Confirm và Sinh chứng từ điều chỉnh (POST /api/stock-counts/{id}/confirm, single adjustment ADJ-yyyyMMdd-XXXXX, surplus IN, deficit OUT, match no-op, all match 0 items).
/// AC-05: Phát hiện giao dịch xen giữa qua Sequence Drift (intervening receipt 409 drift, stock fluctuates & returns 409, other warehouse no false drift, unrelated SKU no false drift).
/// AC-06: WAC và Bảo vệ hàng giữ chỗ (branch WAC, client cannot override existing WAC, surplus WAC=0 requires positive cost 422, deficit preserves WAC, active reservations protected 409).
/// AC-07: State Machine và Cancel an toàn (cancel Draft/Counting, 409 cancel Confirmed/Cancelled, 409 confirm Draft/Cancelled, idempotent confirm replay).
/// AC-08: Concurrency & Deadlock Freedom (50 concurrent confirms, race confirm vs cancel, 2 counts competing for same SKU, zero oversell).
/// AC-09: Fault Injection & Atomic Rollback (Midstream interceptor rollback 100%, retry thành công cho Create, Record, Confirm).
/// AC-10: Phân quyền RBAC (401 unauth, 403 Cashier), Cross-tenant 404, Kiểm tra biên số lượng (null items, scale >3, magnitude, warehouse-branch mismatch).
/// AC-11: Tương thích ngược schema & migration (Cài mới, UpgradeDatabaseSchemaAsync idempotent, phát hiện schema drift, legacy stock count).
/// </summary>
public class StockCountIntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public StockCountIntegrationTests(WebApplicationFactoryWithPostgres factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private class TestTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    public class MidstreamStockCountFaultException : Exception
    {
        public MidstreamStockCountFaultException(string message) : base(message) { }
    }

    public class MidstreamFailureInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public bool ShouldFail { get; set; } = true;
        public int SaveChangesCalls { get; private set; }
        public int LastSavedEntityCount { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            SaveChangesCalls++;
            LastSavedEntityCount = eventData.EntitiesSavedCount;
            if (ShouldFail)
            {
                throw new MidstreamStockCountFaultException("Mô phỏng sự cố mạng/crash sau khi SaveChanges nhưng trước khi COMMIT transaction kiểm kê.");
            }
            return base.SavedChangesAsync(eventData, result, cancellationToken);
        }
    }

    public class LockCoordinationInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public bool PauseFirstSku { get; init; }
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _paused;
        public int Deadlocks;

        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var sql = command.CommandText;
            if (PauseFirstSku && sql.Contains("pg_advisory_xact_lock") && sql.Contains("'/'") && Interlocked.CompareExchange(ref _paused, 1, 0) == 0)
            {
                Reached.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            else if (!PauseFirstSku && sql.Contains("/request_key/"))
            {
                Reached.TrySetResult();
            }
            return result;
        }

        public override Task CommandFailedAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Exception is PostgresException { SqlState: "40P01" })
            {
                Interlocked.Increment(ref Deadlocks);
            }
            return Task.CompletedTask;
        }
    }

    public sealed class CountSavedGate : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> SavedChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            Reached.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return result;
        }
    }

    private static async Task<long> GetDatabaseDeadlockCountAsync()
    {
        await using var conn = new NpgsqlConnection(PostgresTestEnvironment.RuntimeConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT deadlocks FROM pg_stat_database WHERE datname=current_database()", conn);
        var result = await cmd.ExecuteScalarAsync();
        return result is long l ? l : Convert.ToInt64(result);
    }

    private record FullStockCountBaseline(
        int StockCountTotal,
        int StockCountItemTotal,
        int AdjustmentTotal,
        int AdjustmentItemTotal,
        int LedgerAdjustmentTxTotal,
        decimal BranchOnHand,
        decimal BranchReserved,
        decimal BranchWac,
        long BranchLastSequence,
        DateTimeOffset BranchCreatedAt,
        DateTimeOffset? BranchUpdatedAt,
        decimal WhOnHand,
        decimal WhReserved,
        long WhLastSequence,
        DateTimeOffset WhCreatedAt,
        DateTimeOffset? WhUpdatedAt,
        string? CountStatus,
        Guid? CountGeneratedAdjustmentId,
        DateTimeOffset? CountConfirmedAt,
        Guid? CountConfirmedBy,
        DateTimeOffset? CountCreatedAt,
        DateTimeOffset? CountUpdatedAt,
        decimal? ItemExpectedQuantity,
        long? ItemSnapshotSequence,
        decimal? ItemCountedQuantity,
        decimal? ItemAdjustmentUnitCost,
        DateTimeOffset? ItemCreatedAt,
        DateTimeOffset? ItemUpdatedAt
    );

    private async Task<FullStockCountBaseline> CaptureFullBaselineAsync(Guid tenantId, Guid branchId, Guid warehouseId, Guid skuId, Guid? countId)
    {
        var (verifyCtx, _) = CreatePostgresDbContext(tenantId);
        await using (verifyCtx)
        {
            var countTotal = await verifyCtx.StockCounts.CountAsync();
            var itemTotal = await verifyCtx.StockCountItems.CountAsync();
            var adjTotal = await verifyCtx.InventoryAdjustments.CountAsync();
            var adjItemTotal = await verifyCtx.InventoryAdjustmentItems.CountAsync();
            var ledgerAdjTxTotal = await verifyCtx.InventoryTransactions.CountAsync(t => t.AdjustmentItemId.HasValue);

            var branchBal = await verifyCtx.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            var whBal = await verifyCtx.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == warehouseId && w.SkuId == skuId);

            StockCount? count = null;
            StockCountItem? item = null;
            if (countId.HasValue)
            {
                count = await verifyCtx.StockCounts.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == countId.Value);
                item = count?.Items.FirstOrDefault(i => i.SkuId == skuId);
            }

            return new FullStockCountBaseline(
                countTotal,
                itemTotal,
                adjTotal,
                adjItemTotal,
                ledgerAdjTxTotal,
                branchBal.OnHand,
                branchBal.Reserved,
                branchBal.AverageCost,
                branchBal.LastSequence,
                branchBal.CreatedAt,
                branchBal.UpdatedAt,
                whBal.OnHand,
                whBal.Reserved,
                whBal.LastSequence,
                whBal.CreatedAt,
                whBal.UpdatedAt,
                count?.Status,
                count?.GeneratedAdjustmentId,
                count?.ConfirmedAt,
                count?.ConfirmedBy,
                count?.CreatedAt,
                count?.UpdatedAt,
                item?.ExpectedQuantity,
                item?.BalanceSequenceAtSnapshot,
                item?.CountedQuantity,
                item?.AdjustmentUnitCost,
                item?.CreatedAt,
                item?.UpdatedAt
            );
        }
    }

    private async Task AssertMatchesBaselineAsync(Guid tenantId, Guid branchId, Guid warehouseId, Guid skuId, Guid? countId, FullStockCountBaseline baseline, string faultPhase)
    {
        var current = await CaptureFullBaselineAsync(tenantId, branchId, warehouseId, skuId, countId);
        current.StockCountTotal.Should().Be(baseline.StockCountTotal, $"[{faultPhase}] StockCounts count must match baseline after rollback.");
        current.StockCountItemTotal.Should().Be(baseline.StockCountItemTotal, $"[{faultPhase}] StockCountItems count must match baseline after rollback.");
        current.AdjustmentTotal.Should().Be(baseline.AdjustmentTotal, $"[{faultPhase}] Adjustments count must match baseline after rollback.");
        current.AdjustmentItemTotal.Should().Be(baseline.AdjustmentItemTotal, $"[{faultPhase}] AdjustmentItems count must match baseline after rollback.");
        current.LedgerAdjustmentTxTotal.Should().Be(baseline.LedgerAdjustmentTxTotal, $"[{faultPhase}] Ledger transactions must match baseline after rollback.");

        current.BranchOnHand.Should().Be(baseline.BranchOnHand, $"[{faultPhase}] Branch OnHand must match baseline after rollback.");
        current.BranchReserved.Should().Be(baseline.BranchReserved, $"[{faultPhase}] Branch Reserved must match baseline after rollback.");
        current.BranchWac.Should().Be(baseline.BranchWac, $"[{faultPhase}] Branch WAC must match baseline after rollback.");
        current.BranchLastSequence.Should().Be(baseline.BranchLastSequence, $"[{faultPhase}] Branch LastSequence must match baseline after rollback.");
        current.BranchCreatedAt.Should().Be(baseline.BranchCreatedAt, $"[{faultPhase}] Branch CreatedAt must match baseline after rollback.");
        current.BranchUpdatedAt.Should().Be(baseline.BranchUpdatedAt, $"[{faultPhase}] Branch UpdatedAt must match baseline after rollback.");

        current.WhOnHand.Should().Be(baseline.WhOnHand, $"[{faultPhase}] Warehouse OnHand must match baseline after rollback.");
        current.WhReserved.Should().Be(baseline.WhReserved, $"[{faultPhase}] Warehouse Reserved must match baseline after rollback.");
        current.WhLastSequence.Should().Be(baseline.WhLastSequence, $"[{faultPhase}] Warehouse LastSequence must match baseline after rollback.");
        current.WhCreatedAt.Should().Be(baseline.WhCreatedAt, $"[{faultPhase}] Warehouse CreatedAt must match baseline after rollback.");
        current.WhUpdatedAt.Should().Be(baseline.WhUpdatedAt, $"[{faultPhase}] Warehouse UpdatedAt must match baseline after rollback.");

        current.CountStatus.Should().Be(baseline.CountStatus, $"[{faultPhase}] Count Status must match baseline after rollback.");
        current.CountGeneratedAdjustmentId.Should().Be(baseline.CountGeneratedAdjustmentId, $"[{faultPhase}] GeneratedAdjustmentId must match baseline after rollback.");
        current.CountConfirmedAt.Should().Be(baseline.CountConfirmedAt, $"[{faultPhase}] ConfirmedAt must match baseline after rollback.");
        current.CountConfirmedBy.Should().Be(baseline.CountConfirmedBy, $"[{faultPhase}] ConfirmedBy must match baseline after rollback.");
        current.CountCreatedAt.Should().Be(baseline.CountCreatedAt, $"[{faultPhase}] Count CreatedAt must match baseline after rollback.");
        current.CountUpdatedAt.Should().Be(baseline.CountUpdatedAt, $"[{faultPhase}] Count UpdatedAt must match baseline after rollback.");

        current.ItemExpectedQuantity.Should().Be(baseline.ItemExpectedQuantity, $"[{faultPhase}] Item ExpectedQuantity must match baseline after rollback.");
        current.ItemSnapshotSequence.Should().Be(baseline.ItemSnapshotSequence, $"[{faultPhase}] Item BalanceSequenceAtSnapshot must match baseline after rollback.");
        current.ItemCountedQuantity.Should().Be(baseline.ItemCountedQuantity, $"[{faultPhase}] Item CountedQuantity must match baseline after rollback.");
        current.ItemAdjustmentUnitCost.Should().Be(baseline.ItemAdjustmentUnitCost, $"[{faultPhase}] Item AdjustmentUnitCost must match baseline after rollback.");
        current.ItemCreatedAt.Should().Be(baseline.ItemCreatedAt, $"[{faultPhase}] Item CreatedAt must match baseline after rollback.");
        current.ItemUpdatedAt.Should().Be(baseline.ItemUpdatedAt, $"[{faultPhase}] Item UpdatedAt must match baseline after rollback.");
    }

    private (AppDbContext Context, TestTenantService TenantService) CreatePostgresDbContext(Guid? tenantId = null, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        var tenantService = new TestTenantService();
        if (tenantId.HasValue)
        {
            tenantService.SetTenant(tenantId.Value);
        }
        var context = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService, interceptors);
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
        authData!.AccessToken.Should().NotBeNullOrEmpty();
        return authData.AccessToken;
    }

    private record TestInfrastructure(
        Guid TenantId,
        Guid BranchAId,
        Guid WarehouseA1Id,
        Guid WarehouseA2Id,
        Guid BranchBId,
        Guid WarehouseB1Id,
        Guid SupplierId,
        Guid UserId,
        Guid Sku1Id,
        string Sku1Code,
        Guid Sku2Id,
        string Sku2Code,
        Guid Sku3Id,
        string Sku3Code);

    private async Task<TestInfrastructure> SetupInfrastructureAsync()
    {
        await SeedDatabaseAsync();

        var (context, tenantService) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
        await using (context)
        {
            var tenantId = OismsDatabaseSeeder.TenantAId;
            var branchA = await context.Branches.FirstAsync(b => b.TenantId == tenantId);
            var warehouseA1 = await context.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchA.Id);

            var warehouseA2 = await context.Warehouses.FirstOrDefaultAsync(w => w.TenantId == tenantId && w.Name == "Kho Phụ Quận 1");
            if (warehouseA2 == null)
            {
                warehouseA2 = new Warehouse
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    BranchId = branchA.Id,
                    Name = "Kho Phụ Quận 1",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                context.Warehouses.Add(warehouseA2);
            }

            var branchB = await context.Branches.FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Name == "Chi nhánh Quận 3");
            if (branchB == null)
            {
                branchB = new Branch
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Name = "Chi nhánh Quận 3",
                    Address = "456 CMT8, Quận 3, TP.HCM",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                context.Branches.Add(branchB);
            }

            var warehouseB1 = await context.Warehouses.FirstOrDefaultAsync(w => w.TenantId == tenantId && w.BranchId == branchB.Id);
            if (warehouseB1 == null)
            {
                warehouseB1 = new Warehouse
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    BranchId = branchB.Id,
                    Name = "Kho Tổng Quận 3",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                context.Warehouses.Add(warehouseB1);
            }

            var supplier = await context.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            var user = await context.Users.FirstAsync(u => u.TenantId == tenantId && u.Role == "Owner");

            var sku1 = await context.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            var sku2 = await context.ProductVariants.FirstOrDefaultAsync(v => v.TenantId == tenantId && v.SKU == "POLO-NAM-TRANG-L");
            if (sku2 == null)
            {
                sku2 = new ProductVariant
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ProductId = sku1.ProductId,
                    Code = "POLO-NAM-TRANG-L",
                    SKU = "POLO-NAM-TRANG-L",
                    RetailPrice = 350000m,
                    WholesalePrice = 250000m,
                    Unit = "piece",
                    IsActive = true
                };
                context.ProductVariants.Add(sku2);
            }

            var sku3 = await context.ProductVariants.FirstOrDefaultAsync(v => v.TenantId == tenantId && v.SKU == "POLO-NAM-DEN-XL");
            if (sku3 == null)
            {
                sku3 = new ProductVariant
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ProductId = sku1.ProductId,
                    Code = "POLO-NAM-DEN-XL",
                    SKU = "POLO-NAM-DEN-XL",
                    RetailPrice = 380000m,
                    WholesalePrice = 260000m,
                    Unit = "piece",
                    IsActive = true
                };
                context.ProductVariants.Add(sku3);
            }

            await context.SaveChangesAsync();

            return new TestInfrastructure(
                tenantId,
                branchA.Id,
                warehouseA1.Id,
                warehouseA2.Id,
                branchB.Id,
                warehouseB1.Id,
                supplier.Id,
                user.Id,
                sku1.Id,
                sku1.SKU,
                sku2.Id,
                sku2.SKU,
                sku3.Id,
                sku3.SKU);
        }
    }

    private async Task SeedStockAsync(Guid tenantId, Guid branchId, Guid warehouseId, Guid supplierId, Guid userId, Guid skuId, decimal quantity, decimal unitPrice)
    {
        var (context, tenantService) = CreatePostgresDbContext(tenantId);
        await using (context)
        {
            var wacCalc = new WacCalculator();
            var service = new GoodsReceiptService(context, tenantService, wacCalc);

            var receiptDto = new CreateGoodsReceiptDto
            {
                BranchId = branchId,
                WarehouseId = warehouseId,
                SupplierId = supplierId,
                Note = "Seed tồn kho cho stock count test",
                Items = new List<CreateGoodsReceiptItemDto>
                {
                    new() { SkuId = skuId, Quantity = quantity, UnitPrice = unitPrice }
                }
            };

            var receipt = await service.CreateReceiptAsync(receiptDto, userId);
            await service.ConfirmReceiptAsync(receipt.Id, userId);
        }
    }

    // =========================================================================
    // AC-01: Chụp tồn sổ sách và tạo Draft (POST /api/stock-counts)
    // =========================================================================

    [Fact]
    public async Task AC01_CreateDraft_SelectedSkus_Returns201Created_ZeroStockMutation()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 20m, 50000m);

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var requestKey = Guid.NewGuid();
        var requestDto = new CreateStockCountRequestDto
        {
            RequestKey = requestKey,
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            Note = "Kiểm kê định kỳ tháng 10 AC-01",
            SkuIds = new List<Guid> { infra.Sku1Id, infra.Sku2Id }
        };

        var response = await _client.PostAsJsonAsync("/api/stock-counts", requestDto);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, $"Tạo draft stock count thất bại: {body}");

        var count = await response.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
        count.Should().NotBeNull();
        count!.Id.Should().NotBeEmpty();
        count.Number.Should().StartWith($"CNT-{DateTimeOffset.UtcNow:yyyyMMdd}-");
        count.Status.Should().Be("Draft");
        count.IsReplay.Should().BeFalse();
        count.Items.Should().HaveCount(2);

        // SKU1 có tồn 20, seq > 0
        var item1 = count.Items.First(i => i.SkuId == infra.Sku1Id);
        item1.ExpectedQuantity.Should().Be(20m);
        item1.CountedQuantity.Should().BeNull();
        item1.BalanceSequenceAtSnapshot.Should().BeGreaterThan(0L);

        // SKU2 chưa từng có số dư -> snapshot 0, seq 0
        var item2 = count.Items.First(i => i.SkuId == infra.Sku2Id);
        item2.ExpectedQuantity.Should().Be(0m);
        item2.CountedQuantity.Should().BeNull();
        item2.BalanceSequenceAtSnapshot.Should().Be(0L);

        // Zero mutation: tồn kho, ledger, sequence không hề thay đổi sau khi tạo draft
        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var bal1 = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            bal1.OnHand.Should().Be(20m);
            bal1.Reserved.Should().Be(0m);
            bal1.AverageCost.Should().Be(50000m);

            var wBal1 = await verifyContext.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == infra.Sku1Id);
            wBal1.OnHand.Should().Be(20m);
            wBal1.Reserved.Should().Be(0m);

            var ledgerCount = await verifyContext.InventoryTransactions.CountAsync(tx => tx.TenantId == infra.TenantId && tx.SkuId == infra.Sku1Id);
            ledgerCount.Should().Be(1, "Chỉ có đúng 1 bút toán ledger từ SeedStock ban đầu, tạo draft không phát sinh ledger.");
        }
    }

    [Fact]
    public async Task AC01_CreateDraft_AllMode_SnapshotsAllActiveWarehouseSkus()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 15m, 60000m);
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku2Id, 10m, 80000m);

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // Chế độ ALL: SkuIds = null
        var requestDto = new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            Note = "Kiểm kê toàn kho",
            SkuIds = null
        };

        var response = await _client.PostAsJsonAsync("/api/stock-counts", requestDto);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var count = await response.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
        count.Should().NotBeNull();
        count!.Items.Should().Contain(i => i.SkuId == infra.Sku1Id && i.ExpectedQuantity == 15m);
        count.Items.Should().Contain(i => i.SkuId == infra.Sku2Id && i.ExpectedQuantity == 10m);
    }

    [Fact]
    public async Task AC01_CreateDraft_AllMode_EmptyWarehouse_Returns400BadRequest()
    {
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // Kho A2 hoàn toàn chưa có mặt hàng/số dư nào
        var requestDto = new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA2Id,
            Note = "Kiểm kê kho trống toàn diện",
            SkuIds = new List<Guid>() // Rỗng -> ALL mode
        };

        var response = await _client.PostAsJsonAsync("/api/stock-counts", requestDto);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("EMPTY_WAREHOUSE_STOCK_COUNT");
    }

    [Fact]
    public async Task AC01_CreateDraft_AllMode_InterleavedReceiptBetweenDiscoveryAndLock_SnapshotsFreshBalances()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 2m, 50000m);

        var gate = new LockCoordinationInterceptor { PauseFirstSku = true };
        var (context, tenantService) = CreatePostgresDbContext(infra.TenantId, gate);
        await using (context)
        {
            var svc = new StockCountService(context, tenantService, new WacCalculator(), Microsoft.Extensions.Logging.Abstractions.NullLogger<StockCountService>.Instance);
            var createTask = svc.CreateStockCountAsync(new CreateStockCountRequestDto
            {
                RequestKey = Guid.NewGuid(),
                BranchId = infra.BranchAId,
                WarehouseId = infra.WarehouseA1Id,
                Note = "ALL mode interleaved receipt test",
                SkuIds = null
            }, infra.UserId);

            // Chờ tác vụ chạm gate trước khi lấy khóa SKU 1
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));

            // Điều phối: Giao dịch nhập xen giữa vào Kho A1 cho SKU 1 (OnHand: 2 -> 5, Seq: 1 -> 2)
            try
            {
                await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 3m, 50000m);
            }
            finally
            {
                gate.Release.TrySetResult();
            }

            var result = await createTask;

            // Đối soát: Snapshot phải đọc fresh dưới khóa, ExpectedQuantity = 5 và Seq = sequence thực tế mới
            await using var verifyContext = CreatePostgresDbContext(infra.TenantId).Context;
            var actualWh = await verifyContext.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == infra.Sku1Id);
            var snapItem = result.Items.First(i => i.SkuId == infra.Sku1Id);

            snapItem.ExpectedQuantity.Should().Be(actualWh.OnHand, "Snapshot ALL mode phải đọc fresh số dư thực tế dưới khóa.");
            snapItem.BalanceSequenceAtSnapshot.Should().Be(actualWh.LastSequence, "Snapshot ALL mode phải đọc fresh sequence dưới khóa.");
        }
    }

    [Fact]
    public async Task AC01_CreateDraft_AllMode_NewSkuIntroducedBetweenDiscoveryAndLock_ReconcilesLocks_SnapshotsFreshBalances()
    {
        var infra = await SetupInfrastructureAsync();
        // Ban đầu chỉ có SKU1 trong kho (2m @ 50,000)
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 2m, 50000m);

        var discovery = new LockCoordinationInterceptor { PauseFirstSku = true };
        var saved = new CountSavedGate();
        var (context, tenantService) = CreatePostgresDbContext(infra.TenantId, discovery, saved);
        await using (context)
        {
            var svc = new StockCountService(context, tenantService, new WacCalculator(), Microsoft.Extensions.Logging.Abstractions.NullLogger<StockCountService>.Instance);
            var dto = new CreateStockCountRequestDto
            {
                RequestKey = Guid.NewGuid(),
                BranchId = infra.BranchAId,
                WarehouseId = infra.WarehouseA1Id,
                Note = "ALL mode new sku reconciliation test",
                SkuIds = null
            };

            var createTask = svc.CreateStockCountAsync(dto, infra.UserId);

            // Chờ tác vụ discovery xong (chỉ thấy SKU1) và dừng lại trước khi lấy khóa SKU đầu tiên
            await discovery.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));

            // Xuất hiện SKU2 (1m @ 80,000) trong kho sau discovery nhưng trước khi khóa
            try
            {
                await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku2Id, 1m, 80000m);
            }
            finally
            {
                discovery.Release.TrySetResult();
            }

            // Chờ ALL count phát hiện tập SKU thay đổi, rollback, khóa lại đủ cả SKU1 và SKU2,
            // đọc fresh balances dưới khóa, và dừng lại ngay sau SaveChanges trước khi commit
            await saved.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));

            // Chạy competing receipt thêm 3m cho SKU2 trong khi ALL count chưa commit
            var receipt = SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku2Id, 3m, 80000m);
            bool blocked = false;
            decimal beforeQuantity = -1m;
            long beforeSequence = -1;
            try
            {
                // Quan sát thực tế: competing receipt phải bị chặn bởi advisory lock trên SKU2 do ALL transaction đang nắm giữ
                await using var observer = new NpgsqlConnection(PostgresTestEnvironment.RuntimeConnectionString);
                await observer.OpenAsync();
                for (var attempt = 0; attempt < 100 && !blocked && !receipt.IsCompleted; attempt++)
                {
                    await using var command = new NpgsqlCommand(
                        "SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND wait_event='advisory' AND cardinality(pg_blocking_pids(pid))>0",
                        observer);
                    blocked = (long)(await command.ExecuteScalarAsync())! > 0;
                    if (!blocked) await Task.Delay(100);
                }
                blocked = blocked && !receipt.IsCompleted;

                // Xác minh số dư trước commit: Tồn kho SKU2 vẫn ổn định 1m và sequence 1
                var (verifyCtx, _) = CreatePostgresDbContext(infra.TenantId);
                await using (verifyCtx)
                {
                    var actual = await verifyCtx.WarehouseInventoryBalances.FirstAsync(b => b.WarehouseId == infra.WarehouseA1Id && b.SkuId == infra.Sku2Id);
                    beforeQuantity = actual.OnHand;
                    beforeSequence = actual.LastSequence;
                }
            }
            finally
            {
                saved.Release.TrySetResult();
            }

            // Await đầy đủ cả hai tác vụ
            var result = await createTask.WaitAsync(TimeSpan.FromSeconds(20));
            await receipt.WaitAsync(TimeSpan.FromSeconds(20));

            blocked.Should().BeTrue("Competing receipt trên SKU2 bắt buộc phải chờ transaction ALL commit vì SKU2 đã được giữ khóa.");
            beforeQuantity.Should().Be(1m, "Tồn kho SKU2 trước khi commit phải ổn định ở 1m.");
            beforeSequence.Should().Be(1L, "Sequence SKU2 trước khi commit phải ổn định ở 1.");

            result.Items.Should().HaveCount(2, "ALL mode phải nạp lại toàn bộ tập SKU và snapshot cả SKU mới xuất hiện.");
            var item1 = result.Items.First(i => i.SkuId == infra.Sku1Id);
            item1.ExpectedQuantity.Should().Be(2m);

            var item2 = result.Items.First(i => i.SkuId == infra.Sku2Id);
            item2.ExpectedQuantity.Should().Be(1m, "Snapshot phải ghi nhận đúng số dư 1m dưới khóa, không phải 4m sau receipt.");
            item2.BalanceSequenceAtSnapshot.Should().Be(1L, "Snapshot sequence phải ghi nhận sequence 1.");

            var (afterCtx, _) = CreatePostgresDbContext(infra.TenantId);
            await using (afterCtx)
            {
                var final = await afterCtx.WarehouseInventoryBalances.FirstAsync(b => b.WarehouseId == infra.WarehouseA1Id && b.SkuId == infra.Sku2Id);
                final.OnHand.Should().Be(4m, "Receipt chỉ hoàn tất sau khi ALL commit, đưa tổng tồn lên 4m.");
                final.LastSequence.Should().Be(2L, "Sequence sau receipt lên 2.");

                var countRows = await afterCtx.StockCounts.CountAsync(c => c.RequestKey == dto.RequestKey);
                countRows.Should().Be(1, "Chỉ tạo duy nhất một đợt kiểm kê.");
            }
        }
    }

    [Fact]
    public async Task AC01_CreateDraft_AllModeVsSelectedMode_SameRequestKey_NoDeadlock_OneCreatesAndOneMismatch()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 5m, 50000m);

        var sharedKey = Guid.NewGuid();
        var allGate = new LockCoordinationInterceptor { PauseFirstSku = true };
        var selectedGate = new LockCoordinationInterceptor();

        var (allCtx, allTenant) = CreatePostgresDbContext(infra.TenantId, allGate);
        var (selectedCtx, selectedTenant) = CreatePostgresDbContext(infra.TenantId, selectedGate);

        await using (allCtx)
        await using (selectedCtx)
        {
            var allSvc = new StockCountService(allCtx, allTenant, new WacCalculator(), Microsoft.Extensions.Logging.Abstractions.NullLogger<StockCountService>.Instance);
            var selectedSvc = new StockCountService(selectedCtx, selectedTenant, new WacCalculator(), Microsoft.Extensions.Logging.Abstractions.NullLogger<StockCountService>.Instance);

            var allDto = new CreateStockCountRequestDto
            {
                RequestKey = sharedKey,
                BranchId = infra.BranchAId,
                WarehouseId = infra.WarehouseA1Id,
                Note = "ALL count",
                SkuIds = null
            };

            var selectedDto = new CreateStockCountRequestDto
            {
                RequestKey = sharedKey,
                BranchId = infra.BranchAId,
                WarehouseId = infra.WarehouseA1Id,
                Note = "Selected count",
                SkuIds = new List<Guid> { infra.Sku1Id }
            };

            async Task<string> Run(StockCountService svc, CreateStockCountRequestDto dto)
            {
                try
                {
                    var val = await svc.CreateStockCountAsync(dto, infra.UserId);
                    return $"created:{val.Status}";
                }
                catch (OISM.Application.Common.Exceptions.OismException ex)
                {
                    return $"business:{ex.StatusCode}:{ex.ErrorCode}";
                }
                catch (Exception ex)
                {
                    return $"exception:{ex.GetType().Name}";
                }
            }

            var allRun = Run(allSvc, allDto);
            await allGate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var selectedRun = Run(selectedSvc, selectedDto);
            await selectedGate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            allGate.Release.TrySetResult();

            var outcomes = await Task.WhenAll(allRun, selectedRun).WaitAsync(TimeSpan.FromSeconds(60));

            // Xác minh: Tuyệt đối không deadlock 40P01 giữa ALL và Selected
            (allGate.Deadlocks + selectedGate.Deadlocks).Should().Be(0, "Thứ tự khóa nhất quán SKU -> RequestKey triệt tiêu hoàn toàn deadlock 40P01.");
            outcomes.Should().Contain("created:Draft", "Đúng 1 tác vụ tạo phiếu thành công.");
            outcomes.Should().Contain("business:409:IDEMPOTENCY_PAYLOAD_MISMATCH", "Tác vụ còn lại bị từ chối 409 IDEMPOTENCY_PAYLOAD_MISMATCH do khác payload.");
        }
    }

    // =========================================================================
    // AC-02: Idempotency và Replay theo RequestKey
    // =========================================================================

    [Fact]
    public async Task AC02_Idempotency_SameKeyAndPayload_ReplaysExisting_Returns200OK()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var requestKey = Guid.NewGuid();
        var requestDto = new CreateStockCountRequestDto
        {
            RequestKey = requestKey,
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            Note = "Test Replay Idempotency",
            SkuIds = new List<Guid> { infra.Sku1Id }
        };

        // Gửi lần 1: 201 Created
        var res1 = await _client.PostAsJsonAsync("/api/stock-counts", requestDto);
        res1.StatusCode.Should().Be(HttpStatusCode.Created);
        var count1 = await res1.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
        count1!.IsReplay.Should().BeFalse();

        // Gửi lần 2 cùng key và payload: 200 OK replay
        var res2 = await _client.PostAsJsonAsync("/api/stock-counts", requestDto);
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        var count2 = await res2.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
        count2!.IsReplay.Should().BeTrue();
        count2.Id.Should().Be(count1.Id);
        count2.Number.Should().Be(count1.Number);

        // Đối soát CSDL: Chỉ có đúng 1 bản ghi được tạo
        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var total = await verifyContext.StockCounts.CountAsync(c => c.RequestKey == requestKey);
            total.Should().Be(1);
        }
    }

    [Fact]
    public async Task AC02_Idempotency_SameKeyDifferentPayload_Returns409Conflict()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var requestKey = Guid.NewGuid();
        var dto1 = new CreateStockCountRequestDto
        {
            RequestKey = requestKey,
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            Note = "Payload 1",
            SkuIds = new List<Guid> { infra.Sku1Id }
        };

        var res1 = await _client.PostAsJsonAsync("/api/stock-counts", dto1);
        res1.StatusCode.Should().Be(HttpStatusCode.Created);

        // Gửi lại cùng key nhưng đổi Note hoặc đổi SKU -> 409 IDEMPOTENCY_CONFLICT
        var dto2 = new CreateStockCountRequestDto
        {
            RequestKey = requestKey,
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            Note = "Payload 2 (khác note)",
            SkuIds = new List<Guid> { infra.Sku1Id }
        };

        var res2 = await _client.PostAsJsonAsync("/api/stock-counts", dto2);
        res2.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body2 = await res2.Content.ReadAsStringAsync();
        body2.Should().Contain("IDEMPOTENCY_PAYLOAD_MISMATCH");
    }

    [Fact]
    public async Task AC02_Idempotency_ReplayWithReversedItemOrder_Succeeds()
    {
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var requestKey = Guid.NewGuid();
        var dto1 = new CreateStockCountRequestDto
        {
            RequestKey = requestKey,
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id, infra.Sku2Id }
        };

        var res1 = await _client.PostAsJsonAsync("/api/stock-counts", dto1);
        res1.StatusCode.Should().Be(HttpStatusCode.Created);

        // Đảo ngược thứ tự SkuIds -> Canonical hash vẫn trùng khớp, trả replay 200 OK
        var dtoReversed = new CreateStockCountRequestDto
        {
            RequestKey = requestKey,
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku2Id, infra.Sku1Id }
        };

        var res2 = await _client.PostAsJsonAsync("/api/stock-counts", dtoReversed);
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        var count2 = await res2.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
        count2!.IsReplay.Should().BeTrue();
    }

    [Fact]
    public async Task AC02_Idempotency_ReplayAfterStatusChanges_ReturnsCurrentState()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var requestKey = Guid.NewGuid();
        var createDto = new CreateStockCountRequestDto
        {
            RequestKey = requestKey,
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        };

        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", createDto);
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        // Ghi số đếm -> Chuyển sang Counting
        await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 10m } }
        });

        // Replay create endpoint -> trả về 200 OK với trạng thái Counting
        var replayCounting = await _client.PostAsJsonAsync("/api/stock-counts", createDto);
        replayCounting.StatusCode.Should().Be(HttpStatusCode.OK);
        var countingData = await replayCounting.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
        countingData!.Status.Should().Be("Counting");
        countingData.IsReplay.Should().BeTrue();

        // Confirm phiếu -> Chuyển sang Confirmed
        await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());

        // Replay create endpoint -> trả về 200 OK với trạng thái Confirmed
        var replayConfirmed = await _client.PostAsJsonAsync("/api/stock-counts", createDto);
        replayConfirmed.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmedData = await replayConfirmed.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
        confirmedData!.Status.Should().Be("Confirmed");
        confirmedData.IsReplay.Should().BeTrue();
    }

    [Fact]
    public async Task AC02_MultiTenant_SameRequestKeyAcrossTenants_CreatesIndependentRecords()
    {
        var infra = await SetupInfrastructureAsync();
        var sharedKey = Guid.NewGuid();

        // 1. Tenant A tạo phiếu
        var ownerTokenA = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenA);

        var resA = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = sharedKey,
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        });
        resA.StatusCode.Should().Be(HttpStatusCode.Created);
        var countA = await resA.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        // 2. Tenant B tạo phiếu với cùng sharedKey nhưng tài nguyên của Tenant B
        var (contextB, _) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantBId);
        Guid branchBId, warehouseBId, skuBId;
        await using (contextB)
        {
            var branch = await contextB.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantBId);
            var warehouse = await contextB.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantBId);
            var sku = await contextB.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantBId);
            branchBId = branch.Id;
            warehouseBId = warehouse.Id;
            skuBId = sku.Id;
        }

        var ownerTokenB = await LoginAsAsync("owner@shopb.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenB);

        var resB = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = sharedKey,
            BranchId = branchBId,
            WarehouseId = warehouseBId,
            SkuIds = new List<Guid> { skuBId }
        });
        resB.StatusCode.Should().Be(HttpStatusCode.Created, "Cùng RequestKey ở 2 tenant khác nhau phải tạo thành 2 phiếu độc lập.");
        var countB = await resB.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        countA!.Id.Should().NotBe(countB!.Id);
    }

    [Fact]
    public async Task AC02_Concurrency_MultipleDistinctKeys_CreatesSequentialUniqueNumbers()
    {
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        const int concurrency = 20;
        var tasks = Enumerable.Range(0, concurrency).Select(async _ =>
        {
            using var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

            var req = new CreateStockCountRequestDto
            {
                RequestKey = Guid.NewGuid(),
                BranchId = infra.BranchAId,
                WarehouseId = infra.WarehouseA1Id,
                SkuIds = new List<Guid> { infra.Sku1Id }
            };

            var res = await client.PostAsJsonAsync("/api/stock-counts", req);
            var body = await res.Content.ReadAsStringAsync();
            res.StatusCode.Should().Be(HttpStatusCode.Created, $"Concurrent create failed: {body}");
            return await res.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
        });

        var results = await Task.WhenAll(tasks);
        results.Length.Should().Be(concurrency);

        var numbers = results.Select(r => r!.Number).ToList();
        numbers.Distinct().Should().HaveCount(concurrency, "Tất cả số hiệu phiếu kiểm kê được tạo đồng thời phải duy nhất.");
    }

    [Fact]
    public async Task AC02_Idempotency_ConcurrentTwentySameKeyAndPayload_ProducesExactlyOneCount()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var sharedKey = Guid.NewGuid();
        const int concurrency = 20;

        var tasks = Enumerable.Range(0, concurrency).Select(async _ =>
        {
            using var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

            var req = new CreateStockCountRequestDto
            {
                RequestKey = sharedKey,
                BranchId = infra.BranchAId,
                WarehouseId = infra.WarehouseA1Id,
                Note = "Concurrent same key idempotent",
                SkuIds = new List<Guid> { infra.Sku1Id }
            };

            var res = await client.PostAsJsonAsync("/api/stock-counts", req);
            var count = await res.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
            return (StatusCode: res.StatusCode, Count: count!);
        });

        var results = await Task.WhenAll(tasks);
        var createdList = results.Where(r => r.StatusCode == HttpStatusCode.Created).ToList();
        var replayList = results.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();

        createdList.Should().HaveCount(1, "Chỉ duy nhất 1 request tạo thành công phiếu 201 Created.");
        replayList.Should().HaveCount(concurrency - 1, "19 requests còn lại nhận 200 OK replay.");

        var expectedId = createdList.First().Count.Id;
        var expectedNumber = createdList.First().Count.Number;

        foreach (var r in replayList)
        {
            r.Count.Id.Should().Be(expectedId);
            r.Count.Number.Should().Be(expectedNumber);
            r.Count.IsReplay.Should().BeTrue();
        }

        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var dbCount = await verifyContext.StockCounts.CountAsync(c => c.TenantId == infra.TenantId && c.RequestKey == sharedKey);
            dbCount.Should().Be(1, "Database chỉ lưu đúng 1 bản ghi duy nhất cho RequestKey.");
        }
    }

    [Fact]
    public async Task AC02_Idempotency_AllMode_ReplayAfterWarehouseBalancesChange_ReturnsOriginalSnapshot()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var requestKey = Guid.NewGuid();
        var reqDto = new CreateStockCountRequestDto
        {
            RequestKey = requestKey,
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            Note = "ALL mode stable replay test",
            SkuIds = null
        };

        // Tạo lần 1: Expected = 10
        var res1 = await _client.PostAsJsonAsync("/api/stock-counts", reqDto);
        res1.StatusCode.Should().Be(HttpStatusCode.Created);
        var count1 = await res1.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
        count1!.Items.First(i => i.SkuId == infra.Sku1Id).ExpectedQuantity.Should().Be(10m);

        // Thay đổi tồn kho thực tế sau khi tạo: Nhập thêm 20 cái nữa (OnHand: 10 -> 30)
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 20m, 50000m);

        // Gửi lại đúng RequestKey và ALL payload
        var res2 = await _client.PostAsJsonAsync("/api/stock-counts", reqDto);
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        var count2 = await res2.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        count2!.IsReplay.Should().BeTrue();
        count2.Id.Should().Be(count1.Id);
        count2.Items.First(i => i.SkuId == infra.Sku1Id).ExpectedQuantity.Should().Be(10m, "Replay ALL mode phải bảo toàn snapshot cũ, không tự động refresh theo tồn mới.");
    }

    [Fact]
    public async Task AC02_Idempotency_AllMode_ReplayAfterCatalogSkusAdded_ReturnsOriginalSnapshot()
    {
        var infra = await SetupInfrastructureAsync();
        // Ban đầu kho chỉ có SKU1
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var requestKey = Guid.NewGuid();
        var reqDto = new CreateStockCountRequestDto
        {
            RequestKey = requestKey,
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            Note = "ALL mode replay after new SKU added to catalog/warehouse",
            SkuIds = null
        };

        // 1. Tạo lần 1: ALL mode chỉ snapshot 1 SKU (SKU1)
        var res1 = await _client.PostAsJsonAsync("/api/stock-counts", reqDto);
        res1.StatusCode.Should().Be(HttpStatusCode.Created);
        var count1 = await res1.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
        count1!.Items.Should().HaveCount(1);
        count1.Items.First().SkuId.Should().Be(infra.Sku1Id);

        // 2. Xuất hiện thêm SKU2 hoàn toàn mới trong kho sau khi đã tạo phiếu
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku2Id, 15m, 80000m);

        // 3. Replay lại cùng RequestKey và ALL payload
        var res2 = await _client.PostAsJsonAsync("/api/stock-counts", reqDto);
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        var count2 = await res2.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        count2!.IsReplay.Should().BeTrue();
        count2.Id.Should().Be(count1.Id);
        count2.Items.Should().HaveCount(1, "Replay ALL mode phải bảo toàn danh sách SKU ban đầu của chứng từ cũ, không tự nạp thêm SKU mới.");
        count2.Items.First().SkuId.Should().Be(infra.Sku1Id);
    }

    [Fact]
    public async Task AC02_Idempotency_ReplayAfterCancelAndCatalogChange_ReturnsCancelledState()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var requestKey = Guid.NewGuid();
        var reqDto = new CreateStockCountRequestDto
        {
            RequestKey = requestKey,
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            Note = "Replay after cancel",
            SkuIds = new List<Guid> { infra.Sku1Id }
        };

        // 1. Tạo phiếu
        var res1 = await _client.PostAsJsonAsync("/api/stock-counts", reqDto);
        var count1 = await res1.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        // 2. Hủy phiếu
        var cancelRes = await _client.PostAsJsonAsync($"/api/stock-counts/{count1!.Id}/cancel", new CancelStockCountRequestDto { Reason = "Hủy đợt" });
        cancelRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Thay đổi tồn kho hoặc danh mục
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 5m, 50000m);

        // 4. Gửi lại request ban đầu: phải trả về trạng thái Cancelled đã lưu, không tạo phiếu mới
        var resReplay = await _client.PostAsJsonAsync("/api/stock-counts", reqDto);
        resReplay.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayData = await resReplay.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        replayData!.IsReplay.Should().BeTrue();
        replayData.Id.Should().Be(count1.Id);
        replayData.Status.Should().Be("Cancelled");
    }

    // =========================================================================
    // AC-03: Ghi nhận số lượng đếm (POST /api/stock-counts/{id}/record)
    // =========================================================================

    [Fact]
    public async Task AC03_RecordCounts_PartialAndSubsequentUpdates_TransitionsDraftToCounting()
    {
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id, infra.Sku2Id }
        });
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
        count!.Status.Should().Be("Draft");

        // Ghi nhận dòng 1 (SKU1): Trạng thái chuyển Draft -> Counting
        var recRes1 = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 12m } }
        });
        recRes1.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated1 = await recRes1.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
        updated1!.Status.Should().Be("Counting");
        var item1 = updated1.Items.First(i => i.SkuId == infra.Sku1Id);
        item1.CountedQuantity.Should().Be(12m);

        // Ghi nhận tiếp dòng 2 (SKU2): Cập nhật đếm cho SKU2, vẫn giữ SKU1
        var recRes2 = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku2Id, CountedQuantity = 5m } }
        });
        recRes2.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated2 = await recRes2.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
        updated2!.Status.Should().Be("Counting");
        updated2.Items.First(i => i.SkuId == infra.Sku1Id).CountedQuantity.Should().Be(12m);
        updated2.Items.First(i => i.SkuId == infra.Sku2Id).CountedQuantity.Should().Be(5m);
    }

    [Fact]
    public async Task AC03_RecordCounts_ZeroCountedQuantity_DistinguishedFromOmitted()
    {
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id, infra.Sku2Id }
        });
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        // Ghi nhận CountedQuantity = 0 cho SKU1 (mất toàn bộ hàng)
        var recRes = await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 0m } }
        });
        recRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var recorded = await recRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        var item1 = recorded!.Items.First(i => i.SkuId == infra.Sku1Id);
        item1.CountedQuantity.Should().Be(0m, "Số đếm bằng 0 là giá trị hợp lệ.");

        var item2 = recorded.Items.First(i => i.SkuId == infra.Sku2Id);
        item2.CountedQuantity.Should().BeNull("SKU chưa ghi nhận đếm vẫn giữ null (omitted).");
    }

    [Fact]
    public async Task AC03_RecordCounts_InvalidSkuNotInStockCount_Returns400BadRequest()
    {
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        });
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        // Gửi SKU3 không thuộc phiếu kiểm kê
        var recRes = await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku3Id, CountedQuantity = 10m } }
        });
        recRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await recRes.Content.ReadAsStringAsync();
        body.Should().Contain("INVALID_COUNT_ITEM");
    }

    [Fact]
    public async Task AC03_RecordCounts_AtomicBatchFailure_NoPartialMutation()
    {
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        });
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        // Batch gồm 1 SKU hợp lệ và 1 SKU không tồn tại trong đợt -> toàn bộ batch phải bị từ chối
        var recRes = await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto>
            {
                new() { SkuId = infra.Sku1Id, CountedQuantity = 10m },
                new() { SkuId = Guid.NewGuid(), CountedQuantity = 5m }
            }
        });
        recRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Đối soát CSDL: SKU1 vẫn chưa được ghi nhận số đếm (CountedQuantity = null), trạng thái vẫn Draft
        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var c = await verifyContext.StockCounts.Include(x => x.Items).FirstAsync(x => x.Id == count.Id);
            c.Status.Should().Be("Draft");
            c.Items.First().CountedQuantity.Should().BeNull();
        }
    }

    [Fact]
    public async Task AC03_Confirm_IncompleteStockCount_Returns409InvalidStateTransition()
    {
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id, infra.Sku2Id }
        });
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        // Chỉ đếm SKU1, bỏ quên SKU2
        await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 10m } }
        });

        // Confirm khi chưa đếm hết -> 409 INVALID_STATE_TRANSITION
        var confirmRes = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        confirmRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await confirmRes.Content.ReadAsStringAsync();
        body.Should().Contain("INVALID_STATE_TRANSITION");
    }

    // =========================================================================
    // AC-04: Confirm và Sinh chứng từ điều chỉnh (POST /api/stock-counts/{id}/confirm)
    // =========================================================================

    [Fact]
    public async Task AC04_Confirm_MultiSku_SurplusDeficitMatch_GeneratesSingleAdjustmentAndLedger()
    {
        var infra = await SetupInfrastructureAsync();
        // Seed tồn: SKU1 có 20 cái @ 50,000, SKU2 có 15 cái @ 80,000, SKU3 có 10 cái @ 100,000
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 20m, 50000m);
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku2Id, 15m, 80000m);
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku3Id, 10m, 100000m);

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id, infra.Sku2Id, infra.Sku3Id }
        });
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        // Ghi số đếm:
        // SKU1: Đếm 25 (Thừa +5)
        // SKU2: Đếm 12 (Thiếu -3)
        // SKU3: Đếm 10 (Khớp = 0)
        await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto>
            {
                new() { SkuId = infra.Sku1Id, CountedQuantity = 25m },
                new() { SkuId = infra.Sku2Id, CountedQuantity = 12m },
                new() { SkuId = infra.Sku3Id, CountedQuantity = 10m }
            }
        });

        // Confirm
        var confirmRes = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        var body = await confirmRes.Content.ReadAsStringAsync();
        confirmRes.StatusCode.Should().Be(HttpStatusCode.OK, $"Confirm failed: {body}");
        var confirmed = await confirmRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
        confirmed!.Status.Should().Be("Confirmed");
        confirmed.GeneratedAdjustmentId.Should().NotBeNull();

        // Kiểm tra đối soát CSDL chuyên sâu
        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            // 1. Đúng 1 header InventoryAdjustment
            var adj = await verifyContext.InventoryAdjustments
                .Include(a => a.Items)
                .FirstOrDefaultAsync(a => a.Id == confirmed.GeneratedAdjustmentId!.Value);
            adj.Should().NotBeNull();
            adj!.Number.Should().StartWith($"ADJ-{DateTimeOffset.UtcNow:yyyyMMdd}-");
            adj.BranchId.Should().Be(infra.BranchAId);
            adj.WarehouseId.Should().Be(infra.WarehouseA1Id);

            // 2. Chỉ có 2 adjustment items (SKU1 và SKU2). SKU3 khớp tồn -> KHÔNG sinh item điều chỉnh
            adj.Items.Should().HaveCount(2);

            var adjItem1 = adj.Items.First(i => i.SkuId == infra.Sku1Id);
            adjItem1.QuantityDelta.Should().Be(5m);
            adjItem1.UnitCost.Should().Be(50000m); // Dùng WAC hiện có của chi nhánh

            var adjItem2 = adj.Items.First(i => i.SkuId == infra.Sku2Id);
            adjItem2.QuantityDelta.Should().Be(-3m);
            adjItem2.UnitCost.Should().Be(80000m);

            // 3. Đúng 2 bút toán ledger từ đợt kiểm kê (1 IN cho SKU1, 1 OUT cho SKU2)
            var countLedgers = await verifyContext.InventoryTransactions
                .Where(tx => tx.AdjustmentItemId == adjItem1.Id || tx.AdjustmentItemId == adjItem2.Id)
                .ToListAsync();
            countLedgers.Should().HaveCount(2);

            var inTx = countLedgers.First(tx => tx.AdjustmentItemId == adjItem1.Id);
            inTx.Type.Should().Be("IN");
            inTx.Quantity.Should().Be(5m);
            inTx.UnitCost.Should().Be(50000m);

            var outTx = countLedgers.First(tx => tx.AdjustmentItemId == adjItem2.Id);
            outTx.Type.Should().Be("OUT");
            outTx.Quantity.Should().Be(3m);
            outTx.UnitCost.Should().Be(80000m);

            // 4. Số dư OnHand cập nhật chính xác 2 cấp
            var bal1 = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            bal1.OnHand.Should().Be(25m);

            var wBal1 = await verifyContext.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == infra.Sku1Id);
            wBal1.OnHand.Should().Be(25m);

            var bal2 = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku2Id);
            bal2.OnHand.Should().Be(12m);

            var wBal2 = await verifyContext.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == infra.Sku2Id);
            wBal2.OnHand.Should().Be(12m);

            var bal3 = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku3Id);
            bal3.OnHand.Should().Be(10m, "SKU3 khớp tồn giữ nguyên.");
        }
    }

    [Fact]
    public async Task AC04_Confirm_AllItemsMatch_GeneratesAdjustmentWithZeroItemsAndZeroLedger()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        });
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        // Ghi số đếm khớp 100%: 10 = 10
        await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 10m } }
        });

        var confirmRes = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        confirmRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmed = await confirmRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
        confirmed!.Status.Should().Be("Confirmed");
        confirmed.GeneratedAdjustmentId.Should().NotBeNull();

        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var adj = await verifyContext.InventoryAdjustments
                .Include(a => a.Items)
                .FirstAsync(a => a.Id == confirmed.GeneratedAdjustmentId!.Value);
            adj.Items.Should().BeEmpty("Toàn bộ mặt hàng khớp số lượng thì phiếu điều chỉnh có 0 items.");

            var countLedgers = await verifyContext.InventoryTransactions
                .Where(tx => tx.AdjustmentItemId.HasValue)
                .ToListAsync();
            countLedgers.Should().BeEmpty("Không có giao dịch điều chỉnh nào cần ghi nhận vào sổ cái ledger.");
        }
    }

    // =========================================================================
    // AC-05: Phát hiện giao dịch xen giữa qua Sequence Drift
    // =========================================================================

    [Fact]
    public async Task AC05_SequenceDrift_InterveningReceiptInSameWarehouse_Returns409Conflict()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // 1. Tạo đợt kiểm kê và ghi nhận số đếm
        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        });
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 12m } }
        });

        // 2. Giao dịch xen giữa: Có phiếu nhập kho hàng mới cho SKU1 tại Kho A1 -> Sequence của SKU1 tại Kho A1 tăng
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 5m, 50000m);

        // 3. Confirm phiếu kiểm kê -> Phát hiện sequence drift và từ chối 409 STOCK_COUNT_SEQUENCE_DRIFT
        var confirmRes = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        confirmRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await confirmRes.Content.ReadAsStringAsync();
        body.Should().Contain("STOCK_COUNT_SEQUENCE_DRIFT");

        // Đối soát CSDL: Phiếu kiểm kê vẫn ở trạng thái Counting, không sinh InventoryAdjustment
        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var c = await verifyContext.StockCounts.FirstAsync(x => x.Id == count.Id);
            c.Status.Should().Be("Counting");
            c.GeneratedAdjustmentId.Should().BeNull();
        }
    }

    [Fact]
    public async Task AC05_SequenceDrift_StockFluctuatesAndReturnsToOriginalQuantity_StillBlockedByDrift()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 20m, 50000m);

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        });
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 20m } }
        });

        // Mô phỏng tồn biến động rồi quay về giá trị ban đầu nhưng Sequence đã tăng:
        // 1. Nhập thêm 5 cái vào Kho A1 (OnHand: 20 -> 25, Seq tăng)
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 5m, 50000m);

        // 2. Chuyển 5 cái từ Kho A1 sang Kho A2 và dispatch (OnHand Kho A1: 25 -> 20, Seq tăng tiếp)
        var trfRes = await _client.PostAsJsonAsync("/api/stock-transfers", new OISM.Application.Features.StockTransfers.Dtos.CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchAId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseA2Id,
            Items = new List<OISM.Application.Features.StockTransfers.Dtos.StockTransferItemRequestDto>
            {
                new() { SkuId = infra.Sku1Id, Quantity = 5m }
            }
        });
        var trf = await trfRes.Content.ReadFromJsonAsync<OISM.Application.Features.StockTransfers.Dtos.StockTransferResponseDto>(_jsonOptions);
        await _client.PostAsJsonAsync($"/api/stock-transfers/{trf!.Id}/dispatch", new OISM.Application.Features.StockTransfers.Dtos.DispatchStockTransferRequestDto());

        // Confirm phải phát hiện sequence drift
        var confirmRes = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        confirmRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await confirmRes.Content.ReadAsStringAsync();
        body.Should().Contain("STOCK_COUNT_SEQUENCE_DRIFT");
    }

    [Fact]
    public async Task AC05_SequenceDrift_TransactionInOtherWarehouseOfSameBranch_DoesNotTriggerFalseDrift()
    {
        var infra = await SetupInfrastructureAsync();
        // Nhập tồn vào cả Kho A1 và Kho A2
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA2Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // 1. Tạo kiểm kê cho Kho A1
        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        });
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 10m } }
        });

        // 2. Nhập thêm hàng vào Kho A2 (làm tăng Branch Sequence nhưng KHÔNG làm tăng Warehouse A1 Sequence)
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA2Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 5m, 50000m);

        // 3. Confirm phiếu kiểm kê Kho A1: KHÔNG bị báo drift nhầm vì Warehouse A1 Sequence không đổi
        var confirmRes = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        confirmRes.StatusCode.Should().Be(HttpStatusCode.OK, "Giao dịch ở kho khác cùng chi nhánh không được kích hoạt false drift.");
    }

    [Fact]
    public async Task AC05_SequenceDrift_UnrelatedSkuInSameWarehouse_DoesNotTriggerFalseDrift()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku2Id, 10m, 60000m);

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // 1. Kiểm kê chỉ cho SKU1
        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        });
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 10m } }
        });

        // 2. Có giao dịch phát sinh trên SKU2 trong cùng Kho A1
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku2Id, 5m, 60000m);

        // 3. Confirm SKU1: KHÔNG bị false drift vì sequence của SKU1 tại Kho A1 không đổi
        var confirmRes = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        confirmRes.StatusCode.Should().Be(HttpStatusCode.OK, "Giao dịch trên SKU khác không được gây false drift cho SKU đang kiểm kê.");
    }

    // =========================================================================
    // AC-06: WAC và Bảo vệ hàng giữ chỗ (Active Reservations)
    // =========================================================================

    [Fact]
    public async Task AC06_WAC_MultiWarehouseBranch_SurplusUsesBranchWac_ClientCannotOverride()
    {
        var infra = await SetupInfrastructureAsync();
        // Nhập Kho A1: 10 cái @ 40,000; Nhập Kho A2: 10 cái @ 60,000 -> Branch WAC = 50,000 (Tổng 20 cái @ 50,000)
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 40000m);
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA2Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 60000m);

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // Kiểm kê Kho A1: Đếm 15 cái (Thừa +5 cái so với sổ sách 10)
        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        });
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        // Client cố tình truyền giá điều chỉnh 999,999đ trong record
        await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto>
            {
                new() { SkuId = infra.Sku1Id, CountedQuantity = 15m, AdjustmentUnitCost = 999999m }
            }
        });

        // Confirm
        var confirmRes = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        confirmRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Quyết định B: Khi chi nhánh đã có WAC > 0, bắt buộc dùng WAC hiện tại (50,000). Giá client gửi không được ghi đè WAC.
        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var bal = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            bal.OnHand.Should().Be(25m); // 20 + 5
            bal.AverageCost.Should().Be(50000m, "Chi nhánh đã có WAC > 0 thì surplus giữ nguyên WAC 50,000, client không thể ghi đè.");
        }
    }

    [Fact]
    public async Task AC06_WAC_SurplusWhenBranchWacIsZero_RequiresPositiveCost()
    {
        var infra = await SetupInfrastructureAsync();
        // SKU2 chưa từng có số dư và chưa có WAC trong chi nhánh (WAC = null hoặc 0)
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku2Id }
        });
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        // Ghi số đếm thừa: Đếm được 5 cái nhưng không cung cấp đơn giá điều chỉnh
        await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku2Id, CountedQuantity = 5m, AdjustmentUnitCost = null } }
        });

        // 1. Confirm khi thiếu đơn giá -> 422 ADJUSTMENT_COST_REQUIRED
        var confirmFail = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        confirmFail.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var bodyFail = await confirmFail.Content.ReadAsStringAsync();
        bodyFail.Should().Contain("ADJUSTMENT_COST_REQUIRED");

        // 1b. Confirm khi truyền đơn giá = 0 -> cũng phải bị từ chối 422 ADJUSTMENT_COST_REQUIRED (T4B-F03)
        var confirmZeroCost = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto
        {
            SkuAdjustmentCosts = new Dictionary<Guid, decimal> { [infra.Sku2Id] = 0m }
        });
        confirmZeroCost.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var bodyZero = await confirmZeroCost.Content.ReadAsStringAsync();
        bodyZero.Should().Contain("ADJUSTMENT_COST_REQUIRED");

        // 2. Cung cấp đơn giá điều chỉnh hợp lệ qua ConfirmStockCountRequestDto hoặc record lại
        var confirmSuccess = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto
        {
            SkuAdjustmentCosts = new Dictionary<Guid, decimal> { [infra.Sku2Id] = 75000m }
        });
        var successBody = await confirmSuccess.Content.ReadAsStringAsync();
        confirmSuccess.StatusCode.Should().Be(HttpStatusCode.OK, $"Confirm failed: {successBody}");

        // Đối soát WAC mới được thiết lập là 75,000
        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var bal = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku2Id);
            bal.OnHand.Should().Be(5m);
            bal.AverageCost.Should().Be(75000m);
        }
    }

    [Fact]
    public async Task AC06_WAC_DeficitPreservesWac_AndProtectsActiveReservations()
    {
        var infra = await SetupInfrastructureAsync();
        // Nhập tồn 10 cái @ 60,000
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 60000m);

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        decimal sku1Retail;
        await using (var ctx = CreatePostgresDbContext(infra.TenantId).Context)
        {
            sku1Retail = (await ctx.ProductVariants.FirstAsync(v => v.Id == infra.Sku1Id)).RetailPrice;
        }

        // Tạo đơn Admin giữ chỗ 8 sản phẩm -> OnHand=10, Reserved=8, Available=2
        var adminOrderDto = new OISM.Application.Features.Orders.Dtos.CreateOrderRequestDto
        {
            BranchId = infra.BranchAId,
            Channel = "Admin",
            CustomerName = "Khách Hàng Giữ Chỗ",
            CustomerPhone = "0909123456",
            ShippingAddress = "123 Đường Nguyễn Huệ, Q1, TP.HCM",
            RequestKey = Guid.NewGuid(),
            Items = new List<OISM.Application.Features.Orders.Dtos.OrderItemRequestDto>
            {
                new() { SkuId = infra.Sku1Id, SkuCode = infra.Sku1Code, Quantity = 8m, UnitPrice = sku1Retail }
            }
        };
        var orderRes = await _client.PostAsJsonAsync("/api/orders", adminOrderDto);
        orderRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        });
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        // 1. Kiểm kê đếm được 6 cái -> Thiếu 4 cái so với 10 cái sổ sách.
        // Tuy nhiên tồn đếm được (6) < Reserved (8), nếu chốt sẽ dẫn đến Available = 6 - 8 = -2 (vi phạm giữ chỗ!)
        await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 6m } }
        });

        var confirmRes = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        confirmRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await confirmRes.Content.ReadAsStringAsync();
        body.Should().Contain("INSUFFICIENT_STOCK");

        // Đối soát CSDL: Reserved vẫn là 8, OnHand vẫn là 10, đơn hàng online được bảo vệ an toàn
        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var bal = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            bal.OnHand.Should().Be(10m);
            bal.Reserved.Should().Be(8m);
            bal.AverageCost.Should().Be(60000m);
        }
    }

    [Fact]
    public async Task AC06_Confirm_DerivedBranchBalanceOverflow_Returns400ValidationError_ZeroDatabaseMutation()
    {
        var infra = await SetupInfrastructureAsync();
        // Nhập 1 cái vào Kho A2 (Kho khác cùng Chi nhánh A)
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA2Id, infra.SupplierId, infra.UserId, infra.Sku2Id, 1m, 10000m);

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // Kiểm kê tại Kho A1 (chưa có tồn cho SKU2)
        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku2Id }
        });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        // Ghi số đếm cực đại hợp lệ trên input: 999_999_999_999_999.999
        // Nhưng khi cộng vào tổng chi nhánh (1 + 999999999999999.999 = 1000000000000000.999) sẽ vượt numeric(18,3)
        var recRes = await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto>
            {
                new() { SkuId = infra.Sku2Id, CountedQuantity = 999_999_999_999_999.999m }
            }
        });
        recRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Confirm: Hệ thống phải chủ động phát hiện tràn số tính toán và trả 400 VALIDATION_ERROR, KHÔNG lỗi 500
        var confirmRes = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        confirmRes.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Tràn số tính toán cấp chi nhánh phải được kiểm soát trả về 400 VALIDATION_ERROR.");
        var body = await confirmRes.Content.ReadAsStringAsync();
        body.Should().Contain("VALIDATION_ERROR");
        body.Should().Contain("vượt quá giới hạn tối đa cho phép");

        // Đối soát CSDL: Hoàn toàn không bị biến động, đợt kiểm kê vẫn ở trạng thái Counting, link null
        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var dbCount = await verifyContext.StockCounts.FirstAsync(c => c.Id == count.Id);
            dbCount.Status.Should().Be("Counting");
            dbCount.GeneratedAdjustmentId.Should().BeNull();

            var bal = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku2Id);
            bal.OnHand.Should().Be(1m, "Tồn kho chi nhánh được bảo toàn nguyên vẹn khi bị từ chối 400.");

            var adjs = await verifyContext.InventoryAdjustments.CountAsync(a => a.TenantId == infra.TenantId);
            adjs.Should().Be(0, "Không có chứng từ điều chỉnh nào được lưu vào CSDL.");
        }
    }

    [Fact]
    public async Task AC06_Confirm_ValidNumericBoundary_Succeeds()
    {
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // Kiểm kê SKU2 tại Kho A1 (chi nhánh chưa có tồn kho nào)
        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku2Id }
        });
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        // Ghi số đếm biên tối đa cho phép: 999_999_999_999_999.999 với đơn giá 1000.000000
        const decimal maxQty = 999_999_999_999_999.999m;
        const decimal unitCost = 1000m;
        var recRes = await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto>
            {
                new() { SkuId = infra.Sku2Id, CountedQuantity = maxQty, AdjustmentUnitCost = unitCost }
            }
        });
        recRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Confirm: Nằm chính xác trong ngưỡng biên tối đa -> Thành công 200 OK
        var confirmRes = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        var body = await confirmRes.Content.ReadAsStringAsync();
        confirmRes.StatusCode.Should().Be(HttpStatusCode.OK, $"Confirm biên hợp lệ phải thành công 200 OK: {body}");

        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var bal = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku2Id);
            bal.OnHand.Should().Be(maxQty);
            bal.AverageCost.Should().Be(unitCost);
        }
    }

    [Fact]
    public async Task AC06_Confirm_ValidLargeAdjustment_WithoutWac_Succeeds()
    {
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // SKU3 chưa có tồn kho và chưa có WAC
        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku3Id }
        });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        // Ghi số đếm 2000 với đơn giá 900,000,000,000 (tổng giá trị biến động 1,800,000,000,000,000 - nằm hoàn toàn trong giới hạn CLR)
        const decimal countedQty = 2000m;
        const decimal largeUnitCost = 900_000_000_000m;
        var recRes = await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto>
            {
                new() { SkuId = infra.Sku3Id, CountedQuantity = countedQty, AdjustmentUnitCost = largeUnitCost }
            }
        });
        recRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Confirm: Không bị chặn bởi artificial MaxTotalCostAmount cap, thành công 200 OK
        var confirmRes = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        var body = await confirmRes.Content.ReadAsStringAsync();
        confirmRes.StatusCode.Should().Be(HttpStatusCode.OK, $"Confirm giá trị lớn hợp lệ phải thành công 200 OK: {body}");

        var confirmedCount = await confirmRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
        confirmedCount!.Status.Should().Be("Confirmed");
        confirmedCount.GeneratedAdjustmentId.Should().NotBeNull();

        // Đối soát CSDL độc lập: Tồn kho, WAC, Adjustment và Ledger được lưu chính xác
        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var branchBal = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku3Id);
            branchBal.OnHand.Should().Be(countedQty);
            branchBal.AverageCost.Should().Be(largeUnitCost);

            var whBal = await verifyContext.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == infra.Sku3Id);
            whBal.OnHand.Should().Be(countedQty);

            var adj = await verifyContext.InventoryAdjustments.Include(a => a.Items).FirstAsync(a => a.Id == confirmedCount.GeneratedAdjustmentId);
            adj.Status.Should().Be("Confirmed");
            adj.Items.Should().HaveCount(1);
            adj.Items.First().QuantityDelta.Should().Be(countedQty);
            adj.Items.First().UnitCost.Should().Be(largeUnitCost);

            var txRecord = await verifyContext.InventoryTransactions.FirstAsync(t => t.AdjustmentItemId == adj.Items.First().Id);
            txRecord.Quantity.Should().Be(countedQty);
            txRecord.UnitCost.Should().Be(largeUnitCost);
            txRecord.BalanceAfter.Should().Be(countedQty);
        }
    }

    // =========================================================================
    // AC-07: State Machine và Cancel an toàn
    // =========================================================================

    [Fact]
    public async Task AC07_StateMachine_CancelDraftOrCounting_ZeroInventoryMutation()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // 1. Hủy khi còn Draft
        var createRes1 = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        });
        var count1 = await createRes1.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        var cancelRes1 = await _client.PostAsJsonAsync($"/api/stock-counts/{count1!.Id}/cancel", new CancelStockCountRequestDto { Reason = "Nhầm ngày" });
        cancelRes1.StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelled1 = await cancelRes1.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
        cancelled1!.Status.Should().Be("Cancelled");

        // 2. Hủy khi đang Counting
        var createRes2 = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        });
        var count2 = await createRes2.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        await _client.PostAsJsonAsync($"/api/stock-counts/{count2!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 12m } }
        });

        var cancelRes2 = await _client.PostAsJsonAsync($"/api/stock-counts/{count2.Id}/cancel", new CancelStockCountRequestDto { Reason = "Kho bị gián đoạn" });
        cancelRes2.StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelled2 = await cancelRes2.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
        cancelled2!.Status.Should().Be("Cancelled");

        // Đối soát CSDL: Tồn kho, sequence và ledger không bị biến động
        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var bal = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            bal.OnHand.Should().Be(10m);
            var adjCount = await verifyContext.InventoryAdjustments.CountAsync(a => a.TenantId == infra.TenantId);
            adjCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task AC07_StateMachine_CancelConfirmedOrCancelled_Returns409Conflict()
    {
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        });
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        // Hủy lần 1: OK
        var cancel1 = await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/cancel", new CancelStockCountRequestDto());
        cancel1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Hủy lần 2 trên phiếu đã Cancelled -> 409 INVALID_STATE_TRANSITION
        var cancel2 = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/cancel", new CancelStockCountRequestDto());
        cancel2.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await cancel2.Content.ReadAsStringAsync();
        body.Should().Contain("INVALID_STATE_TRANSITION");
    }

    [Fact]
    public async Task AC07_StateMachine_ConfirmDraftOrCancelled_Returns409Conflict()
    {
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // 1. Confirm khi phiếu mới ở Draft (chưa ghi số đếm) -> 409 INVALID_STATE_TRANSITION
        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        });
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        var confirmDraft = await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/confirm", new ConfirmStockCountRequestDto());
        confirmDraft.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var bodyDraft = await confirmDraft.Content.ReadAsStringAsync();
        bodyDraft.Should().Contain("INVALID_STATE_TRANSITION");

        // 2. Hủy phiếu -> Confirm phiếu đã Cancelled -> 409 INVALID_STATE_TRANSITION
        await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/cancel", new CancelStockCountRequestDto());

        var confirmCancelled = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        confirmCancelled.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var bodyCancelled = await confirmCancelled.Content.ReadAsStringAsync();
        bodyCancelled.Should().Contain("INVALID_STATE_TRANSITION");
    }

    [Fact]
    public async Task AC07_StateMachine_ConfirmAlreadyConfirmed_Returns409InvalidStateTransition()
    {
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        });
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 0m } }
        });

        // Confirm lần 1: 200 OK
        var confirmRes1 = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        confirmRes1.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmed1 = await confirmRes1.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
        confirmed1!.IsReplay.Should().BeFalse();

        // Confirm lần 2: 409 INVALID_STATE_TRANSITION (không replay 200 - T4B-F02)
        var confirmRes2 = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        confirmRes2.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body2 = await confirmRes2.Content.ReadAsStringAsync();
        body2.Should().Contain("INVALID_STATE_TRANSITION");
    }

    // =========================================================================
    // AC-08: Concurrency & Deadlock Freedom
    // =========================================================================

    [Fact]
    public async Task AC08_Concurrency_50ConfirmsOnSameCount_ExactlyOneSucceeds()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        });
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 15m } }
        });

        // 50 requests concurrent confirm
        const int concurrency = 50;
        var tasks = Enumerable.Range(0, concurrency).Select(async _ =>
        {
            using var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
            return await client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        });

        var responses = await Task.WhenAll(tasks);
        var okResponses = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();
        var conflictResponses = responses.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();

        // Đúng 1 trả 200 OK, 49 trả 409 INVALID_STATE_TRANSITION (T4B-F02)
        okResponses.Count.Should().Be(1, "Chỉ duy nhất 1 request confirm thành công 200 OK.");
        conflictResponses.Count.Should().Be(concurrency - 1, "49 requests còn lại phải trả 409 Conflict.");

        foreach (var conflictRes in conflictResponses)
        {
            var conflictBody = await conflictRes.Content.ReadAsStringAsync();
            conflictBody.Should().Contain("INVALID_STATE_TRANSITION");
        }

        // Đối soát CSDL: Chỉ có đúng 1 InventoryAdjustment và đúng 1 bút toán Ledger IN
        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var adjCount = await verifyContext.InventoryAdjustments.CountAsync(a => a.TenantId == infra.TenantId);
            adjCount.Should().Be(1, "Chỉ duy nhất 1 phiếu điều chỉnh tồn được sinh ra trong môi trường đồng thời.");

            var bal = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            bal.OnHand.Should().Be(15m, "Tồn kho chỉ được cộng 1 lần duy nhất từ 10 lên 15.");

            var ledgerCount = await verifyContext.InventoryTransactions.CountAsync(t => t.AdjustmentItemId.HasValue);
            ledgerCount.Should().Be(1, "Chỉ duy nhất 1 bút toán sổ cái điều chỉnh được ghi nhận.");
        }
    }

    [Fact]
    public async Task AC08_Concurrency_RaceBetweenConfirmAndCancel_ExactlyOneWins()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        });
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 15m } }
        });

        using var clientConfirm = _factory.CreateClient();
        clientConfirm.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        using var clientCancel = _factory.CreateClient();
        clientCancel.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var taskConfirm = clientConfirm.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        var taskCancel = clientCancel.PostAsJsonAsync($"/api/stock-counts/{count.Id}/cancel", new CancelStockCountRequestDto { Reason = "Race test" });

        var responses = await Task.WhenAll(taskConfirm, taskCancel);
        var okCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        okCount.Should().Be(1, "Chỉ duy nhất một tác vụ (Confirm hoặc Cancel) được thành công.");
        conflictCount.Should().Be(1, "Tác vụ còn lại phải bị từ chối 409 Conflict.");

        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var finalCount = await verifyContext.StockCounts.FirstAsync(x => x.Id == count.Id);
            finalCount.Status.Should().BeOneOf("Confirmed", "Cancelled");
        }
    }

    [Fact]
    public async Task AC08_Concurrency_TwoCountsCompeteForSameSku_TrulyConcurrent_FirstSucceedsSecondDetectsDrift()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // Tạo 2 đợt kiểm kê A và B cùng snapshot SKU1
        var createA = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        });
        var countA = await createA.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        var createB = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        });
        var countB = await createB.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        // Cả 2 đều ghi số đếm
        await _client.PostAsJsonAsync($"/api/stock-counts/{countA!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 12m } }
        });
        await _client.PostAsJsonAsync($"/api/stock-counts/{countB!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 14m } }
        });

        // Chạy thực sự đồng thời 2 lệnh confirm qua Task.WhenAll
        using var clientA = _factory.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        using var clientB = _factory.CreateClient();
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var deadlocksBefore = await GetDatabaseDeadlockCountAsync();

        var confirmTaskA = clientA.PostAsJsonAsync($"/api/stock-counts/{countA.Id}/confirm", new ConfirmStockCountRequestDto());
        var confirmTaskB = clientB.PostAsJsonAsync($"/api/stock-counts/{countB.Id}/confirm", new ConfirmStockCountRequestDto());

        var responses = await Task.WhenAll(confirmTaskA, confirmTaskB);
        var deadlocksAfter = await GetDatabaseDeadlockCountAsync();
        (deadlocksAfter - deadlocksBefore).Should().Be(0, "Cạnh tranh 2 đợt kiểm kê cùng SKU không được gây ra bất kỳ deadlock 40P01 nào.");

        var okCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var driftCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        okCount.Should().Be(1, "Đúng 1 đợt kiểm kê được chốt thành công 200 OK.");
        driftCount.Should().Be(1, "Đợt kiểm kê còn lại phải phát hiện sequence drift và trả 409.");

        var driftRes = responses.First(r => r.StatusCode == HttpStatusCode.Conflict);
        var driftBody = await driftRes.Content.ReadAsStringAsync();
        driftBody.Should().Contain("STOCK_COUNT_SEQUENCE_DRIFT");

        // Đối soát CSDL độc lập: Winner Confirmed, Loser Counting/link NULL, tồn hai cấp 12 hoặc 14 khớp winner
        var (verifyCtx, _) = CreatePostgresDbContext(infra.TenantId);
        await using (verifyCtx)
        {
            var dbCountA = await verifyCtx.StockCounts.FirstAsync(c => c.Id == countA.Id);
            var dbCountB = await verifyCtx.StockCounts.FirstAsync(c => c.Id == countB.Id);

            var whBal = await verifyCtx.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == infra.Sku1Id);
            var brBal = await verifyCtx.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);

            whBal.Reserved.Should().Be(0m);
            brBal.Reserved.Should().Be(0m);
            brBal.AverageCost.Should().Be(50000m);
            whBal.LastSequence.Should().Be(2L, "Sequence kho tăng đúng 1 lần (từ seed 1 lên 2).");
            brBal.LastSequence.Should().Be(2L, "Sequence chi nhánh tăng đúng 1 lần (từ seed 1 lên 2).");

            if (responses[0].StatusCode == HttpStatusCode.OK)
            {
                dbCountA.Status.Should().Be("Confirmed");
                dbCountA.GeneratedAdjustmentId.Should().NotBeNull();
                dbCountB.Status.Should().Be("Counting");
                dbCountB.GeneratedAdjustmentId.Should().BeNull();

                whBal.OnHand.Should().Be(12m);
                brBal.OnHand.Should().Be(12m);

                var adjA = await verifyCtx.InventoryAdjustments.Include(a => a.Items).FirstOrDefaultAsync(a => a.Id == dbCountA.GeneratedAdjustmentId);
                adjA.Should().NotBeNull();
                adjA!.Items.Should().HaveCount(1);
                adjA.Items.First().QuantityDelta.Should().Be(2m);

                var txs = await verifyCtx.InventoryTransactions.Where(t => t.AdjustmentItemId.HasValue && t.SkuId == infra.Sku1Id).ToListAsync();
                txs.Should().HaveCount(1);
                txs[0].Quantity.Should().Be(2m);
                txs[0].UnitCost.Should().Be(50000m);
            }
            else
            {
                dbCountB.Status.Should().Be("Confirmed");
                dbCountB.GeneratedAdjustmentId.Should().NotBeNull();
                dbCountA.Status.Should().Be("Counting");
                dbCountA.GeneratedAdjustmentId.Should().BeNull();

                whBal.OnHand.Should().Be(14m);
                brBal.OnHand.Should().Be(14m);

                var adjB = await verifyCtx.InventoryAdjustments.Include(a => a.Items).FirstOrDefaultAsync(a => a.Id == dbCountB.GeneratedAdjustmentId);
                adjB.Should().NotBeNull();
                adjB!.Items.Should().HaveCount(1);
                adjB.Items.First().QuantityDelta.Should().Be(4m);

                var txs = await verifyCtx.InventoryTransactions.Where(t => t.AdjustmentItemId.HasValue && t.SkuId == infra.Sku1Id).ToListAsync();
                txs.Should().HaveCount(1);
                txs[0].Quantity.Should().Be(4m);
                txs[0].UnitCost.Should().Be(50000m);
            }
        }
    }

    [Fact]
    public async Task AC08_Concurrency_RaceBetweenConfirmAndRecord_ExactlyOneTransitionsOrSucceedsCleanly()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        });
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        // Ghi số đếm lần đầu để đưa phiếu về Counting
        await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 12m } }
        });

        // Chạy đồng thời: 1 request ghi đếm tiếp vs 1 request confirm
        using var clientRecord = _factory.CreateClient();
        clientRecord.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        using var clientConfirm = _factory.CreateClient();
        clientConfirm.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var taskRecord = clientRecord.PostAsJsonAsync($"/api/stock-counts/{count.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 15m } }
        });
        var taskConfirm = clientConfirm.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());

        var responses = await Task.WhenAll(taskRecord, taskConfirm);
        var resRecord = responses[0];
        var resConfirm = responses[1];

        // Confirm phải luôn luôn thành công 200 OK
        resConfirm.StatusCode.Should().Be(HttpStatusCode.OK, "Confirm phải xử lý thành công không bị gián đoạn.");

        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var dbCount = await verifyContext.StockCounts.Include(c => c.Items).FirstAsync(c => c.Id == count.Id);
            dbCount.Status.Should().Be("Confirmed", "Trạng thái cuối cùng của đợt kiểm kê bắt buộc phải là Confirmed.");
            dbCount.GeneratedAdjustmentId.Should().NotBeNull();

            var bal = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            var whBal = await verifyContext.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == infra.Sku1Id);

            if (resRecord.StatusCode == HttpStatusCode.OK)
            {
                // Nếu Record thắng trước: Cả hai 200 OK, số đếm được cập nhật thành 15, confirm chốt theo số mới
                dbCount.Items.First().CountedQuantity.Should().Be(15m);
                bal.OnHand.Should().Be(15m);
                whBal.OnHand.Should().Be(15m);
            }
            else
            {
                // Nếu Confirm thắng trước: Record bị 409 INVALID_STATE_TRANSITION, confirm chốt theo số cũ (12)
                resRecord.StatusCode.Should().Be(HttpStatusCode.Conflict);
                var errBody = await resRecord.Content.ReadAsStringAsync();
                errBody.Should().Contain("INVALID_STATE_TRANSITION");

                dbCount.Items.First().CountedQuantity.Should().Be(12m);
                bal.OnHand.Should().Be(12m);
                whBal.OnHand.Should().Be(12m);
            }

            var adjs = await verifyContext.InventoryAdjustments.CountAsync(a => a.Id == dbCount.GeneratedAdjustmentId);
            adjs.Should().Be(1, "Duy nhất 1 chứng từ InventoryAdjustment được tạo.");

            var txs = await verifyContext.InventoryTransactions.CountAsync(t => t.AdjustmentItemId.HasValue);
            txs.Should().Be(1, "Duy nhất 1 bút toán ledger được sinh ra tương ứng với delta.");
        }
    }

    [Fact]
    public async Task AC08_Concurrency_ConfirmCompetesWithPosCheckout_ZeroOversell_ValidSerialization()
    {
        var infra = await SetupInfrastructureAsync();
        // Nhập tồn 10 cái @ 50,000 vào Kho A1
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        decimal sku1Retail;
        await using (var ctx = CreatePostgresDbContext(infra.TenantId).Context)
        {
            sku1Retail = (await ctx.ProductVariants.FirstAsync(v => v.Id == infra.Sku1Id)).RetailPrice;
        }

        // Tạo kiểm kê đếm được 8 cái (thiếu -2 cái)
        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        });
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 8m } }
        });

        // Chạy đồng thời: POS checkout mua 3 cái vs Confirm kiểm kê chốt tồn
        using var clientPos = _factory.CreateClient();
        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        clientPos.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);

        var posDto = new OISM.Application.Features.Orders.Dtos.PosCheckoutRequestDto
        {
            BranchId = infra.BranchAId,
            RequestKey = Guid.NewGuid(),
            PaymentMethod = "Cash",
            AmountGiven = sku1Retail * 3m,
            Items = new List<OISM.Application.Features.Orders.Dtos.OrderItemRequestDto>
            {
                new() { SkuId = infra.Sku1Id, SkuCode = infra.Sku1Code, Quantity = 3m, UnitPrice = sku1Retail }
            }
        };

        using var clientConfirm = _factory.CreateClient();
        clientConfirm.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var taskPos = clientPos.PostAsJsonAsync("/api/pos/checkout", posDto);
        var taskConfirm = clientConfirm.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());

        var responses = await Task.WhenAll(taskPos, taskConfirm);
        var resPos = responses[0];
        var resConfirm = responses[1];

        // POS checkout bán hàng lẻ tại quầy phải luôn thành công 200 OK
        resPos.StatusCode.Should().Be(HttpStatusCode.OK, "POS checkout phải luôn thành công.");

        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var bal = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            var whBal = await verifyContext.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == infra.Sku1Id);
            var dbCount = await verifyContext.StockCounts.FirstAsync(c => c.Id == count.Id);

            bal.Reserved.Should().Be(0m);
            whBal.Reserved.Should().Be(0m);
            bal.OnHand.Should().Be(whBal.OnHand, "Tồn kho cấp chi nhánh và kho phải nhất quán 100%.");

            if (resConfirm.StatusCode == HttpStatusCode.OK)
            {
                // Thứ tự hợp lệ 1: Confirm hoàn tất trước (tồn 10 -> 8), sau đó POS mua 3 (tồn 8 -> 5)
                dbCount.Status.Should().Be("Confirmed");
                dbCount.GeneratedAdjustmentId.Should().NotBeNull();
                bal.OnHand.Should().Be(5m, "Tồn sau điều chỉnh kiểm kê (8) trừ bán POS (3) phải là 5.");
                whBal.OnHand.Should().Be(5m);

                var adjCount = await verifyContext.InventoryAdjustments.CountAsync(a => a.Id == dbCount.GeneratedAdjustmentId);
                adjCount.Should().Be(1);

                var txs = await verifyContext.InventoryTransactions.Where(t => t.SkuId == infra.Sku1Id).ToListAsync();
                txs.Should().HaveCount(3, "Gồm 1 Seed ban đầu + 1 Adjustment + 1 POS Sale.");
            }
            else
            {
                // Thứ tự hợp lệ 2: POS mua trước (tồn 10 -> 7, sequence tăng), Confirm chạy sau phát hiện sequence drift 409
                resConfirm.StatusCode.Should().Be(HttpStatusCode.Conflict);
                var err = await resConfirm.Content.ReadAsStringAsync();
                err.Should().Contain("STOCK_COUNT_SEQUENCE_DRIFT");

                dbCount.Status.Should().Be("Counting");
                dbCount.GeneratedAdjustmentId.Should().BeNull();
                bal.OnHand.Should().Be(7m, "Tồn sau POS bán 3 cái là 7.");
                whBal.OnHand.Should().Be(7m);

                var adjCount = await verifyContext.InventoryAdjustments.CountAsync(a => a.TenantId == infra.TenantId);
                adjCount.Should().Be(0, "Không có adjustment nào được tạo khi confirm bị sequence drift.");

                var txs = await verifyContext.InventoryTransactions.Where(t => t.SkuId == infra.Sku1Id).ToListAsync();
                txs.Should().HaveCount(2, "Gồm 1 Seed ban đầu + 1 POS Sale.");
            }
        }
    }

    [Fact]
    public async Task AC08_Concurrency_ConfirmCompetesWithStockTransfer_MultiSkuReversedOrder_NoDeadlock()
    {
        var infra = await SetupInfrastructureAsync();
        // Nhập SKU1 và SKU2 vào Kho A1
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku2Id, 10m, 60000m);

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // Tạo kiểm kê cho SKU1 và SKU2 (thứ tự tự nhiên SKU1, SKU2)
        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id, infra.Sku2Id }
        });
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto>
            {
                new() { SkuId = infra.Sku1Id, CountedQuantity = 12m },
                new() { SkuId = infra.Sku2Id, CountedQuantity = 8m }
            }
        });

        // Chuyển kho tạo phiếu với thứ tự đảo ngược (SKU2 trước, SKU1 sau)
        var trfRes = await _client.PostAsJsonAsync("/api/stock-transfers", new OISM.Application.Features.StockTransfers.Dtos.CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchAId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseA2Id,
            Items = new List<OISM.Application.Features.StockTransfers.Dtos.StockTransferItemRequestDto>
            {
                new() { SkuId = infra.Sku2Id, Quantity = 2m },
                new() { SkuId = infra.Sku1Id, Quantity = 2m }
            }
        });
        var trf = await trfRes.Content.ReadFromJsonAsync<OISM.Application.Features.StockTransfers.Dtos.StockTransferResponseDto>(_jsonOptions);

        // Đồng thời gọi Confirm kiểm kê và Dispatch chuyển kho
        using var clientConfirm = _factory.CreateClient();
        clientConfirm.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        using var clientDispatch = _factory.CreateClient();
        clientDispatch.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var deadlocksBefore = await GetDatabaseDeadlockCountAsync();

        var taskConfirm = clientConfirm.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        var taskDispatch = clientDispatch.PostAsJsonAsync($"/api/stock-transfers/{trf!.Id}/dispatch", new OISM.Application.Features.StockTransfers.Dtos.DispatchStockTransferRequestDto());

        var responses = await Task.WhenAll(taskConfirm, taskDispatch);
        var deadlocksAfter = await GetDatabaseDeadlockCountAsync();
        (deadlocksAfter - deadlocksBefore).Should().Be(0, "Cạnh tranh chuyển kho đa SKU đảo thứ tự với kiểm kê không được gây ra bất kỳ deadlock 40P01 nào.");

        var resConfirm = responses[0];
        var resDispatch = responses[1];

        // Dispatch chuyển kho phải luôn thành công 200 OK
        resDispatch.StatusCode.Should().Be(HttpStatusCode.OK, "Dispatch chuyển kho phải luôn thành công.");
        resConfirm.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Conflict);

        var (verifyCtx, _) = CreatePostgresDbContext(infra.TenantId);
        await using (verifyCtx)
        {
            var dbTrf = await verifyCtx.StockTransfers.Include(t => t.Items).FirstAsync(t => t.Id == trf.Id);
            dbTrf.Status.Should().Be("InTransit");
            dbTrf.DispatchedAt.Should().NotBeNull();
            dbTrf.Items.Should().HaveCount(2);
            dbTrf.Items.Should().OnlyContain(i => i.UnitCost.HasValue && i.UnitCost.Value > 0);

            var trfTxs = await verifyCtx.InventoryTransactions.Where(t => t.TransferItemId.HasValue && (t.SkuId == infra.Sku1Id || t.SkuId == infra.Sku2Id)).ToListAsync();
            trfTxs.Should().HaveCount(2, "Phải sinh đúng 2 bút toán xuất chuyển kho OUT.");
            trfTxs.Should().OnlyContain(t => t.Quantity == 2m && t.Type == "OUT" && t.WarehouseId == infra.WarehouseA1Id);

            var dbCount = await verifyCtx.StockCounts.FirstAsync(c => c.Id == count.Id);
            var whBal1 = await verifyCtx.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == infra.Sku1Id);
            var whBal2 = await verifyCtx.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == infra.Sku2Id);
            var brBal1 = await verifyCtx.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            var brBal2 = await verifyCtx.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku2Id);

            whBal1.Reserved.Should().Be(0m);
            whBal2.Reserved.Should().Be(0m);
            brBal1.Reserved.Should().Be(0m);
            brBal2.Reserved.Should().Be(0m);

            brBal1.AverageCost.Should().Be(50000m);
            brBal2.AverageCost.Should().Be(60000m);

            if (resConfirm.StatusCode == HttpStatusCode.OK)
            {
                // Count thắng trước: count 200, sau đó dispatch trừ 2 mỗi SKU -> source/branch cuối 10 và 6
                dbCount.Status.Should().Be("Confirmed");
                dbCount.GeneratedAdjustmentId.Should().NotBeNull();

                whBal1.OnHand.Should().Be(10m, "SKU1: 10 + 2 (count) - 2 (transfer) = 10.");
                whBal2.OnHand.Should().Be(6m, "SKU2: 10 - 2 (count) - 2 (transfer) = 6.");
                brBal1.OnHand.Should().Be(10m);
                brBal2.OnHand.Should().Be(6m);

                var adj = await verifyCtx.InventoryAdjustments.Include(a => a.Items).FirstOrDefaultAsync(a => a.Id == dbCount.GeneratedAdjustmentId);
                adj.Should().NotBeNull();
                adj!.Items.Should().HaveCount(2, "Đúng 2 adjustment rows (+2 và -2).");
                adj.Items.First(i => i.SkuId == infra.Sku1Id).QuantityDelta.Should().Be(2m);
                adj.Items.First(i => i.SkuId == infra.Sku2Id).QuantityDelta.Should().Be(-2m);

                var countTxs = await verifyCtx.InventoryTransactions.Where(t => t.AdjustmentItemId.HasValue && (t.SkuId == infra.Sku1Id || t.SkuId == infra.Sku2Id)).ToListAsync();
                countTxs.Should().HaveCount(2);
                countTxs.First(t => t.SkuId == infra.Sku1Id).Quantity.Should().Be(2m);
                countTxs.First(t => t.SkuId == infra.Sku1Id).Type.Should().Be("IN");
                countTxs.First(t => t.SkuId == infra.Sku2Id).Quantity.Should().Be(2m);
                countTxs.First(t => t.SkuId == infra.Sku2Id).Type.Should().Be("OUT");
            }
            else
            {
                // Dispatch thắng trước: count phát hiện drift 409 -> source/branch cuối 8 và 8, không adjustment
                var body = await resConfirm.Content.ReadAsStringAsync();
                body.Should().Contain("STOCK_COUNT_SEQUENCE_DRIFT");

                dbCount.Status.Should().Be("Counting");
                dbCount.GeneratedAdjustmentId.Should().BeNull();

                whBal1.OnHand.Should().Be(8m, "SKU1: 10 - 2 (transfer) = 8.");
                whBal2.OnHand.Should().Be(8m, "SKU2: 10 - 2 (transfer) = 8.");
                brBal1.OnHand.Should().Be(8m);
                brBal2.OnHand.Should().Be(8m);

                var adjCount = await verifyCtx.InventoryAdjustments.CountAsync(a => a.Id == dbCount.GeneratedAdjustmentId);
                adjCount.Should().Be(0);
            }
        }
    }

    [Fact]
    public async Task AC08_Concurrency_ConfirmCompetesWithGoodsReceipt_SerializationValid_NoDeadlock()
    {
        var infra = await SetupInfrastructureAsync();
        // Nhập tồn ban đầu 10 cái @ 50,000 vào Kho A1
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // Tạo kiểm kê đếm được 15 cái (surplus +5)
        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        });
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 15m } }
        });

        // Chạy đồng thời: Nhập kho thêm 5 cái @ 50,000 bằng GoodsReceiptService thật vs Confirm kiểm kê
        using var clientConfirm = _factory.CreateClient();
        clientConfirm.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var taskReceipt = Task.Run(async () =>
        {
            await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 5m, 50000m);
            return HttpStatusCode.OK;
        });
        var taskConfirm = clientConfirm.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());

        await Task.WhenAll(taskReceipt, taskConfirm);
        var resConfirm = await taskConfirm;

        // Một lịch tuần tự hợp lệ: Cả 2 đều OK (nếu Confirm trước rồi Receipt sau) hoặc Confirm nhận 409 drift (nếu Receipt xong trước làm tăng sequence)
        resConfirm.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Conflict);

        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var bal = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            var whBal = await verifyContext.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == infra.Sku1Id);
            bal.OnHand.Should().Be(whBal.OnHand);

            if (resConfirm.StatusCode == HttpStatusCode.OK)
            {
                // Confirm trước (tồn thành 15), sau đó Receipt thêm 5 -> tổng tồn 20
                bal.OnHand.Should().Be(20m);
            }
            else
            {
                // Receipt trước (tồn thành 15), sau đó Confirm phát hiện drift 409 -> tồn giữ nguyên 15
                var err = await resConfirm.Content.ReadAsStringAsync();
                err.Should().Contain("STOCK_COUNT_SEQUENCE_DRIFT");
                bal.OnHand.Should().Be(15m);
            }
        }
    }

    [Fact]
    public async Task AC08_Concurrency_ConfirmCompetesWithActiveReservationOrder_ProtectsReserved()
    {
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        decimal sku1Retail, sku2Retail, sku3Retail;
        var (setupCtx, _) = CreatePostgresDbContext(infra.TenantId);
        await using (setupCtx)
        {
            sku1Retail = (await setupCtx.ProductVariants.FirstAsync(v => v.Id == infra.Sku1Id)).RetailPrice;
            sku2Retail = (await setupCtx.ProductVariants.FirstAsync(v => v.Id == infra.Sku2Id)).RetailPrice;
            sku3Retail = (await setupCtx.ProductVariants.FirstAsync(v => v.Id == infra.Sku3Id)).RetailPrice;
        }

        // =====================================================================
        // Lịch hợp lệ 1: Admin create POST /api/orders (reserve 8) thắng trước -> Confirm 409 INSUFFICIENT_STOCK
        // =====================================================================
        {
            await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);

            var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
            {
                RequestKey = Guid.NewGuid(),
                BranchId = infra.BranchAId,
                WarehouseId = infra.WarehouseA1Id,
                SkuIds = new List<Guid> { infra.Sku1Id }
            });
            var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

            await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
            {
                Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 5m } }
            });

            var adminOrderDto = new OISM.Application.Features.Orders.Dtos.CreateOrderRequestDto
            {
                BranchId = infra.BranchAId,
                Channel = "Admin",
                CustomerName = "Khách Hàng Giữ Chỗ A",
                CustomerPhone = "0909999881",
                ShippingAddress = "123 Đường Nguyễn Huệ, Q1, TP.HCM",
                RequestKey = Guid.NewGuid(),
                Items = new List<OISM.Application.Features.Orders.Dtos.OrderItemRequestDto>
                {
                    new() { SkuId = infra.Sku1Id, SkuCode = infra.Sku1Code, Quantity = 8m, UnitPrice = sku1Retail }
                }
            };

            // 1. Admin create trước: POST /api/orders là bước giữ chỗ (reserve) thực sự
            var orderRes = await _client.PostAsJsonAsync("/api/orders", adminOrderDto);
            orderRes.StatusCode.Should().Be(HttpStatusCode.Created);
            var order = await orderRes.Content.ReadFromJsonAsync<OISM.Application.Features.Orders.Dtos.OrderResponseDto>(_jsonOptions);
            order!.Status.Should().Be("Reserved");

            // 2. Count confirm đua sau: Tồn counted 5 < Reserved 8 -> phải bị từ chối 409 INSUFFICIENT_STOCK
            var confirmRes = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
            confirmRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var confirmBody = await confirmRes.Content.ReadAsStringAsync();
            confirmBody.Should().Contain("INSUFFICIENT_STOCK");

            var (verifyCtx, _) = CreatePostgresDbContext(infra.TenantId);
            await using (verifyCtx)
            {
                var brBal = await verifyCtx.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
                var whBal = await verifyCtx.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == infra.Sku1Id);
                brBal.OnHand.Should().Be(10m);
                brBal.Reserved.Should().Be(8m);
                whBal.OnHand.Should().Be(10m);
                whBal.Reserved.Should().Be(8m);
                brBal.LastSequence.Should().Be(1L);
                whBal.LastSequence.Should().Be(1L);

                var activeReservations = await verifyCtx.Reservations.Where(r => r.SkuId == infra.Sku1Id && r.Status == "Active").SumAsync(r => r.Quantity);
                activeReservations.Should().Be(8m);

                var dbCount = await verifyCtx.StockCounts.FirstAsync(c => c.Id == count.Id);
                dbCount.Status.Should().Be("Counting");
                dbCount.GeneratedAdjustmentId.Should().BeNull();

                var adjs = await verifyCtx.InventoryAdjustments.CountAsync(a => a.Id == dbCount.GeneratedAdjustmentId);
                adjs.Should().Be(0);
            }
        }

        // =====================================================================
        // Lịch hợp lệ 2: Count confirm thắng trước -> Admin create 409 INSUFFICIENT_STOCK
        // =====================================================================
        {
            await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku2Id, 10m, 60000m);

            var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
            {
                RequestKey = Guid.NewGuid(),
                BranchId = infra.BranchAId,
                WarehouseId = infra.WarehouseA1Id,
                SkuIds = new List<Guid> { infra.Sku2Id }
            });
            var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

            await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
            {
                Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku2Id, CountedQuantity = 5m } }
            });

            // 1. Count confirm trước: Chốt tồn kho về 5m (OUT 5m)
            var confirmRes = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
            confirmRes.StatusCode.Should().Be(HttpStatusCode.OK);

            // 2. Admin create đua sau: Tồn khả dụng chỉ còn 5m < 8m -> phải bị từ chối 409 INSUFFICIENT_STOCK
            var adminKey = Guid.NewGuid();
            var adminOrderDto = new OISM.Application.Features.Orders.Dtos.CreateOrderRequestDto
            {
                BranchId = infra.BranchAId,
                Channel = "Admin",
                CustomerName = "Khách Hàng Giữ Chỗ B",
                CustomerPhone = "0909999882",
                ShippingAddress = "456 Đường Lê Lợi, Q1, TP.HCM",
                RequestKey = adminKey,
                Items = new List<OISM.Application.Features.Orders.Dtos.OrderItemRequestDto>
                {
                    new() { SkuId = infra.Sku2Id, SkuCode = infra.Sku2Code, Quantity = 8m, UnitPrice = sku2Retail }
                }
            };

            var orderRes = await _client.PostAsJsonAsync("/api/orders", adminOrderDto);
            orderRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var orderBody = await orderRes.Content.ReadAsStringAsync();
            orderBody.Should().Contain("INSUFFICIENT_STOCK");

            var (verifyCtx, _) = CreatePostgresDbContext(infra.TenantId);
            await using (verifyCtx)
            {
                var brBal = await verifyCtx.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku2Id);
                var whBal = await verifyCtx.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == infra.Sku2Id);
                brBal.OnHand.Should().Be(5m);
                brBal.Reserved.Should().Be(0m);
                whBal.OnHand.Should().Be(5m);
                whBal.Reserved.Should().Be(0m);
                brBal.AverageCost.Should().Be(60000m);
                whBal.LastSequence.Should().Be(2L);
                brBal.LastSequence.Should().Be(2L);

                var dbCount = await verifyCtx.StockCounts.FirstAsync(c => c.Id == count.Id);
                dbCount.Status.Should().Be("Confirmed");
                dbCount.GeneratedAdjustmentId.Should().NotBeNull();

                var adjs = await verifyCtx.InventoryAdjustments.Include(a => a.Items).FirstOrDefaultAsync(a => a.Id == dbCount.GeneratedAdjustmentId);
                adjs.Should().NotBeNull();
                adjs!.Items.Should().HaveCount(1);
                adjs.Items.First().QuantityDelta.Should().Be(-5m);

                var txs = await verifyCtx.InventoryTransactions.Where(t => t.AdjustmentItemId.HasValue && t.SkuId == infra.Sku2Id).ToListAsync();
                txs.Should().HaveCount(1);
                txs[0].Quantity.Should().Be(5m);
                txs[0].Type.Should().Be("OUT");
                txs[0].UnitCost.Should().Be(60000m);

                var orderExists = await verifyCtx.Orders.AnyAsync(o => o.RequestKey == adminKey);
                orderExists.Should().BeFalse("Không có đơn rác được lưu khi tạo đơn thất bại.");
            }
        }

        // =====================================================================
        // Lịch hợp lệ 3: Chạy thực sự đồng thời (Truly Concurrent Race qua Task.WhenAll)
        // =====================================================================
        {
            await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku3Id, 10m, 70000m);

            var createRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
            {
                RequestKey = Guid.NewGuid(),
                BranchId = infra.BranchAId,
                WarehouseId = infra.WarehouseA1Id,
                SkuIds = new List<Guid> { infra.Sku3Id }
            });
            var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

            await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
            {
                Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku3Id, CountedQuantity = 5m } }
            });

            var adminKey = Guid.NewGuid();
            var adminOrderDto = new OISM.Application.Features.Orders.Dtos.CreateOrderRequestDto
            {
                BranchId = infra.BranchAId,
                Channel = "Admin",
                CustomerName = "Khách Hàng Đua Đồng Thời",
                CustomerPhone = "0909999883",
                ShippingAddress = "789 Đường Hàm Nghi, Q1, TP.HCM",
                RequestKey = adminKey,
                Items = new List<OISM.Application.Features.Orders.Dtos.OrderItemRequestDto>
                {
                    new() { SkuId = infra.Sku3Id, SkuCode = infra.Sku3Code, Quantity = 8m, UnitPrice = sku3Retail }
                }
            };

            using var clientOrder = _factory.CreateClient();
            clientOrder.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
            using var clientConfirm = _factory.CreateClient();
            clientConfirm.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

            var taskOrder = clientOrder.PostAsJsonAsync("/api/orders", adminOrderDto);
            var taskConfirm = clientConfirm.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());

            var responses = await Task.WhenAll(taskOrder, taskConfirm);
            var resOrder = responses[0];
            var resConfirm = responses[1];

            // Tuyệt đối không có bất kỳ mã lỗi 500 nào
            responses.Should().NotContain(r => (int)r.StatusCode >= 500, "Tuyệt đối không phát sinh lỗi 500 khi cạnh tranh.");

            // Không bao giờ xảy ra trường hợp cả 2 cùng trả 409
            (resOrder.StatusCode == HttpStatusCode.Conflict && resConfirm.StatusCode == HttpStatusCode.Conflict)
                .Should().BeFalse("Không bao giờ xảy ra trường hợp cả hai request cùng trả 409 Conflict.");

            var (verifyCtx, _) = CreatePostgresDbContext(infra.TenantId);
            await using (verifyCtx)
            {
                var brBal = await verifyCtx.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku3Id);
                var whBal = await verifyCtx.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == infra.Sku3Id);
                var available = brBal.OnHand - brBal.Reserved;
                available.Should().BeGreaterThanOrEqualTo(0m, "Tồn khả dụng không bao giờ được phép âm.");

                if (resOrder.StatusCode == HttpStatusCode.Created)
                {
                    // Admin thắng trước (201): Confirm phải bị 409 INSUFFICIENT_STOCK
                    resConfirm.StatusCode.Should().Be(HttpStatusCode.Conflict);
                    var body = await resConfirm.Content.ReadAsStringAsync();
                    body.Should().Contain("INSUFFICIENT_STOCK");

                    brBal.OnHand.Should().Be(10m);
                    brBal.Reserved.Should().Be(8m);
                    whBal.OnHand.Should().Be(10m);
                    whBal.Reserved.Should().Be(8m);

                    var dbCount = await verifyCtx.StockCounts.FirstAsync(c => c.Id == count.Id);
                    dbCount.Status.Should().Be("Counting");
                    dbCount.GeneratedAdjustmentId.Should().BeNull();
                }
                else
                {
                    // Confirm thắng trước (200): Admin phải bị 409 INSUFFICIENT_STOCK
                    resConfirm.StatusCode.Should().Be(HttpStatusCode.OK);
                    resOrder.StatusCode.Should().Be(HttpStatusCode.Conflict);
                    var body = await resOrder.Content.ReadAsStringAsync();
                    body.Should().Contain("INSUFFICIENT_STOCK");

                    brBal.OnHand.Should().Be(5m);
                    brBal.Reserved.Should().Be(0m);
                    whBal.OnHand.Should().Be(5m);
                    whBal.Reserved.Should().Be(0m);

                    var dbCount = await verifyCtx.StockCounts.FirstAsync(c => c.Id == count.Id);
                    dbCount.Status.Should().Be("Confirmed");
                    dbCount.GeneratedAdjustmentId.Should().NotBeNull();
                }
            }
        }
    }

    // =========================================================================
    // AC-09: Fault Injection & Atomic Rollback
    // =========================================================================

    [Fact]
    public async Task AC09_FaultInjection_MidstreamFailure_CreateRecordConfirm_RollsBackCleanly_AndRetrySucceeds()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);

        var interceptor = new MidstreamFailureInterceptor { ShouldFail = true };
        var (faultyContext, faultyTenantService) = CreatePostgresDbContext(infra.TenantId, interceptor);
        var faultyService = new StockCountService(faultyContext, faultyTenantService, new WacCalculator(), Microsoft.Extensions.Logging.Abstractions.NullLogger<StockCountService>.Instance);

        var requestKey = Guid.NewGuid();
        var createDto = new CreateStockCountRequestDto
        {
            RequestKey = requestKey,
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        };

        // =====================================================================
        // 1. Fault khi CreateStockCountAsync
        // =====================================================================
        var baselineBeforeCreate = await CaptureFullBaselineAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.Sku1Id, null);

        interceptor.ShouldFail = true;
        var actCreate = () => faultyService.CreateStockCountAsync(createDto, infra.UserId);
        await actCreate.Should().ThrowAsync<MidstreamStockCountFaultException>();

        // Đối soát CSDL độc lập: 100% trường dữ liệu khớp với baseline trước khi gọi Create
        await AssertMatchesBaselineAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.Sku1Id, null, baselineBeforeCreate, "CreateFault");

        // Retry Create thành công đúng 1 lần
        interceptor.ShouldFail = false;
        var created = await faultyService.CreateStockCountAsync(createDto, infra.UserId);
        created.Status.Should().Be("Draft");

        // =====================================================================
        // 2. Fault khi RecordCountsAsync
        // =====================================================================
        var baselineBeforeRecord = await CaptureFullBaselineAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.Sku1Id, created.Id);

        var recordDto = new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 15m } }
        };

        interceptor.ShouldFail = true;
        var actRecord = () => faultyService.RecordCountsAsync(created.Id, recordDto, infra.UserId);
        await actRecord.Should().ThrowAsync<MidstreamStockCountFaultException>();

        // Đối soát CSDL độc lập: 100% trường dữ liệu khớp với baseline trước khi gọi Record
        await AssertMatchesBaselineAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.Sku1Id, created.Id, baselineBeforeRecord, "RecordFault");

        // Retry Record thành công đúng 1 lần
        interceptor.ShouldFail = false;
        var recorded = await faultyService.RecordCountsAsync(created.Id, recordDto, infra.UserId);
        recorded.Status.Should().Be("Counting");

        // =====================================================================
        // 3. Fault khi ConfirmStockCountAsync
        // =====================================================================
        var baselineBeforeConfirm = await CaptureFullBaselineAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.Sku1Id, created.Id);

        interceptor.ShouldFail = true;
        var actConfirm = () => faultyService.ConfirmStockCountAsync(created.Id, new ConfirmStockCountRequestDto(), infra.UserId);
        await actConfirm.Should().ThrowAsync<MidstreamStockCountFaultException>();

        // Bằng chứng EF Core đã sinh và gửi SQL cho StockCount, StockCountItem, Adjustment, Ledger và Balances trước khi ném lỗi
        interceptor.LastSavedEntityCount.Should().BeGreaterThanOrEqualTo(5, "Đã đi qua các câu lệnh SQL cập nhật trạng thái, chứng từ điều chỉnh, bút toán ledger và số dư tồn kho trước khi fault.");

        // Đối soát CSDL độc lập: 100% trường dữ liệu khớp với baseline trước khi gọi Confirm
        await AssertMatchesBaselineAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.Sku1Id, created.Id, baselineBeforeConfirm, "ConfirmFault");

        // Retry Confirm thành công đúng 1 lần
        interceptor.ShouldFail = false;
        var confirmed = await faultyService.ConfirmStockCountAsync(created.Id, new ConfirmStockCountRequestDto(), infra.UserId);
        confirmed.Status.Should().Be("Confirmed");
        confirmed.GeneratedAdjustmentId.Should().NotBeNull();

        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var c = await verifyContext.StockCounts.FirstAsync(x => x.Id == created.Id);
            c.Status.Should().Be("Confirmed");
            c.GeneratedAdjustmentId.Should().Be(confirmed.GeneratedAdjustmentId);
            c.ConfirmedAt.Should().NotBeNull();
            c.ConfirmedBy.Should().Be(infra.UserId);

            var branchBal = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            branchBal.OnHand.Should().Be(15m, "Tồn kho chi nhánh được cập nhật chính xác lên 15 sau retry confirm.");
            branchBal.AverageCost.Should().Be(50000m);
            branchBal.LastSequence.Should().Be(2L);

            var whBal = await verifyContext.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == infra.Sku1Id);
            whBal.OnHand.Should().Be(15m, "Tồn kho tại kho được cập nhật chính xác lên 15 sau retry confirm.");
            whBal.LastSequence.Should().Be(2L, "Sequence kho tăng chính xác lên 2 sau retry confirm.");

            var adjs = await verifyContext.InventoryAdjustments.CountAsync();
            adjs.Should().Be(1, "Có đúng 1 chứng từ điều chỉnh tồn được lưu.");

            var txCount = await verifyContext.InventoryTransactions.CountAsync(t => t.AdjustmentItemId.HasValue);
            txCount.Should().Be(1, "Có đúng 1 bút toán ledger được ghi nhận khi retry confirm.");
        }
    }

    // =========================================================================
    // AC-10: Phân quyền RBAC, Cross-Tenant Isolation & Biên số học
    // =========================================================================

    [Fact]
    public async Task AC10_Security_RBAC_CrossTenant_AndNumericLimits()
    {
        var infra = await SetupInfrastructureAsync();

        // Chuẩn bị 1 phiếu kiểm kê Draft thuộc Tenant A
        var ownerTokenA = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenA);

        var createResA = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        });
        var countA = await createResA.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
        countA.Should().NotBeNull();

        // 1. 401 Unauthorized khi không có token xác thực trên ĐỦ 4 ENDPOINTS
        _client.DefaultRequestHeaders.Authorization = null;

        var unauthCreate = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto());
        unauthCreate.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "Create yêu cầu xác thực 401.");

        var unauthRecord = await _client.PostAsJsonAsync($"/api/stock-counts/{countA!.Id}/record", new RecordStockCountBatchRequestDto());
        unauthRecord.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "Record yêu cầu xác thực 401.");

        var unauthConfirm = await _client.PostAsJsonAsync($"/api/stock-counts/{countA.Id}/confirm", new ConfirmStockCountRequestDto());
        unauthConfirm.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "Confirm yêu cầu xác thực 401.");

        var unauthCancel = await _client.PostAsJsonAsync($"/api/stock-counts/{countA.Id}/cancel", new CancelStockCountRequestDto());
        unauthCancel.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "Cancel yêu cầu xác thực 401.");

        // 2. 403 Forbidden khi đăng nhập bằng vai trò Thu ngân (Cashier) trên ĐỦ 4 ENDPOINTS
        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);

        var cashierCreate = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto());
        cashierCreate.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Cashier không có quyền tạo kiểm kê 403.");

        var cashierRecord = await _client.PostAsJsonAsync($"/api/stock-counts/{countA.Id}/record", new RecordStockCountBatchRequestDto());
        cashierRecord.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Cashier không có quyền ghi số đếm 403.");

        var cashierConfirm = await _client.PostAsJsonAsync($"/api/stock-counts/{countA.Id}/confirm", new ConfirmStockCountRequestDto());
        cashierConfirm.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Cashier không có quyền chốt kiểm kê 403.");

        var cashierCancel = await _client.PostAsJsonAsync($"/api/stock-counts/{countA.Id}/cancel", new CancelStockCountRequestDto());
        cashierCancel.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Cashier không có quyền hủy kiểm kê 403.");

        // 3. Cross-Tenant Negative: Đăng nhập Owner Tenant B và cố gắng thao tác trên tài nguyên của Tenant A -> 404 Not Found trên ĐỦ 4 ENDPOINTS
        var ownerTokenB = await LoginAsAsync("owner@shopb.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenB);

        var crossCreate = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        });
        crossCreate.StatusCode.Should().Be(HttpStatusCode.NotFound, "Tenant B không thể tạo đợt kiểm kê với kho của Tenant A (selected mode).");

        var crossCreateAll = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = null
        });
        crossCreateAll.StatusCode.Should().Be(HttpStatusCode.NotFound, "Tenant B không thể tạo đợt kiểm kê ALL mode với kho của Tenant A.");

        var crossRecord = await _client.PostAsJsonAsync($"/api/stock-counts/{countA.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 5m } }
        });
        crossRecord.StatusCode.Should().Be(HttpStatusCode.NotFound, "Tenant B không thể ghi số đếm cho phiếu của Tenant A.");

        var crossConfirm = await _client.PostAsJsonAsync($"/api/stock-counts/{countA.Id}/confirm", new ConfirmStockCountRequestDto());
        crossConfirm.StatusCode.Should().Be(HttpStatusCode.NotFound, "Tenant B không thể chốt kiểm kê cho phiếu của Tenant A.");

        var crossCancel = await _client.PostAsJsonAsync($"/api/stock-counts/{countA.Id}/cancel", new CancelStockCountRequestDto());
        crossCancel.StatusCode.Should().Be(HttpStatusCode.NotFound, "Tenant B không thể hủy phiếu kiểm kê của Tenant A.");

        // Đối soát CSDL độc lập: Dữ liệu Tenant A hoàn toàn không bị biến động
        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var c = await verifyContext.StockCounts.Include(x => x.Items).FirstAsync(x => x.Id == countA.Id);
            c.Status.Should().Be("Draft");
            c.GeneratedAdjustmentId.Should().BeNull();
            c.Items.First().CountedQuantity.Should().BeNull();

            var adjs = await verifyContext.InventoryAdjustments.CountAsync(a => a.TenantId == infra.TenantId);
            adjs.Should().Be(0);
        }
    }

    [Fact]
    public async Task AC10_Validation_StructuralGuards_EmptyGuid_DuplicateSku_InvalidQuantity_BranchWarehouseMismatch()
    {
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        async Task AssertBadRequest(CreateStockCountRequestDto dto, string expectedErrorSubstr)
        {
            var res = await _client.PostAsJsonAsync("/api/stock-counts", dto);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = await res.Content.ReadAsStringAsync();
            body.Should().Contain(expectedErrorSubstr);
        }

        CreateStockCountRequestDto ValidDto() => new()
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id }
        };

        // 1. Kiểm tra Guid.Empty cho các trường khóa khi Create
        var dtoEmptyKey = ValidDto(); dtoEmptyKey.RequestKey = Guid.Empty;
        await AssertBadRequest(dtoEmptyKey, "VALIDATION_ERROR");

        var dtoEmptyBranch = ValidDto(); dtoEmptyBranch.BranchId = Guid.Empty;
        await AssertBadRequest(dtoEmptyBranch, "VALIDATION_ERROR");

        var dtoEmptyWh = ValidDto(); dtoEmptyWh.WarehouseId = Guid.Empty;
        await AssertBadRequest(dtoEmptyWh, "VALIDATION_ERROR");

        // 2. Kiểm tra sai cặp Kho - Chi nhánh: WarehouseB1 thuộc BranchB, nhưng truyền BranchA
        var dtoMismatch = ValidDto();
        dtoMismatch.WarehouseId = infra.WarehouseB1Id;
        await AssertBadRequest(dtoMismatch, "không thuộc chi nhánh");

        // 3. Kiểm tra danh sách SKU khi Create: Guid.Empty hoặc Duplicate
        var dtoEmptySku = ValidDto();
        dtoEmptySku.SkuIds = new List<Guid> { Guid.Empty };
        await AssertBadRequest(dtoEmptySku, "VALIDATION_ERROR");

        var dtoDuplicateSku = ValidDto();
        dtoDuplicateSku.SkuIds = new List<Guid> { infra.Sku1Id, infra.Sku1Id };
        await AssertBadRequest(dtoDuplicateSku, "VALIDATION_ERROR");

        // 4. Tạo phiếu kiểm kê hợp lệ để kiểm tra validation cho Record và Confirm
        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", ValidDto());
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        // 4.1. Validation khi Record: danh sách rỗng, SkuId rỗng, duplicate SKU, SKU không thuộc đợt, số lượng âm, scale > 3, tràn số
        var badRecordEmptyList = await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto>()
        });
        badRecordEmptyList.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var badRecordEmptySku = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = Guid.Empty, CountedQuantity = 5m } }
        });
        badRecordEmptySku.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var badRecordDuplicateSku = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto>
            {
                new() { SkuId = infra.Sku1Id, CountedQuantity = 5m },
                new() { SkuId = infra.Sku1Id, CountedQuantity = 6m }
            }
        });
        badRecordDuplicateSku.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var badRecordForeignSku = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku2Id, CountedQuantity = 5m } }
        });
        badRecordForeignSku.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var badRecordNeg = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = -1m } }
        });
        badRecordNeg.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var badRecordScale = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 1.2345m } }
        });
        badRecordScale.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var badRecordOverflow = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 1_000_000_000_000_000_000m } }
        });
        badRecordOverflow.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var badRecordNegCost = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 5m, AdjustmentUnitCost = -100m } }
        });
        badRecordNegCost.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var badRecordCostScale = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 5m, AdjustmentUnitCost = 100.1234567m } }
        });
        badRecordCostScale.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Ghi số đếm hợp lệ để chuyển sang Counting
        var goodRecord = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 10m } }
        });
        goodRecord.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4.2. Validation khi Confirm: SkuAdjustmentCosts chứa SKU ngoài đợt, đơn giá âm, đơn giá scale > 6, đơn giá tràn số
        var badConfirmForeignSku = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto
        {
            SkuAdjustmentCosts = new Dictionary<Guid, decimal> { [infra.Sku2Id] = 50000m }
        });
        badConfirmForeignSku.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var badConfirmNegCost = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto
        {
            SkuAdjustmentCosts = new Dictionary<Guid, decimal> { [infra.Sku1Id] = -50000m }
        });
        badConfirmNegCost.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var badConfirmCostScale = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto
        {
            SkuAdjustmentCosts = new Dictionary<Guid, decimal> { [infra.Sku1Id] = 50000.1234567m }
        });
        badConfirmCostScale.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var badConfirmCostOverflow = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto
        {
            SkuAdjustmentCosts = new Dictionary<Guid, decimal> { [infra.Sku1Id] = 1_000_000_000_000_000_000m }
        });
        badConfirmCostOverflow.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // =========================================================================
    // AC-11: Tương thích ngược schema & Upgrade migration
    // =========================================================================

    [Fact]
    public async Task AC11_SchemaDrift_ValidatorDetectsMissingColumns_AndUpgradeIdempotent()
    {
        PostgresTestEnvironment.AssertSafeTestTarget(PostgresTestEnvironment.DatabaseName);
        var migrator = new DatabaseMigrationService();

        // 1. Thao tác trên test database: Xóa cột generated_adjustment_id để mô phỏng schema drift
        await using (var adminConn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await adminConn.OpenAsync();
            await using var dropCmd = new NpgsqlCommand(@"
                ALTER TABLE public.stock_counts DROP CONSTRAINT IF EXISTS fk_stock_counts_generated_adjustment;
                ALTER TABLE public.stock_counts DROP CONSTRAINT IF EXISTS uq_stock_counts_4;
                ALTER TABLE public.stock_counts DROP COLUMN IF EXISTS generated_adjustment_id;
            ", adminConn);
            await dropCmd.ExecuteNonQueryAsync();
        }

        // 2. Chạy validator -> Báo Schema Drift (Success = false)
        var driftCheck = await migrator.InitializeOrBaselineDatabaseAsync(PostgresTestEnvironment.AdminDatabaseConnectionString);
        driftCheck.Success.Should().BeFalse("Validator phải phát hiện thiếu cột generated_adjustment_id.");
        driftCheck.Message.Should().Contain("Phát hiện sai lệch cấu trúc");

        // 3. Chạy UpgradeDatabaseSchemaAsync 2 lần liên tiếp -> Đều thành công
        var upgrade1 = await migrator.UpgradeDatabaseSchemaAsync(PostgresTestEnvironment.AdminDatabaseConnectionString);
        upgrade1.Success.Should().BeTrue();

        var upgrade2 = await migrator.UpgradeDatabaseSchemaAsync(PostgresTestEnvironment.AdminDatabaseConnectionString);
        upgrade2.Success.Should().BeTrue("Upgrade lần 2 phải idempotent và thành công.");

        // 4. Chạy lại validator sau nâng cấp -> Phải đạt Success = true
        var cleanCheck = await migrator.InitializeOrBaselineDatabaseAsync(PostgresTestEnvironment.AdminDatabaseConnectionString);
        cleanCheck.Success.Should().BeTrue("Database sau khi upgrade phải vượt qua toàn bộ kiểm tra ràng buộc.");
    }

    [Fact]
    public async Task AC11_Migration_PreExistingLegacyStockCount_UpgradeTwice_WorkflowSucceeds()
    {
        PostgresTestEnvironment.AssertSafeTestTarget(PostgresTestEnvironment.DatabaseName);
        var infra = await SetupInfrastructureAsync();
        // Nhập tồn ban đầu để phục vụ test workflow của phiếu Counting sau nâng cấp
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);

        var migrator = new DatabaseMigrationService();
        var adminConnStr = PostgresTestEnvironment.AdminDatabaseConnectionString;

        try
        {
            await using (var adminConn = new NpgsqlConnection(adminConnStr))
            {
                await adminConn.OpenAsync();

                // 1. Giả lập schema cũ trước Task 4B: Xóa các cột mới của stock_counts
                await using (var dropCmd = new NpgsqlCommand(@"
                    ALTER TABLE public.stock_counts DROP CONSTRAINT IF EXISTS fk_stock_counts_generated_adjustment;
                    ALTER TABLE public.stock_counts DROP CONSTRAINT IF EXISTS uq_stock_counts_4;
                    ALTER TABLE public.stock_counts DROP CONSTRAINT IF EXISTS uq_stock_counts_3;
                    ALTER TABLE public.stock_counts DROP COLUMN IF EXISTS request_key;
                    ALTER TABLE public.stock_counts DROP COLUMN IF EXISTS payload_signature;
                    ALTER TABLE public.stock_counts DROP COLUMN IF EXISTS generated_adjustment_id;
                ", adminConn))
                {
                    await dropCmd.ExecuteNonQueryAsync();
                }

                // 2. Chèn 3 phiếu kiểm kê legacy vào schema cũ: Draft, Counting, và Confirmed lịch sử
                var now = DateTimeOffset.UtcNow;
                var legacyDraftId = Guid.NewGuid();
                var legacyCountingId = Guid.NewGuid();
                var legacyConfirmedId = Guid.NewGuid();

                await using (var insertCmd = new NpgsqlCommand(@"
                    -- 1. Phiếu Draft legacy
                    INSERT INTO public.stock_counts (id, tenant_id, number, branch_id, warehouse_id, status, created_by, created_at)
                    VALUES (@draftId, @tid, 'CNT-LEG-DRAFT-01', @bid, @wid, 'Draft', @uid, @now);
                    INSERT INTO public.stock_count_items (id, tenant_id, stock_count_id, sku_id, expected_quantity, balance_sequence_at_snapshot, created_at)
                    VALUES (gen_random_uuid(), @tid, @draftId, @sid, 10, 1, @now);

                    -- 2. Phiếu Counting legacy
                    INSERT INTO public.stock_counts (id, tenant_id, number, branch_id, warehouse_id, status, created_by, created_at)
                    VALUES (@countId, @tid, 'CNT-LEG-COUNT-01', @bid, @wid, 'Counting', @uid, @now);
                    INSERT INTO public.stock_count_items (id, tenant_id, stock_count_id, sku_id, expected_quantity, counted_quantity, balance_sequence_at_snapshot, created_at)
                    VALUES (gen_random_uuid(), @tid, @countId, @sid, 10, 12, 1, @now);

                    -- 3. Phiếu Confirmed lịch sử cũ (thỏa mãn ck_stock_counts_confirmed với confirmed_by và confirmed_at)
                    INSERT INTO public.stock_counts (id, tenant_id, number, branch_id, warehouse_id, status, created_by, created_at, confirmed_by, confirmed_at)
                    VALUES (@confId, @tid, 'CNT-LEG-CONF-01', @bid, @wid, 'Confirmed', @uid, @now, @uid, @now);
                    INSERT INTO public.stock_count_items (id, tenant_id, stock_count_id, sku_id, expected_quantity, counted_quantity, balance_sequence_at_snapshot, created_at)
                    VALUES (gen_random_uuid(), @tid, @confId, @sid, 10, 10, 1, @now);
                ", adminConn))
                {
                    insertCmd.Parameters.AddWithValue("draftId", legacyDraftId);
                    insertCmd.Parameters.AddWithValue("countId", legacyCountingId);
                    insertCmd.Parameters.AddWithValue("confId", legacyConfirmedId);
                    insertCmd.Parameters.AddWithValue("tid", infra.TenantId);
                    insertCmd.Parameters.AddWithValue("bid", infra.BranchAId);
                    insertCmd.Parameters.AddWithValue("wid", infra.WarehouseA1Id);
                    insertCmd.Parameters.AddWithValue("uid", infra.UserId);
                    insertCmd.Parameters.AddWithValue("now", now);
                    insertCmd.Parameters.AddWithValue("sid", infra.Sku1Id);
                    await insertCmd.ExecuteNonQueryAsync();
                }

                // 3. Nâng cấp schema 2 lần idempotent
                var up1 = await migrator.UpgradeDatabaseSchemaAsync(adminConnStr);
                up1.Success.Should().BeTrue();

                var up2 = await migrator.UpgradeDatabaseSchemaAsync(adminConnStr);
                up2.Success.Should().BeTrue();

                // 4. Kiểm tra validator xanh
                var check = await migrator.InitializeOrBaselineDatabaseAsync(adminConnStr);
                check.Success.Should().BeTrue();

                // 5. Đối soát lịch sử Confirmed cũ: không tự sinh adjustment / link giả
                await using (var verifyCmd = new NpgsqlCommand(@"
                    SELECT status, generated_adjustment_id FROM public.stock_counts WHERE id = @id;", adminConn))
                {
                    verifyCmd.Parameters.AddWithValue("id", legacyConfirmedId);
                    await using var reader = await verifyCmd.ExecuteReaderAsync();
                    (await reader.ReadAsync()).Should().BeTrue();
                    reader.GetString(0).Should().Be("Confirmed");
                    reader.IsDBNull(1).Should().BeTrue("Lịch sử Confirmed cũ không bị tự tạo adjustment/link giả.");
                }

                var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
                _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

                // Thao tác Confirm/Cancel trên phiếu Confirmed cũ đều bị từ chối 409 INVALID_STATE_TRANSITION
                var badReconfirm = await _client.PostAsJsonAsync($"/api/stock-counts/{legacyConfirmedId}/confirm", new ConfirmStockCountRequestDto());
                badReconfirm.StatusCode.Should().Be(HttpStatusCode.Conflict);

                var badCancelConf = await _client.PostAsJsonAsync($"/api/stock-counts/{legacyConfirmedId}/cancel", new CancelStockCountRequestDto());
                badCancelConf.StatusCode.Should().Be(HttpStatusCode.Conflict);

                // 6. Thao tác trên phiếu Draft legacy: Record chuyển trạng thái sang Counting, Cancel thành công
                var recRes = await _client.PostAsJsonAsync($"/api/stock-counts/{legacyDraftId}/record", new RecordStockCountBatchRequestDto
                {
                    Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 11m } }
                });
                recRes.StatusCode.Should().Be(HttpStatusCode.OK);

                var cancelRes = await _client.PostAsJsonAsync($"/api/stock-counts/{legacyDraftId}/cancel", new CancelStockCountRequestDto { Reason = "Legacy Cancel" });
                cancelRes.StatusCode.Should().Be(HttpStatusCode.OK);

                // 7. Thao tác trên phiếu Counting legacy: Confirm thành công, tạo đúng 1 adjustment và liên kết generated_adjustment_id
                var confRes = await _client.PostAsJsonAsync($"/api/stock-counts/{legacyCountingId}/confirm", new ConfirmStockCountRequestDto());
                confRes.StatusCode.Should().Be(HttpStatusCode.OK, "Phiếu Counting legacy phải confirm thành công sau nâng cấp schema.");
                var confData = await confRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
                confData!.Status.Should().Be("Confirmed");
                confData.GeneratedAdjustmentId.Should().NotBeNull();

                // Đối soát DB: link trỏ đến bản ghi inventory_adjustments hợp lệ
                await using (var verifyCmd = new NpgsqlCommand(@"
                    SELECT COUNT(*) FROM public.inventory_adjustments WHERE id = @adjId;", adminConn))
                {
                    verifyCmd.Parameters.AddWithValue("adjId", confData.GeneratedAdjustmentId!.Value);
                    var count = (long)(await verifyCmd.ExecuteScalarAsync())!;
                    count.Should().Be(1, "Phiếu điều chỉnh tồn được sinh thực tế trong CSDL.");
                }

                // 8. Đối soát ràng buộc FK và Unique (AC-11)
                // 8.1. FK: Gán generated_adjustment_id không tồn tại -> bắt được PostgresException 23503
                var actFkViolation = async () =>
                {
                    await using var fkCmd = new NpgsqlCommand(@"
                        UPDATE public.stock_counts SET generated_adjustment_id = gen_random_uuid() WHERE id = @id;", adminConn);
                    fkCmd.Parameters.AddWithValue("id", legacyDraftId);
                    await fkCmd.ExecuteNonQueryAsync();
                };
                (await actFkViolation.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23503", "Vi phạm khóa ngoại fk_stock_counts_generated_adjustment.");

                // 8.2. Unique: Gán trùng generated_adjustment_id cho 2 phiếu khác nhau -> bắt được PostgresException 23505
                var actUniqueViolation = async () =>
                {
                    await using var uqCmd = new NpgsqlCommand(@"
                        UPDATE public.stock_counts SET generated_adjustment_id = @existingAdjId WHERE id = @id;", adminConn);
                    uqCmd.Parameters.AddWithValue("existingAdjId", confData.GeneratedAdjustmentId!.Value);
                    uqCmd.Parameters.AddWithValue("id", legacyDraftId);
                    await uqCmd.ExecuteNonQueryAsync();
                };
                (await actUniqueViolation.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23505", "Vi phạm ràng buộc duy nhất uq_stock_counts_4.");
            }
        }
        finally
        {
            await migrator.UpgradeDatabaseSchemaAsync(adminConnStr);
        }
    }
}
