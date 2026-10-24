using FluentAssertions;
using OISM.Application.Features.Orders.Services;
using OISM.Application.Features.Simulator.Dtos;
using OISM.Application.Features.Simulator.Services;
using OISM.Domain.Entities;
using Xunit;

namespace OISM.UnitTests;

public class SimulatorServiceTests
{
    [Theory]
    [InlineData("shopee", "Shopee")]
    [InlineData("tiktok", "TikTok")]
    [InlineData("lazada", "Lazada")]
    public async Task IngestWebhook_SupportedPlatforms_ShouldMapToCanonicalOrder(string channelInput, string expectedChannel)
    {
        // Arrange
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var orderService = new OrderService(context, mockTenant.Object);
        var simulatorService = new SimulatorService(context, mockTenant.Object, orderService);

        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Marketplace" };
        var product = new Product { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Bàn phím cơ không dây" };
        var sku = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = product.Id,
            SKU = "KB-MECH-RGB",
            Barcode = "8931234567890",
            RetailPrice = 1200000,
            Product = product
        };

        context.Branches.Add(branch);
        context.Products.Add(product);
        context.Skus.Add(sku);
        await context.SaveChangesAsync();

        var payload = new WebhookOrderPayloadDto
        {
            ExternalOrderId = $"SP-{Guid.NewGuid():N}",
            ExternalShopId = "SHOP-VN-001",
            BranchId = branch.Id,
            CustomerName = "Trần Thị B",
            CustomerPhone = "0987654321",
            ShippingAddress = "Số 456 Lê Duẩn, Đà Nẵng",
            Items = new List<WebhookOrderItemPayloadDto>
            {
                new() { SkuCode = "KB-MECH-RGB", Quantity = 2, UnitPrice = 1100000 }
            }
        };

        // Act
        var result = await simulatorService.IngestWebhookOrderAsync(channelInput, payload, Guid.NewGuid());

        // Assert
        result.Should().NotBeNull();
        result.Channel.Should().Be(expectedChannel);
        result.Status.Should().Be("Reserved");
        result.ExternalOrderId.Should().Be(payload.ExternalOrderId);
        result.TotalAmount.Should().Be(2200000);
        result.Items.Should().HaveCount(1);
        result.Items[0].SkuCode.Should().Be("KB-MECH-RGB");
    }

    [Fact]
    public async Task IngestWebhook_DuplicateExternalOrderId_ShouldReturnExistingOrder_Idempotent()
    {
        // Arrange
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var orderService = new OrderService(context, mockTenant.Object);
        var simulatorService = new SimulatorService(context, mockTenant.Object, orderService);

        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Kho Hà Nội" };
        var product = new Product { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chuột Gaming Logitech" };
        var sku = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = product.Id,
            SKU = "MOUSE-G102",
            RetailPrice = 400000,
            Product = product
        };

        context.Branches.Add(branch);
        context.Products.Add(product);
        context.Skus.Add(sku);
        await context.SaveChangesAsync();

        var payload = new WebhookOrderPayloadDto
        {
            ExternalOrderId = "DUPLICATE-ORDER-999",
            BranchId = branch.Id,
            CustomerName = "Lê Văn C",
            Items = new List<WebhookOrderItemPayloadDto>
            {
                new() { SkuCode = "MOUSE-G102", Quantity = 1, UnitPrice = 400000 }
            }
        };

        // Act 1: Lần đầu tiên
        var firstResult = await simulatorService.IngestWebhookOrderAsync("Shopee", payload, Guid.NewGuid());

        // Act 2: Bắn lại webhook cùng ExternalOrderId
        var secondResult = await simulatorService.IngestWebhookOrderAsync("Shopee", payload, Guid.NewGuid());

        // Assert
        secondResult.Id.Should().Be(firstResult.Id);
        secondResult.Number.Should().Be(firstResult.Number);
        context.Orders.Count(o => o.ExternalOrderId == "DUPLICATE-ORDER-999").Should().Be(1);
    }
}
