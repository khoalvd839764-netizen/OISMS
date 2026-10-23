using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.RegisterTenant;
using OISM.Application.Features.Catalog.Brands;
using OISM.Application.Features.Catalog.Categories;
using OISM.Application.Features.Catalog.Products;
using OISM.Application.Features.Pos;
using Xunit;

namespace OISM.IntegrationTests;

public class CatalogAndProductIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public CatalogAndProductIntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<string> GetOwnerTokenAsync(string tenantCode, string email)
    {
        var regRequest = new RegisterTenantRequest
        {
            TenantName = $"Tenant {tenantCode}",
            TenantCode = tenantCode,
            FullName = "Admin Owner",
            Email = email,
            Password = "Password@123"
        };
        await _client.PostAsJsonAsync("/api/auth/register-tenant", regRequest);

        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = "Password@123"
        });
        var authData = await loginRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        return authData!.AccessToken;
    }

    [Fact]
    public async Task TC_PROD_01_DuplicateSKU_ShouldReturnConflict_InSameTenant()
    {
        // Arrange
        var token = await GetOwnerTokenAsync("SHOP_SKU", "owner_sku@shop.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Tạo Danh mục & Thương hiệu
        var catRes = await _client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest { Name = "Thời trang Nam" });
        var cat = await catRes.Content.ReadFromJsonAsync<CategoryResponse>(_jsonOptions);

        var brandRes = await _client.PostAsJsonAsync("/api/brands", new CreateBrandRequest { Name = "Coolmate" });
        var brand = await brandRes.Content.ReadFromJsonAsync<BrandResponse>(_jsonOptions);

        // Tạo Sản phẩm 1 với SKU: AO-THUN-DEN-L
        var prod1 = new CreateProductRequest
        {
            Name = "Áo Thun Nam Cổ Tròn",
            CategoryId = cat!.Id,
            BrandId = brand!.Id,
            Variants = new List<CreateVariantRequest>
            {
                new()
                {
                    SKU = "AO-THUN-DEN-L",
                    VariantName = "Đen Size L",
                    RetailPrice = 199000,
                    WholesalePrice = 150000,
                    Color = "Đen",
                    Size = "L"
                }
            }
        };
        var prod1Res = await _client.PostAsJsonAsync("/api/products", prod1);
        prod1Res.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act: Tạo Sản phẩm 2 hoặc biến thể cố tình dùng lại SKU: AO-THUN-DEN-L trong cùng Tenant
        var prod2 = new CreateProductRequest
        {
            Name = "Áo Thun Nam Cổ Tim",
            CategoryId = cat.Id,
            BrandId = brand.Id,
            Variants = new List<CreateVariantRequest>
            {
                new()
                {
                    SKU = "AO-THUN-DEN-L", // Trùng SKU
                    VariantName = "Đen Size L (Trùng)",
                    RetailPrice = 220000
                }
            }
        };
        var prod2Res = await _client.PostAsJsonAsync("/api/products", prod2);

        // Assert: Bắt buộc trả về 409 Conflict theo kịch bản TC-PROD-01
        prod2Res.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Category_ShouldRejectCyclicHierarchy()
    {
        // Arrange
        var token = await GetOwnerTokenAsync("SHOP_CAT", "owner_cat@shop.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 1. Tạo Danh mục Cha (Quần Áo)
        var parentRes = await _client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest { Name = "Quần Áo" });
        var parent = await parentRes.Content.ReadFromJsonAsync<CategoryResponse>(_jsonOptions);

        // 2. Tạo Danh mục Con (Áo Nam có ParentId là Quần Áo)
        var childRes = await _client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest
        {
            Name = "Áo Nam",
            ParentId = parent!.Id
        });
        var child = await childRes.Content.ReadFromJsonAsync<CategoryResponse>(_jsonOptions);

        // 3. Act: Cập nhật Danh mục Cha (Quần Áo) nhận Con (Áo Nam) làm Cha -> Vòng lặp đệ quy!
        var updateRes = await _client.PutAsJsonAsync($"/api/categories/{parent.Id}", new UpdateCategoryRequest
        {
            Name = "Quần Áo",
            ParentId = child!.Id,
            IsActive = true
        });

        // Assert: Bị chặn và trả về 400 Bad Request
        updateRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PosCatalog_SearchAndBarcode_ShouldReturnExactProduct()
    {
        // Arrange
        var token = await GetOwnerTokenAsync("SHOP_POS", "owner_pos@shop.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var prodRes = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Giày Thể Thao Sneaker Pro",
            Variants = new List<CreateVariantRequest>
            {
                new()
                {
                    SKU = "SNEAKER-PRO-42",
                    VariantName = "Sneaker Pro Trắng Size 42",
                    Barcode = "8938501234567",
                    RetailPrice = 750000,
                    Color = "Trắng",
                    Size = "42"
                }
            }
        });
        prodRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act 1: Tìm kiếm theo từ khóa "sneaker"
        var searchRes = await _client.GetAsync("/api/pos/search?query=sneaker");
        searchRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var searchItems = await searchRes.Content.ReadFromJsonAsync<List<PosProductSearchResponse>>(_jsonOptions);
        searchItems.Should().NotBeNull();
        searchItems!.Should().Contain(v => v.SKU == "SNEAKER-PRO-42");

        // Act 2: Quét mã vạch Barcode Scanner
        var barcodeRes = await _client.GetAsync("/api/pos/barcode/8938501234567");
        barcodeRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var barcodeItem = await barcodeRes.Content.ReadFromJsonAsync<PosProductSearchResponse>(_jsonOptions);
        barcodeItem.Should().NotBeNull();
        barcodeItem!.SKU.Should().Be("SNEAKER-PRO-42");
        barcodeItem.RetailPrice.Should().Be(750000);
    }
}
