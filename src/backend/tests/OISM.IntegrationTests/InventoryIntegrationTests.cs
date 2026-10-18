using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.RegisterTenant;
using OISM.Application.Features.Branches;
using OISM.Application.Features.Catalog.Brands;
using OISM.Application.Features.Catalog.Categories;
using OISM.Application.Features.Catalog.Products;
using OISM.Application.Features.Inventory;
using OISM.Application.Features.Suppliers;
using OISM.Application.Features.Warehouses;
using Xunit;

namespace OISM.IntegrationTests;

public class InventoryIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public InventoryIntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<string> GetOwnerTokenAsync(string tenantCode, string email)
    {
        var regRequest = new RegisterTenantRequest
        {
            TenantName = $"Tenant {tenantCode}",
            TenantCode = tenantCode,
            FullName = "Inventory Owner",
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
    public async Task GoodsReceipt_MultiLine_And_WAC_And_AppendOnlyLedger_ShouldWorkEndToEnd()
    {
        // Arrange: Đăng ký Tenant & lấy JWT Owner
        var token = await GetOwnerTokenAsync("SHOP_INV_01", "owner_inv01@shop.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 1. Tạo Chi nhánh, Kho vật lý & Nhà cung cấp
        var branchRes = await _client.PostAsJsonAsync("/api/branches", new CreateBranchRequest
        {
            Name = "Chi nhánh Quận 1",
            Address = "123 Nguyễn Huệ, Q1"
        });
        branchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var branch = await branchRes.Content.ReadFromJsonAsync<BranchResponse>(_jsonOptions);

        var whRes = await _client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest
        {
            BranchId = branch!.Id,
            Name = "Kho Chính Quận 1"
        });
        whRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var warehouse = await whRes.Content.ReadFromJsonAsync<WarehouseResponse>(_jsonOptions);

        var supRes = await _client.PostAsJsonAsync("/api/suppliers", new CreateSupplierRequest
        {
            Name = "Nhà phân phối Hòa Phát",
            ContactName = "Nguyễn Văn A",
            Phone = "0901234567"
        });
        supRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var supplier = await supRes.Content.ReadFromJsonAsync<SupplierResponse>(_jsonOptions);

        // 2. Tạo Danh mục, Thương hiệu & Sản phẩm 2 biến thể SKU
        var catRes = await _client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest { Name = "Áo Sơ Mi" });
        var cat = await catRes.Content.ReadFromJsonAsync<CategoryResponse>(_jsonOptions);

        var brandRes = await _client.PostAsJsonAsync("/api/brands", new CreateBrandRequest { Name = "Owen" });
        var brand = await brandRes.Content.ReadFromJsonAsync<BrandResponse>(_jsonOptions);

        var prodRes = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Áo Sơ Mi Bamboo Dài Tay",
            CategoryId = cat!.Id,
            BrandId = brand!.Id,
            Variants = new List<CreateVariantRequest>
            {
                new()
                {
                    SKU = "SOMI-TRANG-M",
                    VariantName = "Trắng Size M",
                    RetailPrice = 350000,
                    WholesalePrice = 280000,
                    Color = "Trắng",
                    Size = "M"
                },
                new()
                {
                    SKU = "SOMI-XANH-L",
                    VariantName = "Xanh Size L",
                    RetailPrice = 350000,
                    WholesalePrice = 280000,
                    Color = "Xanh",
                    Size = "L"
                }
            }
        });
        prodRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var productsListRes = await _client.GetAsync("/api/products");
        var productsList = await productsListRes.Content.ReadFromJsonAsync<List<ProductResponse>>(_jsonOptions);
        var product = productsList!.First(p => p.Name == "Áo Sơ Mi Bamboo Dài Tay");
        var sku1 = product.Variants.First(v => v.SKU == "SOMI-TRANG-M");
        var sku2 = product.Variants.First(v => v.SKU == "SOMI-XANH-L");

        // 3. Tạo Phiếu nhập kho nhiều dòng (Lần 1) và Xác nhận nhập kho:
        // - SKU 1: 10 cái x 100,000đ
        // - SKU 2: 5 cái x 200,000đ
        var createReceipt1Res = await _client.PostAsJsonAsync("/api/inventory/receipts", new CreateGoodsReceiptRequest
        {
            WarehouseId = warehouse!.Id,
            SupplierId = supplier!.Id,
            Note = "Nhập hàng đợt 1",
            Lines = new List<CreateGoodsReceiptLineRequest>
            {
                new() { SkuId = sku1.Id, Quantity = 10, UnitCost = 100000 },
                new() { SkuId = sku2.Id, Quantity = 5, UnitCost = 200000 }
            }
        });
        createReceipt1Res.StatusCode.Should().Be(HttpStatusCode.Created);
        var receipt1Draft = await createReceipt1Res.Content.ReadFromJsonAsync<GoodsReceiptResponse>(_jsonOptions);
        receipt1Draft!.Status.Should().Be("Draft");
        receipt1Draft.TotalAmount.Should().Be(2000000);
        receipt1Draft.Lines.Should().HaveCount(2);

        var confirm1Res = await _client.PostAsync($"/api/inventory/receipts/{receipt1Draft.Id}/confirm", null);
        confirm1Res.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirm1Data = await confirm1Res.Content.ReadFromJsonAsync<ConfirmGoodsReceiptResponse>(_jsonOptions);
        var receipt1Confirmed = confirm1Data!.Receipt;
        receipt1Confirmed.Status.Should().Be("Confirmed");

        // 4. Tạo Phiếu nhập kho Lần 2 và Xác nhận:
        // - SKU 1: 10 cái x 200,000đ -> Kỳ vọng WAC mới của SKU 1 = (10*100,000 + 10*200,000) / 20 = 150,000đ
        var createReceipt2Res = await _client.PostAsJsonAsync("/api/inventory/receipts", new CreateGoodsReceiptRequest
        {
            WarehouseId = warehouse.Id,
            SupplierId = supplier.Id,
            Note = "Nhập hàng đợt 2",
            Lines = new List<CreateGoodsReceiptLineRequest>
            {
                new() { SkuId = sku1.Id, Quantity = 10, UnitCost = 200000 }
            }
        });
        createReceipt2Res.StatusCode.Should().Be(HttpStatusCode.Created);
        var receipt2Draft = await createReceipt2Res.Content.ReadFromJsonAsync<GoodsReceiptResponse>(_jsonOptions);
        receipt2Draft!.Status.Should().Be("Draft");

        var confirm2Res = await _client.PostAsync($"/api/inventory/receipts/{receipt2Draft.Id}/confirm", null);
        confirm2Res.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirm2Data = await confirm2Res.Content.ReadFromJsonAsync<ConfirmGoodsReceiptResponse>(_jsonOptions);
        var receipt2Confirmed = confirm2Data!.Receipt;
        receipt2Confirmed.Status.Should().Be("Confirmed");

        // Cố tình xác nhận lại lần nữa phải bị chặn 400 BadRequest
        var duplicateConfirmRes = await _client.PostAsync($"/api/inventory/receipts/{receipt2Draft.Id}/confirm", null);
        duplicateConfirmRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 5. Kiểm tra Tồn kho & Giá vốn bình quân gia quyền (WAC)
        var balancesRes = await _client.GetAsync($"/api/inventory/balances?warehouseId={warehouse.Id}");
        balancesRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var balances = await balancesRes.Content.ReadFromJsonAsync<List<InventoryBalanceResponse>>(_jsonOptions);

        var balSku1 = balances!.First(b => b.SkuId == sku1.Id);
        balSku1.OnHand.Should().Be(20);
        balSku1.Available.Should().Be(20);
        balSku1.AverageCost.Should().Be(150000); // (10*100k + 10*200k) / 20
        balSku1.TotalValue.Should().Be(3000000);

        var balSku2 = balances!.First(b => b.SkuId == sku2.Id);
        balSku2.OnHand.Should().Be(5);
        balSku2.AverageCost.Should().Be(200000);

        // 6. Cập nhật định mức cảnh báo tồn tối thiểu (Reorder Point) cho SKU 2 lên 15
        var reorderRes = await _client.PutAsJsonAsync("/api/inventory/balances/reorder-point", new UpdateReorderPointRequest
        {
            BranchId = branch.Id,
            SkuId = sku2.Id,
            ReorderPoint = 15
        });
        reorderRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var balancesAfterReorder = await (await _client.GetAsync($"/api/inventory/balances?warehouseId={warehouse.Id}"))
            .Content.ReadFromJsonAsync<List<InventoryBalanceResponse>>(_jsonOptions);
        balancesAfterReorder!.First(b => b.SkuId == sku2.Id).ReorderPoint.Should().Be(15);

        // 7. Kiểm tra Sổ cái kho bất biến (Append-Only Inventory Ledger)
        var ledgerRes = await _client.GetAsync($"/api/inventory/ledger?warehouseId={warehouse.Id}&search=SOMI-TRANG-M");
        ledgerRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var ledgerEntries = await ledgerRes.Content.ReadFromJsonAsync<List<InventoryLedgerEntryResponse>>(_jsonOptions);

        ledgerEntries.Should().HaveCount(2);

        // Mới nhất xếp trước
        var latestTx = ledgerEntries![0];
        latestTx.Direction.Should().Be("IN");
        latestTx.OperationType.Should().Be("GoodsReceipt");
        latestTx.QuantityDelta.Should().Be(10);
        latestTx.BalanceAfter.Should().Be(20);
        latestTx.ReferenceCode.Should().Be(receipt2Confirmed.Code);

        var firstTx = ledgerEntries[1];
        firstTx.Direction.Should().Be("IN");
        firstTx.QuantityDelta.Should().Be(10);
        firstTx.BalanceAfter.Should().Be(10);
        firstTx.ReferenceCode.Should().Be(receipt1Confirmed.Code);
    }
}
