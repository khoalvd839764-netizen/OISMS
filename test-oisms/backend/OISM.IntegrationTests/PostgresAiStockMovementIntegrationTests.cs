using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Ai.Dtos;
using OISM.Application.Features.Ai.Services;
using OISM.Domain.Common;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

public sealed class PostgresAiStockMovementIntegrationTests
{
    private static readonly DateTimeOffset Midnight = new(2026, 1, 10, 17, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly FirstDay = new(2026, 1, 10);

    [Fact]
    public async Task Query_UsesVietnameseDays_SeparatesSourcesAndBranches_AndIsolatesTenants()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
        await using (var seedDb = PostgresTestEnvironment.CreateRuntimeDbContext(new TestTenant(null)))
        {
            await OismsDatabaseSeeder.SeedAsync(seedDb, PostgresTestEnvironment.AppPassword);
        }
        var tenantA = new TestTenant(OismsDatabaseSeeder.TenantAId);
        var tenantB = new TestTenant(OismsDatabaseSeeder.TenantBId);
        await using var dbA = PostgresTestEnvironment.CreateRuntimeDbContext(tenantA);
        await using var dbB = PostgresTestEnvironment.CreateRuntimeDbContext(tenantB);

        var source = await dbA.Warehouses.SingleAsync();
        var sku = await dbA.Skus.SingleAsync();
        var owner = await dbA.Users.SingleAsync(user => user.Role == "Owner");
        var sourceBranch = await dbA.Branches.SingleAsync();
        var destBranch = new Branch { TenantId = owner.TenantId, Name = "Chi nhánh nhận chuyển" };
        var dest = new Warehouse { TenantId = owner.TenantId, Branch = destBranch, Name = "Kho đích" };
        dbA.Warehouses.Add(dest);
        await dbA.SaveChangesAsync();

        var openingItem = AddAdjustment(dbA, source, sku, owner.Id, 10, "A-OPEN");
        var order = new Order
        {
            TenantId = owner.TenantId, BranchId = source.BranchId, Number = "ORD-AI-MOVE",
            Status = OrderStatus.Completed, ConfirmedAt = Midnight, CompletedAt = Midnight
        };
        var saleItem = new OrderItem
        {
            Order = order, TenantId = owner.TenantId, SkuId = sku.Id, Quantity = 3,
            UnitPrice = 200, CostPrice = 100, SkuCodeSnapshot = sku.SKU, SkuNameSnapshot = sku.VariantName
        };
        dbA.OrderItems.Add(saleItem);
        var transfer = new StockTransfer
        {
            TenantId = owner.TenantId, Number = "ST-AI-MOVE", CreatedBy = owner.Id,
            FromBranchId = source.BranchId, FromWarehouseId = source.Id,
            ToBranchId = dest.BranchId, ToWarehouseId = dest.Id, Status = StockTransferStatus.Received,
            DispatchedAt = Midnight.AddMinutes(2), ReceivedAt = Midnight.AddMinutes(3), ReceivedBy = owner.Id
        };
        var transferItem = new StockTransferItem
        {
            Transfer = transfer, TenantId = owner.TenantId, SkuId = sku.Id, Quantity = 2, UnitCost = 100
        };
        dbA.StockTransferItems.Add(transferItem);
        var saleReturn = new SalesReturn
        {
            TenantId = owner.TenantId, Order = order, Number = "RT-AI-MOVE", RequestKey = Guid.NewGuid(),
            BranchId = source.BranchId, WarehouseId = source.Id, CreatedBy = owner.Id,
            Status = SalesReturnStatus.Confirmed, ConfirmedAt = Midnight.AddMinutes(4), ConfirmedBy = owner.Id,
            Reason = "Fixture"
        };
        var returnItem = new SalesReturnItem
        {
            TenantId = owner.TenantId, SalesReturn = saleReturn, OrderId = order.Id,
            OrderItem = saleItem, SkuId = sku.Id, Quantity = 1, OriginalUnitCost = 100
        };
        dbA.SalesReturnItems.Add(returnItem);
        await dbA.SaveChangesAsync();

        var inbound = Ledger(source, sku, "IN", 10, 10, 1, Midnight.AddMinutes(-1));
        inbound.AdjustmentItemId = openingItem.Id;
        var outbound = Ledger(source, sku, "OUT", 3, 7, 2, Midnight.AddMinutes(1));
        outbound.OrderItemId = saleItem.Id;
        var transferOut = Ledger(source, sku, "OUT", 2, 5, 3, Midnight.AddMinutes(2));
        transferOut.TransferItemId = transferItem.Id;
        var transferIn = Ledger(dest, sku, "IN", 2, 2, 1, Midnight.AddMinutes(3));
        transferIn.TransferItemId = transferItem.Id;
        var returned = Ledger(source, sku, "IN", 1, 6, 4, Midnight.AddMinutes(4));
        returned.SalesReturnItemId = returnItem.Id;
        dbA.InventoryTransactions.AddRange(inbound, outbound, transferOut, transferIn, returned);
        SetBalance(dbA, await dbA.InventoryBalances.SingleAsync(), source, sku, 6, 4);
        SetBalance(dbA, new InventoryBalance(), dest, sku, 2, 1);
        await dbA.SaveChangesAsync();

        var otherWarehouse = await dbB.Warehouses.SingleAsync();
        var otherSku = await dbB.Skus.SingleAsync();
        var otherOwner = await dbB.Users.SingleAsync(user => user.Role == "Owner");
        var otherItem = AddAdjustment(dbB, otherWarehouse, otherSku, otherOwner.Id, 99, "B-OPEN");
        await dbB.SaveChangesAsync();
        var other = Ledger(otherWarehouse, otherSku, "IN", 99, 99, 1, Midnight.AddMinutes(1));
        other.AdjustmentItemId = otherItem.Id;
        dbB.InventoryTransactions.Add(other);
        SetBalance(dbB, await dbB.InventoryBalances.SingleAsync(), otherWarehouse, otherSku, 99, 1);
        await dbB.SaveChangesAsync();

        var query = new StockMovementQuery(dbA, tenantA);
        var range = new AiChatDateRange(FirstDay, FirstDay.AddDays(1));
        var result = await query.GetAsync(range, null);

        Assert.Equal(3, result.DailyByBranch.Count);
        var before = Assert.Single(result.DailyByBranch.Where(day => day.Date == FirstDay));
        Assert.Equal(sourceBranch.Name, before.Branch);
        Assert.Equal(new StockMovementQuantity(sku.Unit, 10, 0, 0, 0, 0), Assert.Single(before.Quantities));
        var after = Assert.Single(result.DailyByBranch.Where(day => day.Date == range.To && day.Branch == sourceBranch.Name));
        Assert.Equal(new StockMovementQuantity(sku.Unit, 1, 5, 0, 2, 1), Assert.Single(after.Quantities));
        var received = Assert.Single(result.DailyByBranch.Where(day => day.Branch == destBranch.Name));
        Assert.Equal(new StockMovementQuantity(sku.Unit, 2, 0, 2, 0, 0), Assert.Single(received.Quantities));
        Assert.Equal(new StockMovementQuantity(sku.Unit, 13, 5, 2, 2, 1), Assert.Single(result.Totals));
        Assert.Equal(new StockMovementSku(sku.SKU, sku.VariantName, sku.Unit, 5), Assert.Single(result.TopOutboundSkus));

        var firstDayOnly = await query.GetAsync(new AiChatDateRange(FirstDay, FirstDay), source.BranchId);
        Assert.Equal(before.Date, Assert.Single(firstDayOnly.DailyByBranch).Date);
        Assert.Empty(firstDayOnly.TopOutboundSkus);
        var destOnly = await query.GetAsync(range, dest.BranchId);
        Assert.Equal(destBranch.Name, Assert.Single(destOnly.DailyByBranch).Branch);

        var tenantBResult = await new StockMovementQuery(dbB, tenantB).GetAsync(range, null);
        Assert.Equal(99, Assert.Single(tenantBResult.Totals).In);
    }

