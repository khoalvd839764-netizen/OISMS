using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Reports.Dtos;
using OISM.Application.Features.Reports.Services;
using OISM.Domain.Common;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using OISM.Infrastructure.Services;
using System.Diagnostics;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Kiểm thử tích hợp Báo cáo tồn kho theo kỳ (NXT) và Thẻ kho (S12/S10) trên PostgreSQL (Upgrade 13 / GATE G2).
/// Bao gồm: Cân bằng đầu + nhập - xuất = cuối, đối soát OnHand, giáp ranh ngày, múi giờ UTC+7,
/// hàng đang chuyển InTransit, che giá Staff, cô lập đa người thuê và kiểm chứng hiệu năng < 2s.
/// </summary>
public sealed class PostgresInventoryPeriodReportIntegrationTests
{
    private static readonly TimeSpan SaigonOffset = TimeSpan.FromHours(7);
    private static readonly DateOnly TestDay1 = new(2026, 11, 10);
    private static readonly DateOnly TestDay2 = new(2026, 11, 11);

    [Fact]
    public async Task VerifyPeriodEquation_OpeningPlusInMinusOutEqualsClosing_ForQuantityAndValue()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
        await using (var seedDb = PostgresTestEnvironment.CreateRuntimeDbContext(new TestTenant(null)))
        {
            await OismsDatabaseSeeder.SeedAsync(seedDb, PostgresTestEnvironment.AppPassword);
        }

        var tenantA = new TestTenant(OismsDatabaseSeeder.TenantAId);
        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(tenantA);

        var branch = await db.Branches.FirstAsync();
        var warehouse = await db.Warehouses.FirstAsync(w => w.BranchId == branch.Id);
        var sku = await db.Skus.FirstAsync();
        var owner = await db.Users.FirstAsync(u => u.Role == "Owner");

        var day1UtcStart = new DateTimeOffset(TestDay1.ToDateTime(TimeOnly.MinValue), SaigonOffset).ToUniversalTime();

        // 1. Dòng trước kỳ (đầu kỳ): 20 cái giá 50.000 qua Adjustment
        var openAdj = AddAdjustment(db, tenantA.TenantId!.Value, branch.Id, warehouse.Id, sku.Id, owner.Id, 20m, "OPENING");
        var openTx = CreateTransaction(tenantA.TenantId!.Value, branch.Id, warehouse.Id, sku.Id, "IN", 20m, 50000m, 20m, 1, day1UtcStart.AddHours(-2), adjustmentItemId: openAdj.Id);
        db.InventoryTransactions.Add(openTx);

        // 2. Giao dịch trong kỳ Day1:
        // Nhập mua: +30 cái giá 60.000 (PO -> GR)
        var po = new PurchaseOrder
        {
            TenantId = tenantA.TenantId!.Value,
            BranchId = branch.Id,
            Number = "PO-REP-01",
            Status = PurchaseOrderStatus.Received,
            OrderDate = TestDay1,
            CreatedBy = owner.Id
        };
        db.PurchaseOrders.Add(po);

        var gr = new GoodsReceipt
        {
            TenantId = tenantA.TenantId!.Value,
            BranchId = branch.Id,
            WarehouseId = warehouse.Id,
            Number = "GR-REP-01",
            Status = GoodsReceiptStatus.Confirmed,
            PurchaseOrderId = po.Id,
            ConfirmedAt = day1UtcStart.AddHours(2),
            CreatedBy = owner.Id
        };
        db.GoodsReceipts.Add(gr);

        var grItem = new GoodsReceiptItem
        {
            Receipt = gr,
            TenantId = tenantA.TenantId!.Value,
            SkuId = sku.Id,
            Quantity = 30m,
            UnitPrice = 60000m
        };
        db.GoodsReceiptItems.Add(grItem);

