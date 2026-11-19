using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Helpers;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Reports.Dtos;
using OISM.Application.Features.Reports.Services;
using OISM.Application.Features.Inventory.Services;
using OISM.Application.Features.StockTransfers.Dtos;
using OISM.Application.Features.StockTransfers.Services;
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
[Collection("PostgresIntegrationTests")]
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
        var supplier = await db.Suppliers.FirstAsync();

        var day1UtcStart = new DateTimeOffset(TestDay1.ToDateTime(TimeOnly.MinValue), SaigonOffset).ToUniversalTime();

        var openAdjItem = AddConfirmedAdjustmentItem(db, warehouse, sku, owner.Id, 20m, 50000m, "ADJ-OPEN-1", day1UtcStart.AddHours(-2));

        var receipt = new GoodsReceipt
        {
            TenantId = tenantA.TenantId!.Value,
            BranchId = branch.Id,
            WarehouseId = warehouse.Id,
            SupplierId = supplier.Id,
            Number = "GR-P1-001",
            Status = GoodsReceiptStatus.Confirmed,
            CreatedBy = owner.Id,
            ConfirmedBy = owner.Id,
            ConfirmedAt = day1UtcStart.AddHours(2)
        };
        var receiptItem = new GoodsReceiptItem
        {
            Receipt = receipt,
            TenantId = tenantA.TenantId!.Value,
            SkuId = sku.Id,
            Quantity = 30m,
            UnitPrice = 60000m,
            FactorSnapshot = 1m
        };
        db.GoodsReceiptItems.Add(receiptItem);

        var posOrder = new Order
        {
            TenantId = tenantA.TenantId!.Value,
            BranchId = branch.Id,
            Number = "POS-P1-001",
            Channel = "POS",
            Status = OrderStatus.Completed,
            ConfirmedBy = owner.Id,
            ConfirmedAt = day1UtcStart.AddHours(5),
            CompletedAt = day1UtcStart.AddHours(5),
            TotalAmount = 15m * 100000m
        };
        var posOrderItem = new OrderItem
        {
            Order = posOrder,
            TenantId = tenantA.TenantId!.Value,
            SkuId = sku.Id,
            SkuCodeSnapshot = sku.SKU,
            SkuNameSnapshot = sku.VariantName,
            Quantity = 15m,
            UnitPrice = 100000m,
            CostPrice = 56000m,
            FactorSnapshot = 1m
        };
        db.OrderItems.Add(posOrderItem);
        await db.SaveChangesAsync();

        // 1. Dòng trước kỳ (đầu kỳ): 20 cái giá 50.000
        var openTx = CreateTransaction(tenantA.TenantId!.Value, branch.Id, warehouse.Id, sku.Id, "IN", 20m, 50000m, 20m, 1, day1UtcStart.AddHours(-2));
        openTx.AdjustmentItemId = openAdjItem.Id;
        db.InventoryTransactions.Add(openTx);

        // 2. Giao dịch trong kỳ Day1:
        // Nhập mua: +30 cái giá 60.000
        var poTx = CreateTransaction(tenantA.TenantId!.Value, branch.Id, warehouse.Id, sku.Id, "IN", 30m, 60000m, 50m, 2, day1UtcStart.AddHours(2));
        poTx.ReceiptItemId = receiptItem.Id;
        db.InventoryTransactions.Add(poTx);

        // Xuất bán POS: -15 cái giá vốn 56.000
        var posTx = CreateTransaction(tenantA.TenantId!.Value, branch.Id, warehouse.Id, sku.Id, "OUT", 15m, 56000m, 35m, 3, day1UtcStart.AddHours(5));
        posTx.OrderItemId = posOrderItem.Id;
        db.InventoryTransactions.Add(posTx);

        await SetBalanceAsync(db, warehouse, sku, onHand: 35m, sequence: 3, averageCost: 56000m);
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
        var day2UtcStart = new DateTimeOffset(TestDay2.ToDateTime(TimeOnly.MinValue), SaigonOffset).ToUniversalTime();

        var adj1 = AddConfirmedAdjustmentItem(db, warehouse, sku, owner.Id, 50m, 10000m, "ADJ-CD-1", day1UtcStart.AddHours(3));
        var adj2 = AddConfirmedAdjustmentItem(db, warehouse, sku, owner.Id, -10m, 10000m, "ADJ-CD-2", day2UtcStart.AddHours(3));
        await db.SaveChangesAsync();

        // Giao dịch ngày Day1
        var tx1 = CreateTransaction(tenantA.TenantId!.Value, branch.Id, warehouse.Id, sku.Id, "IN", 50m, 10000m, 50m, 1, day1UtcStart.AddHours(3));
        tx1.AdjustmentItemId = adj1.Id;
        db.InventoryTransactions.Add(tx1);

        // Giao dịch ngày Day2
        var tx2 = CreateTransaction(tenantA.TenantId!.Value, branch.Id, warehouse.Id, sku.Id, "OUT", 10m, 10000m, 40m, 2, day2UtcStart.AddHours(3));
        tx2.AdjustmentItemId = adj2.Id;
        db.InventoryTransactions.Add(tx2);

        await SetBalanceAsync(db, warehouse, sku, onHand: 40m, sequence: 2, averageCost: 10000m);
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

        var adj2359 = AddConfirmedAdjustmentItem(db, warehouse, sku, owner.Id, 10m, 1000m, "ADJ-TZ-2359", time2359Utc);
        var adj0000 = AddConfirmedAdjustmentItem(db, warehouse, sku, owner.Id, 20m, 1000m, "ADJ-TZ-0000", time0000Utc);
        await db.SaveChangesAsync();

        var txAt2359 = CreateTransaction(tenantA.TenantId!.Value, branch.Id, warehouse.Id, sku.Id, "IN", 10m, 1000m, 10m, 1, time2359Utc);
        txAt2359.AdjustmentItemId = adj2359.Id;
        var txAt0000 = CreateTransaction(tenantA.TenantId!.Value, branch.Id, warehouse.Id, sku.Id, "IN", 20m, 1000m, 30m, 2, time0000Utc);
        txAt0000.AdjustmentItemId = adj0000.Id;

        db.InventoryTransactions.AddRange(txAt2359, txAt0000);
        await SetBalanceAsync(db, warehouse, sku, onHand: 30m, sequence: 2, averageCost: 1000m);
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
        var adjMask = AddConfirmedAdjustmentItem(db, warehouse, sku, owner.Id, 10m, 50000m, "ADJ-MASK-1", day1UtcStart.AddHours(2));
        await db.SaveChangesAsync();

        var tx = CreateTransaction(tenantA.TenantId!.Value, branch.Id, warehouse.Id, sku.Id, "IN", 10m, 50000m, 10m, 1, day1UtcStart.AddHours(2));
        tx.AdjustmentItemId = adjMask.Id;
        db.InventoryTransactions.Add(tx);
        await SetBalanceAsync(db, warehouse, sku, onHand: 10m, sequence: 1, averageCost: 50000m);
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

        // 3. Owner xuất Excel: File Excel chứa đầy đủ cột Giá trị
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
        var adjIso = AddConfirmedAdjustmentItem(dbA, whA, skuA, ownerA.Id, 100m, 50000m, "ADJ-ISO-1", day1UtcStart.AddHours(2));
        await dbA.SaveChangesAsync();

        var txA = CreateTransaction(tenantA.TenantId!.Value, branchA.Id, whA.Id, skuA.Id, "IN", 100m, 50000m, 100m, 1, day1UtcStart.AddHours(2));
        txA.AdjustmentItemId = adjIso.Id;
        dbA.InventoryTransactions.Add(txA);
        await SetBalanceAsync(dbA, whA, skuA, onHand: 100m, sequence: 1, averageCost: 50000m);
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
    public async Task VerifyPerformanceAndPagination_100kLedgerRows_SinglePageUnder2Seconds()
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

        // Seed 100.000 dòng sổ cái (Q12): 50.000 dòng trước kỳ và 50.000 dòng trong kỳ Day1, mỗi dòng trỏ tới một
        // dòng phiếu điều chỉnh thật (FK hợp lệ). Sau đó đồng bộ số dư bằng EF để trigger oisms_check_stock kiểm tra
        // toàn bộ sổ cái một lần khi commit.
        const int ledgerRows = 100_000;
        await InsertBulkLedgerAsync(tenantA.TenantId!.Value, branch.Id, warehouse.Id, sku.Id, owner.Id, ledgerRows, day1UtcStart);
        await SetBalanceAsync(db, warehouse, sku, onHand: 0m, sequence: ledgerRows, averageCost: 10000m);
        await db.SaveChangesAsync();
        Assert.Equal(ledgerRows, await db.InventoryTransactions.CountAsync(t => t.SkuId == sku.Id));

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

    [Fact]
    public async Task VerifyAllBranchesInTransit_ClosingFromLedger_BranchesPlusInTransitEqualsStoreTotal()
    {
        // Luồng thật: nhập mua ở chi nhánh A, gửi phiếu chuyển A → B (InTransit, A đã ghi OUT, B chưa ghi IN).
        await PostgresTestEnvironment.ResetTestDataAsync();
        await using (var seedDb = PostgresTestEnvironment.CreateRuntimeDbContext(new TestTenant(null)))
        {
            await OismsDatabaseSeeder.SeedAsync(seedDb, PostgresTestEnvironment.AppPassword);
        }

        var tenantA = new TestTenant(OismsDatabaseSeeder.TenantAId);
        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchAId, warehouseAId, branchBId, warehouseBId, supplierId, ownerId, skuId;
        await using (var setupDb = PostgresTestEnvironment.CreateRuntimeDbContext(tenantA))
        {
            var branchA = await setupDb.Branches.FirstAsync();
            branchAId = branchA.Id;
            warehouseAId = (await setupDb.Warehouses.FirstAsync(w => w.BranchId == branchAId)).Id;
            supplierId = (await setupDb.Suppliers.FirstAsync()).Id;
            ownerId = (await setupDb.Users.FirstAsync(u => u.Role == "Owner")).Id;
            skuId = (await setupDb.ProductVariants.FirstAsync(v => v.IsActive)).Id;

            var branchB = new Branch
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Chi nhánh Quận 3 (G2 in-transit)",
                Address = "456 CMT8, Quận 3, TP.HCM",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            var warehouseB = new Warehouse
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BranchId = branchB.Id,
                Name = "Kho Tổng Quận 3 (G2 in-transit)",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            setupDb.Branches.Add(branchB);
            setupDb.Warehouses.Add(warehouseB);
            await setupDb.SaveChangesAsync();
            branchBId = branchB.Id;
            warehouseBId = warehouseB.Id;
        }

        await using (var receiveDb = PostgresTestEnvironment.CreateRuntimeDbContext(tenantA))
        {
            await PurchaseOrderSeed.ReceiveStockAsync(receiveDb, tenantA, branchAId, warehouseAId, supplierId, ownerId, (skuId, 50m, 20000m));
        }

        await using (var transferDb = PostgresTestEnvironment.CreateRuntimeDbContext(tenantA))
        {
            var transferService = new StockTransferService(
                transferDb,
                tenantA,
                new WacCalculator(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<StockTransferService>.Instance);
            var transfer = await transferService.CreateTransferAsync(new CreateStockTransferRequestDto
            {
                RequestKey = Guid.NewGuid(),
                FromBranchId = branchAId,
                ToBranchId = branchBId,
                FromWarehouseId = warehouseAId,
                ToWarehouseId = warehouseBId,
                Items = new List<StockTransferItemRequestDto> { new() { SkuId = skuId, Quantity = 10m } }
            }, ownerId);
            await transferService.DispatchTransferAsync(transfer.Id, new DispatchStockTransferRequestDto(), ownerId);
        }

        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(tenantA);
        var service = new InventoryPeriodReportService(db, tenantA);
        var today = ReportTimeHelper.ToLocalDate(DateTimeOffset.UtcNow);
        var branchBalanceSum = await db.InventoryBalances.Where(b => b.SkuId == skuId).SumAsync(b => b.OnHand);

        // 1. Chế độ Tất cả: chuyển nội bộ không vào nhập/xuất; cuối kỳ = Σ tồn chi nhánh theo sổ cái
        var all = await service.GetInventoryPeriodReportAsync(new InventoryPeriodFilterDto
        {
            FromDate = today,
            ToDate = today,
            Page = 1,
            PageSize = 100
        }, isOwner: true);
        var allItem = all.Items.Single(i => i.SkuId == skuId);

        Assert.Equal(0m, allItem.OpeningQuantity);
        Assert.Equal(0m, allItem.OpeningInTransitQuantity);
        Assert.Equal(50m, allItem.InQuantity);
        Assert.Equal(0m, allItem.OutQuantity);
        Assert.Equal(0m, allItem.InBreakdown.TransferInQuantity);
        Assert.Equal(0m, allItem.OutBreakdown.TransferOutQuantity);
        Assert.Equal(40m, allItem.ClosingQuantity);
        Assert.Equal(branchBalanceSum, allItem.ClosingQuantity);
        Assert.Equal(10m, allItem.ClosingInTransitQuantity);
        // Đối chiếu Q9: (đầu kỳ + đang chuyển đầu) + nhập − xuất = (cuối kỳ + đang chuyển cuối) = tổng cửa hàng
        Assert.Equal(
            allItem.OpeningQuantity + allItem.OpeningInTransitQuantity + allItem.InQuantity - allItem.OutQuantity,
            allItem.ClosingQuantity + allItem.ClosingInTransitQuantity);
        Assert.Equal(
            allItem.OpeningValue + allItem.OpeningInTransitValue + allItem.InValue - allItem.OutValue,
            allItem.ClosingValue + allItem.ClosingInTransitValue);
        Assert.Equal(200000m, allItem.ClosingInTransitValue);

        // 2. Một chi nhánh A: giữ chuyển đi trong xuất, đầu + nhập − xuất = cuối
        var branchA_ = await service.GetInventoryPeriodReportAsync(new InventoryPeriodFilterDto
        {
            FromDate = today,
            ToDate = today,
            BranchId = branchAId,
            Page = 1,
            PageSize = 100
        }, isOwner: true);
        var aItem = branchA_.Items.Single(i => i.SkuId == skuId);
        Assert.Equal(10m, aItem.OutBreakdown.TransferOutQuantity);
        Assert.Equal(40m, aItem.ClosingQuantity);
        Assert.Equal(aItem.OpeningQuantity + aItem.InQuantity - aItem.OutQuantity, aItem.ClosingQuantity);
        Assert.Equal(aItem.OpeningValue + aItem.InValue - aItem.OutValue, aItem.ClosingValue);

        // 3. Một chi nhánh B: chưa nhận nên tồn 0, cột "Đang chuyển đến" = 10
        var branchB_ = await service.GetInventoryPeriodReportAsync(new InventoryPeriodFilterDto
        {
            FromDate = today,
            ToDate = today,
            BranchId = branchBId,
            Page = 1,
            PageSize = 100
        }, isOwner: false);
        var bItem = branchB_.Items.Single(i => i.SkuId == skuId);
        Assert.Equal(0m, bItem.ClosingQuantity);
        Assert.Equal(10m, bItem.ClosingInTransitQuantity);
        Assert.Null(bItem.ClosingInTransitValue);
    }

    [Fact]
    public async Task VerifyExcelExport_ContainsEverySkuBeyondFirstPage()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
        await using (var seedDb = PostgresTestEnvironment.CreateRuntimeDbContext(new TestTenant(null)))
        {
            await OismsDatabaseSeeder.SeedAsync(seedDb, PostgresTestEnvironment.AppPassword);
        }

        var tenantA = new TestTenant(OismsDatabaseSeeder.TenantAId);
        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(tenantA);
        var product = await db.Products.FirstAsync();
        for (var i = 0; i < 130; i++)
        {
            db.ProductVariants.Add(new ProductVariant
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                ProductId = product.Id,
                Code = $"G2-EXPORT-{i:D3}",
                SKU = $"G2-EXPORT-{i:D3}",
                RetailPrice = 1000m,
                WholesalePrice = 900m,
                Unit = "piece",
                IsActive = true
            });
        }
        await db.SaveChangesAsync();
        var totalSkus = await db.Skus.CountAsync();
        Assert.True(totalSkus > 100);

        var service = new InventoryPeriodReportService(db, tenantA);
        var filter = new InventoryPeriodFilterDto { FromDate = TestDay1, ToDate = TestDay1, Page = 1, PageSize = 10000 };

        var onePage = await service.GetInventoryPeriodReportAsync(filter, isOwner: false);
        Assert.Equal(100, onePage.Items.Count);

        var export = await service.GetInventoryPeriodReportForExportAsync(filter, isOwner: false);
        Assert.Equal(totalSkus, export.Items.Count);
        Assert.Equal(totalSkus, export.Items.Select(i => i.SkuId).Distinct().Count());

        var bytes = new ClosedXmlExcelExportService().ExportInventoryPeriodReport(export, isOwner: false);
        using var ms = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(ms);
        var sheetText = string.Join("\n", workbook.Worksheet(1).CellsUsed().Select(c => c.GetString()));
        Assert.Contains("G2-EXPORT-129", sheetText);
    }

    /// <summary>
    /// Chèn nhanh <paramref name="rowCount"/> dòng sổ (một nửa trước mốc, một nửa sau mốc) bằng generate_series.
    /// Mỗi dòng sổ trỏ tới một dòng của phiếu điều chỉnh Confirmed riêng nên FK và CHECK đều thỏa.
    /// Chỉ bỏ trigger theo dòng (session_replication_role = replica) vì trigger tồn kho chạy lại toàn bộ tổng sổ cái
    /// cho từng dòng (O(n²)); người gọi đồng bộ số dư sau đó để trigger kiểm tra một lần.
    /// </summary>
    private static async Task InsertBulkLedgerAsync(
        Guid tenantId, Guid branchId, Guid warehouseId, Guid skuId, Guid userId, int rowCount, DateTimeOffset pivotUtc)
    {
        await using var conn = new Npgsql.NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var role = new Npgsql.NpgsqlCommand("SET LOCAL session_replication_role = replica", conn, tx))
        {
            await role.ExecuteNonQueryAsync();
        }

        var seed = Guid.NewGuid().ToString("N");
        await using var cmd = new Npgsql.NpgsqlCommand(
            """
            INSERT INTO public.inventory_adjustments
                (id, tenant_id, number, branch_id, warehouse_id, reason, status, created_by, confirmed_by, confirmed_at)
            SELECT md5('adj' || @seed || g)::uuid, @tenant, 'ADJ-PERF-' || g, @branch, @warehouse, 'Fixture hiệu năng',
                   'Confirmed', @user, @user, @pivot + ((g - @half) * interval '1 second')
            FROM generate_series(1, @rows) AS g;

            INSERT INTO public.inventory_adjustment_items (id, tenant_id, adjustment_id, sku_id, quantity_delta, unit_cost)
            SELECT md5(@seed || g)::uuid, @tenant, md5('adj' || @seed || g)::uuid, @sku, CASE WHEN g % 2 = 0 THEN -1 ELSE 1 END, 10000
            FROM generate_series(1, @rows) AS g;

            INSERT INTO public.inventory_transactions
                (id, tenant_id, branch_id, warehouse_id, sku_id, type, quantity, unit_cost, balance_after, sequence_no, adjustment_item_id, created_at)
            SELECT gen_random_uuid(), @tenant, @branch, @warehouse, @sku,
                   CASE WHEN g % 2 = 0 THEN 'OUT' ELSE 'IN' END,
                   1, 10000, g % 2, g, md5(@seed || g)::uuid,
                   @pivot + ((g - @half) * interval '1 second')
            FROM generate_series(1, @rows) AS g;

            ANALYZE public.inventory_transactions;
            """, conn, tx);
        cmd.CommandTimeout = 300;
        cmd.Parameters.AddWithValue("tenant", tenantId);
        cmd.Parameters.AddWithValue("branch", branchId);
        cmd.Parameters.AddWithValue("warehouse", warehouseId);
        cmd.Parameters.AddWithValue("sku", skuId);
        cmd.Parameters.AddWithValue("user", userId);
        cmd.Parameters.AddWithValue("seed", seed);
        cmd.Parameters.AddWithValue("pivot", pivotUtc.ToUniversalTime());
        cmd.Parameters.AddWithValue("half", rowCount / 2);
        cmd.Parameters.AddWithValue("rows", rowCount);
        await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
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
        DateTimeOffset createdAt)
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
            CreatedAt = createdAt
        };
    }

    private static InventoryAdjustmentItem AddConfirmedAdjustmentItem(
        AppDbContext db,
        Warehouse warehouse,
        ProductVariant sku,
        Guid userId,
        decimal quantityDelta,
        decimal unitCost,
        string number,
        DateTimeOffset confirmedAt)
    {
        var adjustment = new InventoryAdjustment
        {
            TenantId = sku.TenantId,
            Number = number,
            BranchId = warehouse.BranchId,
            WarehouseId = warehouse.Id,
            Reason = "Fixture",
            CreatedBy = userId
        };
        adjustment.Confirm(userId, confirmedAt);
        var item = new InventoryAdjustmentItem
        {
            Adjustment = adjustment,
            TenantId = sku.TenantId,
            SkuId = sku.Id,
            QuantityDelta = quantityDelta,
            UnitCost = unitCost
        };
        db.InventoryAdjustmentItems.Add(item);
        return item;
    }

    private static async Task SetBalanceAsync(
        AppDbContext db,
        Warehouse warehouse,
        ProductVariant sku,
        decimal onHand,
        long sequence,
        decimal averageCost)
    {
        var branchBalance = await db.InventoryBalances
            .FirstOrDefaultAsync(b => b.BranchId == warehouse.BranchId && b.SkuId == sku.Id);
        if (branchBalance == null)
        {
            branchBalance = new InventoryBalance
            {
                TenantId = sku.TenantId,
                BranchId = warehouse.BranchId,
                SkuId = sku.Id
            };
            db.InventoryBalances.Add(branchBalance);
        }
        branchBalance.OnHand = onHand;
        branchBalance.LastSequence = sequence;
        branchBalance.AverageCost = averageCost;

        var whBalance = await db.WarehouseInventoryBalances
            .FirstOrDefaultAsync(b => b.WarehouseId == warehouse.Id && b.SkuId == sku.Id);
        if (whBalance == null)
        {
            whBalance = new WarehouseInventoryBalance
            {
                TenantId = sku.TenantId,
                BranchId = warehouse.BranchId,
                WarehouseId = warehouse.Id,
                SkuId = sku.Id
            };
            db.WarehouseInventoryBalances.Add(whBalance);
        }
        whBalance.OnHand = onHand;
        whBalance.LastSequence = sequence;
    }

    private sealed class TestTenant(Guid? tenantId) : ICurrentTenantService
    {
        public Guid? TenantId { get; private set; } = tenantId;
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }
}
