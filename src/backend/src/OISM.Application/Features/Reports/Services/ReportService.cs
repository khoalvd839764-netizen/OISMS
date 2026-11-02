using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Reports.Dtos;

namespace OISM.Application.Features.Reports.Services;

public class ReportService : IReportService
{
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

    public async Task<RevenueProfitReportDto> GetRevenueProfitReportAsync(ReportFilterDto filter, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var ordersQuery = _context.Orders
            .AsNoTracking()
            .Include(o => o.Branch)
            .Include(o => o.Items)
            .Where(o => o.TenantId == tenantId && (o.Status == "Confirmed" || o.Status == "Completed"));

        if (filter.BranchId.HasValue && filter.BranchId.Value != Guid.Empty)
        {
            ordersQuery = ordersQuery.Where(o => o.BranchId == filter.BranchId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Channel))
        {
            var channelTrim = filter.Channel.Trim();
            ordersQuery = ordersQuery.Where(o => o.Channel == channelTrim);
        }

        if (filter.StartDate.HasValue)
        {
            ordersQuery = ordersQuery.Where(o => o.CreatedAt >= filter.StartDate.Value);
        }

        if (filter.EndDate.HasValue)
        {
            ordersQuery = ordersQuery.Where(o => o.CreatedAt <= filter.EndDate.Value);
        }

        var orders = await ordersQuery.ToListAsync(ct);

        decimal totalRevenue = 0;
        decimal totalCogs = 0;
        decimal totalItemsSold = 0;

        foreach (var order in orders)
        {
            totalRevenue += order.TotalAmount;
            foreach (var item in order.Items)
            {
                totalItemsSold += item.Quantity;
                var cost = (item.CostPrice ?? 0) * item.Quantity;
                totalCogs += cost;
            }
        }

        var grossProfit = totalRevenue - totalCogs;
        var margin = totalRevenue > 0 ? Math.Round((grossProfit / totalRevenue) * 100, 2) : 0;

        // Phân rã theo Kênh bán (Channel)
        var byChannel = orders
            .GroupBy(o => o.Channel)
            .Select(g =>
            {
                var chRevenue = g.Sum(o => o.TotalAmount);
                var chCogs = g.SelectMany(o => o.Items).Sum(i => (i.CostPrice ?? 0) * i.Quantity);
                var chProfit = chRevenue - chCogs;
                var chMargin = chRevenue > 0 ? Math.Round((chProfit / chRevenue) * 100, 2) : 0;

                return new ChannelRevenueBreakdownDto
                {
                    Channel = g.Key,
                    Revenue = chRevenue,
                    CostOfGoodsSold = chCogs,
                    GrossProfit = chProfit,
                    GrossMarginPercentage = chMargin,
                    OrderCount = g.Count()
                };
            })
            .OrderByDescending(c => c.Revenue)
            .ToList();

        // Phân rã theo Chi nhánh (Branch)
        var byBranch = orders
            .GroupBy(o => new { o.BranchId, BranchName = o.Branch != null ? o.Branch.Name : "N/A" })
            .Select(g =>
            {
                var brRevenue = g.Sum(o => o.TotalAmount);
                var brCogs = g.SelectMany(o => o.Items).Sum(i => (i.CostPrice ?? 0) * i.Quantity);
                var brProfit = brRevenue - brCogs;

                return new BranchRevenueBreakdownDto
                {
                    BranchId = g.Key.BranchId,
                    BranchName = g.Key.BranchName,
                    Revenue = brRevenue,
                    CostOfGoodsSold = brCogs,
                    GrossProfit = brProfit,
                    OrderCount = g.Count()
                };
            })
            .OrderByDescending(b => b.Revenue)
            .ToList();

        // Xu hướng theo ngày (Daily trend)
        var dailyTrend = orders
            .GroupBy(o => o.CreatedAt.ToString("yyyy-MM-dd"))
            .Select(g =>
            {
                var dRevenue = g.Sum(o => o.TotalAmount);
                var dCogs = g.SelectMany(o => o.Items).Sum(i => (i.CostPrice ?? 0) * i.Quantity);
                return new DailyRevenueBreakdownDto
                {
                    Date = g.Key,
                    Revenue = dRevenue,
                    CostOfGoodsSold = dCogs,
                    GrossProfit = dRevenue - dCogs,
                    OrderCount = g.Count()
                };
            })
            .OrderBy(d => d.Date)
            .ToList();

        return new RevenueProfitReportDto
        {
            TotalNetRevenue = totalRevenue,
            TotalCostOfGoodsSold = totalCogs,
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

        // Lấy dữ liệu bán hàng từ các đơn hoàn tất/xác nhận trong kỳ
        var ordersQuery = _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.TenantId == tenantId && (o.Status == "Confirmed" || o.Status == "Completed"));

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
            ordersQuery = ordersQuery.Where(o => o.CreatedAt <= filter.EndDate.Value);
        }