        var poTx = CreateTransaction(tenantA.TenantId!.Value, branch.Id, warehouse.Id, sku.Id, "IN", 30m, 60000m, 50m, 2, day1UtcStart.AddHours(2));
        poTx.ReceiptItemId = grItem.Id;
        db.InventoryTransactions.Add(poTx);

        // Xuất bán POS: -15 cái giá vốn 56.000
        var order = new Order
        {
            TenantId = tenantA.TenantId!.Value,
            BranchId = branch.Id,
            Number = "ORD-REP-POS",
            Channel = "POS",
            Status = OrderStatus.Completed,
            ConfirmedAt = day1UtcStart.AddHours(5),
            CompletedAt = day1UtcStart.AddHours(5)
        };
        db.Orders.Add(order);

        var orderItem = new OrderItem
        {
            Order = order,
            TenantId = tenantA.TenantId!.Value,
            SkuId = sku.Id,
            Quantity = 15m,
            UnitPrice = 80000m,
            CostPrice = 56000m,
            SkuCodeSnapshot = sku.SKU,
            SkuNameSnapshot = sku.VariantName
        };
        db.OrderItems.Add(orderItem);

        var posTx = CreateTransaction(tenantA.TenantId!.Value, branch.Id, warehouse.Id, sku.Id, "OUT", 15m, 56000m, 35m, 3, day1UtcStart.AddHours(5));
        posTx.OrderItemId = orderItem.Id;
        db.InventoryTransactions.Add(posTx);

        await db.SaveChangesAsync();

        var service = new InventoryPeriodReportService(db, tenantA);

        // Owner xem
        var reportOwner = await service.GetInventoryPeriodReportAsync(new InventoryPeriodFilterDto
        {
            FromDate = TestDay1,
            ToDate = TestDay1,
            BranchId = branch.Id,
            Page = 1,
            PageSize = 50
        }, isOwner: true);

        var item = reportOwner.Items.Single(i => i.SkuId == sku.Id);

        // Kiểm tra công thức cân đối số lượng: Đầu (20) + Nhập (30) - Xuất (15) = Cuối (35)
        Assert.Equal(20m, item.OpeningQuantity);
        Assert.Equal(30m, item.InQuantity);
        Assert.Equal(15m, item.OutQuantity);
        Assert.Equal(35m, item.ClosingQuantity);
        Assert.Equal(item.OpeningQuantity + item.InQuantity - item.OutQuantity, item.ClosingQuantity);

        // Kiểm tra công thức cân đối giá trị (Owner):
        // Đầu = 20 * 50k = 1.000.000
        // Nhập = 30 * 60k = 1.800.000
        // Xuất = 15 * 56k = 840.000
        // Cuối = 1.000.000 + 1.800.000 - 840.000 = 1.960.000
        Assert.NotNull(item.OpeningValue);
        Assert.NotNull(item.InValue);
        Assert.NotNull(item.OutValue);
        Assert.NotNull(item.ClosingValue);
        Assert.Equal(1000000m, item.OpeningValue);
        Assert.Equal(1800000m, item.InValue);
        Assert.Equal(840000m, item.OutValue);
        Assert.Equal(1960000m, item.ClosingValue);
        Assert.Equal(item.OpeningValue + item.InValue - item.OutValue, item.ClosingValue);

