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

        public override ValueTask<int> SavedChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (ShouldFail)
            {
                throw new MidstreamStockCountFaultException("Mô phỏng sự cố mạng/crash sau khi SaveChanges nhưng trước khi COMMIT transaction kiểm kê.");
            }
            return base.SavedChangesAsync(eventData, result, cancellationToken);
        }
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
    public async Task AC03_Confirm_IncompleteStockCount_Returns400BadRequest()
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

        // Confirm khi chưa đếm hết -> 400 INCOMPLETE_STOCK_COUNT
        var confirmRes = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        confirmRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await confirmRes.Content.ReadAsStringAsync();
        body.Should().Contain("INCOMPLETE_STOCK_COUNT");
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

        // 3. Confirm phiếu kiểm kê -> Phát hiện sequence drift và từ chối 409 STOCK_COUNT_DRIFT_DETECTED
        var confirmRes = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        confirmRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await confirmRes.Content.ReadAsStringAsync();
        body.Should().Contain("STOCK_COUNT_DRIFT_DETECTED");

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
        body.Should().Contain("STOCK_COUNT_DRIFT_DETECTED");
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
        body.Should().Contain("INSUFFICIENT_STOCK_FOR_RESERVATION");

        // Đối soát CSDL: Reserved vẫn là 8, OnHand vẫn là 10, đơn hàng online được bảo vệ an toàn
        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var bal = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            bal.OnHand.Should().Be(10m);
            bal.Reserved.Should().Be(8m);
            bal.AverageCost.Should().Be(60000m);
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
    public async Task AC07_StateMachine_ConfirmAlreadyConfirmed_ReplaysIdempotently()
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

        // Confirm lần 2: 200 OK replay idempotent
        var confirmRes2 = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        confirmRes2.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmed2 = await confirmRes2.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
        confirmed2!.IsReplay.Should().BeTrue();
        confirmed2.Id.Should().Be(confirmed1.Id);
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

        // Tất cả các response phải là 200 OK (idempotent replay) hoặc 409 INVALID_STATE_TRANSITION
        (okResponses.Count + conflictResponses.Count).Should().Be(concurrency);
        okResponses.Count.Should().BeGreaterThanOrEqualTo(1);

        // Đối soát CSDL: Chỉ có đúng 1 InventoryAdjustment và đúng 1 bút toán Ledger IN
        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var adjCount = await verifyContext.InventoryAdjustments.CountAsync(a => a.TenantId == infra.TenantId);
            adjCount.Should().Be(1, "Chỉ duy nhất 1 phiếu điều chỉnh tồn được sinh ra trong môi trường đồng thời.");

            var bal = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            bal.OnHand.Should().Be(15m, "Tồn kho chỉ được cộng 1 lần duy nhất từ 10 lên 15.");
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
    public async Task AC08_Concurrency_TwoCountsCompeteForSameSku_FirstSucceedsSecondDetectsDrift()
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

        // Confirm A thành công -> Sequence của SKU1 tại Kho A1 tăng
        var confirmA = await _client.PostAsJsonAsync($"/api/stock-counts/{countA.Id}/confirm", new ConfirmStockCountRequestDto());
        confirmA.StatusCode.Should().Be(HttpStatusCode.OK);

        // Confirm B bị chặn bởi Sequence Drift (409 STOCK_COUNT_DRIFT_DETECTED)
        var confirmB = await _client.PostAsJsonAsync($"/api/stock-counts/{countB.Id}/confirm", new ConfirmStockCountRequestDto());
        confirmB.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var bodyB = await confirmB.Content.ReadAsStringAsync();
        bodyB.Should().Contain("STOCK_COUNT_DRIFT_DETECTED");
    }

    // =========================================================================
    // AC-09: Fault Injection & Atomic Rollback
    // =========================================================================

    [Fact]
    public async Task AC09_FaultInjection_MidstreamFailureDuringConfirm_RollsBackCleanly_RetrySucceeds()
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

        // 1. Fault khi CreateStockCountAsync
        interceptor.ShouldFail = true;
        var actCreate = () => faultyService.CreateStockCountAsync(createDto, infra.UserId);
        await actCreate.Should().ThrowAsync<MidstreamStockCountFaultException>();

        // Đối soát CSDL: Hoàn toàn không có stock_counts nào
        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var count = await verifyContext.StockCounts.CountAsync();
            count.Should().Be(0, "Transaction tạo phiếu đã rollback 100% khi gặp lỗi midstream.");
        }

        // Retry Create thành công
        interceptor.ShouldFail = false;
        var created = await faultyService.CreateStockCountAsync(createDto, infra.UserId);
        created.Status.Should().Be("Draft");

        // 2. Ghi nhận số đếm hợp lệ
        var recordDto = new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 15m } }
        };
        var recorded = await faultyService.RecordCountsAsync(created.Id, recordDto, infra.UserId);
        recorded.Status.Should().Be("Counting");

        // 3. Fault khi ConfirmStockCountAsync
        interceptor.ShouldFail = true;
        var actConfirm = () => faultyService.ConfirmStockCountAsync(created.Id, new ConfirmStockCountRequestDto(), infra.UserId);
        await actConfirm.Should().ThrowAsync<MidstreamStockCountFaultException>();

        // Đối soát CSDL: Phiếu vẫn ở trạng thái Counting, KHÔNG có InventoryAdjustment, KHÔNG có Ledger mới, Tồn kho vẫn là 10
        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var c = await verifyContext.StockCounts.FirstAsync(x => x.Id == created.Id);
            c.Status.Should().Be("Counting");
            c.GeneratedAdjustmentId.Should().BeNull();

            var adjs = await verifyContext.InventoryAdjustments.CountAsync();
            adjs.Should().Be(0, "Không rò rỉ bản ghi InventoryAdjustment khi confirm bị lỗi giữa chừng.");

            var bal = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            bal.OnHand.Should().Be(10m, "Tồn kho không được thay đổi khi confirm bị rollback.");
        }

        // Retry Confirm thành công đúng 1 lần
        interceptor.ShouldFail = false;
        var confirmed = await faultyService.ConfirmStockCountAsync(created.Id, new ConfirmStockCountRequestDto(), infra.UserId);
        confirmed.Status.Should().Be("Confirmed");
        confirmed.GeneratedAdjustmentId.Should().NotBeNull();

        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var bal = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            bal.OnHand.Should().Be(15m, "Tồn kho được cập nhật chính xác lên 15 sau khi retry confirm thành công.");

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

        // 1. 401 Unauthorized khi không có token xác thực
        _client.DefaultRequestHeaders.Authorization = null;
        var unauthRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto());
        unauthRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 2. 403 Forbidden khi đăng nhập bằng vai trò Thu ngân (Cashier)
        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);

        var cashierCreate = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto());
        cashierCreate.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var randomId = Guid.NewGuid();
        var cashierRecord = await _client.PostAsJsonAsync($"/api/stock-counts/{randomId}/record", new RecordStockCountBatchRequestDto());
        cashierRecord.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var cashierConfirm = await _client.PostAsJsonAsync($"/api/stock-counts/{randomId}/confirm", new ConfirmStockCountRequestDto());
        cashierConfirm.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var cashierCancel = await _client.PostAsJsonAsync($"/api/stock-counts/{randomId}/cancel", new CancelStockCountRequestDto());
        cashierCancel.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 3. Đăng nhập lại Owner Tenant A
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

        // 4. Cross-Tenant: Đăng nhập Owner Tenant B và cố gắng thao tác trên phiếu hoặc tài nguyên của Tenant A -> 404 Not Found
        var ownerTokenB = await LoginAsAsync("owner@shopb.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenB);

        var crossRecord = await _client.PostAsJsonAsync($"/api/stock-counts/{countA!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 5m } }
        });
        crossRecord.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var crossConfirm = await _client.PostAsJsonAsync($"/api/stock-counts/{countA.Id}/confirm", new ConfirmStockCountRequestDto());
        crossConfirm.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var crossCancel = await _client.PostAsJsonAsync($"/api/stock-counts/{countA.Id}/cancel", new CancelStockCountRequestDto());
        crossCancel.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Đối soát dữ liệu Tenant A không bị biến động
        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var c = await verifyContext.StockCounts.FirstAsync(x => x.Id == countA.Id);
            c.Status.Should().Be("Draft");
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

        // 1. Kiểm tra Guid.Empty cho các trường khóa
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

        // 3. Kiểm tra validation khi Record: SkuId rỗng, số lượng âm, scale > 3
        var createRes = await _client.PostAsJsonAsync("/api/stock-counts", ValidDto());
        var count = await createRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);

        var badRecordEmptySku = await _client.PostAsJsonAsync($"/api/stock-counts/{count!.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = Guid.Empty, CountedQuantity = 5m } }
        });
        badRecordEmptySku.StatusCode.Should().Be(HttpStatusCode.BadRequest);

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

                // 2. Chèn 1 phiếu kiểm kê legacy vào schema cũ
                var now = DateTimeOffset.UtcNow;
                var legacyId = Guid.NewGuid();
                var itemId = Guid.NewGuid();

                await using (var insertCmd = new NpgsqlCommand(@"
                    INSERT INTO public.stock_counts (id, tenant_id, number, branch_id, warehouse_id, status, created_by, created_at)
                    VALUES (@id, @tid, 'CNT-LEG-PRE-00001', @bid, @wid, 'Draft', @uid, @now);
                    INSERT INTO public.stock_count_items (id, tenant_id, stock_count_id, sku_id, expected_quantity, balance_sequence_at_snapshot, created_at)
                    VALUES (@iid, @tid, @id, @sid, 10, 1, @now);
                ", adminConn))
                {
                    insertCmd.Parameters.AddWithValue("id", legacyId);
                    insertCmd.Parameters.AddWithValue("tid", infra.TenantId);
                    insertCmd.Parameters.AddWithValue("bid", infra.BranchAId);
                    insertCmd.Parameters.AddWithValue("wid", infra.WarehouseA1Id);
                    insertCmd.Parameters.AddWithValue("uid", infra.UserId);
                    insertCmd.Parameters.AddWithValue("now", now);
                    insertCmd.Parameters.AddWithValue("iid", itemId);
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

                // 5. Thao tác trên chứng từ legacy qua API: Cancel thành công
                var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
                _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

                var cancelRes = await _client.PostAsJsonAsync($"/api/stock-counts/{legacyId}/cancel", new CancelStockCountRequestDto { Reason = "Legacy Cancel" });
                cancelRes.StatusCode.Should().Be(HttpStatusCode.OK);

                await using (var verifyCmd = new NpgsqlCommand(@"
                    SELECT status FROM public.stock_counts WHERE id = @id;", adminConn))
                {
                    verifyCmd.Parameters.AddWithValue("id", legacyId);
                    var status = (string)(await verifyCmd.ExecuteScalarAsync())!;
                    status.Should().Be("Cancelled");
                }
            }
        }
        finally
        {
            await migrator.UpgradeDatabaseSchemaAsync(adminConnStr);
        }
    }
}
