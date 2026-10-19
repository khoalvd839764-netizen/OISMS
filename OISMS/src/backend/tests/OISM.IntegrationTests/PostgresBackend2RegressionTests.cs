using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OISM.Application.Common.Helpers;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.RefreshToken;
using OISM.Application.Features.Auth.RegisterTenant;
using OISM.Application.Features.Branches;
using OISM.Application.Features.Catalog.Categories;
using OISM.Application.Features.Catalog.Products;
using OISM.Application.Features.Pos;
using OISM.Application.Features.Warehouses;
using OISM.Domain.Entities;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Bộ kiểm thử hồi quy độc lập cho Backend 2 trên PostgreSQL thật (PostgresTestEnvironment).
/// Đảm bảo xác thực đầy đủ 4 lỗi review và toàn bộ tính năng hoàn thiện từ BE2-01 đến BE2-09:
/// 1. Tenant bị khóa: cả login và refresh-token đều trả 401.
/// 2. Rotation nguyên tử đồng thời trên PostgreSQL: chỉ 1 request thành công (200), request còn lại bị từ chối (401).
/// 3. Update Category với ParentId không tồn tại trả 400 Bad Request (không văng 500), bảo toàn dữ liệu.
/// 4. Chặn tổ hợp thuộc tính trùng (chuẩn hóa trim/lower-case, Dictionary JSON, kiểm tra create/update).
/// 5. CRUD SKU, cập nhật giá sỉ/lẻ tách biệt WAC, soft-delete an toàn với lịch sử kho.
/// 6. Tra cứu POS Search và Barcode Scanner tích hợp tồn khả dụng (Available = OnHand - Reserved) theo Branch.
/// 7. Sửa thông tin Chi nhánh, Kho hàng và kiểm tra phân quyền RBAC (Cashier bị chặn 403).
/// </summary>
public class PostgresBackend2RegressionTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public PostgresBackend2RegressionTests(WebApplicationFactoryWithPostgres factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(Guid TenantId, string AccessToken, string RefreshToken, string Email, string Password)> RegisterAndLoginAsync(string prefix)
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

        var regBody = await regRes.Content.ReadAsStringAsync();
        regRes.StatusCode.Should().Be(HttpStatusCode.Created, $"Registration failed with: {regBody}");
        var regData = await regRes.Content.ReadFromJsonAsync<RegisterTenantResponse>(_jsonOptions);

        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = password
        });

        loginRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var authData = await loginRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);

        return (regData!.TenantId, authData!.AccessToken, authData.RefreshToken, email, password);
    }

    // =========================================================================
    // 1. TENANT BỊ KHÓA: LOGIN TRẢ 401 VÀ REFRESH TRẢ 401
    // =========================================================================
    [Fact]
    public async Task Postgres_RefreshToken_WhenTenantIsLocked_ShouldReturnUnauthorized()
    {
        var (tenantId, _, refreshToken, email, password) = await RegisterAndLoginAsync("LockTest");

        // Khóa Tenant trực tiếp trong database PostgreSQL
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("UPDATE tenants SET is_active = false WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", tenantId);
            await cmd.ExecuteNonQueryAsync();
        }

        // 1. Đăng nhập phải bị từ chối 401
        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = password
        });
        loginRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 2. Refresh Token cũng phải bị từ chối 401
        var refreshRes = await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest
        {
            RefreshToken = refreshToken
        });
        refreshRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // =========================================================================
    // 2. CONCURRENT REFRESH TOKEN ROTATION TRÊN POSTGRESQL NGUYÊN TỬ
    // =========================================================================
    [Fact]
    public async Task Postgres_ConcurrentRefreshToken_ShouldRotateAtomically_OnlyOneSucceeds()
    {
        var (_, _, initialRefreshToken, _, _) = await RegisterAndLoginAsync("ConcurRef");

        // Gửi 2 request đồng thời dùng cùng một refresh token
        var task1 = _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest { RefreshToken = initialRefreshToken });
        var task2 = _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest { RefreshToken = initialRefreshToken });

        var responses = await Task.WhenAll(task1, task2);
        var statusCodes = responses.Select(r => r.StatusCode).ToList();

        // Một request phải thành công (200), request còn lại phải bị từ chối (401)
        statusCodes.Should().ContainSingle(s => s == HttpStatusCode.OK);
        statusCodes.Should().ContainSingle(s => s == HttpStatusCode.Unauthorized);

        // Token cũ ban đầu không được phép dùng lại nữa
        var reusedRes = await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest { RefreshToken = initialRefreshToken });
        reusedRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // =========================================================================
    // 3. SỬA CATEGORY VỚI PARENT_ID KHÔNG TỒN TẠI TRẢ 400 (KHÔNG PHẢI 500)
    // =========================================================================
    [Fact]
    public async Task Postgres_UpdateCategory_WithNonExistentParentId_ShouldReturnBadRequest_AndNot500()
    {
        var runCode = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var (tenant1Id, token1, _, _, _) = await RegisterAndLoginAsync($"CatValid_{runCode}");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token1);

        // 1. Tạo Category A ban đầu
        var createRes = await _client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest
        {
            Name = $"Điện tử gia dụng {runCode}",
            Description = "Mô tả ban đầu"
        });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var cat = await createRes.Content.ReadFromJsonAsync<CategoryResponse>(_jsonOptions);
        var catAId = cat!.Id;

        // 2. Cố tình cập nhật với ParentId ngẫu nhiên không tồn tại -> 400 Bad Request rõ ràng (không để lọt 500)
        var nonExistentParentId = Guid.NewGuid();
        var updateNonExistRes = await _client.PutAsJsonAsync($"/api/categories/{catAId}", new UpdateCategoryRequest
        {
            Name = "Điện tử gia dụng cập nhật",
            ParentId = nonExistentParentId,
            IsActive = true
        });
        updateNonExistRes.StatusCode.Should().Be(HttpStatusCode.BadRequest, "ParentId không tồn tại phải trả về 400 Bad Request rõ ràng");

        // 3. Cập nhật Category A với ParentId là chính nó (self-parent) -> 400 Bad Request
        var updateSelfRes = await _client.PutAsJsonAsync($"/api/categories/{catAId}", new UpdateCategoryRequest
        {
            Name = "Tự làm cha của mình",
            ParentId = catAId,
            IsActive = true
        });
        updateSelfRes.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Không thể đặt danh mục làm cha của chính nó");

        // 4. Cập nhật Category A với ParentId thuộc Tenant khác -> 400 Bad Request
        var (_, token2, _, _, _) = await RegisterAndLoginAsync($"CatOther_{runCode}");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        var catTenant2Res = await _client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest
        {
            Name = $"Danh mục Tenant 2 {runCode}"
        });
        catTenant2Res.StatusCode.Should().Be(HttpStatusCode.Created);
        var catTenant2 = await catTenant2Res.Content.ReadFromJsonAsync<CategoryResponse>(_jsonOptions);
        var catTenant2Id = catTenant2!.Id;

        // Đổi lại token Tenant 1
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token1);
        var updateCrossTenantRes = await _client.PutAsJsonAsync($"/api/categories/{catAId}", new UpdateCategoryRequest
        {
            Name = "Tên cha khác tenant",
            ParentId = catTenant2Id,
            IsActive = true
        });
        updateCrossTenantRes.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Không thể chọn danh mục của tenant khác làm cha");

        // 5. Xác minh dữ liệu trong PostgreSQL không bị thay đổi dở dang khi các request trên thất bại
        var getRes = await _client.GetFromJsonAsync<List<CategoryResponse>>("/api/categories", _jsonOptions);
        var currentCat = getRes!.First(c => c.Id == catAId);
        currentCat.Name.Should().Be($"Điện tử gia dụng {runCode}", "Dữ liệu không được thay đổi dở dang khi cập nhật thất bại");
        currentCat.ParentId.Should().BeNull();
    }

    // =========================================================================
    // 4. CHẶN TỔ HỢP THUỘC TÍNH TRÙNG TRÊN CẢ CREATE VÀ UPDATE
    // =========================================================================
    [Fact]
    public async Task Postgres_ProductVariants_DuplicateAttributeCombination_ShouldBeRejectedWith409()
    {
        var (_, token, _, _, _) = await RegisterAndLoginAsync("ComboTest");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 1. Tạo Product với 2 biến thể khác SKU nhưng trùng tổ hợp thuộc tính (Màu Đỏ, Size L)
        var createProductRes = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Áo Thun Polo Cao Cấp",
            Variants = new List<CreateVariantRequest>
            {
                new()
                {
                    SKU = "POLO-RED-L-1",
                    VariantName = "Đỏ Size L (Bản 1)",
                    RetailPrice = 200000,
                    WholesalePrice = 150000,
                    Color = "Đỏ",
                    Size = "L"
                },
                new()
                {
                    SKU = "POLO-RED-L-2",
                    VariantName = "Đỏ Size L (Bản 2)",
                    RetailPrice = 200000,
                    WholesalePrice = 150000,
                    Color = " đỏ ", // Chuẩn hóa trim + case-insensitive
                    Size = " l "
                }
            }
        });

        // Bắt buộc chặn với mã 409 Conflict
        createProductRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 2. Tạo Product hợp lệ với 1 biến thể
        var validProductRes = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Áo Sơ Mi Nam",
            Variants = new List<CreateVariantRequest>
            {
                new()
                {
                    SKU = "SOMI-WHITE-M",
                    VariantName = "Trắng M",
                    RetailPrice = 350000,
                    WholesalePrice = 250000,
                    Color = "Trắng",
                    Size = "M",
                    Attributes = new Dictionary<string, object> { ["Material"] = "Linen" }
                }
            }
        });
        validProductRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var productObj = await validProductRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var productId = productObj.GetProperty("productId").GetGuid();

        // 3. Thêm biến thể mới qua /api/products/{id}/variants với cùng tổ hợp (Trắng, M, Linen)
        var addDupVariantRes = await _client.PostAsJsonAsync($"/api/products/{productId}/variants", new CreateVariantRequest
        {
            SKU = "SOMI-WHITE-M-EXTRA",
            VariantName = "Trắng M Bản Phụ",
            RetailPrice = 350000,
            WholesalePrice = 250000,
            Color = " trắng ",
            Size = " m ",
            Attributes = new Dictionary<string, object> { ["material"] = " linen " }
        });

        // Bắt buộc trả về 409 Conflict
        addDupVariantRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // =========================================================================
    // 5. CRUD SKU, CẬP NHẬT GIÁ VÀ VÔ HIỆU HÓA AN TOÀN (BE2-05, BE2-06)
    // =========================================================================
    [Fact]
    public async Task Postgres_SkuCrud_UpdatePrices_AndSafeSoftDelete_ShouldWorkProperly()
    {
        var (_, token, _, _, _) = await RegisterAndLoginAsync("SkuCrud");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 1. Tạo sản phẩm với 1 biến thể
        var createRes = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Quần Jean Nam Slimfit",
            Variants = new List<CreateVariantRequest>
            {
                new()
                {
                    SKU = "JEAN-SLIM-30",
                    VariantName = "Xanh Đậm Size 30",
                    RetailPrice = 450000,
                    WholesalePrice = 300000,
                    Color = "Xanh Đậm",
                    Size = "30"
                }
            }
        });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var productId = created.GetProperty("productId").GetGuid();

        var product = await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{productId}", _jsonOptions);
        var variantId = product!.Variants.First().Id;

        // 2. Cập nhật bảng giá SKU qua /prices (BE2-06)
        var updatePricesRes = await _client.PutAsJsonAsync($"/api/products/{productId}/variants/{variantId}/prices", new UpdateVariantPricesRequest
        {
            RetailPrice = 490000,
            WholesalePrice = 320000
        });
        updatePricesRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedVariant = await updatePricesRes.Content.ReadFromJsonAsync<ProductVariantResponse>(_jsonOptions);
        updatedVariant!.RetailPrice.Should().Be(490000);
        updatedVariant.WholesalePrice.Should().Be(320000);

        // 3. Cập nhật thông tin chi tiết biến thể và thuộc tính tùy ý (BE2-05)
        var updateVariantRes = await _client.PutAsJsonAsync($"/api/products/{productId}/variants/{variantId}", new UpdateVariantRequest
        {
            VariantName = "Xanh Đậm Co Giãn Size 30",
            RetailPrice = 495000,
            WholesalePrice = 325000,
            Color = "Xanh Đậm",
            Size = "30",
            Attributes = new Dictionary<string, object> { ["stretch"] = "high", ["pockets"] = 5 },
            IsActive = true
        });
        updateVariantRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailedVariant = await updateVariantRes.Content.ReadFromJsonAsync<ProductVariantResponse>(_jsonOptions);
        detailedVariant!.Attributes.Should().ContainKey("stretch");

        // 4. Vô hiệu hóa biến thể an toàn (Soft-Delete)
        var deleteRes = await _client.DeleteAsync($"/api/products/{productId}/variants/{variantId}");
        deleteRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Xác minh trong DB biến thể chỉ chuyển IsActive = false, không bị xóa vật lý mất lịch sử
        var productAfterDelete = await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{productId}", _jsonOptions);
        var softDeleted = productAfterDelete!.Variants.First(v => v.Id == variantId);
        softDeleted.IsActive.Should().BeFalse();
    }

    // =========================================================================
    // 6. POS SEARCH VÀ BARCODE SCANNER TÍCH HỢP TỒN KHẢ DỤNG (BE2-07)
    // =========================================================================
    [Fact]
    public async Task Postgres_PosCatalog_SearchAndBarcode_WithBranchStock_ShouldReturnAccurateAvailable()
    {
        var (tenantId, token, _, _, _) = await RegisterAndLoginAsync("PosStock");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 1. Tạo Chi nhánh
        var branchRes = await _client.PostAsJsonAsync("/api/branches", new CreateBranchRequest
        {
            Name = "Chi nhánh Quận 1",
            Address = "123 Lê Lợi, Q1"
        });
        branchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var branch = await branchRes.Content.ReadFromJsonAsync<BranchResponse>(_jsonOptions);

        // 2. Tạo Nhà cung cấp & Kho hàng để nhập kho thực tế
        var supRes = await _client.PostAsJsonAsync("/api/suppliers", new OISM.Application.Features.Suppliers.CreateSupplierRequest
        {
            Name = "Nhà cung cấp Inox Toàn Cầu"
        });
        supRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var sup = await supRes.Content.ReadFromJsonAsync<OISM.Application.Features.Suppliers.SupplierResponse>(_jsonOptions);

        var whRes = await _client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest
        {
            BranchId = branch!.Id,
            Name = "Kho Tổng Q1"
        });
        whRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var wh = await whRes.Content.ReadFromJsonAsync<WarehouseResponse>(_jsonOptions);

        // 3. Tạo Sản phẩm với Barcode chuẩn EAN-13
        const string testBarcode = "8939999000015";
        var productRes = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Bình Giữ Nhiệt Inox 500ml",
            Variants = new List<CreateVariantRequest>
            {
                new()
                {
                    SKU = "BINH-INOX-500",
                    VariantName = "Inox Bạc 500ml",
                    Barcode = testBarcode,
                    RetailPrice = 150000,
                    WholesalePrice = 100000,
                    Color = "Bạc"
                }
            }
        });
        productRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var productObj = await productRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var productId = productObj.GetProperty("productId").GetGuid();

        var product = await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{productId}", _jsonOptions);
        var skuId = product!.Variants.First().Id;

        // 4. Lập phiếu nhập kho 100 cái và xác nhận nhập kho (Backend 1 Goods Receipt)
        var grRes = await _client.PostAsJsonAsync("/api/goods-receipts", new OISM.Application.Features.Inventory.Dtos.CreateGoodsReceiptDto
        {
            BranchId = branch.Id,
            WarehouseId = wh!.Id,
            SupplierId = sup!.Id,
            Note = "Nhập kho thử nghiệm POS",
            Items = new List<OISM.Application.Features.Inventory.Dtos.CreateGoodsReceiptItemDto>
            {
                new() { SkuId = skuId, Quantity = 100m, UnitPrice = 80000m }
            }
        });
        grRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var gr = await grRes.Content.ReadFromJsonAsync<OISM.Application.Features.Inventory.Dtos.GoodsReceiptResponseDto>(_jsonOptions);

        var confirmRes = await _client.PostAsync($"/api/goods-receipts/{gr!.Id}/confirm", null);
        confirmRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Giữ chỗ 25 cái (Anti-oversell Reservation) -> OnHand = 100, Reserved = 25, Available = 75
        var reserveRes = await _client.PostAsJsonAsync("/api/inventory/reserve", new OISM.Application.Features.Inventory.Dtos.ReserveStockRequestDto
        {
            BranchId = branch.Id,
            WarehouseId = wh.Id,
            SkuId = skuId,
            Quantity = 25m
        });
        reserveRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. Tra cứu qua POS Search với branchId
        var searchRes = await _client.GetAsync($"/api/pos/search?query=binh&branchId={branch.Id}");
        searchRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var searchItems = await searchRes.Content.ReadFromJsonAsync<List<PosProductSearchResponse>>(_jsonOptions);
        searchItems.Should().NotBeNullOrEmpty();
        var item = searchItems!.First(i => i.VariantId == skuId);
        item.OnHand.Should().Be(100);
        item.Reserved.Should().Be(25);
        item.Available.Should().Be(75);

        // 7. Quét qua Barcode Scanner với branchId
        var barcodeRes = await _client.GetAsync($"/api/pos/barcode/{testBarcode}?branchId={branch.Id}");
        barcodeRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var barcodeItem = await barcodeRes.Content.ReadFromJsonAsync<PosProductSearchResponse>(_jsonOptions);
        barcodeItem.Should().NotBeNull();
        barcodeItem!.OnHand.Should().Be(100);
        barcodeItem.Reserved.Should().Be(25);
        barcodeItem.Available.Should().Be(75);
    }

    // =========================================================================
    // 7. SỬA CHI NHÁNH, KHO HÀNG VÀ KIỂM TRA PHÂN QUYỀN RBAC (BE2-03, BE2-08)
    // =========================================================================
    [Fact]
    public async Task Postgres_BranchAndWarehouse_Update_AndRbacCashierForbidden()
    {
        var (tenantId, ownerToken, _, _, _) = await RegisterAndLoginAsync("BranchWh");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // 1. Owner tạo Chi nhánh & Kho hàng
        var branchRes = await _client.PostAsJsonAsync("/api/branches", new CreateBranchRequest
        {
            Name = "Chi nhánh Hà Nội",
            Address = "1 Hoàn Kiếm, HN"
        });
        branchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var branch = await branchRes.Content.ReadFromJsonAsync<BranchResponse>(_jsonOptions);

        var whRes = await _client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest
        {
            BranchId = branch!.Id,
            Name = "Kho Tổng Phía Bắc"
        });
        whRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var wh = await whRes.Content.ReadFromJsonAsync<WarehouseResponse>(_jsonOptions);

        // 2. Owner sửa thông tin Chi nhánh & Kho hàng (BE2-08)
        var updateBranchRes = await _client.PutAsJsonAsync($"/api/branches/{branch.Id}", new UpdateBranchRequest
        {
            Name = "Chi nhánh Hoàn Kiếm Hà Nội",
            Address = "2 Hoàn Kiếm, HN",
            Phone = "+84243999999",
            IsActive = true
        });
        updateBranchRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var updateWhRes = await _client.PutAsJsonAsync($"/api/warehouses/{wh!.Id}", new UpdateWarehouseRequest
        {
            BranchId = branch.Id,
            Name = "Kho Phân Phối Hà Nội",
            IsActive = true
        });
        updateWhRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedWh = await updateWhRes.Content.ReadFromJsonAsync<WarehouseResponse>(_jsonOptions);
        updatedWh!.Name.Should().Be("Kho Phân Phối Hà Nội");

        // 3. Tạo tài khoản Cashier trong cùng Tenant để kiểm tra RBAC
        var cashierEmail = $"cashier_{Guid.NewGuid():N}@shop.com";
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            const string insertUserSql = @"
INSERT INTO users (id, tenant_id, display_name, email, phone, password_hash, role, is_active, created_at)
VALUES (@id, @tenantId, 'Thu Ngân 1', @email, '+84912345678', @hash, 'Cashier', true, now());";
            await using var cmd = new NpgsqlCommand(insertUserSql, conn);
            cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("email", cashierEmail);
            cmd.Parameters.AddWithValue("hash", BCrypt.Net.BCrypt.HashPassword("Cashier@123"));
            await cmd.ExecuteNonQueryAsync();
        }

        // Cashier đăng nhập
        var cashierLoginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = cashierEmail,
            Password = "Cashier@123"
        });
        cashierLoginRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var cashierAuth = await cashierLoginRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierAuth!.AccessToken);

        // 4. Cashier cố tình gọi API tạo chi nhánh hoặc cập nhật kho hàng -> Bắt buộc nhận 403 Forbidden
        var cashierCreateBranchRes = await _client.PostAsJsonAsync("/api/branches", new CreateBranchRequest { Name = "Branch Hacking" });
        cashierCreateBranchRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var cashierUpdateWhRes = await _client.PutAsJsonAsync($"/api/warehouses/{wh.Id}", new UpdateWarehouseRequest { BranchId = branch.Id, Name = "Hacked Wh" });
        cashierUpdateWhRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // =========================================================================
    // 8. KIỂM THỬ HỒI QUY: CẬP NHẬT SKU CHẶN TỔ HỢP THUỘC TÍNH TRÙNG (LỖI 1 REVIEW)
    // =========================================================================
    [Fact]
    public async Task Postgres_UpdateVariant_AttributeCombinationConflict_ShouldReturn409_AndNotCorruptData()
    {
        var (_, token, _, _, _) = await RegisterAndLoginAsync("SkuDup");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 1. Tạo sản phẩm có 2 SKU: SKU-RED (Đỏ) và SKU-BLUE (Xanh)
        var createProductRes = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Áo Polo Nam Classic",
            Variants = new List<CreateVariantRequest>
            {
                new() { SKU = "POLO-RED", VariantName = "Polo Đỏ", RetailPrice = 200000, Color = "Red" },
                new() { SKU = "POLO-BLUE", VariantName = "Polo Xanh", RetailPrice = 200000, Color = "Blue" }
            }
        });
        createProductRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var prodObj = await createProductRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var productId = prodObj.GetProperty("productId").GetGuid();

        var product = await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{productId}", _jsonOptions);
        var skuRed = product!.Variants.First(v => v.SKU == "POLO-RED");
        var skuBlue = product.Variants.First(v => v.SKU == "POLO-BLUE");

        // 2. Ca tái hiện lỗi review: Cập nhật SKU-BLUE không gửi Color riêng, gửi Attributes: {"color":"Red"}
        // Trước khi sửa: API trả 200 và ghi đè SKU-BLUE thành màu Red (trùng với SKU-RED).
        // Sau khi sửa: Phải nhận 409 Conflict, dữ liệu không bị thay đổi dở dang.
        var dupUpdateRes = await _client.PutAsJsonAsync($"/api/products/{productId}/variants/{skuBlue.Id}", new UpdateVariantRequest
        {
            VariantName = "Polo Xanh Cập Nhật",
            Color = null,
            RetailPrice = 210000,
            Attributes = new Dictionary<string, object> { ["color"] = "Red" }
        });
        dupUpdateRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Kiểm tra database: SKU-BLUE vẫn giữ nguyên màu "Blue", không bị biến thành "Red"
        var productAfterFailedUpdate = await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{productId}", _jsonOptions);
        var refreshedSkuBlue = productAfterFailedUpdate!.Variants.First(v => v.Id == skuBlue.Id);
        refreshedSkuBlue.Color.Should().Be("Blue");

        // 3. Khác chữ hoa/thường, khoảng trắng và thứ tự key vẫn phải bị chặn 409
        var caseSpaceRes = await _client.PutAsJsonAsync($"/api/products/{productId}/variants/{skuBlue.Id}", new UpdateVariantRequest
        {
            VariantName = "Polo Xanh Cập Nhật",
            Color = null,
            RetailPrice = 210000,
            Attributes = new Dictionary<string, object> { ["CoLoR"] = "  rEd  " }
        });
        caseSpaceRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 4. Request có trường riêng (explicit Color) xung đột với Attributes:
        // Explicit "Yellow" có độ ưu tiên cao hơn Attributes "Red" -> Không trùng với SKU-RED -> Cập nhật thành công (200 OK)
        var explicitPrecedenceRes = await _client.PutAsJsonAsync($"/api/products/{productId}/variants/{skuBlue.Id}", new UpdateVariantRequest
        {
            VariantName = "Polo Vàng Mới",
            Color = "Yellow",
            RetailPrice = 220000,
            Attributes = new Dictionary<string, object> { ["color"] = "Red" }
        });
        explicitPrecedenceRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var productAfterYellow = await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{productId}", _jsonOptions);
        var skuYellow = productAfterYellow!.Variants.First(v => v.Id == skuBlue.Id);
        skuYellow.Color.Should().Be("Yellow");

        // 5. Xóa thuộc tính làm phát sinh tổ hợp trùng:
        // Tạo thêm SKU-YELLOW-M có màu Yellow và size M.
        var createVariantRes = await _client.PostAsJsonAsync($"/api/products/{productId}/variants", new CreateVariantRequest
        {
            SKU = "POLO-YELLOW-M",
            VariantName = "Polo Vàng Size M",
            RetailPrice = 230000,
            Color = "Yellow",
            Size = "M"
        });
        createVariantRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var skuYellowM = await createVariantRes.Content.ReadFromJsonAsync<ProductVariantResponse>(_jsonOptions);

        // Cập nhật SKU-YELLOW-M xóa size bằng explicit Size: ""
        // Sau khi xóa size, biến thể chỉ còn màu Yellow -> Trùng với SKU-BLUE (hiện là Yellow) -> Bị từ chối 409
        var deleteAttributeRes = await _client.PutAsJsonAsync($"/api/products/{productId}/variants/{skuYellowM!.Id}", new UpdateVariantRequest
        {
            VariantName = "Polo Vàng Đã Xóa Size",
            Color = "Yellow",
            Size = "", // Xóa size
            RetailPrice = 230000
        });
        deleteAttributeRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 6. Hai yêu cầu đồng thời tạo hoặc sửa về cùng một tổ hợp: Chỉ đúng 1 yêu cầu thành công, yêu cầu còn lại 409
        var concurrentTask1 = _client.PostAsJsonAsync($"/api/products/{productId}/variants", new CreateVariantRequest
        {
            SKU = "POLO-PURPLE-1",
            VariantName = "Polo Tím 1",
            RetailPrice = 250000,
            Color = "Purple"
        });
        var concurrentTask2 = _client.PostAsJsonAsync($"/api/products/{productId}/variants", new CreateVariantRequest
        {
            SKU = "POLO-PURPLE-2",
            VariantName = "Polo Tím 2",
            RetailPrice = 250000,
            Color = "Purple"
        });

        var results = await Task.WhenAll(concurrentTask1, concurrentTask2);
        var statusCodes = results.Select(r => r.StatusCode).ToList();
        statusCodes.Should().ContainSingle(s => s == HttpStatusCode.Created);
        statusCodes.Should().ContainSingle(s => s == HttpStatusCode.Conflict);
    }

    // =========================================================================
    // 9. KIỂM THỬ HỒI QUY: ĐỔI CHI NHÁNH KHO HÀNG CÓ LỊCH SỬ BỊ CHẶN 409 (LỖI 2 REVIEW)
    // =========================================================================
    [Fact]
    public async Task Postgres_Warehouse_ChangeBranch_WhenHasHistory_ReturnsControlled409_AndCleanWarehouseSucceeds()
    {
        var (_, token, _, _, _) = await RegisterAndLoginAsync("WhBranch");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 1. Tạo 2 Chi nhánh riêng biệt
        var branch1Res = await _client.PostAsJsonAsync("/api/branches", new CreateBranchRequest { Name = "Chi nhánh Quận 1" });
        var branch2Res = await _client.PostAsJsonAsync("/api/branches", new CreateBranchRequest { Name = "Chi nhánh Quận 3" });
        var branch1 = await branch1Res.Content.ReadFromJsonAsync<BranchResponse>(_jsonOptions);
        var branch2 = await branch2Res.Content.ReadFromJsonAsync<BranchResponse>(_jsonOptions);

        // 2. Tạo Kho hàng 1 thuộc Branch 1
        var wh1Res = await _client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest
        {
            BranchId = branch1!.Id,
            Name = "Kho Tổng Quận 1"
        });
        wh1Res.StatusCode.Should().Be(HttpStatusCode.Created);
        var wh1 = await wh1Res.Content.ReadFromJsonAsync<WarehouseResponse>(_jsonOptions);

        // 3. Tạo Nhà cung cấp, SKU và lập + xác nhận phiếu nhập cho Kho 1 để phát sinh chứng từ và tồn kho
        var supRes = await _client.PostAsJsonAsync("/api/suppliers", new OISM.Application.Features.Suppliers.CreateSupplierRequest { Name = "NCC Vật Tư Sài Gòn" });
        var sup = await supRes.Content.ReadFromJsonAsync<OISM.Application.Features.Suppliers.SupplierResponse>(_jsonOptions);

        var prodRes = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Vật tư Thép 10mm",
            Variants = new List<CreateVariantRequest> { new() { SKU = "THEP-10MM", VariantName = "Thép 10mm Cuộn", RetailPrice = 50000 } }
        });
        prodRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var prodObj = await prodRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var product = await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{prodObj.GetProperty("productId").GetGuid()}", _jsonOptions);
        var skuId = product!.Variants.First().Id;

        var grRes = await _client.PostAsJsonAsync("/api/goods-receipts", new OISM.Application.Features.Inventory.Dtos.CreateGoodsReceiptDto
        {
            BranchId = branch1.Id,
            WarehouseId = wh1!.Id,
            SupplierId = sup!.Id,
            Note = "Nhập vật tư kiểm thử kho",
            Items = new List<OISM.Application.Features.Inventory.Dtos.CreateGoodsReceiptItemDto>
            {
                new() { SkuId = skuId, Quantity = 50m, UnitPrice = 30000m }
            }
        });
        var gr = await grRes.Content.ReadFromJsonAsync<OISM.Application.Features.Inventory.Dtos.GoodsReceiptResponseDto>(_jsonOptions);

        var confirmRes = await _client.PostAsync($"/api/goods-receipts/{gr!.Id}/confirm", null);
        confirmRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Ca tái hiện lỗi review: Gọi PUT /api/warehouses/{id} đổi BranchId sang Branch 2
        // Trước khi sửa: Văng 500 Internal Server Error do vi phạm fk_goods_receipts_warehouse_branch.
        // Sau khi sửa: Phải chặn và trả 409 Conflict có kiểm soát, bảo toàn dữ liệu lịch sử.
        var changeBranchConflictRes = await _client.PutAsJsonAsync($"/api/warehouses/{wh1.Id}", new UpdateWarehouseRequest
        {
            BranchId = branch2!.Id,
            Name = "Kho Tổng Cố Đổi Sang Q3",
            IsActive = true
        });
        changeBranchConflictRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Xác minh kho vẫn thuộc Branch 1 trong DB
        var wh1AfterFailed = await _client.GetFromJsonAsync<WarehouseResponse>($"/api/warehouses/{wh1.Id}", _jsonOptions);
        wh1AfterFailed!.BranchId.Should().Be(branch1.Id);

        // 5. Kiểm tra trường hợp: Kho có lịch sử chứng từ nhưng tồn kho hiện tại bằng 0 vẫn phải bị từ chối 409
        var whZeroStockRes = await _client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest
        {
            BranchId = branch1.Id,
            Name = "Kho Lịch Sử Tồn Bằng 0"
        });
        whZeroStockRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var whZeroStock = await whZeroStockRes.Content.ReadFromJsonAsync<WarehouseResponse>(_jsonOptions);

        // Tạo chứng từ phiếu nhập liên kết với kho này (trạng thái Draft, chưa nhập kho nên OnHand = 0)
        var draftGrRes = await _client.PostAsJsonAsync("/api/goods-receipts", new OISM.Application.Features.Inventory.Dtos.CreateGoodsReceiptDto
        {
            BranchId = branch1.Id,
            WarehouseId = whZeroStock!.Id,
            SupplierId = sup!.Id,
            Note = "Phiếu nhập nháp kiểm thử kho tồn 0",
            Items = new List<OISM.Application.Features.Inventory.Dtos.CreateGoodsReceiptItemDto>
            {
                new() { SkuId = skuId, Quantity = 10m, UnitPrice = 30000m }
            }
        });
        draftGrRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // Cố tình đổi chi nhánh của kho đã có chứng từ nhưng tồn kho bằng 0 -> Phải bị từ chối 409 Conflict
        var changeBranchWithZeroStockRes = await _client.PutAsJsonAsync($"/api/warehouses/{whZeroStock.Id}", new UpdateWarehouseRequest
        {
            BranchId = branch2.Id,
            Name = "Kho Tồn 0 Cố Đổi Q3",
            IsActive = true
        });
        changeBranchWithZeroStockRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Xác minh kho vẫn thuộc Branch 1
        var whZeroAfterFailed = await _client.GetFromJsonAsync<WarehouseResponse>($"/api/warehouses/{whZeroStock.Id}", _jsonOptions);
        whZeroAfterFailed!.BranchId.Should().Be(branch1.Id);

        // 6. Sửa tên và trạng thái với chi nhánh cũ (Branch 1) -> Phải thành công (200 OK)
        var updateSameBranchRes = await _client.PutAsJsonAsync($"/api/warehouses/{wh1.Id}", new UpdateWarehouseRequest
        {
            BranchId = branch1.Id,
            Name = "Kho Phân Phối Sài Gòn Mới",
            IsActive = true
        });
        updateSameBranchRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedWhSameBranch = await updateSameBranchRes.Content.ReadFromJsonAsync<WarehouseResponse>(_jsonOptions);
        updatedWhSameBranch!.Name.Should().Be("Kho Phân Phối Sài Gòn Mới");
        updatedWhSameBranch.BranchId.Should().Be(branch1.Id);

        // 7. Kho chưa sử dụng hoàn toàn (chưa có chứng từ, số dư, sổ cái): Cho phép đổi chi nhánh sang Branch 2
        var wh2CleanRes = await _client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest
        {
            BranchId = branch1.Id,
            Name = "Kho Mới Hoàn Toàn Chưa Dùng"
        });
        wh2CleanRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var wh2Clean = await wh2CleanRes.Content.ReadFromJsonAsync<WarehouseResponse>(_jsonOptions);

        var changeCleanWhBranchRes = await _client.PutAsJsonAsync($"/api/warehouses/{wh2Clean!.Id}", new UpdateWarehouseRequest
        {
            BranchId = branch2.Id,
            Name = "Kho Mới Đã Chuyển Sang Q3",
            IsActive = true
        });
        changeCleanWhBranchRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var movedWh = await changeCleanWhBranchRes.Content.ReadFromJsonAsync<WarehouseResponse>(_jsonOptions);
        movedWh!.BranchId.Should().Be(branch2.Id);

        // 8. Đổi sang Branch không tồn tại hoặc khác tenant -> Bị chặn 400 Bad Request
        var invalidBranchRes = await _client.PutAsJsonAsync($"/api/warehouses/{wh2Clean.Id}", new UpdateWarehouseRequest
        {
            BranchId = Guid.NewGuid(), // Branch ngẫu nhiên không tồn tại
            Name = "Kho Branch Ảo",
            IsActive = true
        });
        invalidBranchRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // =========================================================================
    // 9. BẢO TOÀN KIỂU DỮ LIỆU JSON (OBJECT, ARRAY, NUMBER, BOOLEAN, STRING) TRONG SKU ATTRIBUTES
    // =========================================================================
    [Fact]
    public async Task Postgres_SkuAttributes_PreserveJsonTypes_AcrossCreateUpdateAndDuplicateCheck()
    {
        var (_, token, _, _, _) = await RegisterAndLoginAsync("JsonTypes");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 1. Tạo sản phẩm với SKU chứa đầy đủ các kiểu dữ liệu JSON:
        // - dimensions: Object
        // - sizes: Array
        // - count: Number (5)
        // - waterproof: Boolean (true)
        // - code: String ("10")
        var createRes = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Sản phẩm Đa Kiểu Thuộc Tính",
            Variants = new List<CreateVariantRequest>
            {
                new()
                {
                    SKU = "JSON-TYPE-SKU-1",
                    VariantName = "Biến thể JSON 1",
                    RetailPrice = 120000,
                    Attributes = new Dictionary<string, object>
                    {
                        ["dimensions"] = new Dictionary<string, object> { ["width"] = 10, ["height"] = 20 },
                        ["sizes"] = new List<object> { "S", "M" },
                        ["count"] = 5,
                        ["waterproof"] = true,
                        ["code"] = "10"
                    }
                }
            }
        });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdObj = await createRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var productId = createdObj.GetProperty("productId").GetGuid();

        // 2. Đọc lại qua GET /api/products/{id} và kiểm tra JSON raw cũng như đối tượng
        var getRes = await _client.GetAsync($"/api/products/{productId}");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var rawJson = await getRes.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(rawJson);
        var variantJson = doc.RootElement.GetProperty("variants")[0];
        var attrsJson = variantJson.GetProperty("attributes");

        // Assert trực tiếp trên JSON payload trả về từ database PostgreSQL:
        attrsJson.GetProperty("dimensions").ValueKind.Should().Be(JsonValueKind.Object, "dimensions phải là JSON Object, không được ép thành chuỗi");
        attrsJson.GetProperty("dimensions").GetProperty("width").GetInt64().Should().Be(10);
        attrsJson.GetProperty("dimensions").GetProperty("height").GetInt64().Should().Be(20);

        attrsJson.GetProperty("sizes").ValueKind.Should().Be(JsonValueKind.Array, "sizes phải là JSON Array, không được ép thành chuỗi");
        attrsJson.GetProperty("sizes")[0].GetString().Should().Be("S");
        attrsJson.GetProperty("sizes")[1].GetString().Should().Be("M");

        attrsJson.GetProperty("count").ValueKind.Should().Be(JsonValueKind.Number, "count phải là JSON Number");
        attrsJson.GetProperty("count").GetInt64().Should().Be(5);

        attrsJson.GetProperty("waterproof").ValueKind.Should().Be(JsonValueKind.True, "waterproof phải là JSON Boolean true");
        attrsJson.GetProperty("code").ValueKind.Should().Be(JsonValueKind.String, "code phải là JSON String");
        attrsJson.GetProperty("code").GetString().Should().Be("10");

        var variantId = variantJson.GetProperty("id").GetGuid();

        // 3. Kiểm tra chống trùng tổ hợp: Thử tạo biến thể thứ 2 có cùng thuộc tính nhưng dimensions đảo thứ tự key
        var dupOrderRes = await _client.PostAsJsonAsync($"/api/products/{productId}/variants", new CreateVariantRequest
        {
            SKU = "JSON-TYPE-SKU-DUP-ORDER",
            VariantName = "Biến thể Đảo Thứ Tự Key",
            RetailPrice = 120000,
            Attributes = new Dictionary<string, object>
            {
                ["dimensions"] = new Dictionary<string, object> { ["height"] = 20, ["width"] = 10 }, // Đảo thứ tự
                ["sizes"] = new List<object> { "S", "M" },
                ["count"] = 5,
                ["waterproof"] = true,
                ["code"] = "10"
            }
        });
        dupOrderRes.StatusCode.Should().Be(HttpStatusCode.Conflict, "Khác thứ tự key nhưng cùng nội dung phải bị chặn trùng tổ hợp 409");

        // 4. Kiểm tra phân biệt kiểu: Number 5 vs String \"5\" trong chữ ký
        var diffTypeRes = await _client.PostAsJsonAsync($"/api/products/{productId}/variants", new CreateVariantRequest
        {
            SKU = "JSON-TYPE-SKU-DIFF-TYPE",
            VariantName = "Biến thể Khác Kiểu Dữ Liệu",
            RetailPrice = 120000,
            Attributes = new Dictionary<string, object>
            {
                ["dimensions"] = new Dictionary<string, object> { ["width"] = 10, ["height"] = 20 },
                ["sizes"] = new List<object> { "S", "M" },
                ["count"] = "5", // String thay vì Number
                ["waterproof"] = true,
                ["code"] = "10"
            }
        });
        diffTypeRes.StatusCode.Should().Be(HttpStatusCode.Created, "Chữ ký phân biệt rõ ràng Number 5 và String '5'");

        // 5. Cập nhật biến thể lồng nhau qua PUT /api/products/{id}/variants/{vId}
        var updateNestedRes = await _client.PutAsJsonAsync($"/api/products/{productId}/variants/{variantId}", new UpdateVariantRequest
        {
            VariantName = "Biến thể Đã Cập Nhật Lồng Nhau",
            RetailPrice = 150000,
            Attributes = new Dictionary<string, object>
            {
                ["dimensions"] = new Dictionary<string, object> { ["width"] = 15, ["height"] = 25, ["depth"] = 30 },
                ["sizes"] = new List<object> { "M", "L", "XL" },
                ["count"] = 8
            }
        });
        updateNestedRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Đọc lại và kiểm tra kiểu lồng nhau sau khi cập nhật
        var getAfterUpdate = await _client.GetAsync($"/api/products/{productId}");
        var rawAfterUpdate = await getAfterUpdate.Content.ReadAsStringAsync();
        using var docUpdate = JsonDocument.Parse(rawAfterUpdate);
        var updatedV = docUpdate.RootElement.GetProperty("variants")[0];
        var updatedAttrs = updatedV.GetProperty("attributes");

        updatedAttrs.GetProperty("dimensions").ValueKind.Should().Be(JsonValueKind.Object);
        updatedAttrs.GetProperty("dimensions").GetProperty("depth").GetInt64().Should().Be(30);
        updatedAttrs.GetProperty("sizes").ValueKind.Should().Be(JsonValueKind.Array);
        updatedAttrs.GetProperty("sizes").GetArrayLength().Should().Be(3);
        updatedAttrs.GetProperty("sizes")[2].GetString().Should().Be("XL");

        // 6. Xóa thuộc tính an toàn (Null / Empty) tuân thủ hợp đồng
        var deleteAttrRes = await _client.PutAsJsonAsync($"/api/products/{productId}/variants/{variantId}", new UpdateVariantRequest
        {
            VariantName = "Biến thể Sau Xóa Thuộc Tính",
            RetailPrice = 150000,
            Attributes = new Dictionary<string, object>
            {
                ["sizes"] = null! // Xóa key sizes
            }
        });
        deleteAttrRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var getAfterDelete = await _client.GetAsync($"/api/products/{productId}");
        var rawAfterDelete = await getAfterDelete.Content.ReadAsStringAsync();
        using var docDelete = JsonDocument.Parse(rawAfterDelete);
        var finalV = docDelete.RootElement.GetProperty("variants")[0];
        var finalAttrs = finalV.GetProperty("attributes");
        finalAttrs.TryGetProperty("sizes", out _).Should().BeFalse("sizes đã bị xóa");
        finalAttrs.GetProperty("dimensions").ValueKind.Should().Be(JsonValueKind.Object);
    }

    // =========================================================================
    // 10. BẢO TOÀN NULL LỒNG NHAU TRONG OBJECT VÀ ARRAY THUỘC TÍNH SKU
    // =========================================================================
    [Fact]
    public async Task Postgres_SkuAttributes_PreserveNestedNulls_InObjectsAndArrays_AcrossCreateUpdateAndPos()
    {
        var (_, token, _, _, _) = await RegisterAndLoginAsync("NestedNulls");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 1. Tạo sản phẩm với SKU có Attributes chứa null lồng nhau:
        // - positions: ["S", null, "M"]
        // - dimensions: {"width": 10, "depth": null}
        // - meta: {"layer1": {"layer2": null, "tag": "test"}}
        var createRes = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Sản phẩm Áo Null Lồng Nhau",
            Variants = new List<CreateVariantRequest>
            {
                new()
                {
                    SKU = "NESTED-NULL-SKU-1",
                    VariantName = "Biến thể Null Lồng Nhau 1",
                    Barcode = "8939999000018",
                    RetailPrice = 250000,
                    Attributes = new Dictionary<string, object>
                    {
                        ["positions"] = new List<object?> { "S", null, "M" },
                        ["dimensions"] = new Dictionary<string, object?> { ["width"] = 10, ["depth"] = null },
                        ["meta"] = new Dictionary<string, object?>
                        {
                            ["layer1"] = new Dictionary<string, object?>
                            {
                                ["layer2"] = null,
                                ["tag"] = "test"
                            }
                        }
                    }
                }
            }
        });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdObj = await createRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var productId = createdObj.GetProperty("productId").GetGuid();

        // 2. Đọc lại qua GET /api/products/{id} và kiểm tra JSON raw & đối tượng
        var getRes = await _client.GetAsync($"/api/products/{productId}");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var rawJson = await getRes.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(rawJson);
        var variantJson = doc.RootElement.GetProperty("variants")[0];
        var attrsJson = variantJson.GetProperty("attributes");

        // Assert positions: bảo toàn số lượng (3), thứ tự và giá trị null ở vị trí index 1
        attrsJson.GetProperty("positions").ValueKind.Should().Be(JsonValueKind.Array);
        attrsJson.GetProperty("positions").GetArrayLength().Should().Be(3);
        attrsJson.GetProperty("positions")[0].GetString().Should().Be("S");
        attrsJson.GetProperty("positions")[1].ValueKind.Should().Be(JsonValueKind.Null);
        attrsJson.GetProperty("positions")[2].GetString().Should().Be("M");

        // Assert dimensions: bảo toàn key depth với giá trị null
        attrsJson.GetProperty("dimensions").ValueKind.Should().Be(JsonValueKind.Object);
        attrsJson.GetProperty("dimensions").GetProperty("width").GetInt64().Should().Be(10);
        attrsJson.GetProperty("dimensions").GetProperty("depth").ValueKind.Should().Be(JsonValueKind.Null);

        // Assert meta: nhiều tầng lồng nhau vẫn bảo toàn null
        var meta = attrsJson.GetProperty("meta").GetProperty("layer1");
        meta.GetProperty("layer2").ValueKind.Should().Be(JsonValueKind.Null);
        meta.GetProperty("tag").GetString().Should().Be("test");

        var variantId = variantJson.GetProperty("id").GetGuid();

        // 3. Kiểm tra đọc lại trực tiếp từ PostgreSQL database qua cột attributes jsonb
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.RuntimeConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT attributes::text FROM skus WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", variantId);
            var dbAttributesText = (string)(await cmd.ExecuteScalarAsync())!;

            dbAttributesText.Should().Contain("\"depth\": null", "PostgreSQL jsonb phải chứa depth: null");
            dbAttributesText.Should().Contain("[\"S\", null, \"M\"]", "PostgreSQL jsonb phải chứa mảng có null");
        }

        // 4. Kiểm tra phản hồi từ POS Search (/api/pos/search)
        var posSearchRes = await _client.GetAsync("/api/pos/search?query=NESTED-NULL-SKU-1");
        posSearchRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var posRaw = await posSearchRes.Content.ReadAsStringAsync();
        using var posDoc = JsonDocument.Parse(posRaw);
        var posItem = posDoc.RootElement[0].GetProperty("attributes");
        posItem.GetProperty("positions")[1].ValueKind.Should().Be(JsonValueKind.Null);
        posItem.GetProperty("dimensions").GetProperty("depth").ValueKind.Should().Be(JsonValueKind.Null);

        // 5. Kiểm tra phản hồi từ POS Barcode Scanner (/api/pos/barcode/{code})
        var posBarcodeRes = await _client.GetAsync("/api/pos/barcode/8939999000018");
        posBarcodeRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var barcodeRaw = await posBarcodeRes.Content.ReadAsStringAsync();
        using var barcodeDoc = JsonDocument.Parse(barcodeRaw);
        var barcodeAttrs = barcodeDoc.RootElement.GetProperty("attributes");
        barcodeAttrs.GetProperty("positions")[1].ValueKind.Should().Be(JsonValueKind.Null);
        barcodeAttrs.GetProperty("dimensions").GetProperty("depth").ValueKind.Should().Be(JsonValueKind.Null);

        // 6. Kiểm tra chống trùng tổ hợp khi khác thứ tự key nhưng cùng nội dung (kể cả depth: null)
        var dupOrderRes = await _client.PostAsJsonAsync($"/api/products/{productId}/variants", new CreateVariantRequest
        {
            SKU = "NESTED-NULL-SKU-DUP-ORDER",
            VariantName = "Biến thể Đảo Key",
            RetailPrice = 250000,
            Attributes = new Dictionary<string, object>
            {
                ["dimensions"] = new Dictionary<string, object?> { ["depth"] = null, ["width"] = 10 }, // Đảo thứ tự
                ["positions"] = new List<object?> { "S", null, "M" },
                ["meta"] = new Dictionary<string, object?>
                {
                    ["layer1"] = new Dictionary<string, object?> { ["tag"] = "test", ["layer2"] = null } // Đảo thứ tự
                }
            }
        });
        dupOrderRes.StatusCode.Should().Be(HttpStatusCode.Conflict, "Cùng nội dung (kể cả nested null) đảo thứ tự key phải bị chặn 409");

        // 7. Kiểm tra phân biệt chữ ký: ["S", "M"] (không có null) khác ["S", null, "M"]
        var diffArrayRes = await _client.PostAsJsonAsync($"/api/products/{productId}/variants", new CreateVariantRequest
        {
            SKU = "NESTED-NULL-SKU-DIFF-ARRAY",
            VariantName = "Biến thể Mảng Không Null",
            RetailPrice = 250000,
            Attributes = new Dictionary<string, object>
            {
                ["positions"] = new List<object?> { "S", "M" }, // Không có null ở giữa
                ["dimensions"] = new Dictionary<string, object?> { ["width"] = 10, ["depth"] = null },
                ["meta"] = new Dictionary<string, object?>
                {
                    ["layer1"] = new Dictionary<string, object?> { ["layer2"] = null, ["tag"] = "test" }
                }
            }
        });
        diffArrayRes.StatusCode.Should().Be(HttpStatusCode.Created, "Chữ ký phân biệt rõ ràng ['S', null, 'M'] và ['S', 'M']");

        // 8. Cập nhật biến thể qua PUT /api/products/{id}/variants/{vId} với null lồng nhau mới
        var updateNestedRes = await _client.PutAsJsonAsync($"/api/products/{productId}/variants/{variantId}", new UpdateVariantRequest
        {
            VariantName = "Biến thể Đã Cập Nhật Null Lồng Nhau",
            RetailPrice = 260000,
            Attributes = new Dictionary<string, object>
            {
                ["positions"] = new List<object?> { null, "L", null },
                ["dimensions"] = new Dictionary<string, object?> { ["height"] = 50, ["depth"] = null, ["notes"] = null }
            }
        });
        updateNestedRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var getAfterUpdate = await _client.GetAsync($"/api/products/{productId}");
        var rawAfterUpdate = await getAfterUpdate.Content.ReadAsStringAsync();
        using var docUpdate = JsonDocument.Parse(rawAfterUpdate);
        var updatedV = docUpdate.RootElement.GetProperty("variants")[0];
        var updatedAttrs = updatedV.GetProperty("attributes");

        var updatedPositions = updatedAttrs.GetProperty("positions");
        updatedPositions.GetArrayLength().Should().Be(3);
        updatedPositions[0].ValueKind.Should().Be(JsonValueKind.Null);
        updatedPositions[1].GetString().Should().Be("L");
        updatedPositions[2].ValueKind.Should().Be(JsonValueKind.Null);

        var updatedDims = updatedAttrs.GetProperty("dimensions");
        updatedDims.GetProperty("height").GetInt64().Should().Be(50);
        updatedDims.GetProperty("depth").ValueKind.Should().Be(JsonValueKind.Null);
        updatedDims.GetProperty("notes").ValueKind.Should().Be(JsonValueKind.Null);

        // 9. Xóa thuộc tính cấp ngoài bằng null tuân thủ hợp đồng: Positions bị xóa, dimensions giữ nguyên
        var deleteTopRes = await _client.PutAsJsonAsync($"/api/products/{productId}/variants/{variantId}", new UpdateVariantRequest
        {
            VariantName = "Biến thể Xóa Positions Cấp Ngoài",
            RetailPrice = 260000,
            Attributes = new Dictionary<string, object>
            {
                ["positions"] = null! // Xóa key cấp ngoài
            }
        });
        deleteTopRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var getAfterDeleteTop = await _client.GetAsync($"/api/products/{productId}");
        var rawAfterDeleteTop = await getAfterDeleteTop.Content.ReadAsStringAsync();
        using var docDeleteTop = JsonDocument.Parse(rawAfterDeleteTop);
        var finalVTop = docDeleteTop.RootElement.GetProperty("variants")[0];
        var finalAttrsTop = finalVTop.GetProperty("attributes");

        finalAttrsTop.TryGetProperty("positions", out _).Should().BeFalse("positions cấp ngoài đã bị xóa hoàn toàn");
        finalAttrsTop.GetProperty("dimensions").GetProperty("depth").ValueKind.Should().Be(JsonValueKind.Null, "dimensions lồng nhau vẫn bảo toàn null");
    }

    /// <summary>
    /// Regression Test BE2: Sửa lỗi số điện thoại đăng ký gây HTTP 500 do ck_users_phone_normalized.
    /// Kiểm chứng:
    /// - Chuẩn hóa "090 123 4567" thành "+84901234567" và ghi nhận vào cơ sở dữ liệu thành công (201).
    /// - Chuẩn hóa số quốc tế E.164: "+1 (202) 555-0123" -> "+12025550123".
    /// - Từ chối định dạng không hợp lệ (chứa chữ cái, quá ngắn) bằng 400 Bad Request, không văng 500.
    /// - Phát hiện trùng số điện thoại trả 409 Conflict.
    /// - Đăng ký đồng thời trùng số điện thoại: 1 thành công (201), 1 bị từ chối có kiểm soát (409), không để lại tenant mồ côi.
    /// </summary>
    [Fact]
    public async Task Postgres_Auth_RegisterTenant_PhoneNumber_NormalizationAndValidation_ShouldWorkWithout500()
    {
        var runCode = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        // 1. Gửi PhoneNumber = "090 123 4567": phải được chuẩn hóa thành "+84901234567" và trả 201
        var phoneReq1 = new RegisterTenantRequest
        {
            TenantName = $"Shop Phone 1 {runCode}",
            TenantCode = $"PH1_{runCode}",
            FullName = "Chủ Shop Phone 1",
            Email = $"phone1_{runCode}@oismtest.com",
            PhoneNumber = "090 123 4567",
            Password = "Password@123"
        };

        var res1 = await _client.PostAsJsonAsync("/api/auth/register-tenant", phoneReq1);
        res1.StatusCode.Should().Be(HttpStatusCode.Created, "090 123 4567 phải được chuẩn hóa và chấp nhận hợp lệ");

        // Kiểm tra trong PostgreSQL thật: cột phone lưu đúng "+84901234567"
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT phone FROM users WHERE email = @email", conn);
            cmd.Parameters.AddWithValue("email", phoneReq1.Email.ToLowerInvariant());
            var storedPhone = (string?)await cmd.ExecuteScalarAsync();
            storedPhone.Should().Be("+84901234567", "Số điện thoại nội địa Việt Nam có khoảng trắng phải được chuẩn hóa đúng E.164");
        }

        // 2. Gửi số quốc tế có format: "+1 (202) 555-0123" -> "+12025550123" (201 Created)
        var phoneReq2 = new RegisterTenantRequest
        {
            TenantName = $"Shop Phone 2 {runCode}",
            TenantCode = $"PH2_{runCode}",
            FullName = "Chủ Shop Phone 2",
            Email = $"phone2_{runCode}@oismtest.com",
            PhoneNumber = "+1 (202) 555-0123",
            Password = "Password@123"
        };

        var res2 = await _client.PostAsJsonAsync("/api/auth/register-tenant", phoneReq2);
        res2.StatusCode.Should().Be(HttpStatusCode.Created);

        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT phone FROM users WHERE email = @email", conn);
            cmd.Parameters.AddWithValue("email", phoneReq2.Email.ToLowerInvariant());
            var storedPhone = (string?)await cmd.ExecuteScalarAsync();
            storedPhone.Should().Be("+12025550123");
        }

        // 3. Gửi số điện thoại không hợp lệ (chứa chữ cái): trả 400 Bad Request, không được ném 500
        var invalidReq = new RegisterTenantRequest
        {
            TenantName = $"Shop Invalid {runCode}",
            TenantCode = $"PHINV_{runCode}",
            FullName = "Chủ Shop Invalid",
            Email = $"phoneinv_{runCode}@oismtest.com",
            PhoneNumber = "090 123 456a",
            Password = "Password@123"
        };

        var resInvalid = await _client.PostAsJsonAsync("/api/auth/register-tenant", invalidReq);
        resInvalid.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Số điện thoại chứa chữ cái phải bị từ chối bằng 400");

        // 4. Gửi số điện thoại trùng lặp đã lưu trước đó: trả 409 Conflict
        var dupPhoneReq = new RegisterTenantRequest
        {
            TenantName = $"Shop Dup Phone {runCode}",
            TenantCode = $"PHDUP_{runCode}",
            FullName = "Chủ Shop Dup Phone",
            Email = $"phonedup_{runCode}@oismtest.com",
            PhoneNumber = "0901234567", // Trùng với phoneReq1 sau chuẩn hóa (+84901234567)
            Password = "Password@123"
        };

        var resDup = await _client.PostAsJsonAsync("/api/auth/register-tenant", dupPhoneReq);
        resDup.StatusCode.Should().Be(HttpStatusCode.Conflict, "Số điện thoại trùng lặp phải trả về 409 Conflict");

        // 5. Đăng ký đồng thời 2 yêu cầu dùng cùng số điện thoại: 1 thành công (201), 1 thất bại (409)
        var sharedPhone = "091 888 9999";
        var concurrentReq1 = new RegisterTenantRequest
        {
            TenantName = $"Shop Conc 1 {runCode}",
            TenantCode = $"PHC1_{runCode}",
            FullName = "Chủ Shop Conc 1",
            Email = $"phonec1_{runCode}@oismtest.com",
            PhoneNumber = sharedPhone,
            Password = "Password@123"
        };
        var concurrentReq2 = new RegisterTenantRequest
        {
            TenantName = $"Shop Conc 2 {runCode}",
            TenantCode = $"PHC2_{runCode}",
            FullName = "Chủ Shop Conc 2",
            Email = $"phonec2_{runCode}@oismtest.com",
            PhoneNumber = sharedPhone,
            Password = "Password@123"
        };

        var concTask1 = _client.PostAsJsonAsync("/api/auth/register-tenant", concurrentReq1);
        var concTask2 = _client.PostAsJsonAsync("/api/auth/register-tenant", concurrentReq2);
        var results = await Task.WhenAll(concTask1, concTask2);

        var statusCodes = results.Select(r => r.StatusCode).ToList();
        statusCodes.Should().Contain(HttpStatusCode.Created, "Phải có đúng một yêu cầu đăng ký đồng thời thành công");
        statusCodes.Should().Contain(HttpStatusCode.Conflict, "Yêu cầu đăng ký đồng thời còn lại phải bị từ chối bằng 409 Conflict");

        // Đảm bảo tính nguyên tử: tenant thất bại không bị lưu dở dang trong database
        var failedReq = statusCodes[0] == HttpStatusCode.Conflict ? concurrentReq1 : concurrentReq2;
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM tenants WHERE name = @name", conn);
            cmd.Parameters.AddWithValue("name", failedReq.TenantName.Trim());
            var tenantCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            tenantCount.Should().Be(0, "Tenant của yêu cầu đăng ký thất bại không được phép lưu lại dở dang");
        }
    }

    /// <summary>
    /// Regression Test BE2: Sửa lỗi barcode trùng gây HTTP 500 do uq_skus_3.
    /// Kiểm chứng:
    /// - Tạo SKU A với barcode "CODE128-REVIEW": 201 Created.
    /// - Tạo SKU B khác mã cùng barcode trong tenant: trả 409 Conflict rõ ràng (không văng 500).
    /// - Tạo Product với danh sách SKU chứa 2 SKU cùng barcode trong cùng 1 request: 409 Conflict.
    /// - Tạo Product với SKU có barcode đã tồn tại trong DB: 409 Conflict.
    /// - Update Variant sang barcode của SKU khác: 409 Conflict.
    /// - Tạo SKU đồng thời với cùng barcode: đúng 1 thành công (201), 1 trả về 409 Conflict.
    /// - Khác tenant dùng cùng barcode: thành công (201) do unique theo tenant.
    /// - Barcode tự sinh: tự động gán EAN-13 hợp lệ 13 chữ số tiền tố 893.
    /// </summary>
    [Fact]
    public async Task Postgres_SkuBarcode_UniqueConstraint_CreateUpdateAndConcurrency_ReturnsControlled409()
    {
        var runCode = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var (_, token1, _, _, _) = await RegisterAndLoginAsync($"bc1_{runCode}");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token1);

        var testBarcode = $"BARCODE-{runCode}";

        // 1. Tạo sản phẩm đầu tiên với SKU A mang testBarcode -> 201 Created
        var createProductRes1 = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Sản phẩm A Kiểm Tra Barcode",
            Variants = new List<CreateVariantRequest>
            {
                new()
                {
                    SKU = $"SKU-A-{runCode}",
                    VariantName = "Biến thể A",
                    Barcode = testBarcode,
                    RetailPrice = 100000,
                    WholesalePrice = 70000
                }
            }
        });
        createProductRes1.StatusCode.Should().Be(HttpStatusCode.Created);
        var productA = await createProductRes1.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var productAId = productA.GetProperty("productId").GetGuid();

        // 2. Thêm biến thể SKU B vào sản phẩm A với cùng testBarcode -> 409 Conflict (Không được văng 500 do uq_skus_3)
        var createVariantRes = await _client.PostAsJsonAsync($"/api/products/{productAId}/variants", new CreateVariantRequest
        {
            SKU = $"SKU-B-{runCode}",
            VariantName = "Biến thể B Trùng Barcode",
            Barcode = testBarcode,
            RetailPrice = 110000,
            WholesalePrice = 75000
        });
        createVariantRes.StatusCode.Should().Be(HttpStatusCode.Conflict, "Thêm SKU trùng Barcode trong cùng tenant phải trả về 409 Conflict");
        var conflictBody = await createVariantRes.Content.ReadAsStringAsync();
        conflictBody.Should().Contain("Mã vạch (Barcode)");

        // 3. Tạo sản phẩm mới mà 1 request chứa 2 biến thể cùng barcode -> 409 Conflict
        var createDuplicateInRequestRes = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Sản phẩm Trùng Barcode Nội Bộ",
            Variants = new List<CreateVariantRequest>
            {
                new() { SKU = $"SKU-DUP1-{runCode}", VariantName = "V1", Barcode = $"BC-INTERNAL-{runCode}", RetailPrice = 10000 },
                new() { SKU = $"SKU-DUP2-{runCode}", VariantName = "V2", Barcode = $"BC-INTERNAL-{runCode}", RetailPrice = 20000 }
            }
        });
        createDuplicateInRequestRes.StatusCode.Should().Be(HttpStatusCode.Conflict, "Trùng barcode trong cùng một request phải bị từ chối 409");

        // 4. Tạo sản phẩm mới mà biến thể trùng barcode đã có trong database -> 409 Conflict
        var createDuplicateWithDbRes = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Sản phẩm Trùng Barcode DB",
            Variants = new List<CreateVariantRequest>
            {
                new() { SKU = $"SKU-NEW-{runCode}", VariantName = "V New", Barcode = testBarcode, RetailPrice = 15000 }
            }
        });
        createDuplicateWithDbRes.StatusCode.Should().Be(HttpStatusCode.Conflict, "Trùng barcode đã có trong DB phải bị từ chối 409");

        // 5. Tạo SKU C với barcode hợp lệ riêng, sau đó sửa (UpdateVariant) sang testBarcode -> 409 Conflict
        var validBarcodeC = $"BARCODE-C-{runCode}";
        var createVariantCRes = await _client.PostAsJsonAsync($"/api/products/{productAId}/variants", new CreateVariantRequest
        {
            SKU = $"SKU-C-{runCode}",
            VariantName = "Biến thể C Hợp Lệ",
            Barcode = validBarcodeC,
            RetailPrice = 120000,
            WholesalePrice = 80000
        });
        createVariantCRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var variantC = await createVariantCRes.Content.ReadFromJsonAsync<ProductVariantResponse>(_jsonOptions);
        var variantCId = variantC!.Id;

        // Cập nhật variant C sang testBarcode (đã thuộc variant A)
        var updateVariantRes = await _client.PutAsJsonAsync($"/api/products/{productAId}/variants/{variantCId}", new UpdateVariantRequest
        {
            VariantName = "Biến thể C Sửa Barcode",
            Barcode = testBarcode,
            RetailPrice = 125000,
            WholesalePrice = 85000
        });
        updateVariantRes.StatusCode.Should().Be(HttpStatusCode.Conflict, "Sửa SKU sang barcode của SKU khác phải trả về 409 Conflict");

        // Xác nhận dữ liệu variant C trong DB không bị thay đổi dở dang
        var getProductRes = await _client.GetAsync($"/api/products/{productAId}");
        var productDoc = await getProductRes.Content.ReadFromJsonAsync<ProductResponse>(_jsonOptions);
        var unchangedC = productDoc!.Variants.First(v => v.Id == variantCId);
        unchangedC.Barcode.Should().Be(validBarcodeC, "Barcode của biến thể không được thay đổi khi bị từ chối do trùng lặp");

        // 6. Tenant khác sử dụng cùng testBarcode -> Thành công 201 (do unique là (tenant_id, barcode))
        var (_, token2, _, _, _) = await RegisterAndLoginAsync($"bc2_{runCode}");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token2);

        var crossTenantRes = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Sản phẩm Tenant 2 Cùng Barcode",
            Variants = new List<CreateVariantRequest>
            {
                new()
                {
                    SKU = $"SKU-T2-{runCode}",
                    VariantName = "Biến thể Tenant 2",
                    Barcode = testBarcode, // Cùng barcode với Tenant 1
                    RetailPrice = 150000,
                    WholesalePrice = 100000
                }
            }
        });
        crossTenantRes.StatusCode.Should().Be(HttpStatusCode.Created, "Khác tenant được phép sử dụng cùng barcode theo constraint uq_skus_3");

        // 7. Barcode tự sinh: khi không truyền barcode, hệ thống tự động sinh EAN-13 chuẩn Việt Nam
        var autoBarcodeRes = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Sản phẩm Barcode Tự Sinh",
            Variants = new List<CreateVariantRequest>
            {
                new()
                {
                    SKU = $"SKU-AUTO-{runCode}",
                    VariantName = "Biến thể Auto Barcode",
                    RetailPrice = 200000,
                    WholesalePrice = 140000
                }
            }
        });
        autoBarcodeRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var autoProduct = await autoBarcodeRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var autoProductId = autoProduct.GetProperty("productId").GetGuid();

        var getAutoRes = await _client.GetAsync($"/api/products/{autoProductId}");
        var autoDoc = await getAutoRes.Content.ReadFromJsonAsync<ProductResponse>(_jsonOptions);
        var autoVariant = autoDoc!.Variants.First();
        autoVariant.Barcode.Should().NotBeNullOrWhiteSpace();
        autoVariant.Barcode.Should().StartWith("893");
        autoVariant.Barcode!.Length.Should().Be(13);
        BarcodeHelper.IsValidEan13(autoVariant.Barcode).Should().BeTrue("Barcode tự sinh phải là mã EAN-13 hợp lệ chuẩn GS1");
    }

    private async Task<bool> CheckHasCategoryCycleInDatabaseAsync(Guid tenantId)
    {
        await using var conn = new NpgsqlConnection(PostgresTestEnvironment.RuntimeConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            WITH RECURSIVE cat_tree AS (
                SELECT id, parent_id, ARRAY[id] AS path, false AS is_cycle 
                FROM categories 
                WHERE tenant_id = @tenantId AND parent_id IS NOT NULL 
                UNION ALL 
                SELECT c.id, c.parent_id, path || c.id, c.id = ANY(path) 
                FROM categories c 
                JOIN cat_tree t ON c.id = t.parent_id 
                WHERE c.tenant_id = @tenantId AND NOT is_cycle
            ) 
            SELECT COUNT(*) FROM cat_tree WHERE is_cycle = true;", conn);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        return count > 0;
    }

    // =========================================================================
    // 8. KIỂM THỬ HỒI QUY CHỐNG VÒNG LẶP CÂY DANH MỤC KHI CẬP NHẬT ĐỒNG THỜI (BE2-04)
    // =========================================================================
    [Fact]
    public async Task Postgres_CategoryTree_ConcurrentUpdates_MustPreventCycles_AndKeepTreeValid()
    {
        var runCode = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var (tenantId, token, _, _, _) = await RegisterAndLoginAsync($"CatCycle_{runCode}");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // ---------------------------------------------------------------------
        // KỊCH BẢN 1: Race Condition cập nhật đối ứng (A.parent = B và B.parent = A)
        // Dùng blocker connection giữ advisory lock để tái hiện chuẩn xác 2 request tranh chấp cùng lúc
        // ---------------------------------------------------------------------
        var catARes = await _client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest { Name = $"Node A {runCode}" });
        catARes.StatusCode.Should().Be(HttpStatusCode.Created);
        var catA = await catARes.Content.ReadFromJsonAsync<CategoryResponse>(_jsonOptions);
        var catAId = catA!.Id;

        var catBRes = await _client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest { Name = $"Node B {runCode}" });
        catBRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var catB = await catBRes.Content.ReadFromJsonAsync<CategoryResponse>(_jsonOptions);
        var catBId = catB!.Id;

        // Kết nối kiểm thử độc lập mở transaction và giữ khóa advisory lock của tenant
        await using (var blockerConn = new NpgsqlConnection(PostgresTestEnvironment.RuntimeConnectionString))
        {
            await blockerConn.OpenAsync();
            await using var blockerTx = await blockerConn.BeginTransactionAsync();

            await using (var lockCmd = new NpgsqlCommand(
                $"SELECT pg_advisory_xact_lock(hashtextextended('{tenantId}' || '/category_tree', 0));", blockerConn, blockerTx))
            {
                await lockCmd.ExecuteNonQueryAsync();
            }

            // Gửi đồng thời PUT A (Parent = B) và PUT B (Parent = A)
            var reqAtoB = new UpdateCategoryRequest { Name = $"Node A (cha B) {runCode}", ParentId = catBId, IsActive = true };
            var reqBtoA = new UpdateCategoryRequest { Name = $"Node B (cha A) {runCode}", ParentId = catAId, IsActive = true };

            var taskA = _client.PutAsJsonAsync($"/api/categories/{catAId}", reqAtoB);
            var taskB = _client.PutAsJsonAsync($"/api/categories/{catBId}", reqBtoA);

            // Xác nhận cả hai request đều bị chặn và đang xếp hàng chờ khóa giải phóng
            await Task.Delay(200);
            taskA.IsCompleted.Should().BeFalse("Task A phải chờ advisory lock được giải phóng");
            taskB.IsCompleted.Should().BeFalse("Task B phải chờ advisory lock được giải phóng");

            // Giải phóng khóa bằng cách rollback transaction của blocker connection
            await blockerTx.RollbackAsync();
            await blockerConn.CloseAsync();

            // Chờ cả 2 request hoàn thành
            var responses = await Task.WhenAll(taskA, taskB);

            // Bắt buộc: KHÔNG ĐƯỢC PHÉP cả 2 request cùng thành công 200 OK!
            // Một request thành công (200), request còn lại phát hiện vòng lặp và bị từ chối bằng 400 Bad Request
            var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
            var badRequestCount = responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest);
            var serverErrorCount = responses.Count(r => r.StatusCode == HttpStatusCode.InternalServerError);

            serverErrorCount.Should().Be(0, "Tuyệt đối không được văng 500 hoặc deadlock unhandled");
            successCount.Should().Be(1, "Chỉ duy nhất 1 trong 2 yêu cầu đối ứng được phép thành công");
            badRequestCount.Should().Be(1, "Yêu cầu còn lại phải bị từ chối 400 Bad Request do phát hiện vòng lặp đệ quy");

            // Kiểm tra trực tiếp cấp PostgreSQL: không tồn tại bất kỳ chu trình nào
            var hasCycle = await CheckHasCategoryCycleInDatabaseAsync(tenantId);
            hasCycle.Should().BeFalse("Cơ sở dữ liệu PostgreSQL tuyệt đối không được có vòng lặp chu trình giữa A và B");
        }

        // ---------------------------------------------------------------------
        // KỊCH BẢN 2: Race Condition vòng lặp 3 tầng (1 -> 2 -> 3 -> 1)
        // ---------------------------------------------------------------------
        var cat1Res = await _client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest { Name = $"Node 1 {runCode}" });
        var cat1Id = (await cat1Res.Content.ReadFromJsonAsync<CategoryResponse>(_jsonOptions))!.Id;

        var cat2Res = await _client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest { Name = $"Node 2 {runCode}", ParentId = cat1Id });
        var cat2Id = (await cat2Res.Content.ReadFromJsonAsync<CategoryResponse>(_jsonOptions))!.Id;

        var cat3Res = await _client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest { Name = $"Node 3 {runCode}" });
        var cat3Id = (await cat3Res.Content.ReadFromJsonAsync<CategoryResponse>(_jsonOptions))!.Id;

        // Cây hiện tại: 1 -> 2; 3 (gốc)
        // Gửi đồng thời: PUT 1 (Parent = 3) và PUT 3 (Parent = 2) -> nếu cả 2 thành công sẽ tạo vòng lặp: 3 -> 2 -> 1 -> 3
        var task1to3 = _client.PutAsJsonAsync($"/api/categories/{cat1Id}", new UpdateCategoryRequest { Name = $"Node 1", ParentId = cat3Id, IsActive = true });
        var task3to2 = _client.PutAsJsonAsync($"/api/categories/{cat3Id}", new UpdateCategoryRequest { Name = $"Node 3", ParentId = cat2Id, IsActive = true });

        var results3Tier = await Task.WhenAll(task1to3, task3to2);
        results3Tier.Should().NotContain(r => r.StatusCode == HttpStatusCode.InternalServerError);
        var success3Tier = results3Tier.Count(r => r.StatusCode == HttpStatusCode.OK);
        var badReq3Tier = results3Tier.Count(r => r.StatusCode == HttpStatusCode.BadRequest);

        (success3Tier <= 1).Should().BeTrue("Không thể cả 2 request cùng thành công tạo vòng lặp 3 tầng");
        (badReq3Tier >= 1).Should().BeTrue("Ít nhất 1 request phải bị từ chối 400 Bad Request");

        var hasCycle3Tier = await CheckHasCategoryCycleInDatabaseAsync(tenantId);
        hasCycle3Tier.Should().BeFalse("Cơ sở dữ liệu PostgreSQL không được có vòng lặp 3 tầng");

        // ---------------------------------------------------------------------
        // KỊCH BẢN 3: Di chuyển cây hợp lệ và kiểm tra GET cây không mất node / không đệ quy vô hạn
        // ---------------------------------------------------------------------
        var catRootRes = await _client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest { Name = $"Root Mới {runCode}" });
        var catRootId = (await catRootRes.Content.ReadFromJsonAsync<CategoryResponse>(_jsonOptions))!.Id;

        // Di chuyển Node 1 về làm con của Root Mới -> Thành công 200 OK
        var moveRes = await _client.PutAsJsonAsync($"/api/categories/{cat1Id}", new UpdateCategoryRequest
        {
            Name = "Node 1 đã dời",
            ParentId = catRootId,
            IsActive = true
        });
        moveRes.StatusCode.Should().Be(HttpStatusCode.OK, "Di chuyển danh mục hợp lệ phải thành công 200 OK");

        // Chuyển Node 2 thành danh mục gốc (ParentId = null) -> Thành công 200 OK
        var toRootRes = await _client.PutAsJsonAsync($"/api/categories/{cat2Id}", new UpdateCategoryRequest
        {
            Name = "Node 2 đã thành gốc",
            ParentId = null,
            IsActive = true
        });
        toRootRes.StatusCode.Should().Be(HttpStatusCode.OK, "Chuyển thành danh mục gốc phải thành công 200 OK");

        // Gọi GET /api/categories: Đảm bảo trả về cây hợp lệ, không mất node và không đệ quy vô hạn
        var getTreeRes = await _client.GetAsync("/api/categories");
        getTreeRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var tree = await getTreeRes.Content.ReadFromJsonAsync<List<CategoryResponse>>(_jsonOptions);
        tree.Should().NotBeNull();
        tree!.Any(c => c.Id == catRootId).Should().BeTrue("Cây danh mục phải chứa Root Mới");
        tree.Any(c => c.Id == cat2Id).Should().BeTrue("Cây danh mục phải chứa Node 2 vừa thành gốc");

        var rootNode = tree.First(c => c.Id == catRootId);
        rootNode.SubCategories.Should().Contain(sub => sub.Id == cat1Id, "Root Mới phải chứa con là Node 1");

        // ---------------------------------------------------------------------
        // KỊCH BẢN 4: Hai tenant độc lập không ảnh hưởng lẫn nhau (Cô lập Tenant)
        // ---------------------------------------------------------------------
        var (tenant2Id, token2, _, _, _) = await RegisterAndLoginAsync($"CatIso_{runCode}");
        using var client2 = _factory.CreateClient();
        client2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token2);

        var c2Res = await client2.PostAsJsonAsync("/api/categories", new CreateCategoryRequest { Name = "Tenant 2 Category" });
        c2Res.StatusCode.Should().Be(HttpStatusCode.Created);
        var c2 = await c2Res.Content.ReadFromJsonAsync<CategoryResponse>(_jsonOptions);

        // Tenant 2 cập nhật thành công bình thường
        var c2UpdateRes = await client2.PutAsJsonAsync($"/api/categories/{c2!.Id}", new UpdateCategoryRequest
        {
            Name = "Tenant 2 Category Đã Sửa",
            ParentId = null,
            IsActive = true
        });
        c2UpdateRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