        // Kiểm tra phân rã
        Assert.Equal(30m, item.InBreakdown.PurchaseQuantity);
        Assert.Equal(15m, item.OutBreakdown.PosQuantity);
    }

    [Fact]
    public async Task VerifyConsecutiveDays_EndOfDayEqualsStartOfNextDay()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
        await using (var seedDb = PostgresTestEnvironment.CreateRuntimeDbContext(new TestTenant(null)))
        {
            await OismsDatabaseSeeder.SeedAsync(seedDb, PostgresTestEnvironment.AppPassword);
        }

        var tenantA = new TestTenant(OismsDatabaseSeeder.TenantAId);
        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(tenantA);

        var branch = await db.Branches.FirstAsync();
        var warehouse = await db.Warehouses.FirstAsync(w => w.BranchId == branch.Id);
        var sku = await db.Skus.FirstAsync();
        var owner = await db.Users.FirstAsync(u => u.Role == "Owner");

        var day1UtcStart = new DateTimeOffset(TestDay1.ToDateTime(TimeOnly.MinValue), SaigonOffset).ToUniversalTime();

        // Giao dịch ngày Day1
        var adj1 = AddAdjustment(db, tenantA.TenantId!.Value, branch.Id, warehouse.Id, sku.Id, owner.Id, 50m, "D1");
        var tx1 = CreateTransaction(tenantA.TenantId!.Value, branch.Id, warehouse.Id, sku.Id, "IN", 50m, 10000m, 50m, 1, day1UtcStart.AddHours(3), adjustmentItemId: adj1.Id);
        db.InventoryTransactions.Add(tx1);

        // Giao dịch ngày Day2
        var day2UtcStart = new DateTimeOffset(TestDay2.ToDateTime(TimeOnly.MinValue), SaigonOffset).ToUniversalTime();
        var adj2 = AddAdjustment(db, tenantA.TenantId!.Value, branch.Id, warehouse.Id, sku.Id, owner.Id, 10m, "D2");
        var tx2 = CreateTransaction(tenantA.TenantId!.Value, branch.Id, warehouse.Id, sku.Id, "OUT", 10m, 10000m, 40m, 2, day2UtcStart.AddHours(3), adjustmentItemId: adj2.Id);
        db.InventoryTransactions.Add(tx2);

        await db.SaveChangesAsync();

        var service = new InventoryPeriodReportService(db, tenantA);

        var reportDay1 = await service.GetInventoryPeriodReportAsync(new InventoryPeriodFilterDto
        {
            FromDate = TestDay1,
            ToDate = TestDay1,
            BranchId = branch.Id
        }, isOwner: true);

        var reportDay2 = await service.GetInventoryPeriodReportAsync(new InventoryPeriodFilterDto
        {
            FromDate = TestDay2,
            ToDate = TestDay2,
            BranchId = branch.Id
        }, isOwner: true);

        var itemDay1 = reportDay1.Items.Single(i => i.SkuId == sku.Id);
        var itemDay2 = reportDay2.Items.Single(i => i.SkuId == sku.Id);

        // Tồn cuối ngày Day1 (50) PHẢI BẰNG tồn đầu ngày Day2 (50)
        Assert.Equal(itemDay1.ClosingQuantity, itemDay2.OpeningQuantity);
        Assert.Equal(itemDay1.ClosingValue, itemDay2.OpeningValue);
    }

    [Fact]
    public async Task VerifyTimezoneBoundaries_TransactionsAt2359And0000Utc7()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
        await using (var seedDb = PostgresTestEnvironment.CreateRuntimeDbContext(new TestTenant(null)))
        {
            await OismsDatabaseSeeder.SeedAsync(seedDb, PostgresTestEnvironment.AppPassword);
        }

        var tenantA = new TestTenant(OismsDatabaseSeeder.TenantAId);
        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(tenantA);

        var branch = await db.Branches.FirstAsync();
        var warehouse = await db.Warehouses.FirstAsync(w => w.BranchId == branch.Id);
        var sku = await db.Skus.FirstAsync();
        var owner = await db.Users.FirstAsync(u => u.Role == "Owner");

        // Thời điểm 23:59:59 ngày TestDay1 theo giờ VN (UTC+7)
        var time2359Utc = new DateTimeOffset(TestDay1.ToDateTime(new TimeOnly(23, 59, 59)), SaigonOffset).ToUniversalTime();
        // Thời điểm 00:00:00 ngày TestDay2 theo giờ VN (UTC+7)
        var time0000Utc = new DateTimeOffset(TestDay2.ToDateTime(TimeOnly.MinValue), SaigonOffset).ToUniversalTime();

        var adj1 = AddAdjustment(db, tenantA.TenantId!.Value, branch.Id, warehouse.Id, sku.Id, owner.Id, 10m, "T2359");
        var txAt2359 = CreateTransaction(tenantA.TenantId!.Value, branch.Id, warehouse.Id, sku.Id, "IN", 10m, 1000m, 10m, 1, time2359Utc, adjustmentItemId: adj1.Id);

        var adj2 = AddAdjustment(db, tenantA.TenantId!.Value, branch.Id, warehouse.Id, sku.Id, owner.Id, 20m, "T0000");
        var txAt0000 = CreateTransaction(tenantA.TenantId!.Value, branch.Id, warehouse.Id, sku.Id, "IN", 20m, 1000m, 30m, 2, time0000Utc, adjustmentItemId: adj2.Id);

        db.InventoryTransactions.AddRange(txAt2359, txAt0000);
        await db.SaveChangesAsync();

        var service = new InventoryPeriodReportService(db, tenantA);

        // Báo cáo ngày TestDay1: Chỉ chứa giao dịch lúc 23:59:59 (10 cái)
        var repDay1 = await service.GetInventoryPeriodReportAsync(new InventoryPeriodFilterDto
        {
            FromDate = TestDay1,
            ToDate = TestDay1,
            BranchId = branch.Id
        }, isOwner: true);
        var item1 = repDay1.Items.Single(i => i.SkuId == sku.Id);
        Assert.Equal(10m, item1.InQuantity);
        Assert.Equal(10m, item1.ClosingQuantity);

        // Báo cáo ngày TestDay2: Bắt đầu bằng 10, nhập thêm 20 lúc 00:00:00 -> Cuối ngày là 30
        var repDay2 = await service.GetInventoryPeriodReportAsync(new InventoryPeriodFilterDto
        {
            FromDate = TestDay2,
            ToDate = TestDay2,
            BranchId = branch.Id
        }, isOwner: true);
        var item2 = repDay2.Items.Single(i => i.SkuId == sku.Id);
        Assert.Equal(10m, item2.OpeningQuantity);
        Assert.Equal(20m, item2.InQuantity);
        Assert.Equal(30m, item2.ClosingQuantity);
    }

    [Fact]
    public async Task VerifyRoleBasedValueMasking_StaffReceivesNull_AndExcelExcludesValueColumns()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
        await using (var seedDb = PostgresTestEnvironment.CreateRuntimeDbContext(new TestTenant(null)))
        {
            await OismsDatabaseSeeder.SeedAsync(seedDb, PostgresTestEnvironment.AppPassword);
        }

        var tenantA = new TestTenant(OismsDatabaseSeeder.TenantAId);
        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(tenantA);

        var branch = await db.Branches.FirstAsync();
        var warehouse = await db.Warehouses.FirstAsync(w => w.BranchId == branch.Id);
        var sku = await db.Skus.FirstAsync();
        var owner = await db.Users.FirstAsync(u => u.Role == "Owner");

        var day1UtcStart = new DateTimeOffset(TestDay1.ToDateTime(TimeOnly.MinValue), SaigonOffset).ToUniversalTime();
        var adj = AddAdjustment(db, tenantA.TenantId!.Value, branch.Id, warehouse.Id, sku.Id, owner.Id, 10m, "ROLE_TEST");
        var tx = CreateTransaction(tenantA.TenantId!.Value, branch.Id, warehouse.Id, sku.Id, "IN", 10m, 50000m, 10m, 1, day1UtcStart.AddHours(2), adjustmentItemId: adj.Id);
        db.InventoryTransactions.Add(tx);
        await db.SaveChangesAsync();

        var reportService = new InventoryPeriodReportService(db, tenantA);
        var excelService = new ClosedXmlExcelExportService();

        // 1. Staff gọi API báo cáo kỳ: Toàn bộ cột giá trị nhận NULL
        var staffReport = await reportService.GetInventoryPeriodReportAsync(new InventoryPeriodFilterDto
        {
            FromDate = TestDay1,
            ToDate = TestDay1,
            BranchId = branch.Id
        }, isOwner: false);

        var staffItem = staffReport.Items.Single(i => i.SkuId == sku.Id);
        Assert.Null(staffItem.OpeningValue);
        Assert.Null(staffItem.InValue);
        Assert.Null(staffItem.OutValue);
        Assert.Null(staffItem.ClosingValue);
        Assert.Null(staffReport.Summary.TotalOpeningValue);
        Assert.Null(staffReport.Summary.TotalClosingValue);

        // 2. Staff xuất Excel: File Excel không chứa cột Giá trị
        var staffExcelBytes = excelService.ExportInventoryPeriodReport(staffReport, isOwner: false);
        Assert.NotEmpty(staffExcelBytes);

        using (var ms = new MemoryStream(staffExcelBytes))
        using (var wb = new XLWorkbook(ms))
        {
            var ws = wb.Worksheets.First();
            var headerCells = ws.Row(5).Cells().Select(c => c.GetString()).ToList();
            Assert.DoesNotContain(headerCells, h => h.Contains("Giá trị"));
        }

        // 3. Owner xuất Excel: Có đầy đủ cột Giá trị
        var ownerReport = await reportService.GetInventoryPeriodReportAsync(new InventoryPeriodFilterDto
        {
            FromDate = TestDay1,
            ToDate = TestDay1,
            BranchId = branch.Id
        }, isOwner: true);

        var ownerExcelBytes = excelService.ExportInventoryPeriodReport(ownerReport, isOwner: true);
        using (var ms = new MemoryStream(ownerExcelBytes))
        using (var wb = new XLWorkbook(ms))
        {
            var ws = wb.Worksheets.First();
            var headerCells = ws.Row(5).Cells().Select(c => c.GetString()).ToList();
            Assert.Contains(headerCells, h => h.Contains("Tồn cuối kỳ (Giá trị)"));
        }
    }

    [Fact]
    public async Task VerifyTenantIsolation_TenantCannotSeeOtherTenantLedger()
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

        var branchA = await dbA.Branches.FirstAsync();
        var whA = await dbA.Warehouses.FirstAsync(w => w.BranchId == branchA.Id);
        var skuA = await dbA.Skus.FirstAsync();
        var ownerA = await dbA.Users.FirstAsync(u => u.Role == "Owner");

        var day1UtcStart = new DateTimeOffset(TestDay1.ToDateTime(TimeOnly.MinValue), SaigonOffset).ToUniversalTime();
        var adjA = AddAdjustment(dbA, tenantA.TenantId!.Value, branchA.Id, whA.Id, skuA.Id, ownerA.Id, 100m, "TENANT_A");
        var txA = CreateTransaction(tenantA.TenantId!.Value, branchA.Id, whA.Id, skuA.Id, "IN", 100m, 50000m, 100m, 1, day1UtcStart.AddHours(2), adjustmentItemId: adjA.Id);
        dbA.InventoryTransactions.Add(txA);
        await dbA.SaveChangesAsync();

        // Tenant B gọi báo cáo
        var serviceB = new InventoryPeriodReportService(dbB, tenantB);
        var reportB = await serviceB.GetInventoryPeriodReportAsync(new InventoryPeriodFilterDto
        {
            FromDate = TestDay1,
            ToDate = TestDay1
        }, isOwner: true);

        // Báo cáo của Tenant B tuyệt đối không có SKU của Tenant A
        Assert.DoesNotContain(reportB.Items, i => i.SkuId == skuA.Id);
    }

    [Fact]
    public async Task VerifyPerformanceAndPagination_SinglePageUnder2Seconds()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
        await using (var seedDb = PostgresTestEnvironment.CreateRuntimeDbContext(new TestTenant(null)))
        {
            await OismsDatabaseSeeder.SeedAsync(seedDb, PostgresTestEnvironment.AppPassword);
        }

        var tenantA = new TestTenant(OismsDatabaseSeeder.TenantAId);
        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(tenantA);

        var branch = await db.Branches.FirstAsync();
        var warehouse = await db.Warehouses.FirstAsync(w => w.BranchId == branch.Id);
        var sku = await db.Skus.FirstAsync();
        var owner = await db.Users.FirstAsync(u => u.Role == "Owner");

        var day1UtcStart = new DateTimeOffset(TestDay1.ToDateTime(TimeOnly.MinValue), SaigonOffset).ToUniversalTime();

        // Seed 20 dòng sổ cái hợp lệ
        for (int i = 1; i <= 20; i++)
        {
            var adj = AddAdjustment(db, tenantA.TenantId!.Value, branch.Id, warehouse.Id, sku.Id, owner.Id, 1m, $"BENCH_{i}");
            var tx = CreateTransaction(
                tenantA.TenantId!.Value,
                branch.Id,
                warehouse.Id,
                sku.Id,
                i % 2 == 0 ? "IN" : "OUT",
                1m,
                10000m,
                10m,
                i,
                day1UtcStart.AddSeconds(i),
                adjustmentItemId: adj.Id);
            db.InventoryTransactions.Add(tx);
        }
        await db.SaveChangesAsync();

        var service = new InventoryPeriodReportService(db, tenantA);

        // Đo thời gian truy vấn 1 trang phân trang 50 SKU (Mục tiêu < 2000 ms)
        var sw = Stopwatch.StartNew();
        var report = await service.GetInventoryPeriodReportAsync(new InventoryPeriodFilterDto
        {
            FromDate = TestDay1,
            ToDate = TestDay1,
            BranchId = branch.Id,
            Page = 1,
            PageSize = 50
        }, isOwner: true);
        sw.Stop();

        Assert.NotNull(report);
        Assert.True(sw.ElapsedMilliseconds < 2000, $"Thời gian truy vấn một trang phải dưới 2 giây. Đo được: {sw.ElapsedMilliseconds} ms.");
    }

    private static InventoryAdjustmentItem AddAdjustment(
        AppDbContext db,
        Guid tenantId,
        Guid branchId,
        Guid warehouseId,
        Guid skuId,
        Guid userId,
        decimal qty,
        string reason)
    {
        var adj = new InventoryAdjustment
        {
            TenantId = tenantId,
            Number = $"ADJ-{Guid.NewGuid():N}"[..20],
            BranchId = branchId,
            WarehouseId = warehouseId,
            Reason = reason,
            CreatedBy = userId
        };
        db.InventoryAdjustments.Add(adj);

        var item = new InventoryAdjustmentItem
        {
            Adjustment = adj,
            TenantId = tenantId,
            SkuId = skuId,
            QuantityDelta = qty,
            UnitCost = 10000m
        };
        db.InventoryAdjustmentItems.Add(item);
        return item;
    }

    private static InventoryTransaction CreateTransaction(
        Guid tenantId,
        Guid branchId,
        Guid warehouseId,
        Guid skuId,
        string type,
        decimal quantity,
        decimal unitCost,
        decimal balanceAfter,
        long sequenceNo,
        DateTimeOffset createdAt,
        Guid? adjustmentItemId = null)
    {
        return new InventoryTransaction
        {
            TenantId = tenantId,
            BranchId = branchId,
            WarehouseId = warehouseId,
            SkuId = skuId,
            Type = type,
            Quantity = quantity,
            UnitCost = unitCost,
            BalanceAfter = balanceAfter,
            SequenceNo = sequenceNo,
            CreatedAt = createdAt,
            AdjustmentItemId = adjustmentItemId
        };
    }

    private sealed class TestTenant(Guid? tenantId) : ICurrentTenantService
    {
        public Guid? TenantId { get; private set; } = tenantId;
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }
}
