using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Helpers;
using OISM.Application.Common.Interfaces;
using OISM.Domain.Common;
using OISM.Domain.Entities;

namespace OISM.Application.Features.Reports.Services;

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

    internal static Task<List<Order>> QueryRevenueOrdersAsync(
        IAppDbContext context,
        Guid tenantId,
        DateTimeOffset? startUtc,
        DateTimeOffset? endUtcExclusive,
        Guid? branchId = null,
        string? channel = null,
        bool includeBranch = false,
        CancellationToken ct = default)
    {
        IQueryable<Order> query = context.Orders.AsNoTracking();
        if (includeBranch)
        {
            query = query.Include(o => o.Branch);
        }

        query = query
            .Include(o => o.Items)
            .Where(o => o.TenantId == tenantId && (o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Completed));

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
            query = query.Where(o => o.CreatedAt >= startUtc.Value);
        }

        if (endUtcExclusive.HasValue)
        {
            query = query.Where(o => o.CreatedAt < endUtcExclusive.Value);
        }

        return query.ToListAsync(ct);
    }

    internal static Task<List<SalesReturn>> QueryConfirmedReturnsAsync(
        IAppDbContext context,
        Guid tenantId,
        DateTimeOffset? startUtc,
        DateTimeOffset? endUtcExclusive,
        Guid? branchId = null,
        string? channel = null,
        bool includeBranch = false,
        CancellationToken ct = default)
    {
        IQueryable<SalesReturn> query = context.SalesReturns
            .AsNoTracking()
            .Include(r => r.Order);

        if (includeBranch)
        {
            query = query.Include(r => r.Branch);
        }

        query = query
            .Include(r => r.Items)
                .ThenInclude(i => i.OrderItem)
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
            query = query.Where(r => r.ConfirmedAt >= startUtc.Value);
        }

        if (endUtcExclusive.HasValue)
        {
            query = query.Where(r => r.ConfirmedAt < endUtcExclusive.Value);
        }

        return query.ToListAsync(ct);
    }

    internal static DateOnly GetOrderBusinessDate(Order order) =>
        ReportTimeHelper.ToLocalDate(order.CreatedAt);

    internal static DateOnly GetReturnBusinessDate(SalesReturn salesReturn) =>
        ReportTimeHelper.ToLocalDate(salesReturn.ConfirmedAt!.Value);

    internal static decimal CalculateOrderItemCogs(OrderItem item) =>
        (item.CostPrice ?? 0m) * item.BaseQuantity;

    internal static decimal CalculateReturnItemBaseQuantity(SalesReturnItem item) =>
        item.Quantity * (item.OrderItem != null ? item.OrderItem.FactorSnapshot : 1m);

    internal static decimal CalculateReturnItemCogs(SalesReturnItem item) =>
        CalculateReturnItemBaseQuantity(item) * item.OriginalUnitCost;

    internal static decimal CalculateReturnCogs(SalesReturn salesReturn) =>
        salesReturn.Items.Sum(CalculateReturnItemCogs);

    /// <summary>
    /// Doanh thu bị giảm do một phiếu trả hàng đã xác nhận.
    /// Đơn POS/Admin: dùng <see cref="SalesReturn.TotalRefundAmount"/>.
    /// Đơn sàn (Shopee/TikTok/Lazada): dùng tổng (SL trả × đơn giá snapshot).
    /// </summary>
    internal static decimal CalculateReturnRevenueAmount(SalesReturn salesReturn)
    {
        if (salesReturn.Order != null && MarketplaceChannels.Contains(salesReturn.Order.Channel))
        {
            return salesReturn.Items.Sum(i => i.Quantity * (i.OrderItem?.UnitPrice ?? 0m));
        }

        return salesReturn.TotalRefundAmount;
    }

    internal static decimal[] AllocateOrderRevenueByItem(Order order)
    {
        var items = order.Items.ToList();
        if (items.Count == 0)
        {
            return Array.Empty<decimal>();
        }

        var rawWeights = items.Select(i => Math.Max(0m, i.Quantity * i.UnitPrice)).ToArray();
        return AllocateProportionally(rawWeights, Math.Round(order.TotalAmount, 2, MidpointRounding.AwayFromZero));
    }

    internal static decimal[] AllocateReturnRevenueByItem(SalesReturn salesReturn)
    {
        var items = salesReturn.Items.ToList();
        if (items.Count == 0)
        {
            return Array.Empty<decimal>();
        }

        var isMarketplace = salesReturn.Order != null && MarketplaceChannels.Contains(salesReturn.Order.Channel);
        var totalReturnAmount = Math.Round(CalculateReturnRevenueAmount(salesReturn), 2, MidpointRounding.AwayFromZero);

        if (!isMarketplace)
        {
            var itemRefunds = items.Select(i => Math.Max(0m, Math.Round(i.RefundAmount, 2, MidpointRounding.AwayFromZero))).ToArray();
            if (itemRefunds.Sum() == totalReturnAmount)
            {
                return itemRefunds;
            }

            var nonZeroSum = itemRefunds.Sum();
            if (nonZeroSum > 0m)
            {
                return AllocateProportionally(itemRefunds, totalReturnAmount);
            }
        }

        var lineWeights = items.Select(i => Math.Max(0m, i.Quantity * (i.OrderItem?.UnitPrice ?? 0m))).ToArray();
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
