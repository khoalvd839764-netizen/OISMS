using OISM.Domain.Common;
using OISM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Helpers;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Reports.Dtos;

namespace OISM.Application.Features.Reports.Services;

public class ReportService : IReportService
{
    private const string UnknownUnitName = "Chưa xác định";

    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _currentTenantService;

    public ReportService(IAppDbContext context, ICurrentTenantService currentTenantService)
    {
        _context = context;
        _currentTenantService = currentTenantService;
    }

    private Guid RequireTenantId()
    {
        var tenantId = _currentTenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Yêu cầu xác thực gian hàng (TenantId hợp lệ).");
        }
        return tenantId.Value;
    }

    private static readonly HashSet<string> MarketplaceChannels = new(StringComparer.OrdinalIgnoreCase) { "Shopee", "TikTok", "Lazada" };

    /// <summary>
    /// Doanh thu bị giảm do một phiếu trả hàng đã xác nhận.
    /// Đơn POS/Admin: bằng số tiền thực hoàn cho khách (TotalRefundAmount, đã trừ phí trả hàng).
    /// Đơn sàn: phiếu không ghi hoàn tiền (sàn hoàn cho khách) nhưng doanh thu của shop vẫn giảm,
    /// nên trừ theo giá trị dòng trả = SL trả × đơn giá snapshot (quyết định người dùng 09/11/2026).
    /// Cần Include(Order) và Items.OrderItem.
    /// </summary>
    private static decimal ReturnRevenueAmount(SalesReturn r)
    {
        if (r.Order != null && MarketplaceChannels.Contains(r.Order.Channel))
        {
            return r.Items.Sum(i => i.Quantity * (i.OrderItem?.UnitPrice ?? 0m));
        }

        return r.TotalRefundAmount;
    }

    public Task<RevenueProfitReportDto> GetRevenueProfitReportAsync(ReportFilterDto filter, CancellationToken ct = default)
        => GetRevenueProfitReportAsync(filter, isOwner: true, ct);

    public async Task<RevenueProfitReportDto> GetRevenueProfitReportAsync(ReportFilterDto filter, bool isOwner, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var ordersQuery = _context.Orders
            .AsNoTracking()
            .Include(o => o.Branch)
            .Include(o => o.Items)
            .Where(o => o.TenantId == tenantId && (o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Completed));

        if (filter.BranchId.HasValue && filter.BranchId.Value != Guid.Empty)
        {
            ordersQuery = ordersQuery.Where(o => o.BranchId == filter.BranchId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Channel))
        {
            var channelTrim = filter.Channel.Trim();
            ordersQuery = ordersQuery.Where(o => o.Channel == channelTrim);
        }

        DateTimeOffset? endBoundary = null;
        if (filter.EndDate.HasValue)
        {
            var boundary = filter.EndDate.Value;
            // [BF-011] Khoảng nửa mở [StartDate, EndDate) chống bỏ sót giao dịch có phần lẻ giây 23:59:59.xxx
            if (boundary.Second == 59)
            {
                boundary = new DateTimeOffset(
                    boundary.Year,
                    boundary.Month,
                    boundary.Day,
                    boundary.Hour,
                    boundary.Minute,
                    59,
                    boundary.Offset).AddSeconds(1);
            }
            endBoundary = boundary;
            ordersQuery = ordersQuery.Where(o => o.CreatedAt < boundary);
        }

        if (filter.StartDate.HasValue)
        {
            ordersQuery = ordersQuery.Where(o => o.CreatedAt >= filter.StartDate.Value);
        }

        var orders = await ordersQuery.ToListAsync(ct);

        // Phiếu trả hàng đã xác nhận (Confirmed) theo ngày confirmed_at
        var returnsQuery = _context.SalesReturns
            .AsNoTracking()
            .Include(r => r.Order)
            .Include(r => r.Branch)
            .Include(r => r.Items)
                .ThenInclude(i => i.OrderItem)
            .Where(r => r.TenantId == tenantId && r.Status == SalesReturnStatus.Confirmed && r.ConfirmedAt != null);

        if (filter.BranchId.HasValue && filter.BranchId.Value != Guid.Empty)
        {
            returnsQuery = returnsQuery.Where(r => r.BranchId == filter.BranchId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Channel))
        {
            var channelTrim = filter.Channel.Trim();
            returnsQuery = returnsQuery.Where(r => r.Order != null && r.Order.Channel == channelTrim);
        }

        if (filter.StartDate.HasValue)
        {
            returnsQuery = returnsQuery.Where(r => r.ConfirmedAt >= filter.StartDate.Value);
        }

        if (endBoundary.HasValue)
        {
            returnsQuery = returnsQuery.Where(r => r.ConfirmedAt < endBoundary.Value);
        }

        var returns = await returnsQuery.ToListAsync(ct);

        decimal CalculateReturnCogs(SalesReturn r)
        {
            return r.Items.Sum(i => i.Quantity * (i.OrderItem != null ? i.OrderItem.FactorSnapshot : 1m) * i.OriginalUnitCost);
        }

        decimal totalRevenue = 0;
        decimal totalOrderCogs = 0;
        decimal totalItemsSold = 0;

        foreach (var order in orders)
        {
            totalRevenue += order.TotalAmount;
            foreach (var item in order.Items)
            {
                totalItemsSold += item.BaseQuantity;
                var cost = (item.CostPrice ?? 0) * item.BaseQuantity;
                totalOrderCogs += cost;
            }
        }

        decimal totalReturnAmount = returns.Sum(r => ReturnRevenueAmount(r));
        decimal totalReturnCogs = returns.Sum(r => CalculateReturnCogs(r));

        decimal netRevenue = totalRevenue - totalReturnAmount;
        decimal netCogs = totalOrderCogs - totalReturnCogs;

        decimal? grossProfit = isOwner ? (netRevenue - netCogs) : null;
        decimal? margin = (isOwner && netRevenue > 0 && grossProfit.HasValue)
            ? Math.Round((grossProfit.Value / netRevenue) * 100, 2)
            : (decimal?)null;

        // Phân rã theo Kênh bán (Channel)
        var allChannels = orders.Select(o => o.Channel)
            .Concat(returns.Select(r => r.Order != null ? r.Order.Channel : "N/A"))
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct()
            .ToList();

        var byChannel = allChannels
            .Select(ch =>
            {
                var chOrders = orders.Where(o => o.Channel == ch).ToList();
                var chReturns = returns.Where(r => (r.Order != null ? r.Order.Channel : "N/A") == ch).ToList();

                var chRevenue = chOrders.Sum(o => o.TotalAmount);
                var chReturnAmount = chReturns.Sum(r => ReturnRevenueAmount(r));
                var chNetRevenue = chRevenue - chReturnAmount;

                var chCogs = chOrders.SelectMany(o => o.Items).Sum(i => (i.CostPrice ?? 0) * i.BaseQuantity);
                var chReturnCogs = chReturns.Sum(r => CalculateReturnCogs(r));
                var chNetCogs = chCogs - chReturnCogs;

                var chProfit = isOwner ? (chNetRevenue - chNetCogs) : (decimal?)null;
                var chMargin = (isOwner && chNetRevenue > 0 && chProfit.HasValue)
                    ? Math.Round((chProfit.Value / chNetRevenue) * 100, 2)
                    : (decimal?)null;

                return new ChannelRevenueBreakdownDto
                {
                    Channel = ch,
                    Revenue = chRevenue,
                    ReturnAmount = chReturnAmount,
                    NetRevenue = chNetRevenue,
                    CostOfGoodsSold = isOwner ? chCogs : null,
                    ReturnCogs = isOwner ? chReturnCogs : null,
                    NetCogs = isOwner ? chNetCogs : null,
                    GrossProfit = chProfit,
                    GrossMarginPercentage = chMargin,
                    OrderCount = chOrders.Count
                };
            })
            .OrderByDescending(c => c.NetRevenue)
            .ToList();

        // Phân rã theo Chi nhánh (Branch)
        var branchMap = new Dictionary<Guid, string>();
        foreach (var o in orders)
        {
            branchMap.TryAdd(o.BranchId, o.Branch != null ? o.Branch.Name : "N/A");
        }
        foreach (var r in returns)
        {
            branchMap.TryAdd(r.BranchId, r.Branch != null ? r.Branch.Name : "N/A");
        }

        var byBranch = branchMap
            .Select(kv =>
            {
                var brId = kv.Key;
                var brName = kv.Value;
                var brOrders = orders.Where(o => o.BranchId == brId).ToList();
                var brReturns = returns.Where(r => r.BranchId == brId).ToList();

                var brRevenue = brOrders.Sum(o => o.TotalAmount);
                var brReturnAmount = brReturns.Sum(r => ReturnRevenueAmount(r));
                var brNetRevenue = brRevenue - brReturnAmount;

                var brCogs = brOrders.SelectMany(o => o.Items).Sum(i => (i.CostPrice ?? 0) * i.BaseQuantity);
                var brReturnCogs = brReturns.Sum(r => CalculateReturnCogs(r));
                var brNetCogs = brCogs - brReturnCogs;

                var brProfit = isOwner ? (brNetRevenue - brNetCogs) : (decimal?)null;

                return new BranchRevenueBreakdownDto
                {
                    BranchId = brId,
                    BranchName = brName,
                    Revenue = brRevenue,
                    ReturnAmount = brReturnAmount,
                    NetRevenue = brNetRevenue,
                    CostOfGoodsSold = isOwner ? brCogs : null,
                    ReturnCogs = isOwner ? brReturnCogs : null,
                    NetCogs = isOwner ? brNetCogs : null,
                    GrossProfit = brProfit,
                    OrderCount = brOrders.Count
                };
            })
            .OrderByDescending(b => b.NetRevenue)
            .ToList();

        // Xu hướng theo ngày (Daily trend theo múi giờ Việt Nam +07:00 - BF-011)
        var vnOffset = TimeSpan.FromHours(7);
        var orderGroups = orders
            .GroupBy(o => o.CreatedAt.ToOffset(vnOffset).ToString("yyyy-MM-dd"))
            .ToDictionary(g => g.Key, g => g.ToList());

        var returnGroups = returns
            .GroupBy(r => r.ConfirmedAt!.Value.ToOffset(vnOffset).ToString("yyyy-MM-dd"))
            .ToDictionary(g => g.Key, g => g.ToList());

        var allDates = orderGroups.Keys.Union(returnGroups.Keys).OrderBy(d => d).ToList();

        var dailyTrend = allDates
            .Select(d =>
            {
                var dOrders = orderGroups.GetValueOrDefault(d) ?? new List<Order>();
                var dReturns = returnGroups.GetValueOrDefault(d) ?? new List<SalesReturn>();

                var dRevenue = dOrders.Sum(o => o.TotalAmount);
                var dReturnAmount = dReturns.Sum(r => ReturnRevenueAmount(r));
                var dNetRevenue = dRevenue - dReturnAmount;

                var dCogs = dOrders.SelectMany(o => o.Items).Sum(i => (i.CostPrice ?? 0) * i.BaseQuantity);
                var dReturnCogs = dReturns.Sum(r => CalculateReturnCogs(r));
                var dNetCogs = dCogs - dReturnCogs;

                var dProfit = isOwner ? (dNetRevenue - dNetCogs) : (decimal?)null;

                return new DailyRevenueBreakdownDto
                {
                    Date = d,
                    Revenue = dRevenue,
                    ReturnAmount = dReturnAmount,
                    NetRevenue = dNetRevenue,
                    CostOfGoodsSold = isOwner ? dCogs : null,
                    ReturnCogs = isOwner ? dReturnCogs : null,
                    NetCogs = isOwner ? dNetCogs : null,
                    GrossProfit = dProfit,
                    OrderCount = dOrders.Count
                };
            })
            .OrderBy(d => d.Date)
            .ToList();

        return new RevenueProfitReportDto
        {
            Revenue = totalRevenue,
            TotalRevenue = totalRevenue,
            ReturnAmount = totalReturnAmount,
            NetRevenue = netRevenue,
            TotalNetRevenue = netRevenue,
            CostOfGoodsSold = isOwner ? totalOrderCogs : null,
            // Bộ ba trường cũ Total* là số thuần để TotalNetRevenue - TotalCostOfGoodsSold = TotalGrossProfit (trang Báo cáo đang đọc bộ này)
            TotalCostOfGoodsSold = isOwner ? netCogs : null,
            ReturnCogs = isOwner ? totalReturnCogs : null,
            NetCogs = isOwner ? netCogs : null,
            GrossProfit = grossProfit,
            TotalGrossProfit = grossProfit,
            GrossMarginPercentage = margin,
            TotalOrders = orders.Count,
            TotalItemsSold = totalItemsSold,
            ByChannel = byChannel,
            ByBranch = byBranch,
            DailyTrend = dailyTrend
        };
    }

    public async Task<InventoryVelocityReportDto> GetInventoryVelocityReportAsync(ReportFilterDto filter, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var balancesQuery = _context.InventoryBalances
            .AsNoTracking()
            .Include(b => b.Sku)
                .ThenInclude(s => s!.Product)
            .Include(b => b.Sku)
                .ThenInclude(s => s!.BaseUnit)
            .Where(b => b.TenantId == tenantId);

        if (filter.BranchId.HasValue && filter.BranchId.Value != Guid.Empty)
        {
            balancesQuery = balancesQuery.Where(b => b.BranchId == filter.BranchId.Value);
        }

        var balances = await balancesQuery.ToListAsync(ct);

        decimal totalValue = 0;
        decimal totalOnHand = 0;
        decimal totalReserved = 0;

        foreach (var b in balances)
        {
            totalOnHand += b.OnHand;
            totalReserved += b.Reserved;
            totalValue += b.OnHand * b.AverageCost;
        }

        var quantityByUnit = balances
            .GroupBy(b => b.Sku?.BaseUnit?.Name ?? UnknownUnitName)
            .Select(g =>
            {
                var onHand = g.Sum(b => b.OnHand);
                var reserved = g.Sum(b => b.Reserved);
                return new UnitQuantityDto
                {
                    UnitName = g.Key,
                    OnHand = onHand,
                    Reserved = reserved,
                    Available = Math.Max(0, onHand - reserved)
                };
            })
            .OrderByDescending(u => u.OnHand)
            .ThenBy(u => u.UnitName)
            .ToList();

        // Lấy dữ liệu bán hàng từ các đơn hoàn tất/xác nhận trong kỳ
        var ordersQuery = _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.TenantId == tenantId && (o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Completed));

        if (filter.BranchId.HasValue && filter.BranchId.Value != Guid.Empty)
        {
            ordersQuery = ordersQuery.Where(o => o.BranchId == filter.BranchId.Value);
        }

        if (filter.StartDate.HasValue)
        {
            ordersQuery = ordersQuery.Where(o => o.CreatedAt >= filter.StartDate.Value);
        }

        if (filter.EndDate.HasValue)
        {
            var endBoundary = filter.EndDate.Value;
            // [BF-011] Khoảng nửa mở [StartDate, EndDate) chống bỏ sót giao dịch có phần lẻ giây 23:59:59.xxx
            if (endBoundary.Second == 59)
            {
                endBoundary = new DateTimeOffset(
                    endBoundary.Year,
                    endBoundary.Month,
                    endBoundary.Day,
                    endBoundary.Hour,
                    endBoundary.Minute,
                    59,
                    endBoundary.Offset).AddSeconds(1);
            }
            ordersQuery = ordersQuery.Where(o => o.CreatedAt < endBoundary);
        }

        var soldItems = await ordersQuery
            .SelectMany(o => o.Items)
            .ToListAsync(ct);

        var salesBySku = soldItems
            .GroupBy(i => i.SkuId)
            .ToDictionary(
                g => g.Key,
                g => new { QuantitySold = g.Sum(x => x.BaseQuantity), Revenue = g.Sum(x => x.Quantity * x.UnitPrice) });

        var topN = Math.Clamp(filter.TopN, 1, 50);

        // Mặt hàng bán chạy nhất (Best sellers)
        var bestSellers = salesBySku
            .OrderByDescending(kv => kv.Value.QuantitySold)
            .Take(topN)
            .Select(kv =>
            {
                var skuBalances = balances.Where(b => b.SkuId == kv.Key).ToList();
                var sku = skuBalances.FirstOrDefault()?.Sku;
                return new SkuVelocityItemDto
                {
                    SkuId = kv.Key,
                    SkuCode = sku?.SKU ?? "SKU-" + kv.Key.ToString()[..8],
                    SkuName = sku?.Product?.Name ?? "Sản phẩm " + kv.Key.ToString()[..8],
                    QuantitySold = kv.Value.QuantitySold,
                    TotalRevenue = kv.Value.Revenue,
                    CurrentOnHand = skuBalances.Sum(b => b.OnHand),
                    DaysWithoutSale = 0,
                    BaseUnitName = sku?.BaseUnit?.Name
                };
            })
            .ToList();

        // Mặt hàng luân chuyển chậm (Slow moving: có tồn kho nhưng bán ít hoặc chưa bán)
        var slowMoving = balances
            .GroupBy(b => b.SkuId)
            .Select(g =>
            {
                var sold = salesBySku.TryGetValue(g.Key, out var s) ? s.QuantitySold : 0;
                var rev = salesBySku.TryGetValue(g.Key, out var s2) ? s2.Revenue : 0;
                return new
                {
                    SkuId = g.Key,
                    Sku = g.First().Sku,
                    OnHand = g.Sum(b => b.OnHand),
                    Sold = sold,
                    Revenue = rev
                };
            })
            .Where(x => x.OnHand > 0)
            .OrderBy(x => x.Sold)
            .ThenByDescending(x => x.OnHand)
            .Take(topN)
            .Select(x => new SkuVelocityItemDto
            {
                SkuId = x.SkuId,
                SkuCode = x.Sku?.SKU ?? "SKU-" + x.SkuId.ToString()[..8],
                SkuName = x.Sku?.Product?.Name ?? "Sản phẩm",
                QuantitySold = x.Sold,
                TotalRevenue = x.Revenue,
                CurrentOnHand = x.OnHand,
                DaysWithoutSale = 30, // Mặc định chu kỳ 30 ngày
                BaseUnitName = x.Sku?.BaseUnit?.Name
            })
            .ToList();

        return new InventoryVelocityReportDto
        {
            TotalInventoryValue = totalValue,
            TotalOnHandQuantity = totalOnHand,
            TotalReservedQuantity = totalReserved,
            TotalAvailableQuantity = Math.Max(0, totalOnHand - totalReserved),
            QuantityByUnit = quantityByUnit,
            BestSellers = bestSellers,
            SlowMovingItems = slowMoving
        };
    }

    public async Task<List<StockAlertDto>> GetStockAlertsAsync(Guid? branchId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var query = _context.InventoryBalances
            .AsNoTracking()
            .Include(b => b.Branch)
            .Include(b => b.Sku)
                .ThenInclude(s => s!.Product)
            .Where(b => b.TenantId == tenantId && b.Sku != null && b.Sku.IsActive && b.ReorderPoint > 0 && (b.OnHand - b.Reserved) <= b.ReorderPoint);

        if (branchId.HasValue && branchId.Value != Guid.Empty)
        {
            query = query.Where(b => b.BranchId == branchId.Value);
        }

        var alerts = await query
            .OrderBy(b => b.OnHand - b.Reserved)
            .Select(b => new
            {
                b.SkuId,
                SkuCode = b.Sku != null ? b.Sku.SKU : string.Empty,
                SkuName = b.Sku != null && b.Sku.Product != null ? b.Sku.Product.Name : string.Empty,
                b.BranchId,
                BranchName = b.Branch != null ? b.Branch.Name : string.Empty,
                b.OnHand,
                b.Reserved,
                Available = b.OnHand - b.Reserved,
                b.ReorderPoint
            })
            .ToListAsync(ct);

        return alerts.Select(a =>
        {
            string level;
            if (a.Available <= 0)
            {
                level = "OutOfStock";
            }
            else if (a.Available <= a.ReorderPoint * 0.5m)
            {
                level = "Critical";
            }
            else
            {
                level = "Warning";
            }

            return new StockAlertDto
            {
                SkuId = a.SkuId,
                SkuCode = a.SkuCode,
                SkuName = a.SkuName,
                BranchId = a.BranchId,
                BranchName = a.BranchName,
                OnHand = a.OnHand,
                Reserved = a.Reserved,
                Available = a.Available,
                ReorderPoint = a.ReorderPoint,
                AlertLevel = level
            };
        }).ToList();
    }

    public async Task<DashboardSummaryDto> GetDashboardSummaryAsync(bool isOwner, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        // 1. Thống kê Doanh thu và Lợi nhuận từ các đơn hàng hợp lệ
        var revenueOrders = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.TenantId == tenantId && (o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Completed))
            .ToListAsync(ct);

        var returns = await _context.SalesReturns
            .AsNoTracking()
            .Include(r => r.Order)
            .Include(r => r.Items)
                .ThenInclude(i => i.OrderItem)
            .Where(r => r.TenantId == tenantId && r.Status == SalesReturnStatus.Confirmed && r.ConfirmedAt != null)
            .ToListAsync(ct);

        decimal CalculateReturnCogs(SalesReturn r)
        {
            return r.Items.Sum(i => i.Quantity * (i.OrderItem != null ? i.OrderItem.FactorSnapshot : 1m) * i.OriginalUnitCost);
        }

        decimal totalRevenue = revenueOrders.Sum(o => o.TotalAmount);
        int totalOrders = revenueOrders.Count;
        decimal totalOrderCogs = revenueOrders.SelectMany(o => o.Items).Sum(i => (i.CostPrice ?? 0) * i.BaseQuantity);

        decimal returnAmount = returns.Sum(r => ReturnRevenueAmount(r));
        decimal returnCogs = returns.Sum(r => CalculateReturnCogs(r));

        decimal netRevenue = totalRevenue - returnAmount;
        decimal netCogs = totalOrderCogs - returnCogs;

        decimal? totalGrossProfit = isOwner ? (netRevenue - netCogs) : null;
        decimal grossMargin = (isOwner && netRevenue > 0) ? Math.Round(((netRevenue - netCogs) / netRevenue) * 100, 2) : 0;

        // 2. Thống kê số lượng SKU đang quản lý
        var skuCount = await _context.Skus
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.IsActive)
            .CountAsync(ct);

        // 3. Thống kê Giá trị tồn kho theo WAC và Cảnh báo tồn thấp
        var balances = await _context.InventoryBalances
            .AsNoTracking()
            .Where(b => b.TenantId == tenantId)
            .ToListAsync(ct);

        decimal? totalInventoryValue = isOwner ? balances.Sum(b => b.OnHand * b.AverageCost) : null;
        // Cảnh báo tồn chỉ tính SKU còn kinh doanh, giống báo cáo cảnh báo; giá trị tồn vẫn tính mọi số dư
        var activeSkuIds = await _context.Skus
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.IsActive)
            .Select(s => s.Id)
            .ToListAsync(ct);
        var activeSkuIdSet = activeSkuIds.ToHashSet();
        int lowStockCount = balances.Count(b => activeSkuIdSet.Contains(b.SkuId) && b.ReorderPoint > 0 && (b.OnHand - b.Reserved) <= b.ReorderPoint);

        // 4. Lấy 5 đơn hàng mới phát sinh gần đây nhất
        var recentOrders = await _context.Orders
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId)
            .OrderByDescending(o => o.CreatedAt)
            .Take(5)
            .Select(o => new RecentOrderDto
            {
                Id = o.Id,
                Number = o.Number,
                Channel = o.Channel,
                CustomerName = o.CustomerName,
                TotalAmount = o.TotalAmount,
                Status = o.Status,
                CreatedAt = o.CreatedAt
            })
            .ToListAsync(ct);

        // 5. Xu hướng doanh thu 7 ngày gần nhất phục vụ vẽ biểu đồ Dashboard
        var trendStart = ReportTimeHelper.StartOfDayUtc(ReportTimeHelper.Today().AddDays(-6));

        var recentOrderGroups = revenueOrders
            .Where(o => o.CreatedAt >= trendStart)
            .GroupBy(o => ReportTimeHelper.ToLocalDate(o.CreatedAt).ToString("yyyy-MM-dd"))
            .ToDictionary(g => g.Key, g => g.ToList());

        var recentReturnGroups = returns
            .Where(r => r.ConfirmedAt >= trendStart)
            .GroupBy(r => ReportTimeHelper.ToLocalDate(r.ConfirmedAt!.Value).ToString("yyyy-MM-dd"))
            .ToDictionary(g => g.Key, g => g.ToList());

        var recentDates = recentOrderGroups.Keys.Union(recentReturnGroups.Keys).OrderBy(d => d).ToList();

        var recentTrend = recentDates
            .Select(date =>
            {
                var dOrders = recentOrderGroups.GetValueOrDefault(date) ?? new List<Order>();
                var dReturns = recentReturnGroups.GetValueOrDefault(date) ?? new List<SalesReturn>();

                var rev = dOrders.Sum(o => o.TotalAmount);
                var retAmt = dReturns.Sum(r => ReturnRevenueAmount(r));
                var netRev = rev - retAmt;

                var cogs = dOrders.SelectMany(o => o.Items).Sum(i => (i.CostPrice ?? 0) * i.BaseQuantity);
                var retCogs = dReturns.Sum(r => CalculateReturnCogs(r));
                var netDayCogs = cogs - retCogs;

                return new DailyRevenueBreakdownDto
                {
                    Date = date,
                    Revenue = rev,
                    ReturnAmount = retAmt,
                    NetRevenue = netRev,
                    CostOfGoodsSold = isOwner ? cogs : null,
                    ReturnCogs = isOwner ? retCogs : null,
                    NetCogs = isOwner ? netDayCogs : null,
                    GrossProfit = isOwner ? (netRev - netDayCogs) : null,
                    OrderCount = dOrders.Count
                };
            })
            .ToList();

        return new DashboardSummaryDto
        {
            TotalRevenue = totalRevenue,
            ReturnAmount = returnAmount,
            NetRevenue = netRevenue,
            ReturnCogs = isOwner ? returnCogs : null,
            NetCogs = isOwner ? netCogs : null,
            TotalOrders = totalOrders,
            TotalGrossProfit = totalGrossProfit,
            GrossMarginPercentage = grossMargin,
            SkuCount = skuCount,
            TotalInventoryValue = totalInventoryValue,
            LowStockCount = lowStockCount,
            RecentOrders = recentOrders,
            RecentRevenueTrend = recentTrend
        };
    }
}
