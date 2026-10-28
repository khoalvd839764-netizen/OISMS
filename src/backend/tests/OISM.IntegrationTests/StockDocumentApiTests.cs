using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.StockCounts.Dtos;
using OISM.Application.Features.StockTransfers.Dtos;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Bộ kiểm thử tích hợp xác thực API đọc chứng từ (StockTransfers và StockCounts)
/// thực thi trên PostgreSQL 16+ thật qua WebApplicationFactoryWithPostgres.
/// Kiểm chứng toàn bộ các tiêu chí của S3-ADMIN-B01 (A01-AC-01..04 và PR26-F04):
/// 1. HTTP list/detail có chứng từ thật trong CSDL PostgreSQL.
/// 2. Phân quyền theo hợp đồng: Owner và Staff 200 OK, Cashier 403 Forbidden, Chưa đăng nhập 401 Unauthorized.
/// 3. Cô lập đa khách thuê (Multi-tenant Isolation): Tenant B tuyệt đối không đọc được chứng từ của Tenant A (404 ProblemDetails, không lộ danh sách).
/// 4. Phiên làm việc / trình duyệt mới đọc lại chứng từ cũ từ API máy chủ thật.
/// 5. ID không tồn tại trả về 404 ProblemDetails chuẩn RFC 7807 (NOT_FOUND).
/// </summary>
public class StockDocumentApiTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly Guid TenantAId = OismsDatabaseSeeder.TenantAId;
    private static readonly Guid TenantBId = OismsDatabaseSeeder.TenantBId;

    public static readonly Guid TransferAId = Guid.Parse("11111111-0000-0000-0009-000000000001");
    public static readonly Guid CountAId = Guid.Parse("11111111-0000-0000-0009-000000000002");
    public static readonly Guid TransferBId = Guid.Parse("22222222-0000-0000-0009-000000000001");
    public static readonly Guid CountBId = Guid.Parse("22222222-0000-0000-0009-000000000002");

    public StockDocumentApiTests(WebApplicationFactoryWithPostgres factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await OismsDatabaseSeeder.SeedAsync(db, PostgresTestEnvironment.AppPassword);
        await SeedDocumentsAsync(db);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedDocumentsAsync(AppDbContext db)
    {
        using (db.OpenPrivilegedScope())
        {
            var branchAId = Guid.Parse("11111111-0000-0000-0001-000000000001");
            var warehouseA1Id = Guid.Parse("11111111-0000-0000-0002-000000000001");
            var warehouseA2Id = Guid.Parse("11111111-0000-0000-0002-000000000002");
            var skuAId = Guid.Parse("11111111-0000-0000-0006-000000000001");
            var userAId = Guid.Parse("11111111-0000-0000-0000-000000000001");

            var branchBId = Guid.Parse("22222222-0000-0000-0001-000000000001");
            var warehouseB1Id = Guid.Parse("22222222-0000-0000-0002-000000000001");
            var warehouseB2Id = Guid.Parse("22222222-0000-0000-0002-000000000002");
            var skuBId = Guid.Parse("22222222-0000-0000-0006-000000000001");
            var userBId = Guid.Parse("22222222-0000-0000-0000-000000000001");

            // Khởi tạo kho thứ hai cho Tenant A và B để chuyển kho hợp lệ
            if (!await db.Warehouses.IgnoreQueryFilters().AnyAsync(w => w.Id == warehouseA2Id))
            {
                db.Warehouses.Add(new Warehouse
                {
                    Id = warehouseA2Id,
                    TenantId = TenantAId,
                    BranchId = branchAId,
                    Name = "Kho Phụ Quận 1",
                    IsActive = true
                });
            }
            if (!await db.Warehouses.IgnoreQueryFilters().AnyAsync(w => w.Id == warehouseB2Id))
            {
                db.Warehouses.Add(new Warehouse
                {
                    Id = warehouseB2Id,
                    TenantId = TenantBId,
                    BranchId = branchBId,
                    Name = "Kho Phụ Đống Đa",
                    IsActive = true
                });
            }
            await db.SaveChangesAsync();

            // Seed Transfer cho Tenant A
            if (!await db.StockTransfers.IgnoreQueryFilters().AnyAsync(t => t.Id == TransferAId))
            {
                db.StockTransfers.Add(new StockTransfer
                {
                    Id = TransferAId,
                    TenantId = TenantAId,
                    Number = "TRF-20261028-00001",
                    FromBranchId = branchAId,
                    ToBranchId = branchAId,
                    FromWarehouseId = warehouseA1Id,
                    ToWarehouseId = warehouseA2Id,
                    Status = "Draft",
                    CreatedBy = userAId,
                    CreatedAt = DateTimeOffset.UtcNow,
                    Items = new List<StockTransferItem>
                    {
                        new()
                        {
                            Id = Guid.NewGuid(),
                            TenantId = TenantAId,
                            SkuId = skuAId,
                            Quantity = 10,
                            UnitCost = 150000
                        }
                    }
                });
            }

            // Seed Count cho Tenant A
            if (!await db.StockCounts.IgnoreQueryFilters().AnyAsync(c => c.Id == CountAId))
            {
                db.StockCounts.Add(new StockCount
                {
                    Id = CountAId,
                    TenantId = TenantAId,
                    Number = "CNT-20261028-00001",
                    BranchId = branchAId,
                    WarehouseId = warehouseA1Id,
                    Status = "Draft",
                    CreatedBy = userAId,
                    CreatedAt = DateTimeOffset.UtcNow,
                    Items = new List<StockCountItem>
                    {
                        new()
                        {
                            Id = Guid.NewGuid(),
                            TenantId = TenantAId,
                            SkuId = skuAId,
                            ExpectedQuantity = 20,
                            CountedQuantity = 20
                        }
                    }
                });
            }

            // Seed Transfer cho Tenant B
            if (!await db.StockTransfers.IgnoreQueryFilters().AnyAsync(t => t.Id == TransferBId))
            {
                db.StockTransfers.Add(new StockTransfer
                {
                    Id = TransferBId,
                    TenantId = TenantBId,
                    Number = "TRF-20261028-00002",
                    FromBranchId = branchBId,
                    ToBranchId = branchBId,
                    FromWarehouseId = warehouseB1Id,
                    ToWarehouseId = warehouseB2Id,
                    Status = "Draft",
                    CreatedBy = userBId,
                    CreatedAt = DateTimeOffset.UtcNow,
                    Items = new List<StockTransferItem>
                    {
                        new()
                        {
                            Id = Guid.NewGuid(),
                            TenantId = TenantBId,
                            SkuId = skuBId,
                            Quantity = 5,
                            UnitCost = 80000
                        }
                    }
                });
            }

            // Seed Count cho Tenant B
            if (!await db.StockCounts.IgnoreQueryFilters().AnyAsync(c => c.Id == CountBId))
            {
                db.StockCounts.Add(new StockCount
                {
                    Id = CountBId,
                    TenantId = TenantBId,
                    Number = "CNT-20261028-00002",
                    BranchId = branchBId,
                    WarehouseId = warehouseB1Id,
                    Status = "Draft",
                    CreatedBy = userBId,
                    CreatedAt = DateTimeOffset.UtcNow,
                    Items = new List<StockCountItem>
                    {
                        new()
                        {
                            Id = Guid.NewGuid(),
                            TenantId = TenantBId,
                            SkuId = skuBId,
                            ExpectedQuantity = 15,
                            CountedQuantity = 15
                        }
                    }
                });
            }

            await db.SaveChangesAsync();
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

        loginRes.StatusCode.Should().Be(HttpStatusCode.OK, $"Đăng nhập thất bại cho user {email}");
        var authData = await loginRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        authData.Should().NotBeNull();
        authData!.AccessToken.Should().NotBeNullOrEmpty();
        return authData.AccessToken;
    }

    [Fact]
    public async Task StockTransfers_Endpoints_RoleAndAuthGuards_AndTenantIsolation()
    {
        var password = PostgresTestEnvironment.AppPassword;
        var ownerTokenA = await LoginAsAsync("owner@shopa.local", password);
        var staffTokenA = await LoginAsAsync("staff@shopa.local", password);
        var cashierTokenA = await LoginAsAsync("cashier@shopa.local", password);
        var ownerTokenB = await LoginAsAsync("owner@shopb.local", password);
        var staffTokenB = await LoginAsAsync("staff@shopb.local", password);

        // 1. Unauthenticated -> 401 Unauthorized
        _client.DefaultRequestHeaders.Authorization = null;
        var unauthListRes = await _client.GetAsync("/api/stock-transfers");
        unauthListRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var unauthDetailRes = await _client.GetAsync($"/api/stock-transfers/{TransferAId}");
        unauthDetailRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 2. Cashier -> 403 Forbidden
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierTokenA);
        var cashierListRes = await _client.GetAsync("/api/stock-transfers");
        cashierListRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var cashierDetailRes = await _client.GetAsync($"/api/stock-transfers/{TransferAId}");
        cashierDetailRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 3. Staff -> 200 OK for list and detail (chứng từ thật của Tenant A)
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffTokenA);
        var staffListRes = await _client.GetAsync("/api/stock-transfers");
        staffListRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var staffListData = await staffListRes.Content.ReadFromJsonAsync<PagedResultDto<StockTransferResponseDto>>(_jsonOptions);
        staffListData.Should().NotBeNull();
        staffListData!.Items.Should().Contain(t => t.Id == TransferAId && t.Number == "TRF-20261028-00001");

        var staffDetailRes = await _client.GetAsync($"/api/stock-transfers/{TransferAId}");
        staffDetailRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var staffDetailData = await staffDetailRes.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);
        staffDetailData.Should().NotBeNull();
        staffDetailData!.Id.Should().Be(TransferAId);
        staffDetailData.Number.Should().Be("TRF-20261028-00001");
        staffDetailData.Status.Should().Be("Draft");
        staffDetailData.Items.Should().NotBeEmpty();

        // 4. Owner -> 200 OK for list and detail
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenA);
        var ownerListRes = await _client.GetAsync("/api/stock-transfers");
        ownerListRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var ownerDetailRes = await _client.GetAsync($"/api/stock-transfers/{TransferAId}");
        ownerDetailRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Multi-tenant isolation: Tenant B (Owner và Staff) hoàn toàn không xem được chứng từ của Tenant A
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenB);
        var crossOwnerRes = await _client.GetAsync($"/api/stock-transfers/{TransferAId}");
        crossOwnerRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var crossOwnerBody = await crossOwnerRes.Content.ReadAsStringAsync();
        crossOwnerBody.Should().Contain("NOT_FOUND");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffTokenB);
        var crossStaffRes = await _client.GetAsync($"/api/stock-transfers/{TransferAId}");
        crossStaffRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Danh sách chứng từ của Tenant B tuyệt đối không chứa chứng từ của Tenant A
        var bListRes = await _client.GetAsync("/api/stock-transfers");
        bListRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var bListData = await bListRes.Content.ReadFromJsonAsync<PagedResultDto<StockTransferResponseDto>>(_jsonOptions);
        bListData.Should().NotBeNull();
        bListData!.Items.Should().NotContain(t => t.Id == TransferAId);
        bListData.Items.Should().Contain(t => t.Id == TransferBId);

        // 6. Owner -> 404 Not Found ProblemDetails cho ID không tồn tại
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenA);
        var missingId = Guid.NewGuid();
        var ownerMissingRes = await _client.GetAsync($"/api/stock-transfers/{missingId}");
        ownerMissingRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var notFoundBody = await ownerMissingRes.Content.ReadAsStringAsync();
        notFoundBody.Should().Contain("NOT_FOUND");
    }

    [Fact]
    public async Task StockCounts_Endpoints_RoleAndAuthGuards_AndTenantIsolation()
    {
        var password = PostgresTestEnvironment.AppPassword;
        var ownerTokenA = await LoginAsAsync("owner@shopa.local", password);
        var staffTokenA = await LoginAsAsync("staff@shopa.local", password);
        var cashierTokenA = await LoginAsAsync("cashier@shopa.local", password);
        var ownerTokenB = await LoginAsAsync("owner@shopb.local", password);
        var staffTokenB = await LoginAsAsync("staff@shopb.local", password);

        // 1. Unauthenticated -> 401 Unauthorized
        _client.DefaultRequestHeaders.Authorization = null;
        var unauthListRes = await _client.GetAsync("/api/stock-counts");
        unauthListRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var unauthDetailRes = await _client.GetAsync($"/api/stock-counts/{CountAId}");
        unauthDetailRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 2. Cashier -> 403 Forbidden
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierTokenA);
        var cashierListRes = await _client.GetAsync("/api/stock-counts");
        cashierListRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var cashierDetailRes = await _client.GetAsync($"/api/stock-counts/{CountAId}");
        cashierDetailRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 3. Staff -> 200 OK for list and detail (chứng từ thật của Tenant A)
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffTokenA);
        var staffListRes = await _client.GetAsync("/api/stock-counts");
        staffListRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var staffListData = await staffListRes.Content.ReadFromJsonAsync<PagedResultDto<StockCountResponseDto>>(_jsonOptions);
        staffListData.Should().NotBeNull();
        staffListData!.Items.Should().Contain(c => c.Id == CountAId && c.Number == "CNT-20261028-00001");

        var staffDetailRes = await _client.GetAsync($"/api/stock-counts/{CountAId}");
        staffDetailRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var staffDetailData = await staffDetailRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
        staffDetailData.Should().NotBeNull();
        staffDetailData!.Id.Should().Be(CountAId);
        staffDetailData.Number.Should().Be("CNT-20261028-00001");
        staffDetailData.Status.Should().Be("Draft");
        staffDetailData.Items.Should().NotBeEmpty();

        // 4. Owner -> 200 OK for list and detail
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenA);
        var ownerListRes = await _client.GetAsync("/api/stock-counts");
        ownerListRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var ownerDetailRes = await _client.GetAsync($"/api/stock-counts/{CountAId}");
        ownerDetailRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Multi-tenant isolation: Tenant B (Owner và Staff) hoàn toàn không xem được chứng từ của Tenant A
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenB);
        var crossOwnerRes = await _client.GetAsync($"/api/stock-counts/{CountAId}");
        crossOwnerRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var crossOwnerBody = await crossOwnerRes.Content.ReadAsStringAsync();
        crossOwnerBody.Should().Contain("NOT_FOUND");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffTokenB);
        var crossStaffRes = await _client.GetAsync($"/api/stock-counts/{CountAId}");
        crossStaffRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Danh sách chứng từ của Tenant B tuyệt đối không chứa chứng từ của Tenant A
        var bListRes = await _client.GetAsync("/api/stock-counts");
        bListRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var bListData = await bListRes.Content.ReadFromJsonAsync<PagedResultDto<StockCountResponseDto>>(_jsonOptions);
        bListData.Should().NotBeNull();
        bListData!.Items.Should().NotContain(c => c.Id == CountAId);
        bListData.Items.Should().Contain(c => c.Id == CountBId);

        // 6. Owner -> 404 Not Found ProblemDetails cho ID không tồn tại
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerTokenA);
        var missingId = Guid.NewGuid();
        var ownerMissingRes = await _client.GetAsync($"/api/stock-counts/{missingId}");
        ownerMissingRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var notFoundBody = await ownerMissingRes.Content.ReadAsStringAsync();
        notFoundBody.Should().Contain("NOT_FOUND");
    }

    [Fact]
    public async Task NewBrowserSession_CanReadBackExistingDocuments_FromPostgreSql()
    {
        var password = PostgresTestEnvironment.AppPassword;

        // Mô phỏng phiên đăng nhập mới của Chủ shop Tenant A
        var newSessionToken = await LoginAsAsync("owner@shopa.local", password);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newSessionToken);

        // Đọc lại phiếu chuyển kho từ máy chủ
        var transferRes = await _client.GetAsync($"/api/stock-transfers/{TransferAId}");
        transferRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var transferData = await transferRes.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);
        transferData.Should().NotBeNull();
        transferData!.Id.Should().Be(TransferAId);
        transferData.Number.Should().Be("TRF-20261028-00001");
        transferData.Items.Should().HaveCount(1);
        transferData.Items[0].Quantity.Should().Be(10);

        // Đọc lại đợt kiểm kê từ máy chủ
        var countRes = await _client.GetAsync($"/api/stock-counts/{CountAId}");
        countRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var countData = await countRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
        countData.Should().NotBeNull();
        countData!.Id.Should().Be(CountAId);
        countData.Number.Should().Be("CNT-20261028-00001");
        countData.Items.Should().HaveCount(1);
        countData.Items[0].ExpectedQuantity.Should().Be(20);
    }
}
