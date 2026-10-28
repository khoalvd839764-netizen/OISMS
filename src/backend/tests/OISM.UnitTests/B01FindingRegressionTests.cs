using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Orders.Services;
using OISM.Application.Features.StockCounts.Dtos;
using OISM.Application.Features.StockTransfers.Dtos;
using OISM.Domain.Entities;
using Xunit;

namespace OISM.UnitTests;

/// <summary>
/// Unit tests kiểm chứng hồi quy 5 finding của đợt S3-ADMIN-B01 (B01-F01..F04, PR26-F04).
/// </summary>
public class B01FindingRegressionTests
{
    [Theory]
    [InlineData("Shopee")]
    [InlineData("TikTok")]
    [InlineData("Lazada")]
    [InlineData("POS")]
    public async Task B01_F01_CreateOrder_NonAdminChannel_ShouldThrow_ValidationError_400(string channel)
    {
        // Arrange
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var orderService = new OrderService(context, mockTenant.Object);

        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Q1", IsActive = true };
        var warehouse = new Warehouse { Id = Guid.NewGuid(), TenantId = tenantId, BranchId = branch.Id, Name = "Kho Q1", IsActive = true };
        var product = new Product { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Sản phẩm A", IsActive = true };
        var sku = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = product.Id,
            SKU = "SKU-TEST-01",
            RetailPrice = 100000,
            IsActive = true,
            Product = product
        };

        context.Branches.Add(branch);
        context.Warehouses.Add(warehouse);
        context.Products.Add(product);
        context.Skus.Add(sku);
        await context.SaveChangesAsync();

        var requestDto = new CreateOrderRequestDto
        {
            BranchId = branch.Id,
            Channel = channel,
            RequestKey = Guid.NewGuid(),
            CustomerName = "Khách hàng sàn",
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = sku.Id, SkuCode = sku.SKU, Quantity = 1, UnitPrice = 100000 }
            }
        };

        // Act & Assert
        var act = () => orderService.CreateOrderAsync(requestDto, Guid.NewGuid());
        var ex = await act.Should().ThrowAsync<OismException>();
        ex.Which.ErrorCode.Should().Be("VALIDATION_ERROR");
        ex.Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task B01_F01_CreateOrder_AdminChannel_ShouldSucceed_AndReserveStock()
    {
        // Arrange
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var orderService = new OrderService(context, mockTenant.Object);

        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Q1", IsActive = true };
        var warehouse = new Warehouse { Id = Guid.NewGuid(), TenantId = tenantId, BranchId = branch.Id, Name = "Kho Q1", IsActive = true };
        var product = new Product { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Sản phẩm B", IsActive = true };
        var sku = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = product.Id,
            SKU = "SKU-ADMIN-01",
            RetailPrice = 150000,
            IsActive = true,
            Product = product
        };

        context.Branches.Add(branch);
        context.Warehouses.Add(warehouse);
        context.Products.Add(product);
        context.Skus.Add(sku);
        context.InventoryBalances.Add(new InventoryBalance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            SkuId = sku.Id,
            OnHand = 50,
            Reserved = 0,
            AverageCost = 80000
        });
        context.WarehouseInventoryBalances.Add(new WarehouseInventoryBalance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            WarehouseId = warehouse.Id,
            SkuId = sku.Id,
            OnHand = 50,
            Reserved = 0
        });
        await context.SaveChangesAsync();

        var requestDto = new CreateOrderRequestDto
        {
            BranchId = branch.Id,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            CustomerName = "Khách hàng Admin",
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = sku.Id, SkuCode = sku.SKU, Quantity = 2, UnitPrice = 150000 }
            }
        };

        // Act
        var result = await orderService.CreateOrderAsync(requestDto, Guid.NewGuid());

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be("Reserved");
        result.TotalAmount.Should().Be(300000);
        result.Channel.Should().Be("Admin");

        var balance = await context.InventoryBalances.FirstAsync(b => b.BranchId == branch.Id && b.SkuId == sku.Id);
        balance.Reserved.Should().Be(2);
    }

    [Fact]
    public async Task B01_F02_IdempotencyReplay_SamePayload_ShouldSucceed_DifferentPayload_ShouldThrow_409()
    {
        // Arrange
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var orderService = new OrderService(context, mockTenant.Object);

        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Q1", IsActive = true };
        var warehouse = new Warehouse { Id = Guid.NewGuid(), TenantId = tenantId, BranchId = branch.Id, Name = "Kho Q1", IsActive = true };
        var product = new Product { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Sản phẩm C", IsActive = true };
        var sku = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = product.Id,
            SKU = "SKU-REPLAY-01",
            RetailPrice = 200000,
            IsActive = true,
            Product = product
        };

        context.Branches.Add(branch);
        context.Warehouses.Add(warehouse);
        context.Products.Add(product);
        context.Skus.Add(sku);
        context.InventoryBalances.Add(new InventoryBalance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            SkuId = sku.Id,
            OnHand = 20,
            Reserved = 0,
            AverageCost = 100000
        });
        context.WarehouseInventoryBalances.Add(new WarehouseInventoryBalance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            WarehouseId = warehouse.Id,
            SkuId = sku.Id,
            OnHand = 20,
            Reserved = 0
        });
        await context.SaveChangesAsync();

        var requestKey = Guid.NewGuid();
        var initialDto = new CreateOrderRequestDto
        {
            BranchId = branch.Id,
            Channel = "Admin",
            RequestKey = requestKey,
            CustomerName = "Khách Ban Đầu",
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = sku.Id, SkuCode = sku.SKU, Quantity = 1, UnitPrice = 200000 }
            }
        };

        // 1. First execution -> Created
        var firstResult = await orderService.CreateOrderAsync(initialDto, Guid.NewGuid());
        firstResult.Should().NotBeNull();
        firstResult.IsReplay.Should().BeFalse();

        // 2. Exact same replay -> Returns cached result safely without re-reserving stock
        var replayResult = await orderService.CreateOrderAsync(initialDto, Guid.NewGuid());
        replayResult.Should().NotBeNull();
        replayResult.Id.Should().Be(firstResult.Id);
        replayResult.IsReplay.Should().BeTrue();

        // 3. Different payload with same key (e.g. price changed to 300,000) -> Throws 409
        var conflictingDto = new CreateOrderRequestDto
        {
            BranchId = branch.Id,
            Channel = "Admin",
            RequestKey = requestKey, // Same key!
            CustomerName = "Khách Ban Đầu",
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = sku.Id, SkuCode = sku.SKU, Quantity = 1, UnitPrice = 300000 } // Price mismatch!
            }
        };

        var actConflict = () => orderService.CreateOrderAsync(conflictingDto, Guid.NewGuid());
        var conflictEx = await actConflict.Should().ThrowAsync<OismException>();
        conflictEx.Which.StatusCode.Should().Be(409);
    }

    [Fact]
    public void PR26_F04_FilterRequestDtos_ShouldHaveCorrectPaginationDefaults()
    {
        var countFilter = new StockCountFilterRequestDto();
        countFilter.PageNumber.Should().Be(1);
        countFilter.PageSize.Should().Be(20);

        var transferFilter = new StockTransferFilterRequestDto();
        transferFilter.PageNumber.Should().Be(1);
        transferFilter.PageSize.Should().Be(20);
    }
}
