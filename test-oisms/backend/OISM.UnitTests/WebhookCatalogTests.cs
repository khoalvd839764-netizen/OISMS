using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.Orders.Services;
using OISM.Application.Features.Webhooks.Dtos;
using OISM.Application.Features.Webhooks.Services;
using OISM.Domain.Entities;
using Xunit;

namespace OISM.UnitTests;

/// <summary>
/// GET /api/webhooks/simulator/catalog: secret chỉ thấy SKU đang bán của doanh nghiệp mình,
/// tồn có thể bán tại chi nhánh của kết nối, giá bán lẻ hiện hành, không có giá vốn.
/// </summary>
public class WebhookCatalogTests
{
    private const string Secret = "whsec_unit_catalog";

    private sealed record Seed(
        WebhookService Service,
        OISM.Infrastructure.Persistence.AppDbContext Context,
        ShopIntegration Integration,
        Tenant Tenant);

    private static async Task<Seed> SetupAsync()
    {
        var tenant = new Tenant { Name = "Shop A", Code = "shop-a", IsActive = true };
        var otherTenant = new Tenant { Name = "Shop B", Code = "shop-b", IsActive = true };
        var (context, tenantMock, _) = TestDbContextFactory.CreateInMemoryContext(tenant.Id);
        // Guard chặn tham chiếu chéo tenant theo tenant đang đăng nhập, nên seed shop B dưới tenant của shop B.
        var currentTenantId = otherTenant.Id;
        tenantMock.Setup(t => t.TenantId).Returns(() => currentTenantId);

        var branch = new Branch { TenantId = tenant.Id, Name = "CN Quận 1", IsActive = true };
        var otherBranch = new Branch { TenantId = tenant.Id, Name = "CN Quận 3", IsActive = true };
        var integration = new ShopIntegration
        {
            TenantId = tenant.Id,
            BranchId = branch.Id,
            Channel = "Shopee",
            ExternalShopId = "SPE_1",
            IsActive = true,
            SecretHash = WebhookSecretHasher.HashSecret(Secret)
        };

        var piece = new Unit { TenantId = tenant.Id, Code = "CAI", Name = "Cái", Symbol = "cái" };
        var retail = new PriceList { TenantId = tenant.Id, Code = "retail", Name = "Bán lẻ" };

        var shirt = new Product { TenantId = tenant.Id, Name = "Áo thun", IsActive = true };
        var mug = new Product { TenantId = tenant.Id, Name = "Cốc sứ", IsActive = true };
        var stopped = new Product { TenantId = tenant.Id, Name = "Hàng ngừng bán", IsActive = false };

        var shirtRed = new ProductVariant { TenantId = tenant.Id, ProductId = shirt.Id, SKU = "ao-do", BaseUnitId = piece.Id, RetailPrice = 1m, VariantName = "Đỏ" };
        var shirtOff = new ProductVariant { TenantId = tenant.Id, ProductId = shirt.Id, SKU = "AO-XANH", BaseUnitId = piece.Id, IsActive = false };
        var mugSku = new ProductVariant { TenantId = tenant.Id, ProductId = mug.Id, SKU = "COC-1", RetailPrice = 45000m, Unit = "cái" };
        var stoppedSku = new ProductVariant { TenantId = tenant.Id, ProductId = stopped.Id, SKU = "NGUNG-1" };

        var foreignProduct = new Product { TenantId = otherTenant.Id, Name = "Hàng shop B", IsActive = true };
        var foreignSku = new ProductVariant { TenantId = otherTenant.Id, ProductId = foreignProduct.Id, SKU = "B-1" };

        using (context.OpenPrivilegedScope())
        {
            context.Tenants.Add(otherTenant);
            context.Products.Add(foreignProduct);
            context.ProductVariants.Add(foreignSku);
            await context.SaveChangesAsync();
        }
        currentTenantId = tenant.Id;

        using (context.OpenPrivilegedScope())
        {
            context.Tenants.Add(tenant);
            context.Branches.AddRange(branch, otherBranch);
            context.ShopIntegrations.Add(integration);
            context.Units.Add(piece);
            context.PriceLists.Add(retail);
            context.Products.AddRange(shirt, mug, stopped);
            context.ProductVariants.AddRange(shirtRed, shirtOff, mugSku, stoppedSku);
            context.SkuUnitPrices.AddRange(
                new SkuUnitPrice { TenantId = tenant.Id, PriceListId = retail.Id, SkuId = shirtRed.Id, UnitId = piece.Id, Price = 120000m, ValidFrom = DateTimeOffset.UtcNow.AddDays(-2) },
                new SkuUnitPrice { TenantId = tenant.Id, PriceListId = retail.Id, SkuId = shirtRed.Id, UnitId = piece.Id, Price = 150000m, ValidFrom = DateTimeOffset.UtcNow.AddDays(-1) },
                new SkuUnitPrice { TenantId = tenant.Id, PriceListId = retail.Id, SkuId = shirtRed.Id, UnitId = piece.Id, Price = 999000m, ValidFrom = DateTimeOffset.UtcNow.AddDays(3) });
            context.InventoryBalances.AddRange(
                new InventoryBalance { TenantId = tenant.Id, BranchId = branch.Id, SkuId = shirtRed.Id, OnHand = 10m, Reserved = 3m, AverageCost = 70000m },
                new InventoryBalance { TenantId = tenant.Id, BranchId = otherBranch.Id, SkuId = mugSku.Id, OnHand = 50m, Reserved = 0m });
            await context.SaveChangesAsync();
        }
        context.ChangeTracker.Clear();

        var service = new WebhookService(context, Mock.Of<IOrderService>(), tenantMock.Object, NullLogger<WebhookService>.Instance);
        return new Seed(service, context, integration, tenant);
    }

