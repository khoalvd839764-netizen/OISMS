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
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.StockTransfers.Dtos;
using OISM.Application.Features.StockTransfers.Services;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Bộ kiểm thử tích hợp thực thi trên cơ sở dữ liệu PostgreSQL 16+ thật (database: oisms_test).
/// Kiểm chứng toàn diện chức năng Chuyển kho nội bộ 2 bước (Task S3-BE1-T04A-STOCK-TRANSFER / v1.1):
/// AC-01: Tạo phiếu chuyển kho Draft multi-SKU (POST /api/stock-transfers, 201 Created, zero stock mutation).
/// AC-02: Idempotency tuyệt đối theo RequestKey (replay 200 OK, payload mismatch 409, replay after status changes/inactivation).
/// AC-03: Bước 1 Dispatch (trừ OnHand kho/chi nhánh xuất, ghi Ledger OUT, snapshot WAC 6 decimals, rollback khi thiếu tồn).
/// AC-04: Bước 2 Receive (cộng OnHand kho/chi nhánh đích, tính lại WAC đích qua IWacCalculator, ghi Ledger IN, tự khởi tạo số dư đích, intra-branch).
/// AC-05: State machine & Cancel an toàn (Draft -> Cancelled, chặn hủy khi InTransit/Received, chặn sai trạng thái).
/// AC-06: Concurrency & Deadlock Freedom (50 Drafts tranh 1 tồn, concurrent dispatch/cancel, bidirectional A->B & B->A).
/// AC-07: Fault Injection & Atomic Rollback (Midstream interceptor rollback 100%, retry thành công cho Create, Dispatch, Receive).
/// AC-08: Phân quyền RBAC (401 unauth, 403 Cashier), Cross-tenant 404, Kiểm tra biên số lượng (null items, scale >3, magnitude).
/// AC-09: Tương thích ngược schema & migration (Cài mới, UpgradeDatabaseSchemaAsync, phát hiện schema drift, legacy transfer null key).
/// </summary>
public class StockTransferIntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public StockTransferIntegrationTests(WebApplicationFactoryWithPostgres factory)
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

    public class MidstreamTransferFaultException : Exception
    {
        public MidstreamTransferFaultException(string message) : base(message) { }
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
                throw new MidstreamTransferFaultException("Mô phỏng sự cố mạng/crash sau khi SaveChanges nhưng trước khi COMMIT transaction chuyển kho.");
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
        string Sku2Code);

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
                sku2.SKU);
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
                Note = "Seed tồn kho cho stock transfer test",
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
    // AC-01: Tạo phiếu chuyển kho Draft multi-SKU (POST /api/stock-transfers)
    // =========================================================================
    [Fact]
    public async Task AC01_CreateTransfer_ValidDraft_MultiSku_Returns201Created_ZeroStockMutation()
    {
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var requestKey = Guid.NewGuid();
        var requestDto = new CreateStockTransferRequestDto
        {
            RequestKey = requestKey,
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Note = "Chuyển kho liên chi nhánh AC-01",
            Items = new List<StockTransferItemRequestDto>
            {
                new() { SkuId = infra.Sku1Id, Quantity = 10m },
                new() { SkuId = infra.Sku2Id, Quantity = 5m }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/stock-transfers", requestDto);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, $"Tạo phiếu chuyển kho thất bại: {body}");

        var transfer = await response.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);
        transfer.Should().NotBeNull();
        transfer!.Status.Should().Be("Draft");
        transfer.Number.Should().StartWith("TRF-");
        transfer.RequestKey.Should().Be(requestKey);
        transfer.FromBranchId.Should().Be(infra.BranchAId);
        transfer.ToBranchId.Should().Be(infra.BranchBId);
        transfer.FromWarehouseId.Should().Be(infra.WarehouseA1Id);
        transfer.ToWarehouseId.Should().Be(infra.WarehouseB1Id);
        transfer.DispatchedAt.Should().BeNull();
        transfer.ReceivedAt.Should().BeNull();
        transfer.ReceivedBy.Should().BeNull();
        transfer.Items.Should().HaveCount(2);

        var (context, _) = CreatePostgresDbContext(infra.TenantId);
        await using (context)
        {
            var dbTransfer = await context.StockTransfers
                .Include(t => t.Items)
                .FirstOrDefaultAsync(t => t.Id == transfer.Id);
            dbTransfer.Should().NotBeNull();
            dbTransfer!.Status.Should().Be("Draft");
            dbTransfer.Items.Should().HaveCount(2);
            dbTransfer.Items.All(i => i.UnitCost == null).Should().BeTrue("Trạng thái Draft chưa được chụp UnitCost.");

            var hasLedger = await context.InventoryTransactions
                .AnyAsync(t => t.TransferItemId != null && dbTransfer.Items.Select(i => i.Id).Contains(t.TransferItemId.Value));
            hasLedger.Should().BeFalse("Draft tuyệt đối không được ghi sổ cái tồn kho.");
        }
    }

    [Fact]
    public async Task AC01_CreateTransfer_ConcurrentTwentyDistinctKeys_GeneratesUniqueNumbers_Zero500Errors()
    {
        // Kiểm chứng T4A-F01: 20 request tạo phiếu đồng thời với key khác nhau phải tạo đủ 20 phiếu duy nhất, không lỗi 500
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var requests = Enumerable.Range(0, 20).Select(i => new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Note = $"Concurrent create #{i}",
            Items = new List<StockTransferItemRequestDto>
            {
                new() { SkuId = infra.Sku1Id, Quantity = 1m }
            }
        }).ToList();

        var tasks = requests.Select(r => _client.PostAsJsonAsync("/api/stock-transfers", r));
        var responses = await Task.WhenAll(tasks);

        responses.All(r => r.StatusCode == HttpStatusCode.Created).Should().BeTrue("Toàn bộ 20 request tạo với key khác nhau phải nhận 201 Created.");

        var transferDtos = new List<StockTransferResponseDto>();
        foreach (var res in responses)
        {
            var dto = await res.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);
            transferDtos.Add(dto!);
        }

        var uniqueNumbers = transferDtos.Select(t => t.Number).Distinct().ToList();
        uniqueNumbers.Should().HaveCount(20, "20 phiếu tạo đồng thời phải có 20 mã số duy nhất.");
    }

    // =========================================================================
    // AC-02: Idempotency tuyệt đối theo RequestKey
    // =========================================================================
    [Fact]
    public async Task AC02_Idempotency_SameKey_SamePayload_Returns200OkReplay_DifferentPayload_409()
    {
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var requestKey = Guid.NewGuid();
        var requestDto = new CreateStockTransferRequestDto
        {
            RequestKey = requestKey,
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Note = "Phiếu test idempotency",
            Items = new List<StockTransferItemRequestDto>
            {
                new() { SkuId = infra.Sku1Id, Quantity = 12m }
            }
        };

        var res1 = await _client.PostAsJsonAsync("/api/stock-transfers", requestDto);
        res1.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await res1.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);

        var res2 = await _client.PostAsJsonAsync("/api/stock-transfers", requestDto);
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayed = await res2.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);
        replayed!.Id.Should().Be(created!.Id);
        replayed.Number.Should().Be(created.Number);

        var mismatchedDto = new CreateStockTransferRequestDto
        {
            RequestKey = requestKey,
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Note = "Phiếu test idempotency",
            Items = new List<StockTransferItemRequestDto>
            {
                new() { SkuId = infra.Sku1Id, Quantity = 99m }
            }
        };
        var res3 = await _client.PostAsJsonAsync("/api/stock-transfers", mismatchedDto);
        res3.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var (context, _) = CreatePostgresDbContext(infra.TenantId);
        await using (context)
        {
            var transferCount = await context.StockTransfers.CountAsync(t => t.RequestKey == requestKey);
            transferCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task AC02_Idempotency_ConcurrentTwentySameKey_ProducesExactlyOneTransfer()
    {
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var requestKey = Guid.NewGuid();
        var requestDto = new CreateStockTransferRequestDto
        {
            RequestKey = requestKey,
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Note = "Concurrent same key",
            Items = new List<StockTransferItemRequestDto>
            {
                new() { SkuId = infra.Sku1Id, Quantity = 5m }
            }
        };

        var tasks = Enumerable.Range(0, 20).Select(_ => _client.PostAsJsonAsync("/api/stock-transfers", requestDto));
        var responses = await Task.WhenAll(tasks);

        var createdCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var replayCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);

        createdCount.Should().Be(1, "Chỉ duy nhất 1 request tạo được bản ghi mới 201 Created.");
        replayCount.Should().Be(19, "19 request còn lại phải nhận 200 OK replay.");

        var (context, _) = CreatePostgresDbContext(infra.TenantId);
        await using (context)
        {
            var totalCount = await context.StockTransfers.CountAsync(t => t.RequestKey == requestKey);
            totalCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task AC02_Idempotency_ReplayAfterSkuInactivated_Returns200OkReplay()
    {
        // Kiểm chứng T4A-F02: Replay cùng key/payload sau khi SKU bị ngừng kinh doanh vẫn trả 200 OK
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var requestKey = Guid.NewGuid();
        var requestDto = new CreateStockTransferRequestDto
        {
            RequestKey = requestKey,
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Note = "Replay after SKU inactive",
            Items = new List<StockTransferItemRequestDto>
            {
                new() { SkuId = infra.Sku1Id, Quantity = 2m }
            }
        };

        var resOriginal = await _client.PostAsJsonAsync("/api/stock-transfers", requestDto);
        resOriginal.StatusCode.Should().Be(HttpStatusCode.Created);
        var originalDto = await resOriginal.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);

        // Ngừng kinh doanh SKU
        var (context, _) = CreatePostgresDbContext(infra.TenantId);
        await using (context)
        {
            var sku = await context.ProductVariants.FirstAsync(v => v.Id == infra.Sku1Id);
            sku.IsActive = false;
            await context.SaveChangesAsync();
        }

        // Gửi lại cùng key và payload -> Phải trả về 200 OK replay phiếu cũ
        var resReplay = await _client.PostAsJsonAsync("/api/stock-transfers", requestDto);
        resReplay.StatusCode.Should().Be(HttpStatusCode.OK, "Replay dưới khóa không được phụ thuộc trạng thái catalog mới.");
        var replayDto = await resReplay.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);
        replayDto!.Id.Should().Be(originalDto!.Id);
    }

    [Fact]
    public async Task AC02_Idempotency_ReplayAfterDispatchedReceivedCancelled_Returns200OkReplay()
    {
        // Kiểm chứng T4A-F02: Replay sau khi phiếu đã chuyển trạng thái hoặc thay đổi Note
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var requestKey = Guid.NewGuid();
        var requestDto = new CreateStockTransferRequestDto
        {
            RequestKey = requestKey,
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Note = "Original note",
            Items = new List<StockTransferItemRequestDto>
            {
                new() { SkuId = infra.Sku1Id, Quantity = 2m }
            }
        };

        var resCreate = await _client.PostAsJsonAsync("/api/stock-transfers", requestDto);
        resCreate.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await resCreate.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);

        // Hủy phiếu (khiến Note bị thay đổi trong DB)
        var cancelRes = await _client.PostAsJsonAsync($"/api/stock-transfers/{created!.Id}/cancel", new CancelStockTransferRequestDto { Reason = "Đổi ý" });
        cancelRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Gửi lại nguyên vẹn requestDto ban đầu -> Vẫn nhận 200 OK replay
        var replayRes = await _client.PostAsJsonAsync("/api/stock-transfers", requestDto);
        replayRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayed = await replayRes.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);
        replayed!.Id.Should().Be(created.Id);
        replayed.Status.Should().Be("Cancelled");
    }

    [Fact]
    public async Task AC02_Idempotency_ReplayWithReversedItemsOrder_Returns200OkReplay()
    {
        // Kiểm chứng T4A-F02: Gửi lại cùng RequestKey với các Items đảo thứ tự nhưng cùng nội dung
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var requestKey = Guid.NewGuid();
        var requestDto1 = new CreateStockTransferRequestDto
        {
            RequestKey = requestKey,
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Note = "Two items",
            Items = new List<StockTransferItemRequestDto>
            {
                new() { SkuId = infra.Sku1Id, Quantity = 3m },
                new() { SkuId = infra.Sku2Id, Quantity = 7m }
            }
        };

        var res1 = await _client.PostAsJsonAsync("/api/stock-transfers", requestDto1);
        res1.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await res1.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);

        // Đảo thứ tự items
        var requestDto2 = new CreateStockTransferRequestDto
        {
            RequestKey = requestKey,
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Note = "Two items",
            Items = new List<StockTransferItemRequestDto>
            {
                new() { SkuId = infra.Sku2Id, Quantity = 7m },
                new() { SkuId = infra.Sku1Id, Quantity = 3m }
            }
        };

        var res2 = await _client.PostAsJsonAsync("/api/stock-transfers", requestDto2);
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayed = await res2.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);
        replayed!.Id.Should().Be(created!.Id);
    }

    [Fact]
    public async Task AC02_Idempotency_SameRequestKeyAcrossDifferentTenants_BothSucceedIndependently()
    {
        var infra = await SetupInfrastructureAsync();
        var sharedKey = Guid.NewGuid();

        // 1. Tenant A tạo phiếu với sharedKey
        var ownerTokenA = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenA);

        var resA = await _client.PostAsJsonAsync("/api/stock-transfers", new CreateStockTransferRequestDto
        {
            RequestKey = sharedKey,
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Items = new List<StockTransferItemRequestDto> { new() { SkuId = infra.Sku1Id, Quantity = 1m } }
        });
        resA.StatusCode.Should().Be(HttpStatusCode.Created);

        // 2. Tenant B tạo phiếu với cùng sharedKey (tài nguyên riêng của Tenant B)
        var ownerTokenB = await LoginAsAsync("owner@shopb.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenB);

        var (contextB, _) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantBId);
        Guid branchBId, warehouseB1Id, warehouseB2Id, skuBId;
        await using (contextB)
        {
            var branch = await contextB.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantBId);
            var warehouse1 = await contextB.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantBId && w.BranchId == branch.Id);
            var warehouse2 = await contextB.Warehouses.FirstOrDefaultAsync(w => w.TenantId == OismsDatabaseSeeder.TenantBId && w.Name == "Kho Phụ Tenant B");
            if (warehouse2 == null)
            {
                warehouse2 = new Warehouse
                {
                    Id = Guid.NewGuid(),
                    TenantId = OismsDatabaseSeeder.TenantBId,
                    BranchId = branch.Id,
                    Name = "Kho Phụ Tenant B",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                contextB.Warehouses.Add(warehouse2);
                await contextB.SaveChangesAsync();
            }
            var sku = await contextB.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantBId);
            branchBId = branch.Id;
            warehouseB1Id = warehouse1.Id;
            warehouseB2Id = warehouse2.Id;
            skuBId = sku.Id;
        }

        var resB = await _client.PostAsJsonAsync("/api/stock-transfers", new CreateStockTransferRequestDto
        {
            RequestKey = sharedKey,
            FromBranchId = branchBId,
            ToBranchId = branchBId,
            FromWarehouseId = warehouseB1Id,
            ToWarehouseId = warehouseB2Id,
            Items = new List<StockTransferItemRequestDto> { new() { SkuId = skuBId, Quantity = 1m } }
        });
        resB.StatusCode.Should().Be(HttpStatusCode.Created, "Cùng RequestKey ở 2 Tenant khác nhau phải tạo thành công 2 phiếu độc lập.");
    }

    // =========================================================================
    // AC-03: Bước 1 Xuất gửi hàng (Dispatch) & Snapshot WAC
    // =========================================================================
    [Fact]
    public async Task AC03_DispatchTransfer_AtomicDeduction_SnapshotsUnitCost_WritesLedgerOut_AndRollsBackOnInsufficientStock()
    {
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 50m, 125456.789123m);

        var createDto = new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Note = "Dispatch test",
            Items = new List<StockTransferItemRequestDto>
            {
                new() { SkuId = infra.Sku1Id, Quantity = 20m }
            }
        };
        var createRes = await _client.PostAsJsonAsync("/api/stock-transfers", createDto);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createRes.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);

        var dispatchRes = await _client.PostAsJsonAsync($"/api/stock-transfers/{created!.Id}/dispatch", new DispatchStockTransferRequestDto { Note = "Đang xuất kho" });
        dispatchRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var dispatched = await dispatchRes.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);
        dispatched!.Status.Should().Be("InTransit");
        dispatched.DispatchedAt.Should().NotBeNull();
        dispatched.Items[0].UnitCost.Should().Be(125456.789123m, "Giá vốn WAC phải được snapshot chính xác 6 chữ số thập phân.");

        var (context, _) = CreatePostgresDbContext(infra.TenantId);
        await using (context)
        {
            var wBal = await context.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == infra.Sku1Id);
            wBal.OnHand.Should().Be(30m);

            var bBal = await context.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            bBal.OnHand.Should().Be(30m);
            bBal.AverageCost.Should().Be(125456.789123m, "WAC kho xuất tuyệt đối không thay đổi khi xuất hàng.");

            var ledgerOut = await context.InventoryTransactions
                .FirstOrDefaultAsync(t => t.TransferItemId == dispatched.Items[0].Id && t.Type == "OUT");
            ledgerOut.Should().NotBeNull();
            ledgerOut!.Quantity.Should().Be(20m);
            ledgerOut.UnitCost.Should().Be(125456.789123m);
            ledgerOut.BalanceAfter.Should().Be(30m);
            ledgerOut.BranchId.Should().Be(infra.BranchAId);
            ledgerOut.WarehouseId.Should().Be(infra.WarehouseA1Id);
        }

        // Thử nghiệm xuất vượt quá tồn kho còn lại (yêu cầu 35 trong khi chỉ còn 30) -> 409
        var createExcessDto = new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Items = new List<StockTransferItemRequestDto>
            {
                new() { SkuId = infra.Sku1Id, Quantity = 35m }
            }
        };
        var createExcessRes = await _client.PostAsJsonAsync("/api/stock-transfers", createExcessDto);
        createExcessRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdExcess = await createExcessRes.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);

        var failDispatchRes = await _client.PostAsJsonAsync($"/api/stock-transfers/{createdExcess!.Id}/dispatch", new DispatchStockTransferRequestDto());
        failDispatchRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var wBal = await verifyContext.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == infra.Sku1Id);
            wBal.OnHand.Should().Be(30m);
        }
    }

    [Fact]
    public async Task AC03_DispatchTransfer_ConcurrentWithPosCheckout_ZeroOverSell()
    {
        // Kiểm chứng cạnh tranh tồn kho: Stock = 5, Dispatch cần 3, POS checkout cần 3 -> Tổng cầu 6 > 5 -> Đúng 1 thành công
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 5m, 100000m);

        var createTransfer = await _client.PostAsJsonAsync("/api/stock-transfers", new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Items = new List<StockTransferItemRequestDto> { new() { SkuId = infra.Sku1Id, Quantity = 3m } }
        });
        var transfer = await createTransfer.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);

        decimal sku1Retail;
        await using (var ctx = CreatePostgresDbContext(infra.TenantId).Context)
        {
            sku1Retail = (await ctx.ProductVariants.FirstAsync(v => v.Id == infra.Sku1Id)).RetailPrice;
        }

        var posDto = new PosCheckoutRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = 3 * sku1Retail,
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = infra.Sku1Id, SkuCode = infra.Sku1Code, Quantity = 3m, UnitPrice = sku1Retail }
            }
        };

        var dispatchTask = _client.PostAsJsonAsync($"/api/stock-transfers/{transfer!.Id}/dispatch", new DispatchStockTransferRequestDto());
        var posTask = _client.PostAsJsonAsync("/api/pos/checkout", posDto);

        var results = await Task.WhenAll(dispatchTask, posTask);
        var dispatchBody = await results[0].Content.ReadAsStringAsync();
        var posBody = await results[1].Content.ReadAsStringAsync();
        
        results[0].StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, $"Dispatch failed with 500: {dispatchBody}");
        results[1].StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, $"POS failed with 500: {posBody}");

        var successCount = results.Count(r => r.IsSuccessStatusCode);
        var conflictCount = results.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        successCount.Should().Be(1, $"Chỉ duy nhất 1 thao tác được phép thành công. Dispatch: {(int)results[0].StatusCode} {dispatchBody}, POS: {(int)results[1].StatusCode} {posBody}");
        conflictCount.Should().Be(1, $"Thao tác còn lại phải bị từ chối do thiếu hàng. Dispatch: {(int)results[0].StatusCode} {dispatchBody}, POS: {(int)results[1].StatusCode} {posBody}");

        var (context, _) = CreatePostgresDbContext(infra.TenantId);
        await using (context)
        {
            var bBal = await context.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            bBal.OnHand.Should().Be(2m, "5 - 3 = 2 tồn kho còn lại chính xác, không âm.");
        }
    }

    [Fact]
    public async Task AC03_DispatchTransfer_ConcurrentWithAdminOrderCreation_ZeroOverSell_AndBranchAsserts()
    {
        // Kiểm chứng T4A-F07: Chuyển kho Dispatch cạnh tranh tồn kho với Tạo đơn Admin (có Active reservations)
        // Stock = 5, Dispatch cần 3, Admin Order cần 3 -> Tổng cầu 6 > 5 -> Đúng 1 thành công
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 5m, 100000m);

        var createTransfer = await _client.PostAsJsonAsync("/api/stock-transfers", new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Items = new List<StockTransferItemRequestDto> { new() { SkuId = infra.Sku1Id, Quantity = 3m } }
        });
        var transfer = await createTransfer.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);

        decimal sku1Retail;
        await using (var ctx = CreatePostgresDbContext(infra.TenantId).Context)
        {
            sku1Retail = (await ctx.ProductVariants.FirstAsync(v => v.Id == infra.Sku1Id)).RetailPrice;
        }

        var adminOrderDto = new CreateOrderRequestDto
        {
            BranchId = infra.BranchAId,
            Channel = "Admin",
            CustomerName = "Khách Đặt Hàng Admin",
            CustomerPhone = "0909123456",
            ShippingAddress = "123 Đường Nguyễn Huệ, Q1, TP.HCM",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = infra.Sku1Id, SkuCode = infra.Sku1Code, Quantity = 3m, UnitPrice = sku1Retail }
            }
        };

        var dispatchTask = _client.PostAsJsonAsync($"/api/stock-transfers/{transfer!.Id}/dispatch", new DispatchStockTransferRequestDto());
        var adminOrderTask = _client.PostAsJsonAsync("/api/orders", adminOrderDto);

        var results = await Task.WhenAll(dispatchTask, adminOrderTask);
        var dispatchBody = await results[0].Content.ReadAsStringAsync();
        var adminBody = await results[1].Content.ReadAsStringAsync();

        results[0].StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, $"Dispatch 500: {dispatchBody}");
        results[1].StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, $"Admin order 500: {adminBody}");

        var successCount = results.Count(r => r.IsSuccessStatusCode);
        var conflictCount = results.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        successCount.Should().Be(1, $"Chỉ 1 thao tác thành công. Dispatch: {(int)results[0].StatusCode}, Admin: {(int)results[1].StatusCode}");
        conflictCount.Should().Be(1, $"1 thao tác bị từ chối 409. Dispatch: {(int)results[0].StatusCode}, Admin: {(int)results[1].StatusCode}");

        var conflictRes = results.First(r => r.StatusCode == HttpStatusCode.Conflict);
        var conflictText = await conflictRes.Content.ReadAsStringAsync();
        conflictText.Should().Contain("INSUFFICIENT_STOCK", "Lỗi xung đột phải là INSUFFICIENT_STOCK.");

        // Đối soát hai nhánh thắng độc lập theo đúng ví dụ review
        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var bBal = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            var wBal = await verifyContext.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == infra.Sku1Id);
            var t = await verifyContext.StockTransfers.FirstAsync(x => x.Id == transfer.Id);
            var ledgerOut = await verifyContext.InventoryTransactions
                .Where(tx => tx.TransferItemId == transfer.Items[0].Id && tx.Type == "OUT")
                .ToListAsync();

            if (results[1].IsSuccessStatusCode)
            {
                // Nhánh 1: Admin Order thắng -> OnHand vẫn 5, Reserved = 3, Available = 2, không có transfer ledger OUT
                var orderDto = await results[1].Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
                orderDto.Should().NotBeNull();
                orderDto!.Status.Should().Be("Reserved");

                bBal.OnHand.Should().Be(5m, "OnHand chi nhánh vẫn 5 khi Admin order thắng.");
                bBal.Reserved.Should().Be(3m, "Reserved chi nhánh tăng lên 3 cho đơn Admin.");
                (bBal.OnHand - bBal.Reserved).Should().Be(2m, "Available còn 2.");

                wBal.OnHand.Should().Be(5m, "OnHand kho vẫn 5 khi Admin order thắng.");
                wBal.Reserved.Should().Be(3m, "Reserved kho tăng lên 3.");

                t.Status.Should().Be("Draft", "Phiếu chuyển kho vẫn là Draft.");
                ledgerOut.Should().BeEmpty("Không có bút toán ledger OUT chuyển kho.");

                var activeReservation = await verifyContext.Reservations
                    .Where(r => r.TenantId == infra.TenantId && r.OrderId == orderDto.Id && r.Status == "Active")
                    .SumAsync(r => r.Quantity);
                activeReservation.Should().Be(3m, "Active reservation đúng 3 sản phẩm.");
            }
            else
            {
                // Nhánh 2: Transfer Dispatch thắng -> OnHand = 2, Reserved = 0, Available = 2, một OUT = 3
                results[0].StatusCode.Should().Be(HttpStatusCode.OK);
                t.Status.Should().Be("InTransit");
                t.DispatchedAt.Should().NotBeNull();

                bBal.OnHand.Should().Be(2m, "OnHand chi nhánh giảm xuống 2 (5 - 3).");
                bBal.Reserved.Should().Be(0m, "Reserved chi nhánh là 0 vì Admin order thất bại.");
                (bBal.OnHand - bBal.Reserved).Should().Be(2m, "Available còn 2.");

                wBal.OnHand.Should().Be(2m, "OnHand kho giảm xuống 2.");
                wBal.Reserved.Should().Be(0m);

                ledgerOut.Should().HaveCount(1, "Có đúng 1 bút toán ledger OUT chuyển kho.");
                ledgerOut[0].Quantity.Should().Be(3m);
            }
        }
    }

    [Fact]
    public async Task AC03_DispatchTransfer_WhenStockIsReservedByActiveAdminOrder_CannotDispatchReservedStock_Returns409()
    {
        // Kiểm chứng T4A-F07: Tồn kho đã bị giữ chỗ bởi Đơn Admin (Active Reservation) không thể bị chiếm bởi Transfer Dispatch
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // Nhập tồn: 5 sản phẩm
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 5m, 100000m);

        decimal sku1Retail;
        await using (var ctx = CreatePostgresDbContext(infra.TenantId).Context)
        {
            sku1Retail = (await ctx.ProductVariants.FirstAsync(v => v.Id == infra.Sku1Id)).RetailPrice;
        }

        // Tạo đơn Admin giữ chỗ 3 sản phẩm -> OnHand=5, Reserved=3, Available=2
        var adminOrderDto = new CreateOrderRequestDto
        {
            BranchId = infra.BranchAId,
            Channel = "Admin",
            CustomerName = "Khách Đặt Hàng Admin Giữ Chỗ",
            CustomerPhone = "0909123456",
            ShippingAddress = "123 Đường Nguyễn Huệ, Q1, TP.HCM",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = infra.Sku1Id, SkuCode = infra.Sku1Code, Quantity = 3m, UnitPrice = sku1Retail }
            }
        };
        var orderRes = await _client.PostAsJsonAsync("/api/orders", adminOrderDto);
        orderRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // Tạo phiếu chuyển kho đòi hỏi 3 sản phẩm (Available chỉ còn 2)
        var createTransfer = await _client.PostAsJsonAsync("/api/stock-transfers", new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Items = new List<StockTransferItemRequestDto> { new() { SkuId = infra.Sku1Id, Quantity = 3m } }
        });
        createTransfer.StatusCode.Should().Be(HttpStatusCode.Created);
        var transfer = await createTransfer.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);

        // Thao tác Dispatch phải bị từ chối 409 INSUFFICIENT_STOCK vì không thể chiếm phần Reserved của đơn online
        var dispatchRes = await _client.PostAsJsonAsync($"/api/stock-transfers/{transfer!.Id}/dispatch", new DispatchStockTransferRequestDto());
        dispatchRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var errBody = await dispatchRes.Content.ReadAsStringAsync();
        errBody.Should().Contain("INSUFFICIENT_STOCK");

        // Đối soát CSDL: Tồn kho Reserved vẫn an toàn, Transfer không chuyển trạng thái, không ghi ledger OUT
        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var bBal = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            bBal.OnHand.Should().Be(5m);
            bBal.Reserved.Should().Be(3m);
            (bBal.OnHand - bBal.Reserved).Should().Be(2m);

            var t = await verifyContext.StockTransfers.FirstAsync(x => x.Id == transfer.Id);
            t.Status.Should().Be("Draft");
            t.DispatchedAt.Should().BeNull();

            var ledgerOut = await verifyContext.InventoryTransactions
                .Where(tx => tx.TransferItemId == transfer.Items[0].Id && tx.Type == "OUT")
                .ToListAsync();
            ledgerOut.Should().BeEmpty("Không được phát sinh bất kỳ bút toán xuất kho chuyển hàng nào.");
        }
    }

    // =========================================================================
    // AC-04: Bước 2 Nhập nhận (Receive), Tính lại WAC đích & Intra-Branch Transfer
    // =========================================================================
    [Fact]
    public async Task AC04_ReceiveTransfer_AtomicAddition_RecalculatesDestWac_WritesLedgerIn_AndHandlesAutoCreation()
    {
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 100m, 100000m);

        var req1 = new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Items = new List<StockTransferItemRequestDto> { new() { SkuId = infra.Sku1Id, Quantity = 10m } }
        };
        var resCreate1 = await _client.PostAsJsonAsync("/api/stock-transfers", req1);
        var transfer1 = await resCreate1.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);

        await _client.PostAsJsonAsync($"/api/stock-transfers/{transfer1!.Id}/dispatch", new DispatchStockTransferRequestDto());

        var resReceive1 = await _client.PostAsJsonAsync($"/api/stock-transfers/{transfer1.Id}/receive", new ReceiveStockTransferRequestDto());
        resReceive1.StatusCode.Should().Be(HttpStatusCode.OK);
        var received1 = await resReceive1.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);
        received1!.Status.Should().Be("Received");
        received1.ReceivedAt.Should().NotBeNull();
        received1.ReceivedBy.Should().NotBeNull();

        var (context, _) = CreatePostgresDbContext(infra.TenantId);
        await using (context)
        {
            var destBBal = await context.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchBId && b.SkuId == infra.Sku1Id);
            destBBal.OnHand.Should().Be(10m);
            destBBal.AverageCost.Should().Be(100000m);

            var ledgerIn = await context.InventoryTransactions
                .FirstOrDefaultAsync(t => t.TransferItemId == received1.Items[0].Id && t.Type == "IN");
            ledgerIn.Should().NotBeNull();
            ledgerIn!.Quantity.Should().Be(10m);
            ledgerIn.UnitCost.Should().Be(100000m);
            ledgerIn.BalanceAfter.Should().Be(10m);
        }

        // Chuyển kho cùng chi nhánh: WarehouseA1 -> WarehouseA2
        var reqIntra = new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchAId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseA2Id,
            Items = new List<StockTransferItemRequestDto> { new() { SkuId = infra.Sku1Id, Quantity = 15m } }
        };
        var resCreateIntra = await _client.PostAsJsonAsync("/api/stock-transfers", reqIntra);
        resCreateIntra.StatusCode.Should().Be(HttpStatusCode.Created);
        var transferIntra = await resCreateIntra.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);

        await _client.PostAsJsonAsync($"/api/stock-transfers/{transferIntra!.Id}/dispatch", new DispatchStockTransferRequestDto());
        var resReceiveIntra = await _client.PostAsJsonAsync($"/api/stock-transfers/{transferIntra.Id}/receive", new ReceiveStockTransferRequestDto());
        resReceiveIntra.StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var wBalA2 = await verifyContext.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA2Id && w.SkuId == infra.Sku1Id);
            wBalA2.OnHand.Should().Be(15m);
        }
    }

    [Fact]
    public async Task AC04_ReceiveTransfer_SameSkuExistingDestWac_WeightedAverageRecalculatedPrecisely()
    {
        // Kiểm chứng tính lại WAC đích cho CÙNG SKU khi chi nhánh đích đã có tồn sẵn:
        // Đích có sẵn: OnHand = 10, WAC = 100,000.
        // Nhập chuyển thêm: Quantity = 10, UnitCost = 120,000.
        // WAC mới kỳ vọng: (10 * 100,000 + 10 * 120,000) / 20 = 110,000.
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // Seed tồn sẵn tại Branch B: 10 cái giá 100,000
        await SeedStockAsync(infra.TenantId, infra.BranchBId, infra.WarehouseB1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 100000m);

        // Seed tồn tại Branch A: 20 cái giá 120,000
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 20m, 120000m);

        // Tạo và dispatch phiếu chuyển 10 cái Sku1 từ A sang B
        var req = new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Items = new List<StockTransferItemRequestDto> { new() { SkuId = infra.Sku1Id, Quantity = 10m } }
        };
        var resCreate = await _client.PostAsJsonAsync("/api/stock-transfers", req);
        var transfer = await resCreate.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);

        await _client.PostAsJsonAsync($"/api/stock-transfers/{transfer!.Id}/dispatch", new DispatchStockTransferRequestDto());

        // Tiếp nhận tại Branch B
        var resReceive = await _client.PostAsJsonAsync($"/api/stock-transfers/{transfer.Id}/receive", new ReceiveStockTransferRequestDto());
        resReceive.StatusCode.Should().Be(HttpStatusCode.OK);

        var (context, _) = CreatePostgresDbContext(infra.TenantId);
        await using (context)
        {
            var bBalB = await context.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchBId && b.SkuId == infra.Sku1Id);
            bBalB.OnHand.Should().Be(20m);
            bBalB.AverageCost.Should().Be(110000m, "WAC phải được tính bình quân gia quyền chính xác cho cùng SKU.");
        }
    }

    [Fact]
    public async Task AC04_ReceiveTransfer_FractionalSixDecimals_And_SourceWacChangePostDispatch_SnapshotImmutable()
    {
        // Kiểm chứng snapshot WAC 6 chữ số thập phân và tính bất biến khi WAC nguồn thay đổi sau khi dispatch
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // Seed tồn tại Branch A với WAC phân số: 7.123456
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 7.123456m);

        var req = new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Items = new List<StockTransferItemRequestDto> { new() { SkuId = infra.Sku1Id, Quantity = 4m } }
        };
        var resCreate = await _client.PostAsJsonAsync("/api/stock-transfers", req);
        var transfer = await resCreate.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);

        await _client.PostAsJsonAsync($"/api/stock-transfers/{transfer!.Id}/dispatch", new DispatchStockTransferRequestDto());

        // Sau khi dispatch, nhập thêm hàng giá cao vào Branch A làm đổi WAC chi nhánh A
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 50m, 99.999999m);

        // Tiếp nhận tại Branch B -> Vẫn phải sử dụng snapshot 7.123456
        var resReceive = await _client.PostAsJsonAsync($"/api/stock-transfers/{transfer.Id}/receive", new ReceiveStockTransferRequestDto());
        resReceive.StatusCode.Should().Be(HttpStatusCode.OK);

        var (context, _) = CreatePostgresDbContext(infra.TenantId);
        await using (context)
        {
            var bBalB = await context.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchBId && b.SkuId == infra.Sku1Id);
            bBalB.AverageCost.Should().Be(7.123456m, "WAC đích phải kế thừa chính xác snapshot 6 chữ số thập phân ban đầu.");

            var ledgerIn = await context.InventoryTransactions.FirstAsync(t => t.TransferItemId == transfer.Items[0].Id && t.Type == "IN");
            ledgerIn.UnitCost.Should().Be(7.123456m);
        }
    }

    [Fact]
    public async Task AC04_ReceiveTransfer_MissingUnitCostSnapshot_Returns409InvalidStateTransition_ZeroMutation()
    {
        // Kiểm chứng T4A-F03: Receive phiếu thiếu snapshot UnitCost (NULL) phải trả 409 INVALID_STATE_TRANSITION trước mutation
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);

        var req = new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Items = new List<StockTransferItemRequestDto> { new() { SkuId = infra.Sku1Id, Quantity = 2m } }
        };
        var resCreate = await _client.PostAsJsonAsync("/api/stock-transfers", req);
        var transfer = await resCreate.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);

        await _client.PostAsJsonAsync($"/api/stock-transfers/{transfer!.Id}/dispatch", new DispatchStockTransferRequestDto());

        // Mô phỏng dữ liệu legacy/lỗi: Đặt UnitCost = NULL trong database
        var (context, _) = CreatePostgresDbContext(infra.TenantId);
        await using (context)
        {
            var item = await context.StockTransferItems.FirstAsync(i => i.TransferId == transfer.Id);
            item.UnitCost = null;
            await context.SaveChangesAsync();
        }

        // Gọi Receive -> Phải nhận 409 INVALID_STATE_TRANSITION
        var resReceive = await _client.PostAsJsonAsync($"/api/stock-transfers/{transfer.Id}/receive", new ReceiveStockTransferRequestDto());
        resReceive.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await resReceive.Content.ReadAsStringAsync();
        body.Should().Contain("INVALID_STATE_TRANSITION");

        // Kiểm tra cơ sở dữ liệu: Phiếu vẫn InTransit, không có Ledger IN, tồn kho đích không thay đổi
        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var t = await verifyContext.StockTransfers.FirstAsync(x => x.Id == transfer.Id);
            t.Status.Should().Be("InTransit");

            var hasLedgerIn = await verifyContext.InventoryTransactions.AnyAsync(tx => tx.TransferItemId == transfer.Items[0].Id && tx.Type == "IN");
            hasLedgerIn.Should().BeFalse("Không được ghi ledger IN khi snapshot bị thiếu.");
        }
    }

    [Fact]
    public async Task AC04_ReceiveTransfer_ValidZeroUnitCostSnapshot_Succeeds()
    {
        // Kiểm chứng T4A-F03: Snapshot 0m là hợp lệ và được phép nhập nhận
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);

        var req = new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Items = new List<StockTransferItemRequestDto> { new() { SkuId = infra.Sku1Id, Quantity = 2m } }
        };
        var resCreate = await _client.PostAsJsonAsync("/api/stock-transfers", req);
        var transfer = await resCreate.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);

        await _client.PostAsJsonAsync($"/api/stock-transfers/{transfer!.Id}/dispatch", new DispatchStockTransferRequestDto());

        // Đặt UnitCost = 0m (hàng khuyến mãi/cho tặng)
        var (context, _) = CreatePostgresDbContext(infra.TenantId);
        await using (context)
        {
            var item = await context.StockTransferItems.FirstAsync(i => i.TransferId == transfer.Id);
            item.UnitCost = 0m;
            await context.SaveChangesAsync();
        }

        var resReceive = await _client.PostAsJsonAsync($"/api/stock-transfers/{transfer.Id}/receive", new ReceiveStockTransferRequestDto());
        resReceive.StatusCode.Should().Be(HttpStatusCode.OK, "Snapshot giá vốn bằng 0 hợp lệ phải được chấp thuận.");

        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var ledgerIn = await verifyContext.InventoryTransactions.FirstAsync(tx => tx.TransferItemId == transfer.Items[0].Id && tx.Type == "IN");
            ledgerIn.UnitCost.Should().Be(0m);
        }
    }

    [Fact]
    public async Task AC04_ReceiveTransfer_IntraBranch_IntermediateReceiptChangesWac_AppliesSnapshotCostCorrectly()
    {
        // Kiểm chứng T4A-F07: Chuyển kho cùng chi nhánh:
        // Ban đầu tồn 10@100,000 -> dispatch 4@100,000 -> nhập xen giữa 6@300,000 làm đổi WAC chi nhánh -> receive 4@100,000
        // WAC cuối cùng của chi nhánh = 175,000.000000m, cả 2 bút toán transfer giữ nguyên UnitCost = 100,000.000000m.
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // 1. Nhập tồn ban đầu: 10 cái @ 100,000 vào Kho A1
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 100000m);

        // 2. Tạo phiếu chuyển kho nội bộ cùng Chi nhánh A: Kho A1 -> Kho A2, Số lượng: 4 cái
        var createRes = await _client.PostAsJsonAsync("/api/stock-transfers", new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchAId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseA2Id,
            Note = "Chuyển nội bộ cùng chi nhánh A",
            Items = new List<StockTransferItemRequestDto> { new() { SkuId = infra.Sku1Id, Quantity = 4m } }
        });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var transfer = await createRes.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);

        // 3. Dispatch: Kho A1 giảm còn 6 cái, snapshot UnitCost = 100,000.000000m, ghi ledger OUT
        var dispatchRes = await _client.PostAsJsonAsync($"/api/stock-transfers/{transfer!.Id}/dispatch", new DispatchStockTransferRequestDto());
        dispatchRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Nhập xen giữa: Nhập thêm 6 cái @ 300,000 vào Kho A1
        // Sau bước này:
        // Kho A1 OnHand = 6 + 6 = 12.
        // Kho A1 / Chi nhánh A AverageCost = (6 * 100,000 + 6 * 300,000) / 12 = 200,000.000000m.
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 6m, 300000m);

        await using (var midContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var bBalMid = await midContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            bBalMid.OnHand.Should().Be(12m);
            bBalMid.AverageCost.Should().Be(200000m, "Nhập xen giữa làm WAC chi nhánh đổi thành 200,000.");
        }

        // 5. Receive: Kho A2 tiếp nhận 4 cái từ phiếu chuyển
        // Tại Chi nhánh A:
        // Đang có 12 cái @ 200,000 (tổng 2,400,000).
        // Nhận thêm 4 cái @ snapshot UnitCost 100,000 (tổng 400,000).
        // Tồn chi nhánh A mới: 12 + 4 = 16.
        // WAC mới của chi nhánh A: (2,400,000 + 400,000) / 16 = 2,800,000 / 16 = 175,000.000000m!
        var receiveRes = await _client.PostAsJsonAsync($"/api/stock-transfers/{transfer.Id}/receive", new ReceiveStockTransferRequestDto());
        receiveRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. Đối soát toàn diện CSDL
        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            // Trạng thái phiếu
            var t = await verifyContext.StockTransfers
                .Include(x => x.Items)
                .FirstAsync(x => x.Id == transfer.Id);
            t.Status.Should().Be("Received");
            t.Items.First().UnitCost.Should().Be(100000.000000m, "Snapshot giá vốn trên phiếu vẫn bất biến là 100,000.");

            // Tồn kho hai cấp
            var wBalA1 = await verifyContext.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == infra.Sku1Id);
            wBalA1.OnHand.Should().Be(12m, "Kho A1 còn 12 cái (10 - 4 + 6).");

            var wBalA2 = await verifyContext.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA2Id && w.SkuId == infra.Sku1Id);
            wBalA2.OnHand.Should().Be(4m, "Kho A2 nhận đủ 4 cái.");

            var bBalA = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            bBalA.OnHand.Should().Be(16m, "Tổng chi nhánh A là 16 (12 + 4).");
            bBalA.AverageCost.Should().Be(175000.000000m, "WAC chi nhánh A tính bình quân gia quyền đúng 175,000.000000m.");

            // Sổ cái
            var ledgerOut = await verifyContext.InventoryTransactions
                .FirstOrDefaultAsync(tx => tx.TransferItemId == transfer.Items[0].Id && tx.Type == "OUT");
            ledgerOut.Should().NotBeNull();
            ledgerOut!.WarehouseId.Should().Be(infra.WarehouseA1Id);
            ledgerOut.Quantity.Should().Be(4m);
            ledgerOut.UnitCost.Should().Be(100000.000000m);

            var ledgerIn = await verifyContext.InventoryTransactions
                .FirstOrDefaultAsync(tx => tx.TransferItemId == transfer.Items[0].Id && tx.Type == "IN");
            ledgerIn.Should().NotBeNull();
            ledgerIn!.WarehouseId.Should().Be(infra.WarehouseA2Id);
            ledgerIn.Quantity.Should().Be(4m);
            ledgerIn.UnitCost.Should().Be(100000.000000m);
        }
    }

    // =========================================================================
    // AC-05: State Machine & Hủy phiếu (Cancel) an toàn
    // =========================================================================
    [Fact]
    public async Task AC05_StateMachine_AndCancel_EnforcesStrictTransitions_AndZeroMutationOnCancel()
    {
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 50m, 50000m);

        var create1 = await _client.PostAsJsonAsync("/api/stock-transfers", new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Items = new List<StockTransferItemRequestDto> { new() { SkuId = infra.Sku1Id, Quantity = 5m } }
        });
        var t1 = await create1.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);

        var cancelRes1 = await _client.PostAsJsonAsync($"/api/stock-transfers/{t1!.Id}/cancel", new CancelStockTransferRequestDto { Reason = "Khách hủy nhu cầu" });
        cancelRes1.StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelled = await cancelRes1.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);
        cancelled!.Status.Should().Be("Cancelled");

        var cancelAgainRes = await _client.PostAsJsonAsync($"/api/stock-transfers/{t1.Id}/cancel", new CancelStockTransferRequestDto());
        cancelAgainRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var create2 = await _client.PostAsJsonAsync("/api/stock-transfers", new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Items = new List<StockTransferItemRequestDto> { new() { SkuId = infra.Sku1Id, Quantity = 5m } }
        });
        var t2 = await create2.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);

        var receiveDraftRes = await _client.PostAsJsonAsync($"/api/stock-transfers/{t2!.Id}/receive", new ReceiveStockTransferRequestDto());
        receiveDraftRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await _client.PostAsJsonAsync($"/api/stock-transfers/{t2.Id}/dispatch", new DispatchStockTransferRequestDto());

        var cancelInTransitRes = await _client.PostAsJsonAsync($"/api/stock-transfers/{t2.Id}/cancel", new CancelStockTransferRequestDto());
        cancelInTransitRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var dispatchAgainRes = await _client.PostAsJsonAsync($"/api/stock-transfers/{t2.Id}/dispatch", new DispatchStockTransferRequestDto());
        dispatchAgainRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await _client.PostAsJsonAsync($"/api/stock-transfers/{t2.Id}/receive", new ReceiveStockTransferRequestDto());

        var cancelReceivedRes = await _client.PostAsJsonAsync($"/api/stock-transfers/{t2.Id}/cancel", new CancelStockTransferRequestDto());
        cancelReceivedRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AC05_SameTransfer_ConcurrentDispatchAndCancel_ExactlyOneSucceeds_Other409()
    {
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 20m, 50000m);

        var createRes = await _client.PostAsJsonAsync("/api/stock-transfers", new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Items = new List<StockTransferItemRequestDto> { new() { SkuId = infra.Sku1Id, Quantity = 5m } }
        });
        var transfer = await createRes.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);

        var dispatchTask = _client.PostAsJsonAsync($"/api/stock-transfers/{transfer!.Id}/dispatch", new DispatchStockTransferRequestDto());
        var cancelTask = _client.PostAsJsonAsync($"/api/stock-transfers/{transfer.Id}/cancel", new CancelStockTransferRequestDto { Reason = "Race dispatch vs cancel" });

        var results = await Task.WhenAll(dispatchTask, cancelTask);
        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);

        var conflictRes = results.First(r => r.StatusCode == HttpStatusCode.Conflict);
        var conflictBody = await conflictRes.Content.ReadAsStringAsync();
        conflictBody.Should().Contain("INVALID_STATE_TRANSITION", "Thao tác thua cuộc phải nhận đúng errorCode INVALID_STATE_TRANSITION.");

        // Đối soát CSDL độc lập: bảo toàn toàn vẹn trạng thái, số dư và ledger
        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var t = await verifyContext.StockTransfers.FirstAsync(x => x.Id == transfer.Id);
            var bBal = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            var wBal = await verifyContext.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == infra.Sku1Id);
            var ledgerOut = await verifyContext.InventoryTransactions
                .Where(tx => tx.TransferItemId == transfer.Items[0].Id && tx.Type == "OUT")
                .ToListAsync();

            if (results[0].StatusCode == HttpStatusCode.OK)
            {
                // Dispatch thắng: InTransit, OnHand giảm 5 (20 -> 15), 1 ledger OUT
                t.Status.Should().Be("InTransit");
                t.DispatchedAt.Should().NotBeNull();
                bBal.OnHand.Should().Be(15m);
                wBal.OnHand.Should().Be(15m);
                ledgerOut.Should().HaveCount(1);
                ledgerOut[0].Quantity.Should().Be(5m);
            }
            else
            {
                // Cancel thắng: Cancelled, OnHand không đổi (20), 0 ledger OUT
                t.Status.Should().Be("Cancelled");
                bBal.OnHand.Should().Be(20m);
                wBal.OnHand.Should().Be(20m);
                ledgerOut.Should().BeEmpty();
            }
        }
    }

    [Fact]
    public async Task AC05_SameTransfer_ConcurrentDispatchDispatch_And_ReceiveReceive_ExactlyOneSucceeds()
    {
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 20m, 50000m);

        // 1. Concurrent Dispatch trên cùng 1 phiếu Draft
        var createRes = await _client.PostAsJsonAsync("/api/stock-transfers", new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Items = new List<StockTransferItemRequestDto> { new() { SkuId = infra.Sku1Id, Quantity = 5m } }
        });
        var transfer = await createRes.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);

        var d1 = _client.PostAsJsonAsync($"/api/stock-transfers/{transfer!.Id}/dispatch", new DispatchStockTransferRequestDto());
        var d2 = _client.PostAsJsonAsync($"/api/stock-transfers/{transfer.Id}/dispatch", new DispatchStockTransferRequestDto());
        var dispatchResults = await Task.WhenAll(d1, d2);

        dispatchResults.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        dispatchResults.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);

        var dConflict = dispatchResults.First(r => r.StatusCode == HttpStatusCode.Conflict);
        var dConflictBody = await dConflict.Content.ReadAsStringAsync();
        dConflictBody.Should().Contain("INVALID_STATE_TRANSITION");

        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var t = await verifyContext.StockTransfers.FirstAsync(x => x.Id == transfer.Id);
            t.Status.Should().Be("InTransit");
            t.DispatchedAt.Should().NotBeNull();

            var bBal = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            var wBal = await verifyContext.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == infra.Sku1Id);
            bBal.OnHand.Should().Be(15m, "OnHand giảm đúng 5 đơn vị.");
            wBal.OnHand.Should().Be(15m);

            var ledgerOut = await verifyContext.InventoryTransactions
                .Where(tx => tx.TransferItemId == transfer.Items[0].Id && tx.Type == "OUT")
                .ToListAsync();
            ledgerOut.Should().HaveCount(1, "Chỉ được ghi duy nhất 1 bút toán ledger OUT.");
        }

        // 2. Concurrent Receive trên cùng 1 phiếu InTransit
        var r1 = _client.PostAsJsonAsync($"/api/stock-transfers/{transfer.Id}/receive", new ReceiveStockTransferRequestDto());
        var r2 = _client.PostAsJsonAsync($"/api/stock-transfers/{transfer.Id}/receive", new ReceiveStockTransferRequestDto());
        var receiveResults = await Task.WhenAll(r1, r2);

        receiveResults.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        receiveResults.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);

        var rConflict = receiveResults.First(r => r.StatusCode == HttpStatusCode.Conflict);
        var rConflictBody = await rConflict.Content.ReadAsStringAsync();
        rConflictBody.Should().Contain("INVALID_STATE_TRANSITION");

        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var t = await verifyContext.StockTransfers.FirstAsync(x => x.Id == transfer.Id);
            t.Status.Should().Be("Received");
            t.ReceivedAt.Should().NotBeNull();
            t.ReceivedBy.Should().NotBeNull();

            var destBBal = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchBId && b.SkuId == infra.Sku1Id);
            var destWBal = await verifyContext.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseB1Id && w.SkuId == infra.Sku1Id);
            destBBal.OnHand.Should().Be(5m, "OnHand đích tăng đúng 5 đơn vị.");
            destWBal.OnHand.Should().Be(5m);

            var ledgerIn = await verifyContext.InventoryTransactions
                .Where(tx => tx.TransferItemId == transfer.Items[0].Id && tx.Type == "IN")
                .ToListAsync();
            ledgerIn.Should().HaveCount(1, "Chỉ được ghi duy nhất 1 bút toán ledger IN.");
        }
    }

    // =========================================================================
    // AC-06: Concurrency & Deadlock Freedom
    // =========================================================================
    [Fact]
    public async Task AC06_Concurrency_FiftyDraftsCompeteForOneStock_ExactlyOneSucceeds_FortyNineInsufficientStock()
    {
        // Kiểm chứng T4A-F07: Đúng 50 Draft transfers cạnh tranh 1 tồn kho duy nhất -> 1 thành công, 49 INSUFFICIENT_STOCK
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 1m, 80000m);

        var transferIds = new List<Guid>();
        for (int i = 0; i < 50; i++)
        {
            var res = await _client.PostAsJsonAsync("/api/stock-transfers", new CreateStockTransferRequestDto
            {
                RequestKey = Guid.NewGuid(),
                FromBranchId = infra.BranchAId,
                ToBranchId = infra.BranchBId,
                FromWarehouseId = infra.WarehouseA1Id,
                ToWarehouseId = infra.WarehouseB1Id,
                Items = new List<StockTransferItemRequestDto> { new() { SkuId = infra.Sku1Id, Quantity = 1m } }
            });
            var t = await res.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);
            transferIds.Add(t!.Id);
        }

        var dispatchTasks = transferIds.Select(id => _client.PostAsJsonAsync($"/api/stock-transfers/{id}/dispatch", new DispatchStockTransferRequestDto()));
        var responses = await Task.WhenAll(dispatchTasks);

        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        successCount.Should().Be(1, "Chỉ duy nhất 1 phiếu được phép xuất kho thành công khi chỉ có 1 sản phẩm tồn.");
        conflictCount.Should().Be(49, "49 phiếu còn lại phải nhận 409 Conflict do hết tồn kho khả dụng.");

        // Kiểm tra toàn bộ 49 conflict đều chứa đúng errorCode INSUFFICIENT_STOCK
        foreach (var conflictRes in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            var conflictBody = await conflictRes.Content.ReadAsStringAsync();
            conflictBody.Should().Contain("INSUFFICIENT_STOCK", "Toàn bộ 49 xung đột phải có mã lỗi INSUFFICIENT_STOCK.");
        }

        var (context, _) = CreatePostgresDbContext(infra.TenantId);
        await using (context)
        {
            var bBal = await context.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            bBal.OnHand.Should().Be(0m);
        }
    }

    [Fact]
    public async Task AC06_Concurrency_BidirectionalReversedSkuOrder_NoDeadlock()
    {
        // Kiểm chứng T4A-F05: Sắp xếp tập khóa SKU theo thứ tự từ điển chống deadlock giữa 2 chiều A->B và B->A
        var infra = await SetupInfrastructureAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku2Id, 10m, 60000m);
        await SeedStockAsync(infra.TenantId, infra.BranchBId, infra.WarehouseB1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);
        await SeedStockAsync(infra.TenantId, infra.BranchBId, infra.WarehouseB1Id, infra.SupplierId, infra.UserId, infra.Sku2Id, 10m, 60000m);

        // Phiếu 1: Branch A -> B với [Sku1, Sku2]
        var t1 = await (await _client.PostAsJsonAsync("/api/stock-transfers", new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Items = new List<StockTransferItemRequestDto>
            {
                new() { SkuId = infra.Sku1Id, Quantity = 2m },
                new() { SkuId = infra.Sku2Id, Quantity = 2m }
            }
        })).Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);

        // Phiếu 2: Branch B -> A với [Sku2, Sku1] (đảo ngược thứ tự)
        var t2 = await (await _client.PostAsJsonAsync("/api/stock-transfers", new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchBId,
            ToBranchId = infra.BranchAId,
            FromWarehouseId = infra.WarehouseB1Id,
            ToWarehouseId = infra.WarehouseA1Id,
            Items = new List<StockTransferItemRequestDto>
            {
                new() { SkuId = infra.Sku2Id, Quantity = 2m },
                new() { SkuId = infra.Sku1Id, Quantity = 2m }
            }
        })).Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);

        var task1 = _client.PostAsJsonAsync($"/api/stock-transfers/{t1!.Id}/dispatch", new DispatchStockTransferRequestDto());
        var task2 = _client.PostAsJsonAsync($"/api/stock-transfers/{t2!.Id}/dispatch", new DispatchStockTransferRequestDto());

        var resBoth = await Task.WhenAll(task1, task2);
        resBoth.All(r => r.StatusCode == HttpStatusCode.OK).Should().BeTrue("Không xảy ra Deadlock khi xuất hàng 2 chiều đảo thứ tự SKU.");
    }

    // =========================================================================
    // AC-07: Fault Injection & Atomic Rollback
    // =========================================================================
    [Fact]
    public async Task AC07_FaultInjection_MidstreamFailure_CreateDispatchReceive_RollsBackCleanly_AndRetrySucceeds()
    {
        // Kiểm chứng T4A-F07: Fault injection sau SQL trước Commit cho cả Create, Dispatch và Receive
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 50m, 60000m);

        long bSeq0, wSeq0;
        int ledgerCount0;
        await using (var ctx = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var b = await ctx.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            var w = await ctx.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == infra.Sku1Id);
            bSeq0 = b.LastSequence;
            wSeq0 = w.LastSequence;
            ledgerCount0 = await ctx.InventoryTransactions.CountAsync();
        }

        var interceptor = new MidstreamFailureInterceptor { ShouldFail = true };
        var (contextWithFault, tenantService) = CreatePostgresDbContext(infra.TenantId, interceptor);
        var wacCalc = new WacCalculator();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<StockTransferService>.Instance;
        var faultyService = new StockTransferService(contextWithFault, tenantService, wacCalc, logger);

        var requestDto = new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Items = new List<StockTransferItemRequestDto> { new() { SkuId = infra.Sku1Id, Quantity = 10m } }
        };

        // 1. Fault khi Create
        var actCreate = () => faultyService.CreateTransferAsync(requestDto, infra.UserId);
        await actCreate.Should().ThrowAsync<MidstreamTransferFaultException>();

        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var count = await verifyContext.StockTransfers.CountAsync(t => t.RequestKey == requestDto.RequestKey);
            count.Should().Be(0, "Transaction tạo phiếu bị crash phải rollback 100%.");

            var b = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            var w = await verifyContext.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == infra.Sku1Id);
            b.OnHand.Should().Be(50m);
            b.Reserved.Should().Be(0m);
            b.AverageCost.Should().Be(60000m);
            b.LastSequence.Should().Be(bSeq0);
            w.OnHand.Should().Be(50m);
            w.Reserved.Should().Be(0m);
            w.LastSequence.Should().Be(wSeq0);

            var ledgerCount = await verifyContext.InventoryTransactions.CountAsync();
            ledgerCount.Should().Be(ledgerCount0);
        }

        // Retry Create thành công đúng 1 lần
        interceptor.ShouldFail = false;
        var created = await faultyService.CreateTransferAsync(requestDto, infra.UserId);
        created.Status.Should().Be("Draft");
        created.DispatchedAt.Should().BeNull();
        created.ReceivedAt.Should().BeNull();
        created.Items.Should().HaveCount(1);
        created.Items[0].UnitCost.Should().BeNull();

        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var t = await verifyContext.StockTransfers.Include(x => x.Items).FirstAsync(x => x.Id == created.Id);
            t.Status.Should().Be("Draft");
            t.DispatchedAt.Should().BeNull();
            t.ReceivedAt.Should().BeNull();
            t.Items.First().UnitCost.Should().BeNull();

            var b = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            var w = await verifyContext.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == infra.Sku1Id);
            b.OnHand.Should().Be(50m);
            b.Reserved.Should().Be(0m);
            w.OnHand.Should().Be(50m);
            w.Reserved.Should().Be(0m);
            (await verifyContext.InventoryTransactions.CountAsync()).Should().Be(ledgerCount0);
        }

        // 2. Fault khi Dispatch
        interceptor.ShouldFail = true;
        var actDispatch = () => faultyService.DispatchTransferAsync(created.Id, new DispatchStockTransferRequestDto(), infra.UserId);
        await actDispatch.Should().ThrowAsync<MidstreamTransferFaultException>();

        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var t = await verifyContext.StockTransfers.Include(x => x.Items).FirstAsync(x => x.Id == created.Id);
            t.Status.Should().Be("Draft");
            t.DispatchedAt.Should().BeNull();
            t.Items.First().UnitCost.Should().BeNull();

            var b = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            var w = await verifyContext.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == infra.Sku1Id);
            b.OnHand.Should().Be(50m, "OnHand không bị trừ khi dispatch bị fault.");
            b.Reserved.Should().Be(0m);
            b.AverageCost.Should().Be(60000m);
            b.LastSequence.Should().Be(bSeq0);
            w.OnHand.Should().Be(50m);
            w.Reserved.Should().Be(0m);
            w.LastSequence.Should().Be(wSeq0);

            var ledgerCount = await verifyContext.InventoryTransactions.CountAsync();
            ledgerCount.Should().Be(ledgerCount0, "Không rò rỉ bút toán ledger OUT khi dispatch fault.");
        }

        // Retry Dispatch thành công đúng 1 lần
        interceptor.ShouldFail = false;
        var dispatched = await faultyService.DispatchTransferAsync(created.Id, new DispatchStockTransferRequestDto(), infra.UserId);
        dispatched.Status.Should().Be("InTransit");
        dispatched.DispatchedAt.Should().NotBeNull();
        dispatched.Items[0].UnitCost.Should().Be(60000m);

        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var t = await verifyContext.StockTransfers.Include(x => x.Items).FirstAsync(x => x.Id == created.Id);
            t.Status.Should().Be("InTransit");
            t.DispatchedAt.Should().NotBeNull();
            t.Items.First().UnitCost.Should().Be(60000m);

            var b = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchAId && b.SkuId == infra.Sku1Id);
            var w = await verifyContext.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == infra.Sku1Id);
            b.OnHand.Should().Be(40m);
            b.Reserved.Should().Be(0m);
            b.AverageCost.Should().Be(60000m);
            b.LastSequence.Should().Be(bSeq0 + 1);
            w.OnHand.Should().Be(40m);
            w.Reserved.Should().Be(0m);
            w.LastSequence.Should().Be(wSeq0 + 1);

            var ledgerOut = await verifyContext.InventoryTransactions
                .Where(tx => tx.TransferItemId == created.Items[0].Id && tx.Type == "OUT")
                .ToListAsync();
            ledgerOut.Should().HaveCount(1, "Có đúng 1 bút toán ledger OUT được ghi khi retry dispatch.");
            ledgerOut[0].Quantity.Should().Be(10m);
            ledgerOut[0].UnitCost.Should().Be(60000m);
        }

        // 3. Fault khi Receive
        interceptor.ShouldFail = true;
        var actReceive = () => faultyService.ReceiveTransferAsync(created.Id, new ReceiveStockTransferRequestDto(), infra.UserId);
        await actReceive.Should().ThrowAsync<MidstreamTransferFaultException>();

        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var t = await verifyContext.StockTransfers.FirstAsync(x => x.Id == created.Id);
            t.Status.Should().Be("InTransit");
            t.ReceivedAt.Should().BeNull();
            t.ReceivedBy.Should().BeNull();

            var destBal = await verifyContext.InventoryBalances.FirstOrDefaultAsync(b => b.BranchId == infra.BranchBId && b.SkuId == infra.Sku1Id);
            (destBal?.OnHand ?? 0m).Should().Be(0m, "Tồn kho nhận chưa được ghi khi receive bị fault.");

            var destWBal = await verifyContext.WarehouseInventoryBalances.FirstOrDefaultAsync(w => w.WarehouseId == infra.WarehouseB1Id && w.SkuId == infra.Sku1Id);
            (destWBal?.OnHand ?? 0m).Should().Be(0m);

            var ledgerInCount = await verifyContext.InventoryTransactions
                .CountAsync(tx => tx.TransferItemId == created.Items[0].Id && tx.Type == "IN");
            ledgerInCount.Should().Be(0, "Không rò rỉ bút toán ledger IN khi receive fault.");
        }

        // Retry Receive thành công đúng 1 lần
        interceptor.ShouldFail = false;
        var received = await faultyService.ReceiveTransferAsync(created.Id, new ReceiveStockTransferRequestDto(), infra.UserId);
        received.Status.Should().Be("Received");
        received.ReceivedAt.Should().NotBeNull();
        received.ReceivedBy.Should().Be(infra.UserId);

        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var t = await verifyContext.StockTransfers.FirstAsync(x => x.Id == created.Id);
            t.Status.Should().Be("Received");
            t.ReceivedAt.Should().NotBeNull();
            t.ReceivedBy.Should().Be(infra.UserId);

            var destBal = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == infra.BranchBId && b.SkuId == infra.Sku1Id);
            destBal.OnHand.Should().Be(10m);
            destBal.Reserved.Should().Be(0m);
            destBal.AverageCost.Should().Be(60000m);

            var destWBal = await verifyContext.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseB1Id && w.SkuId == infra.Sku1Id);
            destWBal.OnHand.Should().Be(10m);
            destWBal.Reserved.Should().Be(0m);

            var ledgerIn = await verifyContext.InventoryTransactions
                .Where(tx => tx.TransferItemId == created.Items[0].Id && tx.Type == "IN")
                .ToListAsync();
            ledgerIn.Should().HaveCount(1, "Có đúng 1 bút toán ledger IN được ghi khi retry receive.");
            ledgerIn[0].Quantity.Should().Be(10m);
            ledgerIn[0].UnitCost.Should().Be(60000m);
        }
    }

    // =========================================================================
    // AC-08: Phân quyền RBAC, Cross-Tenant Isolation & Biên số học
    // =========================================================================
    [Fact]
    public async Task AC08_Security_RBAC_CrossTenant_AndNumericLimits()
    {
        var infra = await SetupInfrastructureAsync();

        // 1. 401 Unauthorized khi không có token
        _client.DefaultRequestHeaders.Authorization = null;
        var unauthRes = await _client.PostAsJsonAsync("/api/stock-transfers", new CreateStockTransferRequestDto());
        unauthRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 2. 403 Forbidden khi đăng nhập bằng vai trò Thu ngân (Cashier)
        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);

        var cashierCreate = await _client.PostAsJsonAsync("/api/stock-transfers", new CreateStockTransferRequestDto());
        cashierCreate.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var randomId = Guid.NewGuid();
        var cashierDispatch = await _client.PostAsJsonAsync($"/api/stock-transfers/{randomId}/dispatch", new DispatchStockTransferRequestDto());
        cashierDispatch.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var cashierReceive = await _client.PostAsJsonAsync($"/api/stock-transfers/{randomId}/receive", new ReceiveStockTransferRequestDto());
        cashierReceive.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var cashierCancel = await _client.PostAsJsonAsync($"/api/stock-transfers/{randomId}/cancel", new CancelStockTransferRequestDto());
        cashierCancel.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 3. Đăng nhập lại Owner Tenant A
        var ownerTokenA = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenA);

        // Kiểm tra items chứa phần tử null -> 400 VALIDATION_ERROR (T4A-F06)
        var nullItemRes = await _client.PostAsJsonAsync("/api/stock-transfers", new
        {
            requestKey = Guid.NewGuid(),
            fromBranchId = infra.BranchAId,
            toBranchId = infra.BranchBId,
            fromWarehouseId = infra.WarehouseA1Id,
            toWarehouseId = infra.WarehouseB1Id,
            items = new object?[] { null }
        });
        nullItemRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var nullBody = await nullItemRes.Content.ReadAsStringAsync();
        nullBody.Should().Contain("VALIDATION_ERROR");

        // Tạo 1 phiếu của Tenant A
        var createResA = await _client.PostAsJsonAsync("/api/stock-transfers", new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Items = new List<StockTransferItemRequestDto> { new() { SkuId = infra.Sku1Id, Quantity = 2m } }
        });
        var transferA = await createResA.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);

        // 4. Cross-Tenant: Đăng nhập Owner Tenant B và cố gắng thao tác trên phiếu hoặc tài nguyên của Tenant A -> 404 Not Found (T4A-F06)
        var ownerTokenB = await LoginAsAsync("owner@shopb.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenB);

        // Tenant B tạo phiếu nhưng truyền FromBranchId của Tenant A -> 404 Not Found
        var crossCreateRes = await _client.PostAsJsonAsync("/api/stock-transfers", new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Items = new List<StockTransferItemRequestDto> { new() { SkuId = infra.Sku1Id, Quantity = 1m } }
        });
        crossCreateRes.StatusCode.Should().Be(HttpStatusCode.NotFound, "Tài nguyên chi nhánh của tenant khác phải trả 404.");

        var crossDispatch = await _client.PostAsJsonAsync($"/api/stock-transfers/{transferA!.Id}/dispatch", new DispatchStockTransferRequestDto());
        crossDispatch.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var crossReceive = await _client.PostAsJsonAsync($"/api/stock-transfers/{transferA.Id}/receive", new ReceiveStockTransferRequestDto());
        crossReceive.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var crossCancel = await _client.PostAsJsonAsync($"/api/stock-transfers/{transferA.Id}/cancel", new CancelStockTransferRequestDto());
        crossCancel.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Đối soát dữ liệu Tenant A không bị biến động
        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var t = await verifyContext.StockTransfers.FirstAsync(x => x.Id == transferA.Id);
            t.Status.Should().Be("Draft");
        }

        // 5. Kiểm tra ràng buộc Kho trùng nhau: FromWarehouseId == ToWarehouseId -> 400 Validation Error
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenA);
        var sameWarehouseRes = await _client.PostAsJsonAsync("/api/stock-transfers", new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchAId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseA1Id,
            Items = new List<StockTransferItemRequestDto> { new() { SkuId = infra.Sku1Id, Quantity = 2m } }
        });
        sameWarehouseRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 6. Kiểm tra giới hạn số lượng: Quá 3 chữ số thập phân (scale > 3) -> 400 Validation Error
        var invalidScaleRes = await _client.PostAsJsonAsync("/api/stock-transfers", new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Items = new List<StockTransferItemRequestDto> { new() { SkuId = infra.Sku1Id, Quantity = 1.2345m } }
        });
        invalidScaleRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AC08_Validation_StructuralGuards_EmptyGuid_NullOrEmptyItems_DuplicateSku_InvalidQuantity_BranchWarehouseMismatch()
    {
        // Kiểm chứng T4A-F07: Bộ validation toàn diện cho AC-08:
        // 401 trên cả 4 endpoints, Guid.Empty, Items null/rỗng/trùng SKU, quantity không dương/vượt magnitude, sai cặp kho-chi nhánh
        var infra = await SetupInfrastructureAsync();
        var randomId = Guid.NewGuid();

        // 1. 401 Unauthorized trên cả 4 endpoints khi không có token xác thực
        _client.DefaultRequestHeaders.Authorization = null;

        var unauthCreate = await _client.PostAsJsonAsync("/api/stock-transfers", new CreateStockTransferRequestDto());
        unauthCreate.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var unauthDispatch = await _client.PostAsJsonAsync($"/api/stock-transfers/{randomId}/dispatch", new DispatchStockTransferRequestDto());
        unauthDispatch.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var unauthReceive = await _client.PostAsJsonAsync($"/api/stock-transfers/{randomId}/receive", new ReceiveStockTransferRequestDto());
        unauthReceive.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var unauthCancel = await _client.PostAsJsonAsync($"/api/stock-transfers/{randomId}/cancel", new CancelStockTransferRequestDto());
        unauthCancel.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Đăng nhập vai trò Owner hợp lệ
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        async Task AssertBadRequest(CreateStockTransferRequestDto dto, string expectedErrorSubstr)
        {
            var res = await _client.PostAsJsonAsync("/api/stock-transfers", dto);
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = await res.Content.ReadAsStringAsync();
            body.Should().Contain(expectedErrorSubstr);
        }

        CreateStockTransferRequestDto ValidDto() => new()
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchAId,
            ToBranchId = infra.BranchBId,
            FromWarehouseId = infra.WarehouseA1Id,
            ToWarehouseId = infra.WarehouseB1Id,
            Items = new List<StockTransferItemRequestDto> { new() { SkuId = infra.Sku1Id, Quantity = 1m } }
        };

        // 2. Kiểm tra Guid.Empty cho các trường khóa chính/ngoại
        var dtoEmptyKey = ValidDto(); dtoEmptyKey.RequestKey = Guid.Empty;
        await AssertBadRequest(dtoEmptyKey, "VALIDATION_ERROR");

        var dtoEmptyFromBranch = ValidDto(); dtoEmptyFromBranch.FromBranchId = Guid.Empty;
        await AssertBadRequest(dtoEmptyFromBranch, "VALIDATION_ERROR");

        var dtoEmptyToBranch = ValidDto(); dtoEmptyToBranch.ToBranchId = Guid.Empty;
        await AssertBadRequest(dtoEmptyToBranch, "VALIDATION_ERROR");

        var dtoEmptyFromWh = ValidDto(); dtoEmptyFromWh.FromWarehouseId = Guid.Empty;
        await AssertBadRequest(dtoEmptyFromWh, "VALIDATION_ERROR");

        var dtoEmptyToWh = ValidDto(); dtoEmptyToWh.ToWarehouseId = Guid.Empty;
        await AssertBadRequest(dtoEmptyToWh, "VALIDATION_ERROR");

        var dtoEmptySku = ValidDto(); dtoEmptySku.Items[0].SkuId = Guid.Empty;
        await AssertBadRequest(dtoEmptySku, "VALIDATION_ERROR");

        // 3. Kiểm tra Items null / rỗng / trùng SKU
        var dtoNullItems = ValidDto(); dtoNullItems.Items = null!;
        await AssertBadRequest(dtoNullItems, "VALIDATION_ERROR");

        var dtoEmptyItems = ValidDto(); dtoEmptyItems.Items = new List<StockTransferItemRequestDto>();
        await AssertBadRequest(dtoEmptyItems, "VALIDATION_ERROR");

        var dtoDuplicateSku = ValidDto();
        dtoDuplicateSku.Items.Add(new StockTransferItemRequestDto { SkuId = infra.Sku1Id, Quantity = 2m });
        await AssertBadRequest(dtoDuplicateSku, "VALIDATION_ERROR");

        // 4. Kiểm tra số lượng: không dương / vượt magnitude
        var dtoZeroQty = ValidDto(); dtoZeroQty.Items[0].Quantity = 0m;
        await AssertBadRequest(dtoZeroQty, "VALIDATION_ERROR");

        var dtoNegativeQty = ValidDto(); dtoNegativeQty.Items[0].Quantity = -5m;
        await AssertBadRequest(dtoNegativeQty, "VALIDATION_ERROR");

        var dtoOverflowQty = ValidDto(); dtoOverflowQty.Items[0].Quantity = 1_000_000_000_000_000_000m;
        await AssertBadRequest(dtoOverflowQty, "VALIDATION_ERROR");

        // 5. Kiểm tra sai cặp kho - chi nhánh
        var dtoFromMismatch = ValidDto();
        dtoFromMismatch.FromWarehouseId = infra.WarehouseB1Id; // Kho B1 thuộc Branch B, nhưng FromBranchId là Branch A
        dtoFromMismatch.ToWarehouseId = infra.WarehouseA1Id;   // Đảm bảo From != To để kiểm tra đúng logic chi nhánh
        await AssertBadRequest(dtoFromMismatch, "Kho xuất không thuộc chi nhánh xuất");

        var dtoToMismatch = ValidDto();
        dtoToMismatch.ToWarehouseId = infra.WarehouseA2Id; // Kho A2 thuộc Branch A, nhưng ToBranchId là Branch B (From: A1 != To: A2)
        await AssertBadRequest(dtoToMismatch, "Kho nhận không thuộc chi nhánh nhận");

        // 6. Đối soát CSDL: Hoàn toàn không phát sinh bất kỳ bản ghi nào
        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var count = await verifyContext.StockTransfers.CountAsync();
            count.Should().Be(0, "Mọi request vi phạm validation đều bị từ chối và không làm phát sinh mutation trong database.");
        }
    }

    // =========================================================================
    // AC-09: Tương thích ngược schema, phát hiện Schema Drift & Nâng cấp an toàn
    // =========================================================================
    [Fact]
    public async Task AC09_SchemaDrift_ValidatorDetectsMissingPayloadSignature_AndUpgradeDatabaseSchemaAsyncIdempotent()
    {
        // Kiểm chứng T4A-F04: Validator phát hiện schema drift thiếu payload_signature, và upgrade chạy 2 lần an toàn
        PostgresTestEnvironment.AssertSafeTestTarget(PostgresTestEnvironment.DatabaseName);

        var migrator = new DatabaseMigrationService();

        // 1. Thao tác trên test database: Xóa cột payload_signature để mô phỏng schema drift
        await using (var adminConn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await adminConn.OpenAsync();
            await using var dropCmd = new NpgsqlCommand("ALTER TABLE public.stock_transfers DROP COLUMN IF EXISTS payload_signature;", adminConn);
            await dropCmd.ExecuteNonQueryAsync();
        }

        // 2. Chạy validator -> Phải báo Schema Drift (Success = false)
        var driftCheck = await migrator.InitializeOrBaselineDatabaseAsync(PostgresTestEnvironment.AdminDatabaseConnectionString);
        driftCheck.Success.Should().BeFalse("Validator phải phát hiện thiếu cột trọng yếu payload_signature.");
        driftCheck.Message.Should().Contain("Phát hiện sai lệch cấu trúc");

        // 3. Chạy UpgradeDatabaseSchemaAsync 2 lần liên tiếp -> Đều thành công
        var upgrade1 = await migrator.UpgradeDatabaseSchemaAsync(PostgresTestEnvironment.AdminDatabaseConnectionString);
        upgrade1.Success.Should().BeTrue();

        var upgrade2 = await migrator.UpgradeDatabaseSchemaAsync(PostgresTestEnvironment.AdminDatabaseConnectionString);
        upgrade2.Success.Should().BeTrue("Upgrade lần 2 phải idempotent và thành công.");

        // 4. Chạy lại validator sau nâng cấp -> Phải đạt Success = true
        var cleanCheck = await migrator.InitializeOrBaselineDatabaseAsync(PostgresTestEnvironment.AdminDatabaseConnectionString);
        cleanCheck.Success.Should().BeTrue("Database sau khi upgrade phải vượt qua toàn bộ kiểm tra ràng buộc.");

        // 5. Kiểm tra phiếu chuyển kho legacy (RequestKey = null) vẫn hoạt động hoàn hảo sau nâng cấp
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 20m, 40000m);

        var legacyTransferId = Guid.NewGuid();
        var (context, _) = CreatePostgresDbContext(infra.TenantId);
        await using (context)
        {
            var legacy = new StockTransfer
            {
                Id = legacyTransferId,
                TenantId = infra.TenantId,
                Number = $"TRF-LEGACY-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
                RequestKey = null,
                PayloadSignature = null,
                FromBranchId = infra.BranchAId,
                ToBranchId = infra.BranchBId,
                FromWarehouseId = infra.WarehouseA1Id,
                ToWarehouseId = infra.WarehouseB1Id,
                Status = "Draft",
                CreatedBy = infra.UserId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            legacy.Items.Add(new StockTransferItem
            {
                Id = Guid.NewGuid(),
                TenantId = infra.TenantId,
                TransferId = legacyTransferId,
                SkuId = infra.Sku1Id,
                Quantity = 5m,
                CreatedAt = DateTimeOffset.UtcNow
            });
            context.StockTransfers.Add(legacy);
            await context.SaveChangesAsync();
        }

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var dispatchRes = await _client.PostAsJsonAsync($"/api/stock-transfers/{legacyTransferId}/dispatch", new DispatchStockTransferRequestDto());
        dispatchRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var receiveRes = await _client.PostAsJsonAsync($"/api/stock-transfers/{legacyTransferId}/receive", new ReceiveStockTransferRequestDto());
        receiveRes.StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var verifyContext = CreatePostgresDbContext(infra.TenantId).Context)
        {
            var t = await verifyContext.StockTransfers.FirstAsync(x => x.Id == legacyTransferId);
            t.Status.Should().Be("Received");
        }
    }

    [Fact]
    public async Task AC09_Migration_PreExistingLegacyDataBeforeSchemaUpgrade_UpgradeTwiceIdempotent_LegacyWorkflowSucceeds()
    {
        // Kiểm chứng T4A-F07: Migration từ schema cũ đang có dữ liệu:
        // Khởi đầu từ schema trước Task 4A (xóa cả request_key, payload_signature và constraint),
        // tạo chứng từ/items legacy tồn tại TRƯỚC nâng schema, sau đó upgrade cả 2 cột/constraint,
        // chạy lại upgrade lần 2 idempotent, bảo toàn dữ liệu và workflow legacy (dispatch, receive, cancel).
        PostgresTestEnvironment.AssertSafeTestTarget(PostgresTestEnvironment.DatabaseName);

        var infra = await SetupInfrastructureAsync();
        var migrator = new DatabaseMigrationService();
        var adminConnStr = PostgresTestEnvironment.AdminDatabaseConnectionString;

        try
        {
            await using (var adminConn = new NpgsqlConnection(adminConnStr))
            {
                await adminConn.OpenAsync();

                // 1. Giả lập schema cũ trước Task 4A: Xóa 2 cột mới và constraint
                await using (var dropCmd = new NpgsqlCommand(@"
                    ALTER TABLE public.stock_transfers DROP CONSTRAINT IF EXISTS uq_stock_transfers_3;
                    ALTER TABLE public.stock_transfers DROP COLUMN IF EXISTS request_key;
                    ALTER TABLE public.stock_transfers DROP COLUMN IF EXISTS payload_signature;
                ", adminConn))
                {
                    await dropCmd.ExecuteNonQueryAsync();
                }

                // 2. Chèn 2 chứng từ chuyển kho legacy trực tiếp vào schema cũ
                var now = DateTimeOffset.UtcNow;
                var legacy1Id = Guid.NewGuid();
                var legacy2Id = Guid.NewGuid();
                var item1Id = Guid.NewGuid();
                var item2Id = Guid.NewGuid();
                var tenantId = infra.TenantId;
                var b1Id = infra.BranchAId;
                var w1Id = infra.WarehouseA1Id;
                var b2Id = infra.BranchBId;
                var w2Id = infra.WarehouseB1Id;
                var sId = infra.Sku1Id;
                var uId = infra.UserId;
                var supId = infra.SupplierId;

                await using (var insertCmd = new NpgsqlCommand(@"
                    INSERT INTO public.stock_transfers (id, tenant_id, number, from_branch_id, to_branch_id, from_warehouse_id, to_warehouse_id, status, created_by, created_at)
                    VALUES 
                    (@l1, @tid, 'TRF-LEG-PRE-00001', @b1, @b2, @w1, @w2, 'Draft', @uid, @now),
                    (@l2, @tid, 'TRF-LEG-PRE-00002', @b1, @b2, @w1, @w2, 'Draft', @uid, @now);
                    INSERT INTO public.stock_transfer_items (id, tenant_id, transfer_id, sku_id, quantity, created_at)
                    VALUES
                    (@i1, @tid, @l1, @sId, 5, @now),
                    (@i2, @tid, @l2, @sId, 3, @now);
                ", adminConn))
                {
                    insertCmd.Parameters.AddWithValue("l1", legacy1Id);
                    insertCmd.Parameters.AddWithValue("l2", legacy2Id);
                    insertCmd.Parameters.AddWithValue("i1", item1Id);
                    insertCmd.Parameters.AddWithValue("i2", item2Id);
                    insertCmd.Parameters.AddWithValue("tid", tenantId);
                    insertCmd.Parameters.AddWithValue("b1", b1Id);
                    insertCmd.Parameters.AddWithValue("b2", b2Id);
                    insertCmd.Parameters.AddWithValue("w1", w1Id);
                    insertCmd.Parameters.AddWithValue("w2", w2Id);
                    insertCmd.Parameters.AddWithValue("sId", sId);
                    insertCmd.Parameters.AddWithValue("uid", uId);
                    insertCmd.Parameters.AddWithValue("now", now);
                    await insertCmd.ExecuteNonQueryAsync();
                }

                // 3. Thực hiện nâng cấp schema lần 1
                var upgrade1 = await migrator.UpgradeDatabaseSchemaAsync(adminConnStr);
                upgrade1.Success.Should().BeTrue("Upgrade schema lần 1 trên database có sẵn dữ liệu legacy phải thành công.");

                // 4. Kiểm tra dữ liệu legacy được bảo toàn nguyên vẹn
                await using (var verifyCmd = new NpgsqlCommand(@"
                    SELECT count(*), count(request_key), count(payload_signature)
                    FROM public.stock_transfers
                    WHERE id IN (@l1, @l2);", adminConn))
                {
                    verifyCmd.Parameters.AddWithValue("l1", legacy1Id);
                    verifyCmd.Parameters.AddWithValue("l2", legacy2Id);
                    await using var vReader = await verifyCmd.ExecuteReaderAsync();
                    await vReader.ReadAsync();
                    vReader.GetInt64(0).Should().Be(2, "Cả 2 phiếu legacy vẫn tồn tại đầy đủ.");
                    vReader.GetInt64(1).Should().Be(0, "request_key của chứng từ cũ là NULL.");
                    vReader.GetInt64(2).Should().Be(0, "payload_signature của chứng từ cũ là NULL.");
                }

                // 5. Thực hiện nâng cấp schema lần 2 (idempotent)
                var upgrade2 = await migrator.UpgradeDatabaseSchemaAsync(adminConnStr);
                upgrade2.Success.Should().BeTrue("Upgrade lần 2 phải idempotent và an toàn.");

                // 6. Kiểm tra lại bằng validator
                var check = await migrator.InitializeOrBaselineDatabaseAsync(adminConnStr);
                check.Success.Should().BeTrue("Validator xác nhận cấu trúc CSDL hợp lệ sau khi nâng cấp.");

                // 7. Nhập tồn để thực hiện workflow
                await SeedStockAsync(tenantId, b1Id, w1Id, supId, uId, sId, 50m, 80000m);

                // 8. Thực hiện workflow trên chứng từ legacy:
                var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
                _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

                // Legacy 1: Dispatch -> Receive thành công
                var dispatchRes = await _client.PostAsJsonAsync($"/api/stock-transfers/{legacy1Id}/dispatch", new DispatchStockTransferRequestDto());
                dispatchRes.StatusCode.Should().Be(HttpStatusCode.OK);

                var receiveRes = await _client.PostAsJsonAsync($"/api/stock-transfers/{legacy1Id}/receive", new ReceiveStockTransferRequestDto());
                receiveRes.StatusCode.Should().Be(HttpStatusCode.OK);

                // Legacy 2: Cancel thành công khi còn Draft
                var cancelRes = await _client.PostAsJsonAsync($"/api/stock-transfers/{legacy2Id}/cancel", new CancelStockTransferRequestDto { Reason = "Legacy Cancel" });
                cancelRes.StatusCode.Should().Be(HttpStatusCode.OK);

                // 9. Đối soát CSDL cho cả 2 phiếu legacy
                await using var checkLegacyCmd = new NpgsqlCommand(@"
                    SELECT st.id, st.status, sti.unit_cost
                    FROM public.stock_transfers st
                    JOIN public.stock_transfer_items sti ON sti.transfer_id = st.id
                    WHERE st.id = @l1;", adminConn);
                checkLegacyCmd.Parameters.AddWithValue("l1", legacy1Id);
                await using var clReader = await checkLegacyCmd.ExecuteReaderAsync();
                await clReader.ReadAsync();
                clReader.GetString(1).Should().Be("Received");
                clReader.GetDecimal(2).Should().Be(80000.000000m);
                await clReader.CloseAsync();

                await using var checkCancelCmd = new NpgsqlCommand(@"
                    SELECT status FROM public.stock_transfers WHERE id = @l2;", adminConn);
                checkCancelCmd.Parameters.AddWithValue("l2", legacy2Id);
                var cancelStatus = (string)(await checkCancelCmd.ExecuteScalarAsync())!;
                cancelStatus.Should().Be("Cancelled");
            }
        }
        finally
        {
            // Bảo đảm schema CSDL luôn được phục hồi đầy đủ
            await migrator.UpgradeDatabaseSchemaAsync(adminConnStr);
        }
    }
}
