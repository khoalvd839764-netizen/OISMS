using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Helpers;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Ai.Dtos;

namespace OISM.Application.Features.Ai.Services;

public class AiForecastingService : IAiForecastingService
{
    private const int VelocityWindowDays = 30;

    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _currentTenantService;

    public AiForecastingService(IAppDbContext context, ICurrentTenantService currentTenantService)
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

    public async Task<ExportHistoryResponseDto> GetExportHistoryAsync(ExportHistoryQueryDto query, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var toDate = query.EndDate?.Date ?? ReportTimeHelper.Today();
        var fromDate = query.StartDate?.Date ?? toDate.AddDays(-(query.Days - 1));

        if (fromDate > toDate)
        {
            (fromDate, toDate) = (toDate, fromDate);
        }

        var daysCount = Math.Max(1, (int)(toDate - fromDate).TotalDays + 1);

        var rows = await LoadExportRowsAsync(tenantId, query.BranchId, query.SkuId, fromDate, toDate, ct);

        var dailyRecords = rows
            .GroupBy(r => new { r.Date, r.SkuId })
            .Select(g =>
            {
                var first = g.First();
                return new ExportHistoryDailyItemDto
                {
                    Date = g.Key.Date,
                    SkuId = g.Key.SkuId,
                    SkuCode = first.SkuCode,
                    SkuName = first.SkuName,
                    Quantity = g.Sum(x => x.Quantity),
                    BaseUnit = first.BaseUnit,
                    TransactionCount = g.Count()
                };
            })
            .OrderBy(d => d.Date)
            .ThenBy(d => d.SkuCode)
            .ToList();

        var totalQty = dailyRecords.Sum(r => r.Quantity);
        var avgDaily = daysCount > 0 ? Math.Round(totalQty / daysCount, 2) : 0;

        return new ExportHistoryResponseDto
        {
            TenantId = tenantId,
            BranchId = query.BranchId,
            FromDate = fromDate,
            ToDate = toDate,
            TotalExportQuantity = totalQty,
            TotalDays = daysCount,
            AverageDailyExportQuantity = avgDaily,
            DailyRecords = dailyRecords
        };
    }

    /// <summary>
    /// Lấy các dòng xuất bán trong khoảng ngày (giờ Việt Nam, lọc nửa mở).
    /// Dòng OUT của chuyển kho nội bộ không phải nhu cầu bán nên bị loại.
    /// </summary>
    private async Task<List<ExportRow>> LoadExportRowsAsync(
        Guid tenantId,
        Guid? branchId,
        Guid? skuId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken ct)
    {
        var fromBoundary = ReportTimeHelper.StartOfDayUtc(fromDate);
        var endBoundary = ReportTimeHelper.StartOfDayUtc(toDate.AddDays(1));

        var transQuery = _context.InventoryTransactions
            .AsNoTracking()
            .Include(t => t.Sku)
            .Where(t => t.TenantId == tenantId && t.Type == "OUT" && t.TransferItemId == null
                        && t.CreatedAt >= fromBoundary && t.CreatedAt < endBoundary);

        if (branchId.HasValue && branchId.Value != Guid.Empty)
        {
            transQuery = transQuery.Where(t => t.BranchId == branchId.Value);
        }

        if (skuId.HasValue && skuId.Value != Guid.Empty)
        {
            transQuery = transQuery.Where(t => t.SkuId == skuId.Value);
        }

        var rawTransactions = await transQuery.ToListAsync(ct);

        if (rawTransactions.Count > 0)
        {
            return rawTransactions
                .Select(t => new ExportRow(
                    t.BranchId,
                    t.SkuId,
                    ReportTimeHelper.ToLocalDate(t.CreatedAt),
                    t.Quantity,
                    t.Sku?.Code ?? string.Empty,
                    !string.IsNullOrWhiteSpace(t.Sku?.VariantName) ? t.Sku.VariantName : (t.Sku?.Code ?? string.Empty),
                    t.Sku?.Unit ?? "piece"))
                .ToList();
        }

        // Fallback sang OrderItems của các đơn hàng đã Confirmed/Completed để đảm bảo dữ liệu đơn hàng
        var orderQuery = _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.TenantId == tenantId && (o.Status == "Confirmed" || o.Status == "Completed")
                        && o.CreatedAt >= fromBoundary && o.CreatedAt < endBoundary);