    [Fact]
    public async Task ValidSecret_ReturnsActiveSkusOfOwnTenant_WithBranchStockAndCurrentRetailPrice()
    {
        var seed = await SetupAsync();

        var catalog = await seed.Service.GetCatalogAsync($" {Secret} ");

        catalog.Select(c => c.SkuCode).Should().Equal("AO-DO", "COC-1");

        var shirt = catalog[0];
        shirt.ProductName.Should().Be("Áo thun");
        shirt.VariantName.Should().Be("Đỏ");
        shirt.UnitName.Should().Be("Cái");
        shirt.UnitPrice.Should().Be(150000m, "lấy giá bán lẻ hiện hành, bỏ giá tương lai");
        shirt.Available.Should().Be(7m, "OnHand 10 - Reserved 3 tại chi nhánh của kết nối");

        var mug = catalog[1];
        mug.VariantName.Should().BeNull();
        mug.UnitName.Should().Be("cái");
        mug.UnitPrice.Should().Be(45000m, "chưa có bảng giá thì dùng giá bán lẻ trên SKU");
        mug.Available.Should().Be(0m, "tồn ở chi nhánh khác không tính");
    }

    [Fact]
    public void CatalogItem_DoesNotExposeCostOrInternalIds()
    {
        typeof(WebhookCatalogItemDto).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo(["SkuCode", "ProductName", "VariantName", "UnitName", "UnitPrice", "Available"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("whsec_wrong")]
    public async Task MissingOrWrongSecret_Returns401(string? secret)
    {
        var seed = await SetupAsync();

        var act = () => seed.Service.GetCatalogAsync(secret);

        var ex = await act.Should().ThrowAsync<OismException>();
        ex.Which.StatusCode.Should().Be(401);
        ex.Which.ErrorCode.Should().Be("UNAUTHORIZED");
    }

    [Fact]
    public async Task InactiveIntegration_Returns401()
    {
        var seed = await SetupAsync();
        seed.Integration.IsActive = false;
        using (seed.Context.OpenPrivilegedScope())
        {
            seed.Context.Update(seed.Integration);
            await seed.Context.SaveChangesAsync();
        }
        seed.Context.ChangeTracker.Clear();

        var act = () => seed.Service.GetCatalogAsync(Secret);

        (await act.Should().ThrowAsync<OismException>()).Which.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task InactiveTenant_Returns401()
    {
        var seed = await SetupAsync();
        seed.Tenant.IsActive = false;
        using (seed.Context.OpenPrivilegedScope())
        {
            seed.Context.Update(seed.Tenant);
            await seed.Context.SaveChangesAsync();
        }
        seed.Context.ChangeTracker.Clear();

        var act = () => seed.Service.GetCatalogAsync(Secret);

        (await act.Should().ThrowAsync<OismException>()).Which.Message.Should().Contain("ngừng hoạt động");
    }
}
