using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Orders.Services;
using OISM.Domain.Entities;
using Xunit;

namespace OISM.UnitTests;

public class OrderServiceTests
{
    [Fact]
    public async Task CreateOrder_ShouldCalculateTotalAmount_AndCreateSnapshots()
    {
        // Arrange
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var orderService = new OrderService(context, mockTenant.Object);

        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Quận 1", IsActive = true };
        var product = new Product { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Áo Thun Polo Cao Cấp", IsActive = true };
        var sku = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = product.Id,
            SKU = "POLO-BLK-L",
            RetailPrice = 250000,
            IsActive = true,
            Product = product
        };
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            Name = "Kho Quận 1",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
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
            OnHand = 100,
            AverageCost = 150000
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

        var dto = new CreateOrderDto
        {
            BranchId = branch.Id,
            Channel = "Admin",
            CustomerName = "Nguyễn Văn A",
            CustomerPhone = "0901234567",
            Items = new List<CreateOrderItemRequestDto>
            {
                new() { SkuId = sku.Id, Quantity = 3, UnitPrice = 250000 }
            }
        };

        // Act
        var result = await orderService.CreateOrderAsync(dto, Guid.NewGuid());

        // Assert
        result.Should().NotBeNull();
        result.TotalAmount.Should().Be(750000);
        result.Status.Should().Be("Reserved");
        result.Items.Should().HaveCount(1);
        result.Items[0].SkuCode.Should().Be("POLO-BLK-L");
        result.Items[0].CostPrice.Should().BeNull();
    }

    [Fact]
    public async Task GetOrders_WithFilter_ShouldFilterByStatusAndChannel()
    {
        // Arrange
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var orderService = new OrderService(context, mockTenant.Object);

        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Thủ Đức", IsActive = true };
        context.Branches.Add(branch);

        context.Orders.AddRange(
            new Order
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Number = "ORD-001",
                BranchId = branch.Id,
                Channel = "Shopee",
                Status = "Confirmed",
                TotalAmount = 500000,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new Order
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Number = "ORD-002",
                BranchId = branch.Id,
                Channel = "TikTok",
                Status = "Draft",
                TotalAmount = 300000,
                CreatedAt = DateTimeOffset.UtcNow
            }
        );
        await context.SaveChangesAsync();

        // Act - Lọc theo kênh Shopee
        var resultShopee = await orderService.GetOrdersAsync(new OrderFilterDto { Channel = "Shopee" });

        // Assert
        resultShopee.Items.Should().HaveCount(1);
        resultShopee.Items[0].Number.Should().Be("ORD-001");
        resultShopee.Items[0].Channel.Should().Be("Shopee");

