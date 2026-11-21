using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Helpers;
using OISM.Application.Common.Interfaces;
using OISM.Domain.Common;
using OISM.Domain.Entities;

namespace OISM.Application.Features.Reports.Services;

internal sealed class RevenueOrderItemRow
{
    public Guid SkuId { get; init; }
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal BaseQuantity { get; init; }
    public decimal? CostPrice { get; init; }
    public string UnitNameSnapshot { get; init; } = string.Empty;
    public string SkuCodeSnapshot { get; init; } = string.Empty;
    public string SkuNameSnapshot { get; init; } = string.Empty;
}

internal sealed class RevenueOrderRow
{
    public Guid Id { get; init; }
    public Guid BranchId { get; init; }
    public string Channel { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public decimal TotalAmount { get; init; }
    public List<RevenueOrderItemRow> Items { get; } = new();
}

internal sealed class RevenueReturnItemRow
{
    public Guid SkuId { get; init; }
    public decimal Quantity { get; init; }
    public decimal RefundAmount { get; init; }
    public decimal OriginalUnitCost { get; init; }
    public decimal? OrderItemUnitPrice { get; init; }
    public decimal OrderItemFactorSnapshot { get; init; } = 1m;
    public string? OrderItemSkuCodeSnapshot { get; init; }
    public string? OrderItemSkuNameSnapshot { get; init; }
}

internal sealed class RevenueReturnRow
{
    public Guid Id { get; init; }
    public Guid BranchId { get; init; }
    public string? OrderChannel { get; init; }
    public DateTimeOffset ConfirmedAt { get; init; }
    public decimal TotalRefundAmount { get; init; }
    public List<RevenueReturnItemRow> Items { get; } = new();
}

/// <summary>
/// Helper dùng chung quy tắc tính doanh thu, hàng trả và giá vốn theo ngày kinh doanh UTC+07
/// giữa <see cref="ReportService"/> (báo cáo trực tiếp) và <see cref="DailySalesSummaryService"/> (bảng tổng hợp ngày).
/// </summary>
internal static class RevenueProfitCalculationHelper
{
    internal static readonly HashSet<string> MarketplaceChannels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shopee",
        "TikTok",
        "Lazada"
    };

    /// <summary>
    /// Chuẩn hóa mốc kết thúc nửa mở [StartDate, EndDate): nếu bộ lọc kết thúc ở giây 59
    /// (ví dụ 23:59:59.999+07:00), làm tròn lên giây kế tiếp để không sót phần nghìn giây cuối ngày.
    /// </summary>
    internal static DateTimeOffset ResolveHalfOpenEndBoundary(DateTimeOffset boundary)
    {
        if (boundary.Second == 59)
        {
            return new DateTimeOffset(
                boundary.Year,
                boundary.Month,
                boundary.Day,
                boundary.Hour,
                boundary.Minute,
                59,
                boundary.Offset).AddSeconds(1);
        }

        return boundary;
    }

    internal static async Task<Dictionary<Guid, string>> QueryBranchNamesAsync(
        IAppDbContext context,
        Guid tenantId,
        IEnumerable<Guid> branchIds,
        CancellationToken ct = default)
    {
        var ids = branchIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await context.Branches
            .AsNoTracking()
            .Where(b => b.TenantId == tenantId && ids.Contains(b.Id))
            .Select(b => new { b.Id, b.Name })
            .ToDictionaryAsync(b => b.Id, b => b.Name, ct);
    }

    private static IQueryable<Order> ApplyOrderFilter(
        IQueryable<Order> query,
        Guid tenantId,
        DateTimeOffset? startUtc,
        DateTimeOffset? endUtcExclusive,
        Guid? branchId,
        string? channel,
        IReadOnlyCollection<Guid>? skuIds)
    {
        query = query.Where(o => o.TenantId == tenantId && (o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Completed));

        if (branchId.HasValue && branchId.Value != Guid.Empty)
        {
            query = query.Where(o => o.BranchId == branchId.Value);
        }

        if (!string.IsNullOrWhiteSpace(channel))
        {
            var channelTrim = channel.Trim();
            query = query.Where(o => o.Channel == channelTrim);
        }

        if (startUtc.HasValue)
        {
            var startNormalized = startUtc.Value.ToUniversalTime();
            query = query.Where(o => o.CreatedAt >= startNormalized);
        }

        if (endUtcExclusive.HasValue)
        {
            var endNormalized = endUtcExclusive.Value.ToUniversalTime();
            query = query.Where(o => o.CreatedAt < endNormalized);
        }

        if (skuIds is { Count: > 0 })
        {
            query = query.Where(o => o.Items.Any(i => skuIds.Contains(i.SkuId)));
        }

        return query;
    }