    private static InventoryAdjustmentItem AddAdjustment(
        AppDbContext db, Warehouse warehouse, ProductVariant sku, Guid userId, decimal quantity, string number)
    {
        var adjustment = new InventoryAdjustment
        {
            TenantId = sku.TenantId, Number = number, BranchId = warehouse.BranchId,
            WarehouseId = warehouse.Id, Reason = "Fixture", CreatedBy = userId
        };
        adjustment.Confirm(userId, Midnight.AddMinutes(-1));
        var item = new InventoryAdjustmentItem
        {
            Adjustment = adjustment, TenantId = sku.TenantId, SkuId = sku.Id, QuantityDelta = quantity, UnitCost = 100
        };
        db.InventoryAdjustmentItems.Add(item);
        return item;
    }

    private static InventoryTransaction Ledger(
        Warehouse warehouse, ProductVariant sku, string type, decimal quantity, decimal balance, int sequence, DateTimeOffset at) =>
        new()
        {
            TenantId = sku.TenantId, BranchId = warehouse.BranchId, WarehouseId = warehouse.Id, SkuId = sku.Id,
            Type = type, Quantity = quantity, BalanceAfter = balance, SequenceNo = sequence, UnitCost = 100, CreatedAt = at
        };

    private static void SetBalance(
        AppDbContext db, InventoryBalance branch, Warehouse warehouse, ProductVariant sku, decimal onHand, int sequence)
    {
        branch.TenantId = sku.TenantId;
        branch.BranchId = warehouse.BranchId;
        branch.SkuId = sku.Id;
        branch.OnHand = onHand;
        branch.LastSequence = sequence;
        branch.AverageCost = 100;
        if (db.Entry(branch).State == EntityState.Detached)
        {
            db.InventoryBalances.Add(branch);
        }

        db.WarehouseInventoryBalances.Add(new WarehouseInventoryBalance
        {
            TenantId = sku.TenantId, BranchId = warehouse.BranchId, WarehouseId = warehouse.Id, SkuId = sku.Id,
            OnHand = onHand, LastSequence = sequence
        });
    }

    private sealed class TestTenant(Guid? tenantId) : ICurrentTenantService
    {
        public Guid? TenantId { get; private set; } = tenantId;
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }
}
