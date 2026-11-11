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
    public async Task GetInventoryVelocityReport_ShouldSplitQuantityByBaseUnit()
    {
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var reportService = new ReportService(context, mockTenant.Object);

        var branchA = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Kho A" };
        var branchB = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Kho B" };
        var product = new Product { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Hàng hỗn hợp" };
        var piece = new Unit { Id = Guid.NewGuid(), TenantId = tenantId, Code = "cai", Name = "Cái" };
        var kilogram = new Unit { Id = Guid.NewGuid(), TenantId = tenantId, Code = "kg", Name = "Kg" };

        var skuPiece = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, ProductId = product.Id, SKU = "SKU-CAI", Product = product, BaseUnitId = piece.Id, BaseUnit = piece };
        var skuKg = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, ProductId = product.Id, SKU = "SKU-KG", Product = product, BaseUnitId = kilogram.Id, BaseUnit = kilogram };
        var skuNoUnit = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, ProductId = product.Id, SKU = "SKU-NONE", Product = product };

        context.Branches.AddRange(branchA, branchB);
        context.Products.Add(product);
        context.Units.AddRange(piece, kilogram);
        context.Skus.AddRange(skuPiece, skuKg, skuNoUnit);

        InventoryBalance Balance(ProductVariant sku, Branch branch, decimal onHand, decimal reserved) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            SkuId = sku.Id,
            OnHand = onHand,
            Reserved = reserved,
            AverageCost = 1000
        };

        context.InventoryBalances.AddRange(
            Balance(skuPiece, branchA, 10, 2),
            Balance(skuPiece, branchB, 5, 1),
            Balance(skuKg, branchA, 2.5m, 0.5m),
            Balance(skuNoUnit, branchA, 3, 0));
        await context.SaveChangesAsync();

        var report = await reportService.GetInventoryVelocityReportAsync(new ReportFilterDto());

        report.QuantityByUnit.Should().HaveCount(3);
        report.QuantityByUnit[0].Should().BeEquivalentTo(new UnitQuantityDto { UnitName = "Cái", OnHand = 15, Reserved = 3, Available = 12 });
        report.QuantityByUnit[1].Should().BeEquivalentTo(new UnitQuantityDto { UnitName = "Chưa xác định", OnHand = 3, Reserved = 0, Available = 3 });
        report.QuantityByUnit[2].Should().BeEquivalentTo(new UnitQuantityDto { UnitName = "Kg", OnHand = 2.5m, Reserved = 0.5m, Available = 2 });

        // Lọc theo chi nhánh thì phần tách đơn vị cũng chỉ tính chi nhánh đó
        var branchReport = await reportService.GetInventoryVelocityReportAsync(new ReportFilterDto { BranchId = branchB.Id });
        branchReport.QuantityByUnit.Should().ContainSingle();
        branchReport.QuantityByUnit[0].Should().BeEquivalentTo(new UnitQuantityDto { UnitName = "Cái", OnHand = 5, Reserved = 1, Available = 4 });

        report.SlowMovingItems.Single(s => s.SkuId == skuKg.Id).BaseUnitName.Should().Be("Kg");
        report.SlowMovingItems.Single(s => s.SkuId == skuNoUnit.Id).BaseUnitName.Should().BeNull();
    }

    [Fact]
    public async Task GetInventoryVelocityReport_ShouldSumOnHandAcrossBranches_PerSku()
    {
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var reportService = new ReportService(context, mockTenant.Object);

        var branchA = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Kho A" };
        var branchB = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Kho B" };
        var product = new Product { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Sữa" };
        var skuSold = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, ProductId = product.Id, SKU = "SKU-SOLD", Product = product };
        var skuIdle = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, ProductId = product.Id, SKU = "SKU-IDLE", Product = product };
        var skuEmpty = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, ProductId = product.Id, SKU = "SKU-EMPTY", Product = product };

        context.Branches.AddRange(branchA, branchB);
        context.Products.Add(product);
        context.Skus.AddRange(skuSold, skuIdle, skuEmpty);

        InventoryBalance Balance(ProductVariant sku, Branch branch, decimal onHand) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            SkuId = sku.Id,
            OnHand = onHand,
            AverageCost = 1000
        };

        context.InventoryBalances.AddRange(
            Balance(skuSold, branchA, 18),
            Balance(skuSold, branchB, 3),
            Balance(skuIdle, branchA, 4),
            Balance(skuIdle, branchB, 6),
            Balance(skuEmpty, branchA, 0),
            Balance(skuEmpty, branchB, 0));

        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "ORD-MULTI-BRANCH",
            BranchId = branchA.Id,
            Status = "Confirmed",
            TotalAmount = 2000,
            CreatedAt = DateTimeOffset.UtcNow
        };
        order.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            SkuId = skuSold.Id,
            Quantity = 2,
            UnitPrice = 1000,
            CostPrice = 500
        });
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var report = await reportService.GetInventoryVelocityReportAsync(new ReportFilterDto());

        report.BestSellers.Single(s => s.SkuId == skuSold.Id).CurrentOnHand.Should().Be(21);

        // Mỗi SKU chỉ một dòng, SKU đã hết hàng ở mọi chi nhánh không được liệt kê
        report.SlowMovingItems.Select(s => s.SkuId).Should().OnlyHaveUniqueItems();
        report.SlowMovingItems.Select(s => s.SkuId).Should().NotContain(skuEmpty.Id);
        report.SlowMovingItems.Single(s => s.SkuId == skuIdle.Id).CurrentOnHand.Should().Be(10);
        report.SlowMovingItems.Single(s => s.SkuId == skuSold.Id).CurrentOnHand.Should().Be(21);

        // Lọc một chi nhánh thì chỉ tính tồn của chi nhánh đó
        var branchReport = await reportService.GetInventoryVelocityReportAsync(new ReportFilterDto { BranchId = branchB.Id });
        branchReport.SlowMovingItems.Single(s => s.SkuId == skuSold.Id).CurrentOnHand.Should().Be(3);
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

    [Fact]
    public async Task GetStockAlerts_OutOfStockWithoutThreshold_IsAlertedButStockedWithoutThresholdIsNot()
    {
        // Lỗi người dùng báo 10/11/2026: POLO-NAM-DEN-M tồn 0, ngưỡng 0 nhưng không có cảnh báo.
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Quận 1" };
        var product = new Product { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Áo Polo Nam" };
        var empty = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, ProductId = product.Id, SKU = "POLO-NAM-DEN-M", Product = product };
        var reservedOut = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, ProductId = product.Id, SKU = "POLO-GIU-CHO", Product = product };
        var stocked = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, ProductId = product.Id, SKU = "POLO-CON-HANG", Product = product };
        var inactive = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, ProductId = product.Id, SKU = "POLO-NGUNG", Product = product, IsActive = false };
        context.Branches.Add(branch);
        context.Products.Add(product);
        context.Skus.AddRange(empty, reservedOut, stocked, inactive);
        context.InventoryBalances.AddRange(
            new InventoryBalance { Id = Guid.NewGuid(), TenantId = tenantId, BranchId = branch.Id, SkuId = empty.Id, OnHand = 0, Reserved = 0, ReorderPoint = 0 },
            new InventoryBalance { Id = Guid.NewGuid(), TenantId = tenantId, BranchId = branch.Id, SkuId = reservedOut.Id, OnHand = 3, Reserved = 3, ReorderPoint = 0 },
            new InventoryBalance { Id = Guid.NewGuid(), TenantId = tenantId, BranchId = branch.Id, SkuId = stocked.Id, OnHand = 7, Reserved = 0, ReorderPoint = 0 },
            new InventoryBalance { Id = Guid.NewGuid(), TenantId = tenantId, BranchId = branch.Id, SkuId = inactive.Id, OnHand = 0, Reserved = 0, ReorderPoint = 0 });
        await context.SaveChangesAsync();
        var service = new ReportService(context, mockTenant.Object);

        var alerts = await service.GetStockAlertsAsync(null);
        var summary = await service.GetDashboardSummaryAsync(isOwner: false);

        alerts.Select(a => a.SkuCode).Should().BeEquivalentTo(new[] { "POLO-NAM-DEN-M", "POLO-GIU-CHO" });
        alerts.Should().OnlyContain(a => a.AlertLevel == "OutOfStock" && a.Available == 0 && a.ReorderPoint == 0);
        summary.LowStockCount.Should().Be(2);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_LowStockCount_ExcludesInactiveSku()
    {
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var active = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-ACTIVE", Unit = "piece", IsActive = true };
        var inactive = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-INACTIVE", Unit = "piece", IsActive = false };
        context.Skus.AddRange(active, inactive);
        foreach (var sku in new[] { active, inactive })
        {
            context.InventoryBalances.Add(new InventoryBalance
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BranchId = Guid.NewGuid(),
                SkuId = sku.Id,
                OnHand = 1m,
                Reserved = 0m,
                ReorderPoint = 5m
            });
        }
        await context.SaveChangesAsync();

        var summary = await new ReportService(context, tenantMock.Object).GetDashboardSummaryAsync(isOwner: true);

        summary.LowStockCount.Should().Be(1);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_CalculatesKPIs_AndProtectsProfitWhenNotOwner()
    {
        // Arrange
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var reportService = new ReportService(context, mockTenant.Object);

        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Tân Bình" };
        var sku = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-DASH-01" };

        context.Branches.Add(branch);
        context.Skus.Add(sku);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "ORD-DASH-01",
            BranchId = branch.Id,
            Channel = "POS",
            Status = "Completed",
            TotalAmount = 500000,
            CreatedAt = DateTimeOffset.UtcNow
        };
        order.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            SkuId = sku.Id,
            Quantity = 2,
            UnitPrice = 250000,
            CostPrice = 150000 // 2 * 150k = 300k, Lãi = 200k
        });
        context.Orders.Add(order);

        context.InventoryBalances.Add(new InventoryBalance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            SkuId = sku.Id,
            OnHand = 10,
            AverageCost = 150000,
            ReorderPoint = 5
        });

        await context.SaveChangesAsync();

        // Act 1: Gọi với quyền Owner
        var ownerSummary = await reportService.GetDashboardSummaryAsync(isOwner: true);

        // Assert 1: Owner thấy đủ doanh thu, số đơn, lãi gộp
        ownerSummary.TotalOrders.Should().Be(1);
        ownerSummary.TotalRevenue.Should().Be(500000);
        ownerSummary.TotalGrossProfit.Should().Be(200000);
        ownerSummary.GrossMarginPercentage.Should().Be(40);
        ownerSummary.TotalInventoryValue.Should().Be(1500000); // 10 * 150k
        ownerSummary.RecentOrders.Should().HaveCount(1);
        ownerSummary.RecentOrders[0].Number.Should().Be("ORD-DASH-01");

        // Act 2: Gọi với quyền Staff (isOwner = false)
        var staffSummary = await reportService.GetDashboardSummaryAsync(isOwner: false);

        // Assert 2: Staff bị ẩn lợi nhuận gộp (bảo mật giá vốn)
        staffSummary.TotalGrossProfit.Should().BeNull();
        staffSummary.GrossMarginPercentage.Should().Be(0);
        staffSummary.TotalInventoryValue.Should().BeNull();
        staffSummary.TotalRevenue.Should().Be(500000);
    }

    [Fact]
    public async Task GetRevenueProfitReport_ShouldIncludeSubSecondEndOfDay_AndGroupDailyTrendInVietnamTime()
    {
        // Arrange (BF-011)
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var reportService = new ReportService(context, mockTenant.Object);

        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Q1" };
        var sku = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-VN" };
        context.Branches.Add(branch);
        context.Skus.Add(sku);

        // Đơn 1: Lúc 01:00 sáng ngày 28/10/2026 giờ Việt Nam (= 2026-10-27T18:00:00Z)
        var orderEarlyMorningVn = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "ORD-VN-01",
            BranchId = branch.Id,
            Channel = "Admin",
            Status = "Confirmed",
            TotalAmount = 300000,
            CreatedAt = new DateTimeOffset(2026, 10, 27, 18, 0, 0, TimeSpan.Zero)
        };

        // Đơn 2: Lúc 23:59:59.999 giờ Việt Nam ngày 28/10/2026 (= 2026-10-28T16:59:59.999Z)
        var orderSubSecondEndVn = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "ORD-VN-02",
            BranchId = branch.Id,
            Channel = "Admin",
            Status = "Completed",
            TotalAmount = 450000,
            CreatedAt = new DateTimeOffset(2026, 10, 28, 16, 59, 59, 999, TimeSpan.Zero)
        };

        context.Orders.AddRange(orderEarlyMorningVn, orderSubSecondEndVn);
        await context.SaveChangesAsync();

        // Act: Lọc ngày 28/10/2026 giờ Việt Nam với EndDate gửi dạng 23:59:59.999+07:00 (= 2026-10-28T16:59:59.999Z)
        var filter = new ReportFilterDto
        {
            Channel = "Admin",
            StartDate = new DateTimeOffset(2026, 10, 27, 17, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(2026, 10, 28, 16, 59, 59, 999, TimeSpan.Zero)
        };
        var report = await reportService.GetRevenueProfitReportAsync(filter);

        // Assert
        report.TotalOrders.Should().Be(2);
        report.TotalNetRevenue.Should().Be(750000);
        report.DailyTrend.Should().ContainSingle();
        report.DailyTrend[0].Date.Should().Be("2026-10-28");
    }

    [Fact]
    public async Task GetRevenueProfitReport_WithConfirmedReturns_CalculatesNetNumbersAndBreakdowns()
    {
        // Arrange
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var reportService = new ReportService(context, mockTenant.Object);

        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Quận 1" };
        var sku1 = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-01" };
        context.Branches.Add(branch);
        context.Skus.Add(sku1);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "ORD-01",
            BranchId = branch.Id,
            Channel = "Shopee",
            Status = "Confirmed",
            TotalAmount = 1_000_000m,
            CreatedAt = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.FromHours(7))
        };
        var orderItem = new OrderItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            SkuId = sku1.Id,
            Quantity = 2,
            FactorSnapshot = 1m,
            UnitPrice = 500_000m,
            CostPrice = 300_000m // 2 * 300k = 600k COGS
        };
        order.Items.Add(orderItem);
        context.Orders.Add(order);

        // Phiếu trả confirmed
        var salesReturn = new SalesReturn
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "RT-01",
            OrderId = order.Id,
            Order = order,
            BranchId = branch.Id,
            Branch = branch,
            Status = "Confirmed",
            // Đơn sàn: phiếu không ghi hoàn tiền, báo cáo vẫn trừ doanh thu theo giá trị dòng trả (1 × 500k)
            TotalRefundAmount = 0m,
            ConfirmedAt = new DateTimeOffset(2026, 11, 2, 14, 0, 0, TimeSpan.FromHours(7))
        };
        salesReturn.Items.Add(new SalesReturnItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SalesReturnId = salesReturn.Id,
            OrderId = order.Id,
            OrderItemId = orderItem.Id,
            OrderItem = orderItem,
            SkuId = sku1.Id,
            Quantity = 1,
            RefundAmount = 0m,
            OriginalUnitCost = 300_000m
        });
        context.SalesReturns.Add(salesReturn);

        await context.SaveChangesAsync();

        // Act
        var report = await reportService.GetRevenueProfitReportAsync(new ReportFilterDto());

        // Assert
        report.Revenue.Should().Be(1_000_000m);
        report.ReturnAmount.Should().Be(500_000m);
        report.NetRevenue.Should().Be(500_000m);
        report.TotalNetRevenue.Should().Be(500_000m);
        report.CostOfGoodsSold.Should().Be(600_000m);
        report.ReturnCogs.Should().Be(300_000m);
        report.NetCogs.Should().Be(300_000m);
        report.TotalCostOfGoodsSold.Should().Be(300_000m, "Total* là số thuần: TotalNetRevenue - TotalCostOfGoodsSold = TotalGrossProfit");
        report.GrossProfit.Should().Be(200_000m);
        report.TotalGrossProfit.Should().Be(200_000m);
        report.GrossMarginPercentage.Should().Be(40m);

        report.ByChannel.Should().ContainSingle(c => c.Channel == "Shopee");
        var ch = report.ByChannel.Single(c => c.Channel == "Shopee");
        ch.Revenue.Should().Be(1_000_000m);
        ch.ReturnAmount.Should().Be(500_000m);
        ch.NetRevenue.Should().Be(500_000m);
        ch.CostOfGoodsSold.Should().Be(600_000m);
        ch.ReturnCogs.Should().Be(300_000m);
        ch.NetCogs.Should().Be(300_000m);
        ch.GrossProfit.Should().Be(200_000m);
        ch.GrossMarginPercentage.Should().Be(40m);

        report.ByBranch.Should().ContainSingle(b => b.BranchId == branch.Id);
        var br = report.ByBranch.Single(b => b.BranchId == branch.Id);
        br.Revenue.Should().Be(1_000_000m);
        br.ReturnAmount.Should().Be(500_000m);
        br.NetRevenue.Should().Be(500_000m);
        br.CostOfGoodsSold.Should().Be(600_000m);
        br.ReturnCogs.Should().Be(300_000m);
        br.NetCogs.Should().Be(300_000m);
        br.GrossProfit.Should().Be(200_000m);
    }

    [Fact]
    public async Task GetRevenueProfitReport_WithReturnsInDifferentDate_SeparatesPeriodsAccurately()
    {
        // Arrange
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var reportService = new ReportService(context, mockTenant.Object);

        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Quận 1" };
        var sku1 = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-01" };
        context.Branches.Add(branch);
        context.Skus.Add(sku1);

        // Bán ngày 2026-10-25 (+07:00)
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "ORD-D1",
            BranchId = branch.Id,
            Channel = "POS",
            Status = "Completed",
            TotalAmount = 800_000m,
            CreatedAt = new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.FromHours(7))
        };
        var orderItem = new OrderItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            SkuId = sku1.Id,
            Quantity = 2,
            FactorSnapshot = 1m,
            UnitPrice = 400_000m,
            CostPrice = 250_000m // 2 * 250k = 500k COGS
        };
        order.Items.Add(orderItem);
        context.Orders.Add(order);

        // Trả ngày 2026-10-28 (+07:00)
        var salesReturn = new SalesReturn
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "RT-D2",
            OrderId = order.Id,
            Order = order,
            BranchId = branch.Id,
            Branch = branch,
            Status = "Confirmed",
            TotalRefundAmount = 300_000m,
            ConfirmedAt = new DateTimeOffset(2026, 10, 28, 15, 0, 0, TimeSpan.FromHours(7))
        };
        salesReturn.Items.Add(new SalesReturnItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SalesReturnId = salesReturn.Id,
            OrderId = order.Id,
            OrderItemId = orderItem.Id,
            OrderItem = orderItem,
            SkuId = sku1.Id,
            Quantity = 1,
            RefundAmount = 300_000m,
            OriginalUnitCost = 250_000m
        });
        context.SalesReturns.Add(salesReturn);
        await context.SaveChangesAsync();

        // 1. Kỳ 1: Chỉ ngày 2026-10-25 (Bán)
        var filterDay1 = new ReportFilterDto
        {
            StartDate = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.FromHours(7)),
            EndDate = new DateTimeOffset(2026, 10, 25, 23, 59, 59, 999, TimeSpan.FromHours(7))
        };
        var reportD1 = await reportService.GetRevenueProfitReportAsync(filterDay1);
        reportD1.Revenue.Should().Be(800_000m);
        reportD1.ReturnAmount.Should().Be(0m);
        reportD1.NetRevenue.Should().Be(800_000m);
        reportD1.CostOfGoodsSold.Should().Be(500_000m);
        reportD1.ReturnCogs.Should().Be(0m);
        reportD1.NetCogs.Should().Be(500_000m);
        reportD1.GrossProfit.Should().Be(300_000m);
        reportD1.DailyTrend.Should().ContainSingle(d => d.Date == "2026-10-25");

        // 2. Kỳ 2: Chỉ ngày 2026-10-28 (Trả)
        var filterDay2 = new ReportFilterDto
        {
            StartDate = new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.FromHours(7)),
            EndDate = new DateTimeOffset(2026, 10, 28, 23, 59, 59, 999, TimeSpan.FromHours(7))
        };
        var reportD2 = await reportService.GetRevenueProfitReportAsync(filterDay2);
        reportD2.Revenue.Should().Be(0m);
        reportD2.ReturnAmount.Should().Be(300_000m);
        reportD2.NetRevenue.Should().Be(-300_000m);
        reportD2.CostOfGoodsSold.Should().Be(0m);
        reportD2.ReturnCogs.Should().Be(250_000m);
        reportD2.NetCogs.Should().Be(-250_000m);
        reportD2.GrossProfit.Should().Be(-50_000m); // -300k - (-250k) = -50k
        reportD2.DailyTrend.Should().ContainSingle(d => d.Date == "2026-10-28");

        // 3. Toàn kỳ bao gồm cả 2 ngày
        var filterAll = new ReportFilterDto
        {
            StartDate = new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.FromHours(7)),
            EndDate = new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.FromHours(7))
        };
        var reportAll = await reportService.GetRevenueProfitReportAsync(filterAll);
        reportAll.Revenue.Should().Be(800_000m);
        reportAll.ReturnAmount.Should().Be(300_000m);
        reportAll.NetRevenue.Should().Be(500_000m);
        reportAll.CostOfGoodsSold.Should().Be(500_000m);
        reportAll.ReturnCogs.Should().Be(250_000m);
        reportAll.NetCogs.Should().Be(250_000m);
        reportAll.GrossProfit.Should().Be(250_000m);
        reportAll.DailyTrend.Should().HaveCount(2);
        reportAll.DailyTrend.Select(d => d.Date).Should().BeEquivalentTo(new[] { "2026-10-25", "2026-10-28" });
    }

    [Fact]
    public async Task GetRevenueProfitReport_IgnoresDraftAndCancelledReturns()
    {
        // Arrange
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var reportService = new ReportService(context, mockTenant.Object);

        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Quận 1" };
        var sku1 = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-01" };
        context.Branches.Add(branch);
        context.Skus.Add(sku1);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "ORD-01",
            BranchId = branch.Id,
            Channel = "POS",
            Status = "Confirmed",
            TotalAmount = 500_000m,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var orderItem = new OrderItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            SkuId = sku1.Id,
            Quantity = 5,
            FactorSnapshot = 1m,
            UnitPrice = 100_000m,
            CostPrice = 60_000m // 5 * 60k = 300k
        };
        order.Items.Add(orderItem);
        context.Orders.Add(order);

        // Return 1: Draft
        var returnDraft = new SalesReturn
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "RT-DRAFT",
            OrderId = order.Id,
            Order = order,
            BranchId = branch.Id,
            Status = "Draft",
            TotalRefundAmount = 100_000m
        };
        returnDraft.Items.Add(new SalesReturnItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SalesReturnId = returnDraft.Id,
            OrderId = order.Id,
            OrderItemId = orderItem.Id,
            OrderItem = orderItem,
            SkuId = sku1.Id,
            Quantity = 1,
            RefundAmount = 100_000m,
            OriginalUnitCost = 60_000m
        });

        // Return 2: Cancelled
        var returnCancelled = new SalesReturn
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "RT-CANCEL",
            OrderId = order.Id,
            Order = order,
            BranchId = branch.Id,
            Status = "Cancelled",
            TotalRefundAmount = 150_000m
        };
        returnCancelled.Items.Add(new SalesReturnItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SalesReturnId = returnCancelled.Id,
            OrderId = order.Id,
            OrderItemId = orderItem.Id,
            OrderItem = orderItem,
            SkuId = sku1.Id,
            Quantity = 1,
            RefundAmount = 150_000m,
            OriginalUnitCost = 60_000m
        });

        // Return 3: Confirmed
        var returnConfirmed = new SalesReturn
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "RT-CONFIRM",
            OrderId = order.Id,
            Order = order,
            BranchId = branch.Id,
            Status = "Confirmed",
            TotalRefundAmount = 200_000m,
            ConfirmedAt = DateTimeOffset.UtcNow
        };
        returnConfirmed.Items.Add(new SalesReturnItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SalesReturnId = returnConfirmed.Id,
            OrderId = order.Id,
            OrderItemId = orderItem.Id,
            OrderItem = orderItem,
            SkuId = sku1.Id,
            Quantity = 2,
            RefundAmount = 200_000m,
            OriginalUnitCost = 60_000m
        });

        context.SalesReturns.AddRange(returnDraft, returnCancelled, returnConfirmed);
        await context.SaveChangesAsync();

        // Act
        var report = await reportService.GetRevenueProfitReportAsync(new ReportFilterDto());

        // Assert: Chỉ tính phiếu Confirmed (200k tiền hoàn, 120k giá vốn trả)
        report.Revenue.Should().Be(500_000m);
        report.ReturnAmount.Should().Be(200_000m);
        report.NetRevenue.Should().Be(300_000m);
        report.CostOfGoodsSold.Should().Be(300_000m);
        report.ReturnCogs.Should().Be(120_000m);
        report.NetCogs.Should().Be(180_000m);
        report.GrossProfit.Should().Be(120_000m);
    }

    [Fact]
    public async Task GetRevenueProfitReport_StaffRole_HidesGrossProfitAndCogs()
    {
        // Arrange
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var reportService = new ReportService(context, mockTenant.Object);

        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Quận 1" };
        var sku1 = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-01" };
        context.Branches.Add(branch);
        context.Skus.Add(sku1);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "ORD-STAFF",
            BranchId = branch.Id,
            Channel = "POS",
            Status = "Confirmed",
            TotalAmount = 500_000m,
            CreatedAt = DateTimeOffset.UtcNow
        };
        order.Items.Add(new OrderItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            SkuId = sku1.Id,
            Quantity = 2,
            FactorSnapshot = 1m,
            UnitPrice = 250_000m,
            CostPrice = 150_000m
        });
        context.Orders.Add(order);

        var salesReturn = new SalesReturn
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "RT-STAFF",
            OrderId = order.Id,
            Order = order,
            BranchId = branch.Id,
            Branch = branch,
            Status = "Confirmed",
            TotalRefundAmount = 250_000m,
            ConfirmedAt = DateTimeOffset.UtcNow
        };
        salesReturn.Items.Add(new SalesReturnItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SalesReturnId = salesReturn.Id,
            OrderId = order.Id,
            OrderItemId = order.Items.First().Id,
            OrderItem = order.Items.First(),
            SkuId = sku1.Id,
            Quantity = 1,
            RefundAmount = 250_000m,
            OriginalUnitCost = 150_000m
        });
        context.SalesReturns.Add(salesReturn);
        await context.SaveChangesAsync();

        // Act: Gọi với isOwner = false (Staff)
        var report = await reportService.GetRevenueProfitReportAsync(new ReportFilterDto(), isOwner: false);

        // Assert: Staff thấy doanh thu và hàng trả, nhưng không thấy giá vốn và lãi gộp
        report.Revenue.Should().Be(500_000m);
        report.ReturnAmount.Should().Be(250_000m);
        report.NetRevenue.Should().Be(250_000m);

        report.CostOfGoodsSold.Should().BeNull();
        report.TotalCostOfGoodsSold.Should().BeNull();
        report.ReturnCogs.Should().BeNull();
        report.NetCogs.Should().BeNull();
        report.GrossProfit.Should().BeNull();
        report.TotalGrossProfit.Should().BeNull();
        report.GrossMarginPercentage.Should().BeNull();

        // Breakdowns cũng bị ẩn
        report.ByChannel.Should().NotBeEmpty();
        foreach (var ch in report.ByChannel)
        {
            ch.CostOfGoodsSold.Should().BeNull();
            ch.ReturnCogs.Should().BeNull();
            ch.NetCogs.Should().BeNull();
            ch.GrossProfit.Should().BeNull();
            ch.GrossMarginPercentage.Should().BeNull();
            ch.Revenue.Should().BeGreaterThan(0);
        }

        report.ByBranch.Should().NotBeEmpty();
        foreach (var br in report.ByBranch)
        {
            br.CostOfGoodsSold.Should().BeNull();
            br.ReturnCogs.Should().BeNull();
            br.NetCogs.Should().BeNull();
            br.GrossProfit.Should().BeNull();
            br.Revenue.Should().BeGreaterThan(0);
        }

        report.DailyTrend.Should().NotBeEmpty();
        foreach (var d in report.DailyTrend)
        {
            d.CostOfGoodsSold.Should().BeNull();
            d.ReturnCogs.Should().BeNull();
            d.NetCogs.Should().BeNull();
            d.GrossProfit.Should().BeNull();
        }
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_WithReturns_CalculatesNetNumbersAndProtectsStaff()
    {
        // Arrange
        var (context, mockTenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var reportService = new ReportService(context, mockTenant.Object);

        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Tân Bình" };
        var sku = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-DASH-RET", IsActive = true };
        context.Branches.Add(branch);
        context.Skus.Add(sku);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "ORD-DASH-RET",
            BranchId = branch.Id,
            Channel = "POS",
            Status = "Completed",
            TotalAmount = 600_000m,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var orderItem = new OrderItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            SkuId = sku.Id,
            Quantity = 3,
            FactorSnapshot = 1m,
            UnitPrice = 200_000m,
            CostPrice = 120_000m // 3 * 120k = 360k COGS
        };
        order.Items.Add(orderItem);
        context.Orders.Add(order);

        var salesReturn = new SalesReturn
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = "RT-DASH-RET",
            OrderId = order.Id,
            BranchId = branch.Id,
            Status = "Confirmed",
            TotalRefundAmount = 200_000m,
            ConfirmedAt = DateTimeOffset.UtcNow
        };
        salesReturn.Items.Add(new SalesReturnItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SalesReturnId = salesReturn.Id,
            OrderId = order.Id,
            OrderItemId = orderItem.Id,
            OrderItem = orderItem,
            SkuId = sku.Id,
            Quantity = 1,
            RefundAmount = 200_000m,
            OriginalUnitCost = 120_000m
        });
        context.SalesReturns.Add(salesReturn);

        await context.SaveChangesAsync();

        // Act 1: Owner
        var ownerSummary = await reportService.GetDashboardSummaryAsync(isOwner: true);
        ownerSummary.TotalRevenue.Should().Be(600_000m);
        ownerSummary.ReturnAmount.Should().Be(200_000m);
        ownerSummary.NetRevenue.Should().Be(400_000m);
        ownerSummary.ReturnCogs.Should().Be(120_000m);
        ownerSummary.NetCogs.Should().Be(240_000m); // 360k - 120k
        ownerSummary.TotalGrossProfit.Should().Be(160_000m); // 400k - 240k
        ownerSummary.GrossMarginPercentage.Should().Be(40m); // 160k / 400k * 100

        // Act 2: Staff
        var staffSummary = await reportService.GetDashboardSummaryAsync(isOwner: false);
        staffSummary.TotalRevenue.Should().Be(600_000m);
        staffSummary.ReturnAmount.Should().Be(200_000m);
        staffSummary.NetRevenue.Should().Be(400_000m);
        staffSummary.ReturnCogs.Should().BeNull();
        staffSummary.NetCogs.Should().BeNull();
        staffSummary.TotalGrossProfit.Should().BeNull();
        staffSummary.GrossMarginPercentage.Should().Be(0m);
    }
}
