using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Features.Search.Services;
using OISM.Domain.Entities;
using Xunit;

namespace OISM.UnitTests;

public class GlobalSearchTests
{
    [Fact]
    public async Task SearchAsync_FindsMatchingSkuAndOrderAndReceipt()
    {
        // Arrange
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Quận 1" };
        var warehouse = new Warehouse { Id = Guid.NewGuid(), TenantId = tenantId, BranchId = branch.Id, Name = "Kho Tổng" };
        var supplier = new Supplier { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Nhà cung cấp May 10" };
        var product = new Product { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Áo khoác gió thời trang" };
        var sku = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, ProductId = product.Id, SKU = "AKG-01", Barcode = "893123456789" };

        context.Branches.Add(branch);
        context.Warehouses.Add(warehouse);
        context.Suppliers.Add(supplier);
        context.Products.Add(product);
        context.Skus.Add(sku);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "ORD-AKG-999",
            BranchId = branch.Id,
            CustomerName = "Nguyễn Khoác Gió",
            TotalAmount = 450000,
            Status = "Completed"
        };
        context.Orders.Add(order);

        var receipt = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "GR-AKG-100",
            BranchId = branch.Id,
            WarehouseId = warehouse.Id,
            SupplierId = supplier.Id,
            Status = "Confirmed"
        };
        context.GoodsReceipts.Add(receipt);

        await context.SaveChangesAsync();

        var searchService = new SearchService(context, tenantMock.Object);

        // Act
        var result = await searchService.SearchAsync("AKG", userRole: "Owner");

        // Assert
        result.Should().NotBeNull();
        result.Products.Should().HaveCount(1);
        result.Products[0].Title.Should().Be("Áo khoác gió thời trang");

        result.Orders.Should().HaveCount(1);
        result.Orders[0].Title.Should().Be("Đơn hàng ORD-AKG-999");

        result.GoodsReceipts.Should().HaveCount(1);
        result.GoodsReceipts[0].Title.Should().Be("Phiếu nhập GR-AKG-100");
    }

    [Fact]
    public async Task SearchAsync_CashierRole_HidesGoodsReceipts()
    {
        // Arrange
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Quận 1" };
        var warehouse = new Warehouse { Id = Guid.NewGuid(), TenantId = tenantId, BranchId = branch.Id, Name = "Kho Tổng" };
        var supplier = new Supplier { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Nhà cung cấp Test" };

        context.Branches.Add(branch);
        context.Warehouses.Add(warehouse);
        context.Suppliers.Add(supplier);

        context.GoodsReceipts.Add(new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "GR-SECRET-01",
            BranchId = branch.Id,
            WarehouseId = warehouse.Id,
            SupplierId = supplier.Id,
            Status = "Confirmed"
        });
        await context.SaveChangesAsync();

        var searchService = new SearchService(context, tenantMock.Object);

        // Act: Tìm kiếm với vai trò Thu ngân (Cashier)
        var result = await searchService.SearchAsync("SECRET", userRole: "Cashier");

        // Assert: Cashier bị chặn xem Phiếu nhập kho (bảo mật giá vốn & nhà cung cấp)
        result.GoodsReceipts.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmptyResponse()
    {
        // Arrange
        var (context, tenantMock, _) = TestDbContextFactory.CreateInMemoryContext();
        var searchService = new SearchService(context, tenantMock.Object);

        // Act
        var result = await searchService.SearchAsync("   ", userRole: "Owner");

        // Assert
        result.TotalCount.Should().Be(0);
        result.Products.Should().BeEmpty();
        result.Orders.Should().BeEmpty();
        result.GoodsReceipts.Should().BeEmpty();
    }
}
