using FluentAssertions;
using OISM.Application.Common.Exceptions;
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

        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Marketplace", IsActive = true };
        var product = new Product { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Bàn phím cơ không dây", IsActive = true };
        var sku = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = product.Id,
            SKU = "KB-MECH-RGB",
            Barcode = "8931234567890",
            RetailPrice = 1200000,
            IsActive = true,
            Product = product
        };
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            Name = "Kho Marketplace",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        context.Branches.Add(branch);
        context.Warehouses.Add(warehouse);
        context.Products.Add(product);
        context.Skus.Add(sku);
        context.ShopIntegrations.Add(new ShopIntegration
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Channel = expectedChannel,
            ExternalShopId = "SHOP-VN-001",
            BranchId = branch.Id,
            IsActive = true,
            SecretHash = "dummy-secret"
        });
        context.InventoryBalances.Add(new InventoryBalance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            SkuId = sku.Id,
            OnHand = 100,
            Reserved = 0
        });
        context.WarehouseInventoryBalances.Add(new WarehouseInventoryBalance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            WarehouseId = warehouse.Id,
            SkuId = sku.Id,
            OnHand = 100,
            Reserved = 0
        });
        await context.SaveChangesAsync();

        var payload = new WebhookOrderPayloadDto
        {
            ExternalOrderId = $"SP-{Guid.NewGuid():N}",
            ExternalShopId = "SHOP-VN-001",
            BranchId = branch.Id,
            CustomerName = "Trần Thị B",
            CustomerPhone = "0987654321",
            ShippingAddress = "Số 456 Lê Duẩn, Đà Nẵng",
            OrderTimestamp = DateTimeOffset.UtcNow,
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

        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Kho Hà Nội", IsActive = true };
        var product = new Product { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chuột Gaming Logitech", IsActive = true };
        var sku = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = product.Id,
            SKU = "MOUSE-G102",
            RetailPrice = 400000,
            IsActive = true,
            Product = product
        };
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            Name = "Kho Hà Nội 1",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        context.Branches.Add(branch);
        context.Warehouses.Add(warehouse);
        context.Products.Add(product);
        context.Skus.Add(sku);
        context.ShopIntegrations.Add(new ShopIntegration
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Channel = "Shopee",
            ExternalShopId = "SHOP-VN-002",
            BranchId = branch.Id,
            IsActive = true,
            SecretHash = "dummy-secret"
        });
        context.InventoryBalances.Add(new InventoryBalance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            SkuId = sku.Id,
            OnHand = 100,
            Reserved = 0
        });
        context.WarehouseInventoryBalances.Add(new WarehouseInventoryBalance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            WarehouseId = warehouse.Id,
            SkuId = sku.Id,
            OnHand = 100,
            Reserved = 0
        });
        await context.SaveChangesAsync();

        var payload = new WebhookOrderPayloadDto
        {
            ExternalOrderId = "DUPLICATE-ORDER-999",
            ExternalShopId = "SHOP-VN-002",
            BranchId = branch.Id,
            CustomerName = "Lê Văn C",
            OrderTimestamp = DateTimeOffset.UtcNow,
            Items = new List<WebhookOrderItemPayloadDto>
            {
                new() { SkuCode = "MOUSE-G102", Quantity = 1, UnitPrice = 400000 }
            }
        };

        // Act 1: Lần đầu tiên
        var firstResult = await simulatorService.IngestWebhookOrderAsync("Shopee", payload, Guid.NewGuid());

        // Act 2: Bắn lại webhook cùng ExternalOrderId và cùng nội dung
        var secondResult = await simulatorService.IngestWebhookOrderAsync("Shopee", payload, Guid.NewGuid());

        // Assert
        secondResult.Id.Should().Be(firstResult.Id);
        secondResult.Number.Should().Be(firstResult.Number);
        context.Orders.Count(o => o.ExternalOrderId == "DUPLICATE-ORDER-999").Should().Be(1);
    }

    [Fact]
    public async Task IngestWebhook_SameTotalChangedItems_ShouldThrowPayloadMismatchException()
    {
        // Arrange
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var orderService = new OrderService(context, mockTenant.Object);
        var simulatorService = new SimulatorService(context, mockTenant.Object, orderService);

        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Kho Hà Nội", IsActive = true };
        var product = new Product { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chuột Gaming Logitech", IsActive = true };
        var sku = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = product.Id,
            SKU = "MOUSE-G102",
            RetailPrice = 400000,
            IsActive = true,
            Product = product
        };
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            Name = "Kho Hà Nội 1",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        context.Branches.Add(branch);
        context.Warehouses.Add(warehouse);
        context.Products.Add(product);
        context.Skus.Add(sku);
        context.ShopIntegrations.Add(new ShopIntegration
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Channel = "Shopee",
            ExternalShopId = "SHOP-VN-003",
            BranchId = branch.Id,
            IsActive = true,
            SecretHash = "dummy-secret"
        });
        context.InventoryBalances.Add(new InventoryBalance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            SkuId = sku.Id,
            OnHand = 100,
            Reserved = 0
        });
        context.WarehouseInventoryBalances.Add(new WarehouseInventoryBalance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            WarehouseId = warehouse.Id,
            SkuId = sku.Id,
            OnHand = 100,
            Reserved = 0
        });
        await context.SaveChangesAsync();

        var fixedTimestamp = DateTimeOffset.UtcNow;
        var payload1 = new WebhookOrderPayloadDto
        {
            ExternalOrderId = "SAME-TOTAL-ORDER-01",
            ExternalShopId = "SHOP-VN-003",
            BranchId = branch.Id,
            CustomerName = "Nguyễn Văn A",
            OrderTimestamp = fixedTimestamp,
            Items = new List<WebhookOrderItemPayloadDto>
            {
                new() { SkuCode = "MOUSE-G102", Quantity = 1, UnitPrice = 100 }
            }
        };

        // Act 1: Lần đầu tiên quantity 1, price 100 -> tổng 100
        var firstResult = await simulatorService.IngestWebhookOrderAsync("Shopee", payload1, Guid.NewGuid());
        firstResult.Should().NotBeNull();

        // Act 2: Bắn lại với quantity 2, price 50 -> tổng vẫn 100 nhưng thay đổi mặt hàng
        var payload2 = new WebhookOrderPayloadDto
        {
            ExternalOrderId = "SAME-TOTAL-ORDER-01",
            ExternalShopId = "SHOP-VN-003",
            BranchId = branch.Id,
            CustomerName = "Nguyễn Văn A",
            OrderTimestamp = fixedTimestamp,
            Items = new List<WebhookOrderItemPayloadDto>
            {
                new() { SkuCode = "MOUSE-G102", Quantity = 2, UnitPrice = 50 }
            }
        };

        Func<Task> act = async () => await simulatorService.IngestWebhookOrderAsync("Shopee", payload2, Guid.NewGuid());
        await act.Should().ThrowAsync<OismException>()
            .Where(ex => ex.ErrorCode == "WEBHOOK_PAYLOAD_MISMATCH");
    }

    [Fact]
    public async Task IngestWebhook_UnconfiguredShop_ShouldThrowIntegrationNotFound()
    {
        // Arrange
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var orderService = new OrderService(context, mockTenant.Object);
        var simulatorService = new SimulatorService(context, mockTenant.Object, orderService);

        var payload = new WebhookOrderPayloadDto
        {
            ExternalOrderId = "ORDER-NO-SHOP-1",
            ExternalShopId = "UNCONFIGURED-SHOP-XYZ",
            BranchId = Guid.NewGuid(),
            OrderTimestamp = DateTimeOffset.UtcNow,
            Items = new List<WebhookOrderItemPayloadDto>
            {
                new() { SkuCode = "SKU-TEST", Quantity = 1, UnitPrice = 100000 }
            }
        };

        Func<Task> act = async () => await simulatorService.IngestWebhookOrderAsync("Shopee", payload, Guid.NewGuid());
        await act.Should().ThrowAsync<OismException>()
            .Where(ex => ex.ErrorCode == "INTEGRATION_NOT_FOUND");
    }

    [Fact]
    public async Task IngestWebhook_MissingTimestamp_MustThrowValidationError_WithoutMutatingPayload()
    {
        // Arrange
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var orderService = new OrderService(context, mockTenant.Object);
        var simulatorService = new SimulatorService(context, mockTenant.Object, orderService);

        var payload = new WebhookOrderPayloadDto
        {
            ExternalOrderId = "MISSING-TS-ORDER",
            ExternalShopId = "SHOP-VN-001",
            BranchId = Guid.NewGuid(),
            OrderTimestamp = null,
            Items = new List<WebhookOrderItemPayloadDto>
            {
                new() { SkuCode = "SKU-TEST", Quantity = 1, UnitPrice = 100000 }
            }
        };

        // Act
        var act = async () => await simulatorService.IngestWebhookOrderAsync("Shopee", payload, Guid.NewGuid());

        // Assert
        var ex = await act.Should().ThrowAsync<OismException>();
        ex.Where(e => e.ErrorCode == "VALIDATION_ERROR" && e.StatusCode == 400);
        payload.OrderTimestamp.Should().BeNull("Simulator adapter không được tự gán timestamp ngầm vào payload đối số");
    }

    [Fact]
    public async Task IngestWebhook_DefaultTimestamp_MustThrowValidationError()
    {
        // Arrange
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var orderService = new OrderService(context, mockTenant.Object);
        var simulatorService = new SimulatorService(context, mockTenant.Object, orderService);

        var payload = new WebhookOrderPayloadDto
        {
            ExternalOrderId = "DEFAULT-TS-ORDER",
            ExternalShopId = "SHOP-VN-001",
            BranchId = Guid.NewGuid(),
            OrderTimestamp = default(DateTimeOffset),
            Items = new List<WebhookOrderItemPayloadDto>
            {
                new() { SkuCode = "SKU-TEST", Quantity = 1, UnitPrice = 100000 }
            }
        };

        // Act
        var act = async () => await simulatorService.IngestWebhookOrderAsync("Shopee", payload, Guid.NewGuid());

        // Assert
        var ex = await act.Should().ThrowAsync<OismException>();
        ex.Where(e => e.ErrorCode == "VALIDATION_ERROR" && e.StatusCode == 400);
    }

    [Fact]
    public async Task IngestWebhook_NullItemInList_MustThrowValidationError_NotThrowNullReferenceException()
    {
        // Arrange
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var orderService = new OrderService(context, mockTenant.Object);
        var simulatorService = new SimulatorService(context, mockTenant.Object, orderService);

        var payload = new WebhookOrderPayloadDto
        {
            ExternalOrderId = "NULL-ITEM-ORDER",
            ExternalShopId = "SHOP-VN-001",
            BranchId = Guid.NewGuid(),
            OrderTimestamp = DateTimeOffset.UtcNow,
            Items = new List<WebhookOrderItemPayloadDto> { null! }
        };

        // Act
        var act = async () => await simulatorService.IngestWebhookOrderAsync("Shopee", payload, Guid.NewGuid());

        // Assert: Ném OismException 400 chuẩn hóa thay vì NullReferenceException 500
        var ex = await act.Should().ThrowAsync<OismException>();
        ex.Where(e => e.ErrorCode == "VALIDATION_ERROR" && e.StatusCode == 400);
    }
}
