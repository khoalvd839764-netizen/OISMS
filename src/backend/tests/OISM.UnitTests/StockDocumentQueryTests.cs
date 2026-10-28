using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using OISM.Application.Features.Inventory.Services;
using OISM.Application.Features.StockCounts.Dtos;
using OISM.Application.Features.StockCounts.Services;
using OISM.Application.Features.StockTransfers.Dtos;
using OISM.Application.Features.StockTransfers.Services;
using OISM.Domain.Entities;
using Xunit;

namespace OISM.UnitTests;

public class StockDocumentQueryTests
{
    private static (StockTransferService service, Guid tenantA, Guid tenantB, StockTransfer transferA, StockTransfer transferB)
        SetupTransferService()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var (context, mockTenant, _) = TestDbContextFactory.CreateInMemoryContext(tenantA);
        var mockWac = new Mock<IWacCalculator>();
        var mockLogger = new Mock<ILogger<StockTransferService>>();

        // Seed Tenant A data
        var branchA1 = new Branch { Id = Guid.NewGuid(), TenantId = tenantA, Name = "Chi nhánh A1", IsActive = true };
        var branchA2 = new Branch { Id = Guid.NewGuid(), TenantId = tenantA, Name = "Chi nhánh A2", IsActive = true };
        var warehouseA1 = new Warehouse { Id = Guid.NewGuid(), TenantId = tenantA, BranchId = branchA1.Id, Name = "Kho A1", IsActive = true };
        var warehouseA2 = new Warehouse { Id = Guid.NewGuid(), TenantId = tenantA, BranchId = branchA2.Id, Name = "Kho A2", IsActive = true };

