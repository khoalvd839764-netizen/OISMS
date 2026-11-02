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
}