        var soldItems = await ordersQuery
            .SelectMany(o => o.Items)
            .ToListAsync(ct);

        var salesBySku = soldItems
            .GroupBy(i => i.SkuId)
            .ToDictionary(
                g => g.Key,
                g => new { QuantitySold = g.Sum(x => x.Quantity), Revenue = g.Sum(x => x.Quantity * x.UnitPrice) });

        var topN = Math.Clamp(filter.TopN, 1, 50);

        // Mặt hàng bán chạy nhất (Best sellers)
        var bestSellers = salesBySku
            .OrderByDescending(kv => kv.Value.QuantitySold)
            .Take(topN)
            .Select(kv =>
            {
                var skuBalance = balances.FirstOrDefault(b => b.SkuId == kv.Key);
                var sku = skuBalance?.Sku;
                return new SkuVelocityItemDto
                {
                    SkuId = kv.Key,
                    SkuCode = sku?.SKU ?? "SKU-" + kv.Key.ToString()[..8],
                    SkuName = sku?.Product?.Name ?? "Sản phẩm " + kv.Key.ToString()[..8],
                    QuantitySold = kv.Value.QuantitySold,
                    TotalRevenue = kv.Value.Revenue,
                    CurrentOnHand = skuBalance?.OnHand ?? 0,
                    DaysWithoutSale = 0
                };
            })
            .ToList();

        // Mặt hàng luân chuyển chậm (Slow moving: có tồn kho nhưng bán ít hoặc chưa bán)
        var slowMoving = balances
            .Where(b => b.OnHand > 0)
            .Select(b =>
            {
                var sold = salesBySku.TryGetValue(b.SkuId, out var s) ? s.QuantitySold : 0;
                var rev = salesBySku.TryGetValue(b.SkuId, out var s2) ? s2.Revenue : 0;
                return new
                {
                    Balance = b,
                    Sold = sold,
                    Revenue = rev
                };
            })
            .OrderBy(x => x.Sold)
            .ThenByDescending(x => x.Balance.OnHand)
            .Take(topN)
            .Select(x => new SkuVelocityItemDto
            {
                SkuId = x.Balance.SkuId,
                SkuCode = x.Balance.Sku?.SKU ?? "SKU-" + x.Balance.SkuId.ToString()[..8],
                SkuName = x.Balance.Sku?.Product?.Name ?? "Sản phẩm",
                QuantitySold = x.Sold,
                TotalRevenue = x.Revenue,
                CurrentOnHand = x.Balance.OnHand,
                DaysWithoutSale = 30 // Mặc định chu kỳ 30 ngày
            })
            .ToList();

        return new InventoryVelocityReportDto
        {
            TotalInventoryValue = totalValue,
            TotalOnHandQuantity = totalOnHand,
            TotalReservedQuantity = totalReserved,
            TotalAvailableQuantity = Math.Max(0, totalOnHand - totalReserved),
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
            .Where(b => b.TenantId == tenantId && b.ReorderPoint > 0 && (b.OnHand - b.Reserved) <= b.ReorderPoint);

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
            .Where(o => o.TenantId == tenantId && (o.Status == "Confirmed" || o.Status == "Completed"))
            .ToListAsync(ct);

        decimal totalRevenue = revenueOrders.Sum(o => o.TotalAmount);
        int totalOrders = revenueOrders.Count;
        decimal totalCogs = revenueOrders.SelectMany(o => o.Items).Sum(i => (i.CostPrice ?? 0) * i.Quantity);
        decimal? totalGrossProfit = isOwner ? (totalRevenue - totalCogs) : null;
        decimal grossMargin = (isOwner && totalRevenue > 0) ? Math.Round(((totalRevenue - totalCogs) / totalRevenue) * 100, 2) : 0;

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

        decimal totalInventoryValue = balances.Sum(b => b.OnHand * b.AverageCost);
        int lowStockCount = balances.Count(b => b.ReorderPoint > 0 && (b.OnHand - b.Reserved) <= b.ReorderPoint);

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
        var sevenDaysAgo = DateTimeOffset.UtcNow.AddDays(-7);
        var recentTrend = revenueOrders
            .Where(o => o.CreatedAt >= sevenDaysAgo)
            .GroupBy(o => o.CreatedAt.ToString("yyyy-MM-dd"))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var rev = g.Sum(o => o.TotalAmount);
                var cogs = g.SelectMany(o => o.Items).Sum(i => (i.CostPrice ?? 0) * i.Quantity);
                return new DailyRevenueBreakdownDto
                {
                    Date = g.Key,
                    Revenue = rev,
                    CostOfGoodsSold = isOwner ? cogs : 0,
                    GrossProfit = isOwner ? (rev - cogs) : 0,
                    OrderCount = g.Count()
                };
            })
            .ToList();

        return new DashboardSummaryDto
        {
            TotalRevenue = totalRevenue,
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