    internal static async Task<List<RevenueOrderRow>> QueryRevenueOrdersAsync(
        IAppDbContext context,
        Guid tenantId,
        DateTimeOffset? startUtc,
        DateTimeOffset? endUtcExclusive,
        Guid? branchId = null,
        string? channel = null,
        IReadOnlyCollection<Guid>? skuIds = null,
        CancellationToken ct = default)
    {
        var orders = await ApplyOrderFilter(
                context.Orders.AsNoTracking(),
                tenantId,
                startUtc,
                endUtcExclusive,
                branchId,
                channel,
                skuIds)
            .Select(o => new RevenueOrderRow
            {
                Id = o.Id,
                BranchId = o.BranchId,
                Channel = o.Channel,
                CreatedAt = o.CreatedAt,
                TotalAmount = o.TotalAmount
            })
            .ToListAsync(ct);

        if (orders.Count == 0)
        {
            return orders;
        }

        var orderMap = new Dictionary<Guid, RevenueOrderRow>(orders.Count);
        foreach (var order in orders)
        {
            orderMap[order.Id] = order;
        }

        // Tách truy vấn dòng hàng để tránh nhân bản cột cha; sắp xếp theo Id để giữ thứ tự phân bổ làm tròn ổn định.
        IQueryable<OrderItem> itemQuery = context.OrderItems
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId
                && i.Order != null
                && (i.Order.Status == OrderStatus.Confirmed || i.Order.Status == OrderStatus.Completed));

        if (branchId.HasValue && branchId.Value != Guid.Empty)
        {
            itemQuery = itemQuery.Where(i => i.Order!.BranchId == branchId.Value);
        }

        if (!string.IsNullOrWhiteSpace(channel))
        {
            var channelTrim = channel.Trim();
            itemQuery = itemQuery.Where(i => i.Order!.Channel == channelTrim);
        }

        if (startUtc.HasValue)
        {
            var startNormalized = startUtc.Value.ToUniversalTime();
            itemQuery = itemQuery.Where(i => i.Order!.CreatedAt >= startNormalized);
        }

        if (endUtcExclusive.HasValue)
        {
            var endNormalized = endUtcExclusive.Value.ToUniversalTime();
            itemQuery = itemQuery.Where(i => i.Order!.CreatedAt < endNormalized);
        }

        if (skuIds is { Count: > 0 })
        {
            itemQuery = itemQuery.Where(i => i.Order!.Items.Any(x => skuIds.Contains(x.SkuId)));
        }

        var items = await itemQuery
            .OrderBy(i => i.OrderId)
            .ThenBy(i => i.Id)
            .Select(i => new
            {
                i.OrderId,
                Row = new RevenueOrderItemRow
                {
                    SkuId = i.SkuId,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice,
                    BaseQuantity = i.BaseQuantity,
                    CostPrice = i.CostPrice,
                    UnitNameSnapshot = i.UnitNameSnapshot,
                    SkuCodeSnapshot = i.SkuCodeSnapshot,
                    SkuNameSnapshot = i.SkuNameSnapshot
                }
            })
            .ToListAsync(ct);

        foreach (var item in items)
        {
            if (orderMap.TryGetValue(item.OrderId, out var order))
            {
                order.Items.Add(item.Row);
            }
        }

