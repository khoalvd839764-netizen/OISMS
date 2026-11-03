using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Inventory.Services;
using OISM.Domain.Entities;
using Xunit;

namespace OISM.UnitTests;

public class ReorderPointTests
{
    [Fact]
    public async Task UpdateReorderPointAsync_ExistingBalance_UpdatesSuccessfully()
    {
        // Arrange
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Quận 3" };
        var sku = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-TEST-01" };

        context.Branches.Add(branch);
        context.Skus.Add(sku);

        context.InventoryBalances.Add(new InventoryBalance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            SkuId = sku.Id,
            OnHand = 20,
            Reserved = 2,
            AverageCost = 50000,
            ReorderPoint = 5
        });
        await context.SaveChangesAsync();

        var service = new InventoryService(context, tenantMock.Object);

        var request = new UpdateReorderPointDto
        {
            BranchId = branch.Id,
            SkuId = sku.Id,
            ReorderPoint = 15
        };

        // Act
        var result = await service.UpdateReorderPointAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.ReorderPoint.Should().Be(15);
        result.OnHand.Should().Be(20);

        var balanceInDb = await context.InventoryBalances.FirstOrDefaultAsync(b => b.BranchId == branch.Id && b.SkuId == sku.Id);
        balanceInDb!.ReorderPoint.Should().Be(15);
    }

    [Fact]
    public async Task UpdateReorderPointAsync_NonExistingBalance_CreatesNewBalance()
    {
        // Arrange
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Tân Phú" };
        var sku = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-NEW-01" };

        context.Branches.Add(branch);
        context.Skus.Add(sku);
        await context.SaveChangesAsync();

        var service = new InventoryService(context, tenantMock.Object);

        var request = new UpdateReorderPointDto
        {
            BranchId = branch.Id,
            SkuId = sku.Id,
            ReorderPoint = 8
        };

        // Act
        var result = await service.UpdateReorderPointAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.ReorderPoint.Should().Be(8);
        result.OnHand.Should().Be(0);
        result.Reserved.Should().Be(0);

        var balanceInDb = await context.InventoryBalances.FirstOrDefaultAsync(b => b.BranchId == branch.Id && b.SkuId == sku.Id);
        balanceInDb.Should().NotBeNull();
        balanceInDb!.ReorderPoint.Should().Be(8);
    }

    [Fact]
    public async Task UpdateReorderPointAsync_NonExistingSku_ThrowsNotFound404()
    {
        // Arrange
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Quận 1" };
        context.Branches.Add(branch);
        await context.SaveChangesAsync();

        var service = new InventoryService(context, tenantMock.Object);

        var request = new UpdateReorderPointDto
        {
            BranchId = branch.Id,
            SkuId = Guid.NewGuid(),
            ReorderPoint = 10
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<OismException>(() => service.UpdateReorderPointAsync(request));
        ex.StatusCode.Should().Be(404);
        ex.ErrorCode.Should().Be("SKU_NOT_FOUND");
    }

    [Fact]
    public async Task BulkUpdateReorderPointAsync_UpdatesMultipleSkusSuccessfully()
    {
        // Arrange
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Thủ Đức" };
        var sku1 = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-BULK-01" };
        var sku2 = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-BULK-02" };

        context.Branches.Add(branch);
        context.Skus.AddRange(sku1, sku2);

        context.InventoryBalances.Add(new InventoryBalance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branch.Id,
            SkuId = sku1.Id,
            OnHand = 10,
            ReorderPoint = 2
        });
        await context.SaveChangesAsync();

        var service = new InventoryService(context, tenantMock.Object);

        var request = new BulkUpdateReorderPointDto
        {
            BranchId = branch.Id,
            Items = new List<SkuReorderPointItemDto>
            {
                new() { SkuId = sku1.Id, ReorderPoint = 20 },
                new() { SkuId = sku2.Id, ReorderPoint = 30 }
            }
        };

        // Act
        var count = await service.BulkUpdateReorderPointAsync(request);

        // Assert
        count.Should().Be(2);

        var b1 = await context.InventoryBalances.FirstOrDefaultAsync(b => b.BranchId == branch.Id && b.SkuId == sku1.Id);
        var b2 = await context.InventoryBalances.FirstOrDefaultAsync(b => b.BranchId == branch.Id && b.SkuId == sku2.Id);

        b1!.ReorderPoint.Should().Be(20);
        b2!.ReorderPoint.Should().Be(30);
    }

    [Fact]
    public async Task BulkUpdateReorderPointAsync_DuplicateSku_ThrowsBadRequest400()
    {
        // Arrange
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Thủ Đức" };
        var sku = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-DUP" };

        context.Branches.Add(branch);
        context.Skus.Add(sku);
        await context.SaveChangesAsync();

        var service = new InventoryService(context, tenantMock.Object);
        var request = new BulkUpdateReorderPointDto
        {
            BranchId = branch.Id,
            Items = new List<SkuReorderPointItemDto>
            {
                new() { SkuId = sku.Id, ReorderPoint = 1 },
                new() { SkuId = sku.Id, ReorderPoint = 2 }
            }
        };

        // Act
        var ex = await Assert.ThrowsAsync<OismException>(() => service.BulkUpdateReorderPointAsync(request));

        // Assert
        ex.StatusCode.Should().Be(400);
        ex.ErrorCode.Should().Be("DUPLICATE_SKU");
    }

    [Fact]
    public async Task BulkUpdateReorderPointAsync_UnknownSku_ThrowsNotFound404()
    {
        // Arrange
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Thủ Đức" };

        context.Branches.Add(branch);
        await context.SaveChangesAsync();

        var service = new InventoryService(context, tenantMock.Object);
        var request = new BulkUpdateReorderPointDto
        {
            BranchId = branch.Id,
            Items = new List<SkuReorderPointItemDto> { new() { SkuId = Guid.NewGuid(), ReorderPoint = 1 } }
        };

        // Act
        var ex = await Assert.ThrowsAsync<OismException>(() => service.BulkUpdateReorderPointAsync(request));

        // Assert
        ex.StatusCode.Should().Be(404);
        ex.ErrorCode.Should().Be("SKU_NOT_FOUND");
        (await context.InventoryBalances.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task UpdateReorderPointAsync_NewBalance_ReturnsSkuAndBranchInfo()
    {
        // Arrange
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Chi nhánh Quận 3" };
        var sku = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, SKU = "SKU-INFO" };

        context.Branches.Add(branch);
        context.Skus.Add(sku);
        await context.SaveChangesAsync();

        var service = new InventoryService(context, tenantMock.Object);

        // Act
        var result = await service.UpdateReorderPointAsync(new UpdateReorderPointDto
        {
            BranchId = branch.Id,
            SkuId = sku.Id,
            ReorderPoint = 7
        });

        // Assert
        result.SkuCode.Should().Be("SKU-INFO");
        result.BranchName.Should().Be("Chi nhánh Quận 3");
        result.ReorderPoint.Should().Be(7);
    }
}