        // Act - Lọc theo status Draft
        var resultDraft = await orderService.GetOrdersAsync(new OrderFilterDto { Status = "Draft" });
        resultDraft.Items.Should().HaveCount(1);
        resultDraft.Items[0].Number.Should().Be("ORD-002");
    }

    [Fact]
    public async Task ConfirmOrder_ShouldDeductOnHand_AndCreateOutLedgerTransaction()
    {
        // Arrange
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var orderService = new OrderService(context, mockTenant.Object);

        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh 1", IsActive = true };
        var sku = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "JEAN-BLU-30", RetailPrice = 400000, IsActive = true };
        var warehouse = new Warehouse { Id = Guid.NewGuid(), TenantId = tenantId, BranchId = branch.Id, Name = "Kho 1", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };

        var balance = new InventoryBalance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            SkuId = sku.Id,
            OnHand = 50,
            Reserved = 2,
            AverageCost = 200000
        };

        var whBalance = new WarehouseInventoryBalance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            WarehouseId = warehouse.Id,
            SkuId = sku.Id,
            OnHand = 50,
            Reserved = 2
        };

        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "ORD-CONFIRM-TEST",
            BranchId = branch.Id,
            Channel = "Admin",
            Status = "Reserved",
            TotalAmount = 800000,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var orderItem = new OrderItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            SkuId = sku.Id,
            Quantity = 2,
            UnitPrice = 400000
        };
        order.Items.Add(orderItem);

        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            OrderItemId = orderItem.Id,
            BranchId = branch.Id,
            WarehouseId = warehouse.Id,
            SkuId = sku.Id,
            Quantity = 2,
            Status = "Active",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        order.Reservations.Add(reservation);

        context.Branches.Add(branch);
        context.Warehouses.Add(warehouse);
        context.Skus.Add(sku);
        context.InventoryBalances.Add(balance);
        context.WarehouseInventoryBalances.Add(whBalance);
        context.Orders.Add(order);
        context.Reservations.Add(reservation);
        await context.SaveChangesAsync();

        // Act
        var confirmed = await orderService.ConfirmOrderAsync(order.Id, Guid.NewGuid());

        // Assert
        confirmed.Status.Should().Be("Confirmed");
        confirmed.ConfirmedAt.Should().NotBeNull();

        var updatedBalance = await context.InventoryBalances.AsNoTracking().FirstOrDefaultAsync(b => b.Id == balance.Id);
        updatedBalance!.OnHand.Should().Be(48); // 50 - 2
        updatedBalance.Reserved.Should().Be(0); // 2 - 2

        var updatedReservation = await context.Reservations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reservation.Id);
        updatedReservation!.Status.Should().Be("Consumed");

        var outTransaction = await context.InventoryTransactions.AsNoTracking().FirstOrDefaultAsync(t => t.OrderItemId == orderItem.Id);
        outTransaction.Should().NotBeNull();
        outTransaction!.Type.Should().Be("OUT");
        outTransaction.Quantity.Should().Be(2);
        outTransaction.UnitCost.Should().Be(200000);
    }

    [Fact]
    public async Task CancelOrder_ShouldReleaseReservation_AndRestoreReservedStock()
    {
        // Arrange
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var orderService = new OrderService(context, mockTenant.Object);

        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh 1", IsActive = true };
        var sku = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SHIRT-WHT", RetailPrice = 300000, IsActive = true };
        var warehouse = new Warehouse { Id = Guid.NewGuid(), TenantId = tenantId, BranchId = branch.Id, Name = "Kho 1", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };

        var balance = new InventoryBalance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            SkuId = sku.Id,
            OnHand = 20,
            Reserved = 5
        };

        var whBalance = new WarehouseInventoryBalance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            WarehouseId = warehouse.Id,
            SkuId = sku.Id,
            OnHand = 20,
            Reserved = 5
        };

        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "ORD-CANCEL-TEST",
            BranchId = branch.Id,
            Channel = "Admin",
            Status = "Reserved",
            CreatedAt = DateTimeOffset.UtcNow
        };

        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            BranchId = branch.Id,
            WarehouseId = warehouse.Id,
            SkuId = sku.Id,
            Quantity = 5,
            Status = "Active",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        order.Reservations.Add(reservation);

        context.Branches.Add(branch);
        context.Warehouses.Add(warehouse);
        context.Skus.Add(sku);
        context.InventoryBalances.Add(balance);
        context.WarehouseInventoryBalances.Add(whBalance);
        context.Orders.Add(order);
        context.Reservations.Add(reservation);
        await context.SaveChangesAsync();

        // Act
        var cancelled = await orderService.CancelOrderAsync(order.Id, Guid.NewGuid());

        // Assert
        cancelled.Status.Should().Be("Cancelled");
        cancelled.CancelledAt.Should().NotBeNull();

        var updatedReservation = await context.Reservations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reservation.Id);
        updatedReservation!.Status.Should().Be("Released");

        var updatedBalance = await context.InventoryBalances.AsNoTracking().FirstOrDefaultAsync(b => b.Id == balance.Id);
        updatedBalance!.Reserved.Should().Be(0); // 5 - 5
    }
}
