using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Features.Ai.Dtos;
using OISM.Application.Features.Ai.Services;
using OISM.Domain.Entities;
using Xunit;

namespace OISM.UnitTests;

public class AiForecastingTests
{
    [Fact]
    public async Task GetExportHistoryAsync_ReturnsGroupedDailyQuantities_WhenTransactionsExist()
    {
        // Arrange
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Quận 1" };
        var sku = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-AI-01", Unit = "piece" };

        context.Branches.Add(branch);
        context.Skus.Add(sku);

        var today = DateTime.UtcNow.Date;

        // Thêm 2 bản ghi xuất kho OUT
        context.InventoryTransactions.AddRange(
            new InventoryTransaction
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BranchId = branch.Id,
                SkuId = sku.Id,
                Type = "OUT",
                Quantity = 5,
                CreatedAt = today.AddHours(10)
            },
            new InventoryTransaction
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BranchId = branch.Id,
                SkuId = sku.Id,
                Type = "OUT",
                Quantity = 7,
                CreatedAt = today.AddHours(14)
            }
        );
        await context.SaveChangesAsync();

        var service = new AiForecastingService(context, tenantMock.Object);

        // Act
        var result = await service.GetExportHistoryAsync(new ExportHistoryQueryDto
        {
            BranchId = branch.Id,
            Days = 7
        });

        // Assert
        result.Should().NotBeNull();
        result.TotalExportQuantity.Should().Be(12);
        result.DailyRecords.Should().HaveCount(1);
        result.DailyRecords[0].Quantity.Should().Be(12);
        result.DailyRecords[0].TransactionCount.Should().Be(2);
        result.DailyRecords[0].SkuCode.Should().Be("SKU-AI-01");
    }

    [Fact]
    public async Task GetReorderRecommendationsAsync_ExcludesInactiveSku_AndFormatsReason()
    {
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh A" };
        var active = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-ACTIVE", Unit = "piece", IsActive = true };
        var inactive = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-INACTIVE", Unit = "piece", IsActive = false };
        context.Branches.Add(branch);
        context.Skus.AddRange(active, inactive);
        foreach (var sku in new[] { active, inactive })
        {
            context.InventoryBalances.Add(new InventoryBalance
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BranchId = branch.Id,
                SkuId = sku.Id,
                OnHand = 5.000m,
                Reserved = 0m,
                ReorderPoint = 10.000m
            });
        }
        await context.SaveChangesAsync();

        var result = await new AiForecastingService(context, tenantMock.Object)
            .GetReorderRecommendationsAsync(new ReorderRecommendationQueryDto());

        result.TotalSkusAnalyzed.Should().Be(1);
        var rec = result.Recommendations.Should().ContainSingle().Subject;
        rec.SkuCode.Should().Be("SKU-ACTIVE");
        rec.Reason.Should().Contain("(5)").And.Contain("(10)");
        rec.Reason.Should().NotContain("999 ngày");
    }

    [Fact]
    public async Task GetReorderRecommendationsAsync_SuggestsReorder_WhenStockBelowReorderPoint()
    {
        // Arrange
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Thủ Đức" };
        var sku = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-LOW-STOCK", Unit = "piece" };

        context.Branches.Add(branch);
        context.Skus.Add(sku);

        // Tồn kho OnHand = 4, Reserved = 1 -> Available = 3. ReorderPoint = 10
        context.InventoryBalances.Add(new InventoryBalance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            SkuId = sku.Id,
            OnHand = 4,
            Reserved = 1,
            ReorderPoint = 10
        });
        await context.SaveChangesAsync();

        var service = new AiForecastingService(context, tenantMock.Object);

        // Act
        var result = await service.GetReorderRecommendationsAsync(new ReorderRecommendationQueryDto
        {
            BranchId = branch.Id,
            LeadTimeDays = 7,
            SafetyStockDays = 3
        });

        // Assert
        result.Should().NotBeNull();
        result.TotalSkusAnalyzed.Should().Be(1);
        result.SkusNeedingReorder.Should().Be(1);
        result.Recommendations.Should().HaveCount(1);

        var rec = result.Recommendations[0];
        rec.NeedsReorder.Should().BeTrue();
        rec.AvailableQuantity.Should().Be(3);
        rec.ReorderPoint.Should().Be(10);
        rec.SuggestedQuantity.Should().BeGreaterThan(0);
        rec.Reason.Should().Contain("ngưỡng cảnh báo");
    }

    [Fact]
    public async Task GetReorderRecommendationsAsync_DoesNotSuggestReorder_WhenStockIsAbundant()
    {
        // Arrange
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Bình Thạnh" };
        var sku = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-SAFE", Unit = "box" };

        context.Branches.Add(branch);
        context.Skus.Add(sku);

        // Tồn kho OnHand = 100, Reserved = 5 -> Available = 95. ReorderPoint = 10. Không có vận tốc bán cao
        context.InventoryBalances.Add(new InventoryBalance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            SkuId = sku.Id,
            OnHand = 100,
            Reserved = 5,
            ReorderPoint = 10
        });
        await context.SaveChangesAsync();

        var service = new AiForecastingService(context, tenantMock.Object);

        // Act
        var result = await service.GetReorderRecommendationsAsync(new ReorderRecommendationQueryDto
        {
            BranchId = branch.Id,
            OnlyNeedsReorder = true
        });

        // Assert
        result.Should().NotBeNull();
        result.SkusNeedingReorder.Should().Be(0);
        result.Recommendations.Should().BeEmpty();
    }

    [Fact]
    public async Task GetReorderRecommendationsAsync_EnforcesTenantIsolation()
    {
        // Arrange
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var otherTenantId = Guid.NewGuid();

        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh My Tenant" };
        var sku = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-MY-TENANT" };

        context.Branches.Add(branch);
        context.Skus.Add(sku);

        context.InventoryBalances.Add(new InventoryBalance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            SkuId = sku.Id,
            OnHand = 2,
            ReorderPoint = 10
        });
        await context.SaveChangesAsync();

        tenantMock.Setup(t => t.TenantId).Returns(otherTenantId);
        using (context.OpenPrivilegedScope())
        {
            var otherBranch = new Branch { Id = Guid.NewGuid(), TenantId = otherTenantId, Name = "Chi nhánh Other Tenant" };
            var otherSku = new ProductVariant { Id = Guid.NewGuid(), TenantId = otherTenantId, SKU = "SKU-OTHER-TENANT" };
            context.Branches.Add(otherBranch);
            context.Skus.Add(otherSku);

            context.InventoryBalances.Add(new InventoryBalance
            {
                Id = Guid.NewGuid(),
                TenantId = otherTenantId,
                BranchId = otherBranch.Id,
                SkuId = otherSku.Id,
                OnHand = 0,
                ReorderPoint = 50
            });
            await context.SaveChangesAsync();
        }
        tenantMock.Setup(t => t.TenantId).Returns(tenantId);

        var service = new AiForecastingService(context, tenantMock.Object);

        // Act
        var result = await service.GetReorderRecommendationsAsync(new ReorderRecommendationQueryDto());

        // Assert: Chỉ thấy SKU của Tenant hiện tại, tuyệt đối không lộ dữ liệu của Tenant khác
        result.Should().NotBeNull();
        result.TotalSkusAnalyzed.Should().Be(1);
        result.Recommendations.Should().OnlyContain(r => r.SkuCode == "SKU-MY-TENANT");
    }

    [Fact]
    public async Task GetReorderRecommendationsAsync_UsesVelocityOfEachBranch_WhenNoBranchFilter()
    {
        // Arrange: chi nhánh A bán 300 cái trong kỳ, chi nhánh B không bán gì
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var branchA = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh A" };
        var branchB = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh B" };
        var sku = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-MULTI", Unit = "piece" };

        context.Branches.AddRange(branchA, branchB);
        context.Skus.Add(sku);
        context.InventoryBalances.AddRange(
            new InventoryBalance { Id = Guid.NewGuid(), TenantId = tenantId, BranchId = branchA.Id, SkuId = sku.Id, OnHand = 100 },
            new InventoryBalance { Id = Guid.NewGuid(), TenantId = tenantId, BranchId = branchB.Id, SkuId = sku.Id, OnHand = 100 });
        context.InventoryTransactions.Add(new InventoryTransaction
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchA.Id,
            SkuId = sku.Id,
            Type = "OUT",
            Quantity = 300,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await context.SaveChangesAsync();

        var service = new AiForecastingService(context, tenantMock.Object);

        // Act
        var result = await service.GetReorderRecommendationsAsync(new ReorderRecommendationQueryDto());

        // Assert
        var recA = result.Recommendations.Single(r => r.BranchId == branchA.Id);
        var recB = result.Recommendations.Single(r => r.BranchId == branchB.Id);
        recA.DailySalesVelocity.Should().Be(10);
        recB.DailySalesVelocity.Should().Be(0);
        recB.NeedsReorder.Should().BeFalse();
    }

    [Fact]
    public async Task GetExportHistoryAsync_ExcludesInternalTransfers()
    {
        // Arrange
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh A" };
        var sku = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-TRANSFER", Unit = "piece" };

        context.Branches.Add(branch);
        context.Skus.Add(sku);
        context.InventoryTransactions.AddRange(
            new InventoryTransaction
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BranchId = branch.Id,
                SkuId = sku.Id,
                Type = "OUT",
                Quantity = 4,
                OrderItemId = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
            },
            new InventoryTransaction
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BranchId = branch.Id,
                SkuId = sku.Id,
                Type = "OUT",
                Quantity = 300,
                TransferItemId = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
            });
        await context.SaveChangesAsync();

        var service = new AiForecastingService(context, tenantMock.Object);

        // Act
        var result = await service.GetExportHistoryAsync(new ExportHistoryQueryDto { Days = 7 });

        // Assert
        result.TotalExportQuantity.Should().Be(4);
    }

    [Fact]
    public async Task GetExportHistoryAsync_UsesSaigonDayBoundaries()
    {
        // Arrange: 23:30 UTC ngày 09/10 là 06:30 ngày 10/10 giờ VN; 17:00 UTC ngày 10/10 là 00:00 ngày 11/10 giờ VN
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh A" };
        var sku = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-TZ", Unit = "piece" };

        context.Branches.Add(branch);
        context.Skus.Add(sku);
        context.InventoryTransactions.AddRange(
            new InventoryTransaction
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BranchId = branch.Id,
                SkuId = sku.Id,
                Type = "OUT",
                Quantity = 2,
                CreatedAt = new DateTimeOffset(2026, 10, 9, 23, 30, 0, TimeSpan.Zero)
            },
            new InventoryTransaction
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BranchId = branch.Id,
                SkuId = sku.Id,
                Type = "OUT",
                Quantity = 50,
                CreatedAt = new DateTimeOffset(2026, 10, 10, 17, 0, 0, TimeSpan.Zero)
            });
        await context.SaveChangesAsync();

        var service = new AiForecastingService(context, tenantMock.Object);

        // Act
        var result = await service.GetExportHistoryAsync(new ExportHistoryQueryDto
        {
            StartDate = new DateTime(2026, 10, 10),
            EndDate = new DateTime(2026, 10, 10)
        });

        // Assert
        result.TotalExportQuantity.Should().Be(2);
        result.DailyRecords.Should().ContainSingle();
        result.DailyRecords[0].Date.Should().Be(new DateOnly(2026, 10, 10));
    }
}
