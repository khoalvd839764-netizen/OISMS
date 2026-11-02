using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Ai.Dtos;

namespace OISM.Application.Features.Ai.Services;

public class AiForecastingService : IAiForecastingService
{
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

        var toDate = query.EndDate?.Date ?? DateTime.UtcNow.Date;
        var fromDate = query.StartDate?.Date ?? toDate.AddDays(-query.Days);

        if (fromDate > toDate)
        {
            (fromDate, toDate) = (toDate, fromDate);
        }

        var daysCount = Math.Max(1, (int)(toDate - fromDate).TotalDays + 1);

        // 1. Quét từ Sổ cái kho (InventoryTransaction) với Type = 'OUT'
        var fromOffset = new DateTimeOffset(fromDate, TimeSpan.Zero);
        var toOffset = new DateTimeOffset(toDate.AddDays(1).AddTicks(-1), TimeSpan.Zero);

        var transQuery = _context.InventoryTransactions
            .AsNoTracking()
            .Include(t => t.Sku)
            .Where(t => t.TenantId == tenantId && t.Type == "OUT" && t.CreatedAt >= fromOffset && t.CreatedAt <= toOffset);

        if (query.BranchId.HasValue && query.BranchId.Value != Guid.Empty)
        {
            transQuery = transQuery.Where(t => t.BranchId == query.BranchId.Value);
        }

        if (query.SkuId.HasValue && query.SkuId.Value != Guid.Empty)
        {
            transQuery = transQuery.Where(t => t.SkuId == query.SkuId.Value);
        }

        var rawTransactions = await transQuery.ToListAsync(ct);

        var dailyRecords = new List<ExportHistoryDailyItemDto>();

        if (rawTransactions.Count > 0)
        {
            dailyRecords = rawTransactions
                .GroupBy(t => new { Date = DateOnly.FromDateTime(t.CreatedAt.UtcDateTime), t.SkuId })
                .Select(g =>
                {
                    var first = g.First();
                    var name = !string.IsNullOrWhiteSpace(first.Sku?.VariantName) 
                        ? first.Sku.VariantName 
                        : (first.Sku?.Code ?? string.Empty);
                    return new ExportHistoryDailyItemDto
                    {
                        Date = g.Key.Date,
                        SkuId = g.Key.SkuId,
                        SkuCode = first.Sku?.Code ?? string.Empty,
                        SkuName = name,
                        Quantity = g.Sum(x => x.Quantity),
                        BaseUnit = first.Sku?.Unit ?? "piece",
                        TransactionCount = g.Count()
                    };
                })
                .OrderBy(d => d.Date)
                .ThenBy(d => d.SkuCode)
                .ToList();
        }
        else
        {
            // Fallback sang OrderItems của các đơn hàng đã Confirmed/Completed để đảm bảo dữ liệu đơn hàng
            var orderQuery = _context.Orders
                .AsNoTracking()
                .Include(o => o.Items)
                .Where(o => o.TenantId == tenantId && (o.Status == "Confirmed" || o.Status == "Completed")
                            && o.CreatedAt >= fromOffset && o.CreatedAt <= toOffset);

            if (query.BranchId.HasValue && query.BranchId.Value != Guid.Empty)
            {
                orderQuery = orderQuery.Where(o => o.BranchId == query.BranchId.Value);
            }

            var orders = await orderQuery.ToListAsync(ct);
            var orderItems = orders.SelectMany(o => o.Items.Select(item => new { Order = o, Item = item })).ToList();

            if (query.SkuId.HasValue && query.SkuId.Value != Guid.Empty)
            {
                orderItems = orderItems.Where(x => x.Item.SkuId == query.SkuId.Value).ToList();
            }

            var skuIds = orderItems.Select(x => x.Item.SkuId).Distinct().ToList();
            var skuMap = await _context.ProductVariants
                .AsNoTracking()
                .Include(s => s.Product)
                .Where(s => s.TenantId == tenantId && skuIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, ct);

            dailyRecords = orderItems
                .GroupBy(x => new { Date = DateOnly.FromDateTime(x.Order.CreatedAt.UtcDateTime), x.Item.SkuId })
                .Select(g =>
                {
                    skuMap.TryGetValue(g.Key.SkuId, out var skuInfo);
                    var name = !string.IsNullOrWhiteSpace(skuInfo?.VariantName)
                        ? skuInfo.VariantName
                        : (skuInfo?.Code ?? string.Empty);
                    return new ExportHistoryDailyItemDto
                    {
                        Date = g.Key.Date,
                        SkuId = g.Key.SkuId,
                        SkuCode = skuInfo?.Code ?? string.Empty,
                        SkuName = name,
                        Quantity = g.Sum(x => x.Item.Quantity),
                        BaseUnit = skuInfo?.Unit ?? "piece",
                        TransactionCount = g.Count()
                    };
                })
                .OrderBy(d => d.Date)
                .ThenBy(d => d.SkuCode)
                .ToList();
        }

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

        // 2. Lấy dữ liệu xuất kho 30 ngày qua để tính vận tốc bán hàng trung bình ngày (Daily Sales Velocity)
        var thirtyDaysAgo = DateTime.UtcNow.Date.AddDays(-30);
        var exportHistory = await GetExportHistoryAsync(new ExportHistoryQueryDto
        {
            BranchId = query.BranchId,
            Days = 30,
            StartDate = thirtyDaysAgo,
            EndDate = DateTime.UtcNow.Date
        }, ct);

        // Gom nhóm vận tốc bán theo SkuId
        var salesVelocityBySku = exportHistory.DailyRecords
            .GroupBy(r => r.SkuId)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    TotalExport = g.Sum(x => x.Quantity),
                    ActiveDays = g.Select(x => x.Date).Distinct().Count(),
                    DailyVelocity = Math.Round(g.Sum(x => x.Quantity) / 30m, 2)
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

            salesVelocityBySku.TryGetValue(skuId, out var velocityInfo);
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
