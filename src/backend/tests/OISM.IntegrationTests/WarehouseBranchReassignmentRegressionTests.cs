using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.RegisterTenant;
using OISM.Application.Features.Branches;
using OISM.Application.Features.Catalog.Products;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Suppliers;
using OISM.Application.Features.Warehouses;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Bộ kiểm thử hồi quy chuyên biệt cho tính năng Đổi chi nhánh kho hàng (S3-B01-FIX).
/// Kiểm chứng trên PostgreSQL thật với đầy đủ AC-01 đến AC-06:
/// - AC-01: Kho sạch đổi sang branch hợp lệ cùng tenant -> 200, bảo toàn WarehouseId, xác nhận DB mới.
/// - AC-02: Kho có lịch sử/chứng từ/số dư liên quan (kể cả tồn 0) -> 409 Conflict, dữ liệu không đổi.
/// - AC-03: Không đổi branch, chỉ sửa tên/trạng thái -> 200 OK.
/// - AC-04: Branch không tồn tại hoặc khác tenant -> 400 Bad Request, không cập nhật chéo tenant.
/// - AC-05: Request tới kho thuộc tenant khác không đọc hoặc sửa được dữ liệu (404 Not Found).
/// - AC-06: Race condition hoặc xung đột với chứng từ phụ thuộc -> đảm bảo tính nguyên tử và toàn vẹn dữ liệu FK.
/// </summary>
public class WarehouseBranchReassignmentRegressionTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class TestTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    public WarehouseBranchReassignmentRegressionTests(WebApplicationFactoryWithPostgres factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(Guid TenantId, string AccessToken, string Email)> RegisterAndLoginAsync(string prefix)
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var runCode = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var email = $"{prefix.ToLowerInvariant()}_{runCode}@oismtest.com";
        var password = "TestPassword_2026!";

        var regRes = await _client.PostAsJsonAsync("/api/auth/register-tenant", new RegisterTenantRequest
        {
            TenantName = $"Tenant {prefix} {runCode}",
            TenantCode = $"T_{runCode}",
            FullName = $"Owner {prefix}",
            Email = email,
            Password = password,
            PhoneNumber = $"09{Random.Shared.Next(10000000, 99999999)}"
        });

        regRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var regData = await regRes.Content.ReadFromJsonAsync<RegisterTenantResponse>(_jsonOptions);

        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = password
        });

        loginRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var authData = await loginRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);

        return (regData!.TenantId, authData!.AccessToken, email);
    }

    [Fact]
    public async Task AC01_CleanWarehouse_ReassignBranch_Returns200_PreservesWarehouseId_UpdatesDbDirectly()
    {
        // Arrange
        var (tenantId, token, _) = await RegisterAndLoginAsync("AC01");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var b1Res = await _client.PostAsJsonAsync("/api/branches", new CreateBranchRequest { Name = "Chi nhánh Quận 1" });
        var b2Res = await _client.PostAsJsonAsync("/api/branches", new CreateBranchRequest { Name = "Chi nhánh Quận 2" });
        var branch1 = await b1Res.Content.ReadFromJsonAsync<BranchResponse>(_jsonOptions);
        var branch2 = await b2Res.Content.ReadFromJsonAsync<BranchResponse>(_jsonOptions);

        var createWhRes = await _client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest
        {
            BranchId = branch1!.Id,
            Name = "Kho Sạch Ban Đầu"
        });
        createWhRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var initialWh = await createWhRes.Content.ReadFromJsonAsync<WarehouseResponse>(_jsonOptions);
        var originalWhId = initialWh!.Id;

        // Act: Đổi chi nhánh kho sạch sang Branch 2
        var updateRes = await _client.PutAsJsonAsync($"/api/warehouses/{originalWhId}", new UpdateWarehouseRequest
        {
            BranchId = branch2!.Id,
            Name = "Kho Sạch Đã Chuyển Sang Q2",
            IsActive = true
        });

        // Assert: HTTP 200 và giữ nguyên WarehouseId
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedWh = await updateRes.Content.ReadFromJsonAsync<WarehouseResponse>(_jsonOptions);
        updatedWh!.Id.Should().Be(originalWhId, "WarehouseId phải được bảo toàn nguyên vẹn");
        updatedWh.BranchId.Should().Be(branch2.Id);
        updatedWh.BranchName.Should().Be("Chi nhánh Quận 2");
        updatedWh.Name.Should().Be("Kho Sạch Đã Chuyển Sang Q2");

        // GET xác minh API trả về đúng dữ liệu mới
        var getRes = await _client.GetFromJsonAsync<WarehouseResponse>($"/api/warehouses/{originalWhId}", _jsonOptions);
        getRes!.BranchId.Should().Be(branch2.Id);
        getRes.Name.Should().Be("Kho Sạch Đã Chuyển Sang Q2");

        // Truy vấn DB bằng context độc lập xác nhận trực tiếp trong PostgreSQL
        var tenantService = new TestTenantService { TenantId = tenantId };
        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService);
        var dbRow = await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == originalWhId);
        dbRow.Should().NotBeNull();
        dbRow!.BranchId.Should().Be(branch2.Id);
        dbRow.Name.Should().Be("Kho Sạch Đã Chuyển Sang Q2");
        dbRow.IsActive.Should().BeTrue();
        dbRow.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task AC02_WarehouseWithHistory_Returns409_AndDataUnchanged_IncludingZeroStock()
    {
        // Arrange
        var (tenantId, token, _) = await RegisterAndLoginAsync("AC02");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var b1Res = await _client.PostAsJsonAsync("/api/branches", new CreateBranchRequest { Name = "Chi nhánh Quận 5" });
        var b2Res = await _client.PostAsJsonAsync("/api/branches", new CreateBranchRequest { Name = "Chi nhánh Quận 7" });
        var branch1 = await b1Res.Content.ReadFromJsonAsync<BranchResponse>(_jsonOptions);
        var branch2 = await b2Res.Content.ReadFromJsonAsync<BranchResponse>(_jsonOptions);

        var createWhRes = await _client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest
        {
            BranchId = branch1!.Id,
            Name = "Kho Có Lịch Sử"
        });
        var wh = await createWhRes.Content.ReadFromJsonAsync<WarehouseResponse>(_jsonOptions);

        var supRes = await _client.PostAsJsonAsync("/api/suppliers", new CreateSupplierRequest { Name = "NCC AC02" });
        var sup = await supRes.Content.ReadFromJsonAsync<SupplierResponse>(_jsonOptions);

        var prodRes = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Sản phẩm AC02",
            Variants = new List<CreateVariantRequest> { new() { SKU = "SKU-AC02", VariantName = "V-AC02", RetailPrice = 100000 } }
        });
        var prodObj = await prodRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var prodDetail = await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{prodObj.GetProperty("productId").GetGuid()}", _jsonOptions);
        var skuId = prodDetail!.Variants.First().Id;

        // Tạo chứng từ Draft (tồn kho hiện tại bằng 0 nhưng đã có chứng từ)
        var draftGrRes = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            BranchId = branch1.Id,
            WarehouseId = wh!.Id,
            SupplierId = sup!.Id,
            Note = "Phiếu nhập nháp AC02",
            Items = new List<CreateGoodsReceiptItemDto>
            {
                new() { SkuId = skuId, Quantity = 5, UnitPrice = 80000 }
            }
        });
        draftGrRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act: Cố đổi branch của kho có chứng từ draft (tồn 0)
        var changeBranchRes = await _client.PutAsJsonAsync($"/api/warehouses/{wh.Id}", new UpdateWarehouseRequest
        {
            BranchId = branch2!.Id,
            Name = "Kho Cố Đổi Sang Q7",
            IsActive = true
        });

        // Assert: Bị từ chối 409 Conflict
        changeBranchRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Xác minh DB không bị thay đổi
        var tenantService = new TestTenantService { TenantId = tenantId };
        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService);
        var dbRow = await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == wh.Id);
        dbRow.Should().NotBeNull();
        dbRow!.BranchId.Should().Be(branch1.Id, "BranchId phải giữ nguyên sau khi bị từ chối 409");
        dbRow.Name.Should().Be("Kho Có Lịch Sử");
    }

    [Fact]
    public async Task AC03_SameBranch_UpdateNameAndStatus_Returns200()
    {
        // Arrange
        var (tenantId, token, _) = await RegisterAndLoginAsync("AC03");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var bRes = await _client.PostAsJsonAsync("/api/branches", new CreateBranchRequest { Name = "Chi nhánh Quận 10" });
        var branch = await bRes.Content.ReadFromJsonAsync<BranchResponse>(_jsonOptions);

        var createWhRes = await _client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest
        {
            BranchId = branch!.Id,
            Name = "Kho Quận 10 Gốc"
        });
        var wh = await createWhRes.Content.ReadFromJsonAsync<WarehouseResponse>(_jsonOptions);

        // Act: Chỉ sửa tên và trạng thái IsActive = false, BranchId không đổi
        var updateRes = await _client.PutAsJsonAsync($"/api/warehouses/{wh!.Id}", new UpdateWarehouseRequest
        {
            BranchId = branch.Id,
            Name = "Kho Quận 10 Đã Đổi Tên",
            IsActive = false
        });

        // Assert: 200 OK
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateRes.Content.ReadFromJsonAsync<WarehouseResponse>(_jsonOptions);
        updated!.BranchId.Should().Be(branch.Id);
        updated.Name.Should().Be("Kho Quận 10 Đã Đổi Tên");
        updated.IsActive.Should().BeFalse();

        // Kiểm tra qua DB
        var tenantService = new TestTenantService { TenantId = tenantId };
        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService);
        var dbRow = await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == wh.Id);
        dbRow!.BranchId.Should().Be(branch.Id);
        dbRow.Name.Should().Be("Kho Quận 10 Đã Đổi Tên");
        dbRow.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task AC04_TargetBranchDoesNotExist_OrDifferentTenant_Returns400_NoCrossTenantUpdate()
    {
        // Arrange
        var (tenantAId, tokenA, _) = await RegisterAndLoginAsync("AC04A");
        var (_, tokenB, _) = await RegisterAndLoginAsync("AC04B");

        // Tenant A: tạo branch 1 và warehouse
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var bARes = await _client.PostAsJsonAsync("/api/branches", new CreateBranchRequest { Name = "Branch Tenant A" });
        var branchA = await bARes.Content.ReadFromJsonAsync<BranchResponse>(_jsonOptions);

        var whARes = await _client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest
        {
            BranchId = branchA!.Id,
            Name = "Kho Tenant A"
        });
        var whA = await whARes.Content.ReadFromJsonAsync<WarehouseResponse>(_jsonOptions);

        // Tenant B: tạo branch
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        var bBRes = await _client.PostAsJsonAsync("/api/branches", new CreateBranchRequest { Name = "Branch Tenant B" });
        var branchB = await bBRes.Content.ReadFromJsonAsync<BranchResponse>(_jsonOptions);

        // Switch back to Tenant A
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);

        // Subcase 1: BranchId không tồn tại -> 400 Bad Request
        var nonExistentBranchRes = await _client.PutAsJsonAsync($"/api/warehouses/{whA!.Id}", new UpdateWarehouseRequest
        {
            BranchId = Guid.NewGuid(),
            Name = "Kho Thử Đổi Branch Không Tồn Tại",
            IsActive = true
        });
        nonExistentBranchRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Subcase 2: BranchId thuộc Tenant B -> 400 Bad Request (không thể gán chi nhánh chéo tenant)
        var crossTenantBranchRes = await _client.PutAsJsonAsync($"/api/warehouses/{whA.Id}", new UpdateWarehouseRequest
        {
            BranchId = branchB!.Id,
            Name = "Kho Thử Đổi Sang Branch Tenant B",
            IsActive = true
        });
        crossTenantBranchRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Xác minh kho của Tenant A vẫn giữ nguyên
        var tenantServiceA = new TestTenantService { TenantId = tenantAId };
        await using var dbA = PostgresTestEnvironment.CreateRuntimeDbContext(tenantServiceA);
        var dbRow = await dbA.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == whA.Id);
        dbRow!.BranchId.Should().Be(branchA.Id);
        dbRow.Name.Should().Be("Kho Tenant A");
    }

    [Fact]
    public async Task AC05_RequestToWarehouseOfDifferentTenant_Returns404_CannotReadOrModify()
    {
        // Arrange
        var (tenantAId, tokenA, _) = await RegisterAndLoginAsync("AC05A");
        var (_, tokenB, _) = await RegisterAndLoginAsync("AC05B");

        // Tenant A: tạo branch và warehouse
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var bARes = await _client.PostAsJsonAsync("/api/branches", new CreateBranchRequest { Name = "Branch Tenant A" });
        var branchA = await bARes.Content.ReadFromJsonAsync<BranchResponse>(_jsonOptions);

        var whARes = await _client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest
        {
            BranchId = branchA!.Id,
            Name = "Kho Riêng Tenant A"
        });
        var whA = await whARes.Content.ReadFromJsonAsync<WarehouseResponse>(_jsonOptions);

        // Đăng nhập Tenant B: tạo branch cho Tenant B
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        var bBRes = await _client.PostAsJsonAsync("/api/branches", new CreateBranchRequest { Name = "Branch Tenant B" });
        var branchB = await bBRes.Content.ReadFromJsonAsync<BranchResponse>(_jsonOptions);

        // Act & Assert 1: Tenant B cố đọc kho của Tenant A -> 404 Not Found
        var getOtherTenantWhRes = await _client.GetAsync($"/api/warehouses/{whA!.Id}");
        getOtherTenantWhRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Act & Assert 2: Tenant B cố sửa kho của Tenant A -> 404 Not Found
        var updateOtherTenantWhRes = await _client.PutAsJsonAsync($"/api/warehouses/{whA.Id}", new UpdateWarehouseRequest
        {
            BranchId = branchB!.Id,
            Name = "Hacker Tenant B Chiếm Kho",
            IsActive = true
        });
        updateOtherTenantWhRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Act & Assert 3: Tenant B cố bật/tắt kho của Tenant A -> 404 Not Found
        var toggleRes = await _client.PutAsync($"/api/warehouses/{whA.Id}/toggle-status", null);
        toggleRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Xác minh kho của Tenant A hoàn toàn nguyên vẹn
        var tenantServiceA = new TestTenantService { TenantId = tenantAId };
        await using var dbA = PostgresTestEnvironment.CreateRuntimeDbContext(tenantServiceA);
        var dbRow = await dbA.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == whA.Id);
        dbRow.Should().NotBeNull();
        dbRow!.Name.Should().Be("Kho Riêng Tenant A");
        dbRow.BranchId.Should().Be(branchA.Id);
        dbRow.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task AC06_ConcurrentCreationRace_WithDependentHistory_GuaranteesAtomicityAndIntegrity()
    {
        // Arrange
        var (tenantId, token, _) = await RegisterAndLoginAsync("AC06");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var b1Res = await _client.PostAsJsonAsync("/api/branches", new CreateBranchRequest { Name = "Branch Đua 1" });
        var b2Res = await _client.PostAsJsonAsync("/api/branches", new CreateBranchRequest { Name = "Branch Đua 2" });
        var branch1 = await b1Res.Content.ReadFromJsonAsync<BranchResponse>(_jsonOptions);
        var branch2 = await b2Res.Content.ReadFromJsonAsync<BranchResponse>(_jsonOptions);

        var whRes = await _client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest
        {
            BranchId = branch1!.Id,
            Name = "Kho Đua Concurrency"
        });
        var wh = await whRes.Content.ReadFromJsonAsync<WarehouseResponse>(_jsonOptions);

        var supRes = await _client.PostAsJsonAsync("/api/suppliers", new CreateSupplierRequest { Name = "NCC Đua" });
        var sup = await supRes.Content.ReadFromJsonAsync<SupplierResponse>(_jsonOptions);

        var prodRes = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Sản phẩm Đua",
            Variants = new List<CreateVariantRequest> { new() { SKU = "SKU-RACE", VariantName = "V-RACE", RetailPrice = 50000 } }
        });
        var prodObj = await prodRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var prodDetail = await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{prodObj.GetProperty("productId").GetGuid()}", _jsonOptions);
        var skuId = prodDetail!.Variants.First().Id;

        // Act: Đua đồng thời:
        // - Luồng 1: Đổi chi nhánh kho từ Branch 1 sang Branch 2
        // - Luồng 2: Tạo phiếu nhập kho và xác nhận gắn với kho tại Branch 1
        var moveTask = _client.PutAsJsonAsync($"/api/warehouses/{wh!.Id}", new UpdateWarehouseRequest
        {
            BranchId = branch2!.Id,
            Name = "Kho Đã Đổi Sang Branch 2",
            IsActive = true
        });

        var receiptTask = Task.Run(async () =>
        {
            var grRes = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
            {
                BranchId = branch1.Id,
                WarehouseId = wh.Id,
                SupplierId = sup!.Id,
                Note = "Phiếu nhập đồng thời",
                Items = new List<CreateGoodsReceiptItemDto>
                {
                    new() { SkuId = skuId, Quantity = 10, UnitPrice = 40000 }
                }
            });
            if (grRes.StatusCode == HttpStatusCode.Created)
            {
                var gr = await grRes.Content.ReadFromJsonAsync<GoodsReceiptResponseDto>(_jsonOptions);
                return await _client.PostAsync($"/api/goods-receipts/{gr!.Id}/confirm", null);
            }
            return grRes;
        });

        var results = await Task.WhenAll(moveTask, receiptTask);

        // Assert: Toàn vẹn dữ liệu
        // Nếu move thành công (200) thì receipt tại branch 1 hoặc không tạo được hoặc move xảy ra trước khi có receipt
        // Nếu receipt tạo thành công trước thì move phải trả 409 Conflict
        // Trong mọi tình huống, quan hệ (tenant_id, warehouse_id, branch_id) của mọi goods_receipt phải khớp chính xác với warehouses
        var tenantService = new TestTenantService { TenantId = tenantId };
        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService);
        var dbWh = await db.Warehouses.AsNoTracking().FirstAsync(w => w.Id == wh.Id);
        var dbReceipts = await db.GoodsReceipts.AsNoTracking().Where(g => g.WarehouseId == wh.Id).ToListAsync();

        foreach (var gr in dbReceipts)
        {
            gr.BranchId.Should().Be(dbWh.BranchId, "Không bao giờ được tồn tại chứng từ trỏ sai cặp (warehouse, branch) vi phạm foreign key");
        }
    }
}