        var product = new Product { Id = Guid.NewGuid(), TenantId = tenantA, Name = "Áo Thun OISMS", IsActive = true };
        var sku = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA,
            ProductId = product.Id,
            SKU = "AT-OISMS-M",
            RetailPrice = 150000,
            Product = product
        };

        var transferA = new StockTransfer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA,
            Number = "TRF-20261028-00001",
            FromBranchId = branchA1.Id,
            FromBranch = branchA1,
            ToBranchId = branchA2.Id,
            ToBranch = branchA2,
            FromWarehouseId = warehouseA1.Id,
            FromWarehouse = warehouseA1,
            ToWarehouseId = warehouseA2.Id,
            ToWarehouse = warehouseA2,
            Status = "Draft",
            CreatedBy = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            Items = new List<StockTransferItem>
            {
                new StockTransferItem
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantA,
                    SkuId = sku.Id,
                    Sku = sku,
                    Quantity = 10,
                    UnitCost = 80000
                }
            }
        };

        context.Branches.AddRange(branchA1, branchA2);
        context.Warehouses.AddRange(warehouseA1, warehouseA2);
        context.Products.Add(product);
        context.Skus.Add(sku);
        context.StockTransfers.Add(transferA);
        context.SaveChanges();

        // Seed Tenant B data (switch context to tenantB)
        mockTenant.Setup(t => t.TenantId).Returns(tenantB);
        var branchB = new Branch { Id = Guid.NewGuid(), TenantId = tenantB, Name = "Chi nhánh B", IsActive = true };
        var warehouseB = new Warehouse { Id = Guid.NewGuid(), TenantId = tenantB, BranchId = branchB.Id, Name = "Kho B", IsActive = true };
        var transferB = new StockTransfer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantB,
            Number = "TRF-20261028-00002",
            FromBranchId = branchB.Id,
            FromBranch = branchB,
            ToBranchId = branchB.Id,
            ToBranch = branchB,
            FromWarehouseId = warehouseB.Id,
            FromWarehouse = warehouseB,
            ToWarehouseId = warehouseB.Id,
            ToWarehouse = warehouseB,
            Status = "Draft",
            CreatedBy = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.Branches.Add(branchB);
        context.Warehouses.Add(warehouseB);
        context.StockTransfers.Add(transferB);
        context.SaveChanges();

        // Switch back to tenantA for testing
        mockTenant.Setup(t => t.TenantId).Returns(tenantA);

        var service = new StockTransferService(context, mockTenant.Object, mockWac.Object, mockLogger.Object);
        return (service, tenantA, tenantB, transferA, transferB);
    }

    private static (StockCountService service, Guid tenantA, Guid tenantB, StockCount countA, StockCount countB)
        SetupCountService()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var (context, mockTenant, _) = TestDbContextFactory.CreateInMemoryContext(tenantA);
        var mockWac = new Mock<IWacCalculator>();
        var mockLogger = new Mock<ILogger<StockCountService>>();

        // Seed Tenant A data
        var branchA = new Branch { Id = Guid.NewGuid(), TenantId = tenantA, Name = "Chi nhánh Đếm A", IsActive = true };
        var warehouseA = new Warehouse { Id = Guid.NewGuid(), TenantId = tenantA, BranchId = branchA.Id, Name = "Kho Đếm A", IsActive = true };

        var product = new Product { Id = Guid.NewGuid(), TenantId = tenantA, Name = "Giày Thể Thao OISMS", IsActive = true };
        var sku = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA,
            ProductId = product.Id,
            SKU = "SHOES-OISMS-42",
            RetailPrice = 450000,
            Product = product
        };

        var countA = new StockCount
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA,
            Number = "SC-20261028-00001",
            BranchId = branchA.Id,
            Branch = branchA,
            WarehouseId = warehouseA.Id,
            Warehouse = warehouseA,
            Status = "Draft",
            CreatedBy = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            Items = new List<StockCountItem>
            {
                new StockCountItem
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantA,
                    SkuId = sku.Id,
                    Sku = sku,
                    ExpectedQuantity = 20,
                    CountedQuantity = 18,
                    BalanceSequenceAtSnapshot = 1
                }
            }
        };

        context.Branches.Add(branchA);
        context.Warehouses.Add(warehouseA);
        context.Products.Add(product);
        context.Skus.Add(sku);
        context.StockCounts.Add(countA);
        context.SaveChanges();

        // Seed Tenant B data (switch context to tenantB)
        mockTenant.Setup(t => t.TenantId).Returns(tenantB);
        var branchB = new Branch { Id = Guid.NewGuid(), TenantId = tenantB, Name = "Chi nhánh B", IsActive = true };
        var warehouseB = new Warehouse { Id = Guid.NewGuid(), TenantId = tenantB, BranchId = branchB.Id, Name = "Kho B", IsActive = true };
        var countB = new StockCount
        {
            Id = Guid.NewGuid(),
            TenantId = tenantB,
            Number = "SC-20261028-00002",
            BranchId = branchB.Id,
            Branch = branchB,
            WarehouseId = warehouseB.Id,
            Warehouse = warehouseB,
            Status = "Draft",
            CreatedBy = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        context.Branches.Add(branchB);
        context.Warehouses.Add(warehouseB);
        context.StockCounts.Add(countB);
        context.SaveChanges();

        // Switch back to tenantA for testing
        mockTenant.Setup(t => t.TenantId).Returns(tenantA);

        var service = new StockCountService(context, mockTenant.Object, mockWac.Object, mockLogger.Object);
        return (service, tenantA, tenantB, countA, countB);
    }

    [Fact]
    public async Task GetTransfersAsync_ShouldReturnTenantTransfers_AndIsolateTenantB()
    {
        // Arrange
        var (service, tenantA, tenantB, transferA, transferB) = SetupTransferService();

        // Act
        var result = await service.GetTransfersAsync(new StockTransferFilterRequestDto());

        // Assert
        result.Should().NotBeNull();
        result.TotalCount.Should().Be(1);
        result.Items.Should().HaveCount(1);
        result.Items[0].Id.Should().Be(transferA.Id);
        result.Items[0].FromBranchName.Should().Be("Chi nhánh A1");
        result.Items[0].ToWarehouseName.Should().Be("Kho A2");
        result.Items[0].Items.Should().HaveCount(1);
        result.Items[0].Items[0].ProductName.Should().Be("Áo Thun OISMS");

        // Verify Tenant B is not present
        result.Items.Any(i => i.Id == transferB.Id).Should().BeFalse();
    }

    [Fact]
    public async Task GetTransfersAsync_FilterByStatusAndBranch_ShouldFilterCorrectly()
    {
        // Arrange
        var (service, _, _, transferA, _) = SetupTransferService();

        // Act 1: Filter with matching status
        var matchStatus = await service.GetTransfersAsync(new StockTransferFilterRequestDto { Status = "Draft" });
        matchStatus.TotalCount.Should().Be(1);

        // Act 2: Filter with non-matching status
        var noMatchStatus = await service.GetTransfersAsync(new StockTransferFilterRequestDto { Status = "Received" });
        noMatchStatus.TotalCount.Should().Be(0);

        // Act 3: Filter by FromBranchId
        var matchBranch = await service.GetTransfersAsync(new StockTransferFilterRequestDto { BranchId = transferA.FromBranchId });
        matchBranch.TotalCount.Should().Be(1);

        // Act 4: Filter by unknown BranchId
        var unknownBranch = await service.GetTransfersAsync(new StockTransferFilterRequestDto { BranchId = Guid.NewGuid() });
        unknownBranch.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetTransferByIdAsync_ShouldReturnDetailForTenantA_AndNullForTenantBOrUnknown()
    {
        // Arrange
        var (service, _, _, transferA, transferB) = SetupTransferService();

        // Act & Assert 1: Valid transfer belonging to Tenant A
        var detailA = await service.GetTransferByIdAsync(transferA.Id);
        detailA.Should().NotBeNull();
        detailA!.Id.Should().Be(transferA.Id);
        detailA.Number.Should().Be(transferA.Number);
        detailA.Items.Should().HaveCount(1);
        detailA.Items[0].SkuCode.Should().Be("AT-OISMS-M");
        detailA.Items[0].ProductName.Should().Be("Áo Thun OISMS");

        // Act & Assert 2: Unknown ID returns null (controller returns 404)
        var detailUnknown = await service.GetTransferByIdAsync(Guid.NewGuid());
        detailUnknown.Should().BeNull();

        // Act & Assert 3: Transfer B belonging to Tenant B returns null (tenant isolation)
        var detailB = await service.GetTransferByIdAsync(transferB.Id);
        detailB.Should().BeNull();
    }

    [Fact]
    public async Task GetStockCountsAsync_ShouldReturnTenantCounts_AndIsolateTenantB()
    {
        // Arrange
        var (service, tenantA, tenantB, countA, countB) = SetupCountService();

        // Act
        var result = await service.GetStockCountsAsync(new StockCountFilterRequestDto());

        // Assert
        result.Should().NotBeNull();
        result.TotalCount.Should().Be(1);
        result.Items.Should().HaveCount(1);
        result.Items[0].Id.Should().Be(countA.Id);
        result.Items[0].BranchName.Should().Be("Chi nhánh Đếm A");
        result.Items[0].WarehouseName.Should().Be("Kho Đếm A");
        result.Items[0].Items.Should().HaveCount(1);
        result.Items[0].Items[0].ProductName.Should().Be("Giày Thể Thao OISMS");
        result.Items[0].Items[0].DifferenceQuantity.Should().Be(-2);

        // Verify Tenant B is not present
        result.Items.Any(i => i.Id == countB.Id).Should().BeFalse();
    }

    [Fact]
    public async Task GetStockCountByIdAsync_ShouldReturnDetailForTenantA_AndNullForTenantBOrUnknown()
    {
        // Arrange
        var (service, _, _, countA, countB) = SetupCountService();

        // Act & Assert 1: Valid count belonging to Tenant A
        var detailA = await service.GetStockCountByIdAsync(countA.Id);
        detailA.Should().NotBeNull();
        detailA!.Id.Should().Be(countA.Id);
        detailA.Number.Should().Be(countA.Number);
        detailA.Items.Should().HaveCount(1);
        detailA.Items[0].SkuCode.Should().Be("SHOES-OISMS-42");
        detailA.Items[0].ProductName.Should().Be("Giày Thể Thao OISMS");
        detailA.Items[0].ExpectedQuantity.Should().Be(20);
        detailA.Items[0].CountedQuantity.Should().Be(18);

        // Act & Assert 2: Unknown ID returns null
        var detailUnknown = await service.GetStockCountByIdAsync(Guid.NewGuid());
        detailUnknown.Should().BeNull();

        // Act & Assert 3: Count B belonging to Tenant B returns null
        var detailB = await service.GetStockCountByIdAsync(countB.Id);
        detailB.Should().BeNull();
    }
}
