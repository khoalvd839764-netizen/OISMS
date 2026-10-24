using FluentAssertions;
using OISM.Application.Features.Reports.Dtos;
using OISM.Application.Features.Reports.Services;
using OISM.Domain.Entities;
using Xunit;

namespace OISM.UnitTests;

public class ReportServiceTests
{
    [Fact]
    public async Task GetRevenueProfitReport_ShouldCalculateAccurateGrossProfit_AndBreakdowns()
    {
        // Arrange
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var reportService = new ReportService(context, mockTenant.Object);

        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Quận 1" };
        var sku1 = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-01" };
        var sku2 = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-02" };

        context.Branches.Add(branch);
        context.Skus.AddRange(sku1, sku2);

        // Đơn 1: Kênh Shopee, Doanh thu 500,000, Giá vốn 300,000 => Lãi 200,000
        var order1 = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "ORD-01",
            BranchId = branch.Id,
            Channel = "Shopee",
            Status = "Confirmed",
            TotalAmount = 500000,
            CreatedAt = DateTimeOffset.UtcNow
        };
        order1.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order1.Id,
            SkuId = sku1.Id,
            Quantity = 2,
            UnitPrice = 250000,
            CostPrice = 150000 // 2 * 150k = 300k
        });

        // Đơn 2: Kênh POS, Doanh thu 200,000, Giá vốn 100,000 => Lãi 100,000
        var order2 = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "ORD-02",
            BranchId = branch.Id,
            Channel = "POS",
            Status = "Completed",
            TotalAmount = 200000,
            CreatedAt = DateTimeOffset.UtcNow
        };
        order2.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order2.Id,
            SkuId = sku2.Id,
            Quantity = 1,
            UnitPrice = 200000,
            CostPrice = 100000 // 1 * 100k = 100k
        });

        // Đơn 3: Draft (chưa duyệt, không được tính vào doanh thu)
        var order3 = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "ORD-03",
            BranchId = branch.Id,
            Channel = "Admin",
            Status = "Draft",
            TotalAmount = 999999,
            CreatedAt = DateTimeOffset.UtcNow
        };

        context.Orders.AddRange(order1, order2, order3);
        await context.SaveChangesAsync();

        // Act
        var report = await reportService.GetRevenueProfitReportAsync(new ReportFilterDto());

        // Assert
        report.TotalNetRevenue.Should().Be(700000); // 500k + 200k
        report.TotalCostOfGoodsSold.Should().Be(400000); // 300k + 100k
        report.TotalGrossProfit.Should().Be(300000); // 700k - 400k
        report.GrossMarginPercentage.Should().BeApproximately(42.86m, 0.01m); // 300 / 700 * 100
        report.TotalOrders.Should().Be(2);

        report.ByChannel.Should().HaveCount(2);
        var shopeeChannel = report.ByChannel.First(c => c.Channel == "Shopee");
        shopeeChannel.Revenue.Should().Be(500000);
        shopeeChannel.CostOfGoodsSold.Should().Be(300000);
        shopeeChannel.GrossProfit.Should().Be(200000);
    }

    [Fact]
    public async Task GetInventoryVelocityReport_ShouldCalculateTotalValue_AndRankBestSellers()
    {
        // Arrange
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var reportService = new ReportService(context, mockTenant.Object);

        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Kho Tổng" };
        var product = new Product { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Tai nghe Bluetooth" };
        var skuBest = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, ProductId = product.Id, SKU = "EAR-PRO", Product = product };
        var skuSlow = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, ProductId = product.Id, SKU = "EAR-BASIC", Product = product };

        context.Branches.Add(branch);
        context.Products.Add(product);
        context.Skus.AddRange(skuBest, skuSlow);

        context.InventoryBalances.AddRange(
            new InventoryBalance
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BranchId = branch.Id,
                SkuId = skuBest.Id,
                OnHand = 50,
                Reserved = 10,
                AverageCost = 300000
            },
            new InventoryBalance
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BranchId = branch.Id,
                SkuId = skuSlow.Id,
                OnHand = 80,
                Reserved = 0,
                AverageCost = 100000
            }
        );

        // Đơn hàng bán 15 cái skuBest
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "ORD-VELOCITY",
            BranchId = branch.Id,
            Status = "Confirmed",
            TotalAmount = 7500000,
            CreatedAt = DateTimeOffset.UtcNow
        };
        order.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            SkuId = skuBest.Id,
            Quantity = 15,
            UnitPrice = 500000,
            CostPrice = 300000
        });

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        // Act
        var report = await reportService.GetInventoryVelocityReportAsync(new ReportFilterDto());

        // Assert
        // Total Value: 50 * 300k + 80 * 100k = 15,000,000 + 8,000,000 = 23,000,000
        report.TotalInventoryValue.Should().Be(23000000);
        report.TotalOnHandQuantity.Should().Be(130);
        report.TotalReservedQuantity.Should().Be(10);
        report.TotalAvailableQuantity.Should().Be(120);

        report.BestSellers.Should().NotBeEmpty();
        report.BestSellers[0].SkuId.Should().Be(skuBest.Id);
        report.BestSellers[0].QuantitySold.Should().Be(15);

        report.SlowMovingItems.Should().NotBeEmpty();
        report.SlowMovingItems.Any(s => s.SkuId == skuSlow.Id && s.QuantitySold == 0).Should().BeTrue();
    }

    [Fact]
    public async Task GetStockAlerts_ShouldClassifyAlertLevels_Correctly()
    {
        // Arrange
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var reportService = new ReportService(context, mockTenant.Object);

        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh 1" };
        var product = new Product { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Sản phẩm test tồn kho" };
        var sku1 = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, ProductId = product.Id, SKU = "SKU-OUT", Product = product };
        var sku2 = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, ProductId = product.Id, SKU = "SKU-CRITICAL", Product = product };
        var sku3 = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, ProductId = product.Id, SKU = "SKU-WARNING", Product = product };
        var skuSafe = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, ProductId = product.Id, SKU = "SKU-SAFE", Product = product };

        context.Branches.Add(branch);
        context.Products.Add(product);
        context.Skus.AddRange(sku1, sku2, sku3, skuSafe);

        context.InventoryBalances.AddRange(
            // Out of stock: OnHand 2, Reserved 2 => Available 0 <= 0
            new InventoryBalance
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BranchId = branch.Id,
                SkuId = sku1.Id,
                OnHand = 2,
                Reserved = 2,
                ReorderPoint = 10
            },
            // Critical: ReorderPoint 20, Available 5 (<= 50% * 20 = 10)
            new InventoryBalance
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BranchId = branch.Id,
                SkuId = sku2.Id,
                OnHand = 5,
                Reserved = 0,
                ReorderPoint = 20
            },
            // Warning: ReorderPoint 20, Available 15 (> 10 && <= 20)
            new InventoryBalance
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BranchId = branch.Id,
                SkuId = sku3.Id,
                OnHand = 15,
                Reserved = 0,
                ReorderPoint = 20
            },
            // Safe: ReorderPoint 10, Available 50 (> 10) -> Không tạo cảnh báo
            new InventoryBalance
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BranchId = branch.Id,
                SkuId = skuSafe.Id,
                OnHand = 50,
                Reserved = 0,
                ReorderPoint = 10
            }
        );
        await context.SaveChangesAsync();

        // Act
        var alerts = await reportService.GetStockAlertsAsync(branch.Id);

        // Assert
        alerts.Should().HaveCount(3);
        alerts.Any(a => a.SkuId == skuSafe.Id).Should().BeFalse();

        var outAlert = alerts.First(a => a.SkuId == sku1.Id);
        outAlert.AlertLevel.Should().Be("OutOfStock");
        outAlert.Available.Should().Be(0);

        var critAlert = alerts.First(a => a.SkuId == sku2.Id);
        critAlert.AlertLevel.Should().Be("Critical");
        critAlert.Available.Should().Be(5);

        var warnAlert = alerts.First(a => a.SkuId == sku3.Id);
        warnAlert.AlertLevel.Should().Be("Warning");
        warnAlert.Available.Should().Be(15);
    }
}
