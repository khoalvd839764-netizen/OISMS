using OISM.Domain.Common;
using OISM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Helpers;
using OISM.Application.Common.Interfaces;
using OISM.Application.Common.Services;
using OISM.Application.Features.Reports.Dtos;

namespace OISM.Application.Features.Reports.Services;

public class ReportService : IReportService
{
    private const string UnknownUnitName = "Chưa xác định";

    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _currentTenantService;
    private readonly ICostVisibilityService _costVisibilityService;

    public ReportService(
        IAppDbContext context,
        ICurrentTenantService currentTenantService,
        ICostVisibilityService? costVisibilityService = null)
    {
        _context = context;
        _currentTenantService = currentTenantService;
        _costVisibilityService = costVisibilityService ?? AllowAllCostVisibilityService.Instance;
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

    private static decimal ReturnRevenueAmount(RevenueReturnRow r) =>
        RevenueProfitCalculationHelper.CalculateReturnRevenueAmount(r);

    public Task<RevenueProfitReportDto> GetRevenueProfitReportAsync(ReportFilterDto filter, CancellationToken ct = default)
        => GetRevenueProfitReportAsync(filter, isOwner: _costVisibilityService.IsOwner, ct);

    public const int MaxSkuFilterCount = 20;

    public async Task<RevenueProfitReportDto> GetRevenueProfitReportAsync(ReportFilterDto filter, bool isOwner, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var skuIds = filter.SkuIds?
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList() ?? new List<Guid>();

        if (skuIds.Count > MaxSkuFilterCount)
        {
            throw new OismException("VALIDATION_ERROR", $"Chỉ được chọn tối đa {MaxSkuFilterCount} SKU mỗi lần lọc.", 400);
        }

        DateTimeOffset? endBoundary = filter.EndDate.HasValue
            ? RevenueProfitCalculationHelper.ResolveHalfOpenEndBoundary(filter.EndDate.Value)
            : null;

        var orders = await RevenueProfitCalculationHelper.QueryRevenueOrdersAsync(
            _context,
            tenantId,
            filter.StartDate,
            endBoundary,
            filter.BranchId,
            filter.Channel,
            skuIds: skuIds,
            ct: ct);

        // Phiếu trả hàng đã xác nhận (Confirmed) theo ngày confirmed_at
        var returns = await RevenueProfitCalculationHelper.QueryConfirmedReturnsAsync(
            _context,
            tenantId,
            filter.StartDate,
            endBoundary,
            filter.BranchId,
            filter.Channel,
            skuIds: skuIds,
            ct: ct);

        var branchNames = await RevenueProfitCalculationHelper.QueryBranchNamesAsync(
            _context,
            tenantId,
            orders.Select(o => o.BranchId).Concat(returns.Select(r => r.BranchId)),
            ct);

        if (skuIds.Count > 0)
        {
            return await BuildSkuFilteredRevenueProfitReportAsync(orders, returns, branchNames, skuIds, isOwner, ct);
        }

        static decimal CalculateReturnCogs(RevenueReturnRow r) =>
            RevenueProfitCalculationHelper.CalculateReturnCogs(r);

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
            .Concat(returns.Select(r => r.OrderChannel ?? "N/A"))
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct()
            .ToList();

        var byChannel = allChannels
            .Select(ch =>
            {
                var chOrders = orders.Where(o => o.Channel == ch).ToList();
                var chReturns = returns.Where(r => (r.OrderChannel ?? "N/A") == ch).ToList();

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
            branchMap.TryAdd(o.BranchId, branchNames.GetValueOrDefault(o.BranchId, "N/A"));
        }
        foreach (var r in returns)
        {
            branchMap.TryAdd(r.BranchId, branchNames.GetValueOrDefault(r.BranchId, "N/A"));
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
            .GroupBy(r => r.ConfirmedAt.ToOffset(vnOffset).ToString("yyyy-MM-dd"))
            .ToDictionary(g => g.Key, g => g.ToList());

        var allDates = orderGroups.Keys.Union(returnGroups.Keys).OrderBy(d => d).ToList();

        var dailyTrend = allDates
            .Select(d =>
            {
                var dOrders = orderGroups.GetValueOrDefault(d) ?? new List<RevenueOrderRow>();
                var dReturns = returnGroups.GetValueOrDefault(d) ?? new List<RevenueReturnRow>();

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

    private sealed class SkuAccumulator
    {
        public string SkuCode { get; set; } = string.Empty;
        public string SkuName { get; set; } = string.Empty;
        public decimal Revenue { get; set; }
        public decimal ReturnAmount { get; set; }
        public decimal OrderCogs { get; set; }
        public decimal ReturnCogs { get; set; }
        public HashSet<Guid> OrderIds { get; } = new();
        public Dictionary<string, (decimal Quantity, decimal BaseQuantity)> UnitQuantities { get; } = new(StringComparer.Ordinal);
    }

    private sealed class BucketAccumulator
    {
        public string Label { get; set; } = string.Empty;
        public decimal Revenue { get; set; }
        public decimal ReturnAmount { get; set; }
        public decimal OrderCogs { get; set; }
        public decimal ReturnCogs { get; set; }
        public HashSet<Guid> OrderIds { get; } = new();
    }

    private async Task<RevenueProfitReportDto> BuildSkuFilteredRevenueProfitReportAsync(
        List<RevenueOrderRow> orders,
        List<RevenueReturnRow> returns,
        Dictionary<Guid, string> branchNames,
        List<Guid> skuIds,
        bool isOwner,
        CancellationToken ct)
    {
        var skuSet = skuIds.ToHashSet();
        decimal totalRevenue = 0m;
        decimal totalOrderCogs = 0m;
        decimal totalReturnAmount = 0m;
        decimal totalReturnCogs = 0m;
        decimal totalItemsSold = 0m;

        var channelBuckets = new Dictionary<string, BucketAccumulator>(StringComparer.Ordinal);
        var branchBuckets = new Dictionary<Guid, BucketAccumulator>();
        var dailyBuckets = new Dictionary<string, BucketAccumulator>(StringComparer.Ordinal);
        var skuBuckets = new Dictionary<Guid, SkuAccumulator>();

        foreach (var order in orders)
        {
            var orderItems = order.Items;
            var allocatedRevenues = RevenueProfitCalculationHelper.AllocateOrderRevenueByItem(order);
            var dateKey = RevenueProfitCalculationHelper.GetOrderBusinessDate(order).ToString("yyyy-MM-dd");
            var branchName = branchNames.GetValueOrDefault(order.BranchId, "N/A");

            for (int i = 0; i < orderItems.Count; i++)
            {
                var item = orderItems[i];
                if (!skuSet.Contains(item.SkuId))
                {
                    continue;
                }

                var lineRevenue = allocatedRevenues[i];
                var lineCogs = RevenueProfitCalculationHelper.CalculateOrderItemCogs(item);
                var unitName = string.IsNullOrWhiteSpace(item.UnitNameSnapshot) ? UnknownUnitName : item.UnitNameSnapshot;

                totalRevenue += lineRevenue;
                totalOrderCogs += lineCogs;
                totalItemsSold += item.BaseQuantity;

                if (!string.IsNullOrEmpty(order.Channel))
                {
                    var ch = GetOrCreateBucket(channelBuckets, order.Channel, order.Channel);
                    ch.Revenue += lineRevenue;
                    ch.OrderCogs += lineCogs;
                    ch.OrderIds.Add(order.Id);
                }

                var br = GetOrCreateBucket(branchBuckets, order.BranchId, branchName);
                br.Revenue += lineRevenue;
                br.OrderCogs += lineCogs;
                br.OrderIds.Add(order.Id);

                var dy = GetOrCreateBucket(dailyBuckets, dateKey, dateKey);
                dy.Revenue += lineRevenue;
                dy.OrderCogs += lineCogs;
                dy.OrderIds.Add(order.Id);

                if (!skuBuckets.TryGetValue(item.SkuId, out var sb))
                {
                    sb = new SkuAccumulator
                    {
                        SkuCode = item.SkuCodeSnapshot,
                        SkuName = item.SkuNameSnapshot
                    };
                    skuBuckets[item.SkuId] = sb;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(sb.SkuCode) && !string.IsNullOrWhiteSpace(item.SkuCodeSnapshot))
                        sb.SkuCode = item.SkuCodeSnapshot;
                    if (string.IsNullOrWhiteSpace(sb.SkuName) && !string.IsNullOrWhiteSpace(item.SkuNameSnapshot))
                        sb.SkuName = item.SkuNameSnapshot;
                }

                sb.Revenue += lineRevenue;
                sb.OrderCogs += lineCogs;
                sb.OrderIds.Add(order.Id);
                sb.UnitQuantities.TryGetValue(unitName, out var prevQty);
                sb.UnitQuantities[unitName] = (prevQty.Quantity + item.Quantity, prevQty.BaseQuantity + item.BaseQuantity);
            }
        }

        foreach (var salesReturn in returns)
        {
            var returnItems = salesReturn.Items;
            var allocatedReturns = RevenueProfitCalculationHelper.AllocateReturnRevenueByItem(salesReturn);
            var channel = salesReturn.OrderChannel ?? "N/A";
            var dateKey = RevenueProfitCalculationHelper.GetReturnBusinessDate(salesReturn).ToString("yyyy-MM-dd");
            var branchName = branchNames.GetValueOrDefault(salesReturn.BranchId, "N/A");

            for (int i = 0; i < returnItems.Count; i++)
            {
                var item = returnItems[i];
                if (!skuSet.Contains(item.SkuId))
                {
                    continue;
                }

                var lineReturn = allocatedReturns[i];
                var lineReturnCogs = RevenueProfitCalculationHelper.CalculateReturnItemCogs(item);

                totalReturnAmount += lineReturn;
                totalReturnCogs += lineReturnCogs;

                if (!string.IsNullOrEmpty(channel))
                {
                    var ch = GetOrCreateBucket(channelBuckets, channel, channel);
                    ch.ReturnAmount += lineReturn;
                    ch.ReturnCogs += lineReturnCogs;
                }

                var br = GetOrCreateBucket(branchBuckets, salesReturn.BranchId, branchName);
                br.ReturnAmount += lineReturn;
                br.ReturnCogs += lineReturnCogs;

                var dy = GetOrCreateBucket(dailyBuckets, dateKey, dateKey);
                dy.ReturnAmount += lineReturn;
                dy.ReturnCogs += lineReturnCogs;

                if (!skuBuckets.TryGetValue(item.SkuId, out var sb))
                {
                    sb = new SkuAccumulator
                    {
                        SkuCode = item.OrderItemSkuCodeSnapshot ?? string.Empty,
                        SkuName = item.OrderItemSkuNameSnapshot ?? string.Empty
                    };
                    skuBuckets[item.SkuId] = sb;
                }

                sb.ReturnAmount += lineReturn;
                sb.ReturnCogs += lineReturnCogs;
            }
        }

        var missingMetaIds = skuBuckets
            .Where(kv => string.IsNullOrWhiteSpace(kv.Value.SkuCode) || string.IsNullOrWhiteSpace(kv.Value.SkuName))
            .Select(kv => kv.Key)
            .ToList();

        if (missingMetaIds.Count > 0)
        {
            var skuMeta = await _context.Skus
                .AsNoTracking()
                .Include(s => s.Product)
                .Where(s => missingMetaIds.Contains(s.Id))
                .Select(s => new { s.Id, s.SKU, ProductName = s.Product != null ? s.Product.Name : string.Empty })
                .ToListAsync(ct);

            foreach (var m in skuMeta)
            {
                if (skuBuckets.TryGetValue(m.Id, out var sb))
                {
                    if (string.IsNullOrWhiteSpace(sb.SkuCode)) sb.SkuCode = m.SKU;
                    if (string.IsNullOrWhiteSpace(sb.SkuName)) sb.SkuName = m.ProductName;
                }
            }
        }

        var netRevenue = totalRevenue - totalReturnAmount;
        var netCogs = totalOrderCogs - totalReturnCogs;
        decimal? grossProfit = isOwner ? (netRevenue - netCogs) : null;
        decimal? margin = (isOwner && netRevenue > 0 && grossProfit.HasValue)
            ? Math.Round((grossProfit.Value / netRevenue) * 100, 2)
            : null;

        var byChannel = channelBuckets.Values
            .Select(b =>
            {
                var bNetRev = b.Revenue - b.ReturnAmount;
                var bNetCogs = b.OrderCogs - b.ReturnCogs;
                var bProfit = isOwner ? (bNetRev - bNetCogs) : (decimal?)null;
                var bMargin = (isOwner && bNetRev > 0 && bProfit.HasValue)
                    ? Math.Round((bProfit.Value / bNetRev) * 100, 2)
                    : (decimal?)null;

                return new ChannelRevenueBreakdownDto
                {
                    Channel = b.Label,
                    Revenue = b.Revenue,
                    ReturnAmount = b.ReturnAmount,
                    NetRevenue = bNetRev,
                    CostOfGoodsSold = isOwner ? b.OrderCogs : null,
                    ReturnCogs = isOwner ? b.ReturnCogs : null,
                    NetCogs = isOwner ? bNetCogs : null,
                    GrossProfit = bProfit,
                    GrossMarginPercentage = bMargin,
                    OrderCount = b.OrderIds.Count
                };
            })
            .OrderByDescending(c => c.NetRevenue)
            .ToList();

        var byBranch = branchBuckets
            .Select(kv =>
            {
                var b = kv.Value;
                var bNetRev = b.Revenue - b.ReturnAmount;
                var bNetCogs = b.OrderCogs - b.ReturnCogs;
                var bProfit = isOwner ? (bNetRev - bNetCogs) : (decimal?)null;

                return new BranchRevenueBreakdownDto
                {
                    BranchId = kv.Key,
                    BranchName = b.Label,
                    Revenue = b.Revenue,
                    ReturnAmount = b.ReturnAmount,
                    NetRevenue = bNetRev,
                    CostOfGoodsSold = isOwner ? b.OrderCogs : null,
                    ReturnCogs = isOwner ? b.ReturnCogs : null,
                    NetCogs = isOwner ? bNetCogs : null,
                    GrossProfit = bProfit,
                    OrderCount = b.OrderIds.Count
                };
            })
            .OrderByDescending(b => b.NetRevenue)
            .ToList();

        var dailyTrend = dailyBuckets.Values
            .Select(b =>
            {
                var bNetRev = b.Revenue - b.ReturnAmount;
                var bNetCogs = b.OrderCogs - b.ReturnCogs;
                var bProfit = isOwner ? (bNetRev - bNetCogs) : (decimal?)null;

                return new DailyRevenueBreakdownDto
                {
                    Date = b.Label,
                    Revenue = b.Revenue,
                    ReturnAmount = b.ReturnAmount,
                    NetRevenue = bNetRev,
                    CostOfGoodsSold = isOwner ? b.OrderCogs : null,
                    ReturnCogs = isOwner ? b.ReturnCogs : null,
                    NetCogs = isOwner ? bNetCogs : null,
                    GrossProfit = bProfit,
                    OrderCount = b.OrderIds.Count
                };
            })
            .OrderBy(d => d.Date)
            .ToList();

        var skuBreakdown = skuBuckets
            .Select(kv =>
            {
                var sb = kv.Value;
                var sNetRev = sb.Revenue - sb.ReturnAmount;
                var sNetCogs = sb.OrderCogs - sb.ReturnCogs;
                var sProfit = isOwner ? (sNetRev - sNetCogs) : (decimal?)null;

                return new SkuRevenueBreakdownDto
                {
                    SkuId = kv.Key,
                    SkuCode = sb.SkuCode,
                    SkuName = sb.SkuName,
                    Revenue = sb.Revenue,
                    ReturnAmount = sb.ReturnAmount,
                    NetRevenue = sNetRev,
                    CostOfGoodsSold = isOwner ? sb.OrderCogs : null,
                    ReturnCogs = isOwner ? sb.ReturnCogs : null,
                    NetCogs = isOwner ? sNetCogs : null,
                    GrossProfit = sProfit,
                    OrderCount = sb.OrderIds.Count,
                    QuantityByUnit = sb.UnitQuantities
                        .Select(u => new SkuSoldUnitQuantityDto
                        {
                            UnitName = u.Key,
                            Quantity = u.Value.Quantity,
                            BaseQuantity = u.Value.BaseQuantity
                        })
                        .OrderBy(u => u.UnitName, StringComparer.Ordinal)
                        .ToList()
                };
            })
            .OrderByDescending(s => s.NetRevenue)
            .ThenBy(s => s.SkuCode, StringComparer.Ordinal)
            .ToList();

        return new RevenueProfitReportDto
        {
            Revenue = totalRevenue,
            TotalRevenue = totalRevenue,
            ReturnAmount = totalReturnAmount,
            NetRevenue = netRevenue,
            TotalNetRevenue = netRevenue,
            CostOfGoodsSold = isOwner ? totalOrderCogs : null,
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
            DailyTrend = dailyTrend,
            SkuBreakdown = skuBreakdown
        };
    }

    private static BucketAccumulator GetOrCreateBucket<TKey>(Dictionary<TKey, BucketAccumulator> dict, TKey key, string label)
        where TKey : notnull
    {
        if (!dict.TryGetValue(key, out var bucket))
        {
            bucket = new BucketAccumulator { Label = label };
            dict[key] = bucket;
        }
        return bucket;
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

        var hasMultipleUnits = quantityByUnit.Count > 1;

        return new InventoryVelocityReportDto
        {
            TotalInventoryValue = _costVisibilityService.IsOwner ? totalValue : null,
            TotalOnHandQuantity = hasMultipleUnits ? null : totalOnHand,
            TotalReservedQuantity = hasMultipleUnits ? null : totalReserved,
            TotalAvailableQuantity = hasMultipleUnits ? null : Math.Max(0, totalOnHand - totalReserved),
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
            .Where(b => b.TenantId == tenantId && b.Sku != null && b.Sku.IsActive
                        // FR-REP-03: hết hàng (khả dụng <= 0) luôn cảnh báo, kể cả SKU chưa đặt ngưỡng (ReorderPoint = 0).
                        // Còn hàng thì chỉ cảnh báo khi đã đặt ngưỡng và khả dụng chạm hoặc dưới ngưỡng.
                        && ((b.OnHand - b.Reserved) <= 0 || (b.ReorderPoint > 0 && (b.OnHand - b.Reserved) <= b.ReorderPoint)));

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

    public Task<DashboardSummaryDto> GetDashboardSummaryAsync(CancellationToken ct = default)
        => GetDashboardSummaryAsync(_costVisibilityService.IsOwner, ct);

    public async Task<DashboardSummaryDto> GetDashboardSummaryAsync(bool isOwner, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        // 1. Thống kê Doanh thu và Lợi nhuận từ các đơn hàng hợp lệ
        var revenueOrders = await RevenueProfitCalculationHelper.QueryRevenueOrdersAsync(
            _context,
            tenantId,
            startUtc: null,
            endUtcExclusive: null,
            ct: ct);

        var returns = await RevenueProfitCalculationHelper.QueryConfirmedReturnsAsync(
            _context,
            tenantId,
            startUtc: null,
            endUtcExclusive: null,
            ct: ct);

        static decimal CalculateReturnCogs(RevenueReturnRow r) =>
            RevenueProfitCalculationHelper.CalculateReturnCogs(r);

        decimal totalRevenue = revenueOrders.Sum(o => o.TotalAmount);
        int totalOrders = revenueOrders.Count;
        decimal totalOrderCogs = revenueOrders.SelectMany(o => o.Items).Sum(i => (i.CostPrice ?? 0) * i.BaseQuantity);

        decimal returnAmount = returns.Sum(r => ReturnRevenueAmount(r));
        decimal returnCogs = returns.Sum(r => CalculateReturnCogs(r));

        decimal netRevenue = totalRevenue - returnAmount;
        decimal netCogs = totalOrderCogs - returnCogs;

        decimal? totalGrossProfit = isOwner ? (netRevenue - netCogs) : null;
        decimal? grossMargin = (isOwner && netRevenue > 0)
            ? Math.Round(((netRevenue - netCogs) / netRevenue) * 100, 2)
            : (isOwner ? 0m : null);

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
        // Cùng quy tắc với GetStockAlertsAsync: hết hàng luôn tính, còn hàng chỉ tính khi đã đặt ngưỡng.
        int lowStockCount = balances.Count(b => activeSkuIdSet.Contains(b.SkuId)
            && ((b.OnHand - b.Reserved) <= 0 || (b.ReorderPoint > 0 && (b.OnHand - b.Reserved) <= b.ReorderPoint)));

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
            .GroupBy(r => ReportTimeHelper.ToLocalDate(r.ConfirmedAt).ToString("yyyy-MM-dd"))
            .ToDictionary(g => g.Key, g => g.ToList());

        var recentDates = recentOrderGroups.Keys.Union(recentReturnGroups.Keys).OrderBy(d => d).ToList();

        var recentTrend = recentDates
            .Select(date =>
            {
                var dOrders = recentOrderGroups.GetValueOrDefault(date) ?? new List<RevenueOrderRow>();
                var dReturns = recentReturnGroups.GetValueOrDefault(date) ?? new List<RevenueReturnRow>();

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
            GrossMarginPercentage = isOwner ? grossMargin ?? 0m : null,
            SkuCount = skuCount,
            TotalInventoryValue = totalInventoryValue,
            LowStockCount = lowStockCount,
            RecentOrders = recentOrders,
            RecentRevenueTrend = recentTrend
        };
    }
}