        return orders;
    }

    internal static async Task<List<RevenueReturnRow>> QueryConfirmedReturnsAsync(
        IAppDbContext context,
        Guid tenantId,
        DateTimeOffset? startUtc,
        DateTimeOffset? endUtcExclusive,
        Guid? branchId = null,
        string? channel = null,
        IReadOnlyCollection<Guid>? skuIds = null,
        CancellationToken ct = default)
    {
        IQueryable<SalesReturn> query = context.SalesReturns
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.Status == SalesReturnStatus.Confirmed && r.ConfirmedAt != null);

        if (branchId.HasValue && branchId.Value != Guid.Empty)
        {
            query = query.Where(r => r.BranchId == branchId.Value);
        }

        if (!string.IsNullOrWhiteSpace(channel))
        {
            var channelTrim = channel.Trim();
            query = query.Where(r => r.Order != null && r.Order.Channel == channelTrim);
        }

        if (startUtc.HasValue)
        {
            var startNormalized = startUtc.Value.ToUniversalTime();
            query = query.Where(r => r.ConfirmedAt >= startNormalized);
        }

        if (endUtcExclusive.HasValue)
        {
            var endNormalized = endUtcExclusive.Value.ToUniversalTime();
            query = query.Where(r => r.ConfirmedAt < endNormalized);
        }

        if (skuIds is { Count: > 0 })
        {
            query = query.Where(r => r.Items.Any(i => skuIds.Contains(i.SkuId)));
        }

        var returns = await query
            .Select(r => new RevenueReturnRow
            {
                Id = r.Id,
                BranchId = r.BranchId,
                OrderChannel = r.Order != null ? r.Order.Channel : null,
                ConfirmedAt = r.ConfirmedAt!.Value,
                TotalRefundAmount = r.TotalRefundAmount
            })
            .ToListAsync(ct);

        if (returns.Count == 0)
        {
            return returns;
        }

        var returnMap = new Dictionary<Guid, RevenueReturnRow>(returns.Count);
        foreach (var ret in returns)
        {
            returnMap[ret.Id] = ret;
        }

        IQueryable<SalesReturnItem> itemQuery = context.SalesReturnItems
            .AsNoTracking()
            .Where(ri => ri.TenantId == tenantId
                && ri.SalesReturn != null
                && ri.SalesReturn.Status == SalesReturnStatus.Confirmed
                && ri.SalesReturn.ConfirmedAt != null);

        if (branchId.HasValue && branchId.Value != Guid.Empty)
        {
            itemQuery = itemQuery.Where(ri => ri.SalesReturn!.BranchId == branchId.Value);
        }

        if (!string.IsNullOrWhiteSpace(channel))
        {
            var channelTrim = channel.Trim();
            itemQuery = itemQuery.Where(ri => ri.SalesReturn!.Order != null && ri.SalesReturn.Order.Channel == channelTrim);
        }

        if (startUtc.HasValue)
        {
            var startNormalized = startUtc.Value.ToUniversalTime();
            itemQuery = itemQuery.Where(ri => ri.SalesReturn!.ConfirmedAt >= startNormalized);
        }

        if (endUtcExclusive.HasValue)
        {
            var endNormalized = endUtcExclusive.Value.ToUniversalTime();
            itemQuery = itemQuery.Where(ri => ri.SalesReturn!.ConfirmedAt < endNormalized);
        }

        if (skuIds is { Count: > 0 })
        {
            itemQuery = itemQuery.Where(ri => ri.SalesReturn!.Items.Any(x => skuIds.Contains(x.SkuId)));
        }

        var items = await itemQuery
            .OrderBy(ri => ri.SalesReturnId)
            .ThenBy(ri => ri.Id)
            .Select(ri => new
            {
                ri.SalesReturnId,
                Row = new RevenueReturnItemRow
                {
                    SkuId = ri.SkuId,
                    Quantity = ri.Quantity,
                    RefundAmount = ri.RefundAmount,
                    OriginalUnitCost = ri.OriginalUnitCost,
                    OrderItemUnitPrice = ri.OrderItem != null ? ri.OrderItem.UnitPrice : (decimal?)null,
                    OrderItemFactorSnapshot = ri.OrderItem != null ? ri.OrderItem.FactorSnapshot : 1m,
                    OrderItemSkuCodeSnapshot = ri.OrderItem != null ? ri.OrderItem.SkuCodeSnapshot : null,
                    OrderItemSkuNameSnapshot = ri.OrderItem != null ? ri.OrderItem.SkuNameSnapshot : null
                }
            })
            .ToListAsync(ct);

        foreach (var item in items)
        {
            if (returnMap.TryGetValue(item.SalesReturnId, out var ret))
            {
                ret.Items.Add(item.Row);
            }
        }

        return returns;
    }

    internal static DateOnly GetOrderBusinessDate(RevenueOrderRow order) =>
        ReportTimeHelper.ToLocalDate(order.CreatedAt);

    internal static DateOnly GetReturnBusinessDate(RevenueReturnRow salesReturn) =>
        ReportTimeHelper.ToLocalDate(salesReturn.ConfirmedAt);

    internal static decimal CalculateOrderItemCogs(RevenueOrderItemRow item) =>
        (item.CostPrice ?? 0m) * item.BaseQuantity;

    internal static decimal CalculateReturnItemBaseQuantity(RevenueReturnItemRow item) =>
        item.Quantity * item.OrderItemFactorSnapshot;

    internal static decimal CalculateReturnItemCogs(RevenueReturnItemRow item) =>
        CalculateReturnItemBaseQuantity(item) * item.OriginalUnitCost;

    internal static decimal CalculateReturnCogs(RevenueReturnRow salesReturn) =>
        salesReturn.Items.Sum(CalculateReturnItemCogs);

    /// <summary>
    /// Doanh thu bị giảm do một phiếu trả hàng đã xác nhận.
    /// Đơn POS/Admin: dùng <see cref="SalesReturn.TotalRefundAmount"/>.
    /// Đơn sàn (Shopee/TikTok/Lazada): dùng tổng (SL trả × đơn giá snapshot).
    /// </summary>
    internal static decimal CalculateReturnRevenueAmount(RevenueReturnRow salesReturn)
    {
        if (salesReturn.OrderChannel != null && MarketplaceChannels.Contains(salesReturn.OrderChannel))
        {
            return salesReturn.Items.Sum(i => i.Quantity * (i.OrderItemUnitPrice ?? 0m));
        }

        return salesReturn.TotalRefundAmount;
    }

    internal static decimal[] AllocateOrderRevenueByItem(RevenueOrderRow order)
    {
        var items = order.Items;
        if (items.Count == 0)
        {
            return Array.Empty<decimal>();
        }

        var rawWeights = new decimal[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            rawWeights[i] = Math.Max(0m, items[i].Quantity * items[i].UnitPrice);
        }
        return AllocateProportionally(rawWeights, Math.Round(order.TotalAmount, 2, MidpointRounding.AwayFromZero));
    }

    internal static decimal[] AllocateReturnRevenueByItem(RevenueReturnRow salesReturn)
    {
        var items = salesReturn.Items;
        if (items.Count == 0)
        {
            return Array.Empty<decimal>();
        }

        var isMarketplace = salesReturn.OrderChannel != null && MarketplaceChannels.Contains(salesReturn.OrderChannel);
        var totalReturnAmount = Math.Round(CalculateReturnRevenueAmount(salesReturn), 2, MidpointRounding.AwayFromZero);

        if (!isMarketplace)
        {
            var itemRefunds = new decimal[items.Count];
            decimal refundSum = 0m;
            for (int i = 0; i < items.Count; i++)
            {
                var r = Math.Max(0m, Math.Round(items[i].RefundAmount, 2, MidpointRounding.AwayFromZero));
                itemRefunds[i] = r;
                refundSum += r;
            }
            if (refundSum == totalReturnAmount)
            {
                return itemRefunds;
            }

            if (refundSum > 0m)
            {
                return AllocateProportionally(itemRefunds, totalReturnAmount);
            }
        }

        var lineWeights = new decimal[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            lineWeights[i] = Math.Max(0m, items[i].Quantity * (items[i].OrderItemUnitPrice ?? 0m));
        }
        return AllocateProportionally(lineWeights, totalReturnAmount);
    }

    private static decimal[] AllocateProportionally(decimal[] weights, decimal targetTotal)
    {
        var result = new decimal[weights.Length];
        if (weights.Length == 0)
        {
            return result;
        }

        if (weights.Length == 1)
        {
            result[0] = Math.Max(0m, targetTotal);
            return result;
        }

        var roundedWeights = weights.Select(w => Math.Round(w, 2, MidpointRounding.AwayFromZero)).ToArray();
        if (roundedWeights.Sum() == targetTotal)
        {
            return roundedWeights;
        }

        var totalWeight = weights.Sum();
        var maxIdx = 0;
        for (int i = 1; i < weights.Length; i++)
        {
            if (weights[i] > weights[maxIdx])
            {
                maxIdx = i;
            }
        }

        decimal runningSum = 0m;
        for (int i = 0; i < weights.Length; i++)
        {
            if (i == maxIdx)
            {
                continue;
            }

            decimal share = totalWeight > 0m
                ? Math.Round(targetTotal * (weights[i] / totalWeight), 2, MidpointRounding.AwayFromZero)
                : Math.Round(targetTotal / weights.Length, 2, MidpointRounding.AwayFromZero);

            share = Math.Max(0m, share);
            result[i] = share;
            runningSum += share;
        }

        result[maxIdx] = Math.Max(0m, Math.Round(targetTotal - runningSum, 2, MidpointRounding.AwayFromZero));
        return result;
    }
}