        if (branchId.HasValue && branchId.Value != Guid.Empty)
        {
            orderQuery = orderQuery.Where(o => o.BranchId == branchId.Value);
        }

        var orders = await orderQuery.ToListAsync(ct);
        var orderItems = orders.SelectMany(o => o.Items.Select(item => new { Order = o, Item = item })).ToList();

        if (skuId.HasValue && skuId.Value != Guid.Empty)
        {
            orderItems = orderItems.Where(x => x.Item.SkuId == skuId.Value).ToList();
        }

        var skuIds = orderItems.Select(x => x.Item.SkuId).Distinct().ToList();
        var skuMap = await _context.ProductVariants
            .AsNoTracking()
            .Include(s => s.Product)
            .Where(s => s.TenantId == tenantId && skuIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);

        return orderItems
            .Select(x =>
            {
                skuMap.TryGetValue(x.Item.SkuId, out var skuInfo);
                return new ExportRow(
                    x.Order.BranchId,
                    x.Item.SkuId,
                    ReportTimeHelper.ToLocalDate(x.Order.CreatedAt),
                    x.Item.BaseQuantity,
                    skuInfo?.Code ?? string.Empty,
                    !string.IsNullOrWhiteSpace(skuInfo?.VariantName) ? skuInfo.VariantName : (skuInfo?.Code ?? string.Empty),
                    skuInfo?.Unit ?? "piece");
            })
            .ToList();
    }

    private sealed record ExportRow(
        Guid BranchId,
        Guid SkuId,
        DateOnly Date,
        decimal Quantity,
        string SkuCode,
        string SkuName,
        string BaseUnit);

    public async Task<ReorderRecommendationSummaryDto> GetReorderRecommendationsAsync(ReorderRecommendationQueryDto query, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        // 1. Lấy danh sách số dư tồn kho InventoryBalance
        var balanceQuery = _context.InventoryBalances
            .AsNoTracking()
            .Include(b => b.Branch)
            .Include(b => b.Sku)
            .Where(b => b.TenantId == tenantId);

        if (query.BranchId.HasValue && query.BranchId.Value != Guid.Empty)
        {
            balanceQuery = balanceQuery.Where(b => b.BranchId == query.BranchId.Value);
        }

        var balances = await balanceQuery.ToListAsync(ct);

        // 2. Lấy dữ liệu xuất bán 30 ngày qua để tính vận tốc bán trung bình ngày (Daily Sales Velocity)
        var today = ReportTimeHelper.Today();
        var exportRows = await LoadExportRowsAsync(tenantId, query.BranchId, null, today.AddDays(-(VelocityWindowDays - 1)), today, ct);

        // Gom nhóm theo từng chi nhánh + SKU: nhu cầu của chi nhánh này không được tính cho chi nhánh khác
        var salesVelocity = exportRows
            .GroupBy(r => (r.BranchId, r.SkuId))
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    ActiveDays = g.Select(x => x.Date).Distinct().Count(),
                    DailyVelocity = Math.Round(g.Sum(x => x.Quantity) / VelocityWindowDays, 2)
                });

        var recommendations = new List<ReorderRecommendationDto>();

        foreach (var bal in balances)
        {
            var sku = bal.Sku;
            var skuId = bal.SkuId;
            var onHand = bal.OnHand;
            var reserved = bal.Reserved;
            var available = onHand - reserved;
            var reorderPoint = bal.ReorderPoint;

            salesVelocity.TryGetValue((bal.BranchId, skuId), out var velocityInfo);
            var dailyVelocity = velocityInfo?.DailyVelocity ?? 0m;
            var activeDays = velocityInfo?.ActiveDays ?? 0;

            // Tính số ngày dự kiến cạn kho
            int daysUntilStockout;
            if (available <= 0)
            {
                daysUntilStockout = 0;
            }
            else if (dailyVelocity > 0)
            {
                daysUntilStockout = (int)Math.Floor(available / dailyVelocity);
            }
            else
            {
                daysUntilStockout = 999; // Tồn an toàn do chưa phát sinh xuất bán
            }

            // Đánh giá nhu cầu nhập lại
            var leadTime = query.LeadTimeDays;
            var safetyDays = query.SafetyStockDays;
            var horizonDays = leadTime + safetyDays;

            var needsReorder = available <= reorderPoint || (dailyVelocity > 0 && daysUntilStockout <= horizonDays);

            decimal suggestedQuantity = 0;
            string confidenceLevel;
            string reason;

            if (activeDays >= 14)
            {
                confidenceLevel = "High";
            }
            else if (activeDays >= 5)
            {
                confidenceLevel = "Medium";
            }
            else
            {
                confidenceLevel = "Low";
            }

            if (needsReorder)
            {
                var targetBuffer = Math.Max(reorderPoint, 5m);
                var expectedDemand = Math.Ceiling(dailyVelocity * horizonDays);
                var needed = (expectedDemand + targetBuffer) - available;
                suggestedQuantity = Math.Max(1, needed);

                if (available <= 0)
                {
                    reason = $"Hết hàng khả dụng (Available = {available}). Tốc độ bán {dailyVelocity} {sku?.Unit ?? "sp"}/ngày. Đề xuất nhập ngay để tránh gián đoạn.";
                }
                else if (available <= reorderPoint)
                {
                    reason = $"Tồn khả dụng ({available}) chạm hoặc dưới ngưỡng cảnh báo an toàn ({reorderPoint}). Dự kiến cạn kho trong {daysUntilStockout} ngày.";
                }
                else
                {
                    reason = $"Tốc độ bán {dailyVelocity} {sku?.Unit ?? "sp"}/ngày. Tồn hiện tại chỉ đủ dùng {daysUntilStockout} ngày, nhỏ hơn thời gian cung ứng ({horizonDays} ngày).";
                }

                if (confidenceLevel == "Low")
                {
                    reason += " (Lưu ý: Dữ liệu lịch sử xuất bán còn ít, cần tích lũy thêm để tăng độ chính xác).";
                }
            }
            else
            {
                reason = $"Tồn kho an toàn. Khả dụng ({available}) cao hơn ngưỡng ReorderPoint ({reorderPoint}) và đủ đáp ứng nhu cầu dự kiến {daysUntilStockout} ngày tới.";
            }

            if (!query.OnlyNeedsReorder || needsReorder)
            {
                var skuDisplayName = !string.IsNullOrWhiteSpace(sku?.VariantName)
                    ? sku.VariantName
                    : (sku?.Code ?? string.Empty);

                recommendations.Add(new ReorderRecommendationDto
                {
                    SkuId = skuId,
                    SkuCode = sku?.Code ?? string.Empty,
                    SkuName = skuDisplayName,
                    BaseUnit = sku?.Unit ?? "piece",
                    BranchId = bal.BranchId,
                    BranchName = bal.Branch?.Name ?? string.Empty,
                    OnHandQuantity = onHand,
                    ReservedQuantity = reserved,
                    AvailableQuantity = available,
                    ReorderPoint = reorderPoint,
                    DailySalesVelocity = dailyVelocity,
                    DaysUntilStockout = daysUntilStockout,
                    LeadTimeDays = leadTime,
                    SafetyStockDays = safetyDays,
                    NeedsReorder = needsReorder,
                    SuggestedQuantity = suggestedQuantity,
                    ConfidenceLevel = confidenceLevel,
                    Reason = reason
                });
            }
        }

        // Ưu tiên sắp xếp các mặt hàng cấp bách lên đầu
        var sortedRecommendations = recommendations
            .OrderByDescending(r => r.NeedsReorder)
            .ThenBy(r => r.DaysUntilStockout)
            .ThenByDescending(r => r.SuggestedQuantity)
            .ToList();

        return new ReorderRecommendationSummaryDto
        {
            TenantId = tenantId,
            BranchId = query.BranchId,
            EvaluatedAt = DateTime.UtcNow,
            TotalSkusAnalyzed = balances.Count,
            SkusNeedingReorder = sortedRecommendations.Count(r => r.NeedsReorder),
            TotalSuggestedQuantity = sortedRecommendations.Sum(r => r.SuggestedQuantity),
            Recommendations = sortedRecommendations
        };
    }
}
