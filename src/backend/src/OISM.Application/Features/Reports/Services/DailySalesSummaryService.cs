using System.Security;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Helpers;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Reports.Dtos;
using OISM.Domain.Entities;

namespace OISM.Application.Features.Reports.Services;

public class DailySalesSummaryService : IDailySalesSummaryService
{
    public const int MaxRebuildRangeDays = 31;
    private const int DefaultComparisonDays = 7;

    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _currentTenantService;
    private readonly IReportService _reportService;

    public DailySalesSummaryService(
        IAppDbContext context,
        ICurrentTenantService currentTenantService,
        IReportService reportService)
    {
        _context = context;
        _currentTenantService = currentTenantService;
        _reportService = reportService;
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

    public async Task<int> RebuildAsync(Guid tenantId, DateOnly businessDate, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId không được rỗng.", nameof(tenantId));
        }

        if (_currentTenantService.TenantId.HasValue && _currentTenantService.TenantId.Value != tenantId)
        {
            throw new SecurityException($"Không được phép tổng hợp dữ liệu cho tenant '{tenantId}' từ ngữ cảnh tenant khác.");
        }

        var dayLocal = businessDate.ToDateTime(TimeOnly.MinValue);
        var startUtc = ReportTimeHelper.StartOfDayUtc(dayLocal);
        var endUtcExclusive = ReportTimeHelper.StartOfDayUtc(dayLocal.AddDays(1));

        return await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            _context.UnitOfWork.ResetTracking();
            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);

            // Khóa sau khi mở transaction để tuần tự hóa thao tác xóa/chèn cho cùng (tenant, ngày).
            await _context.Locks.LockDailySalesSummaryAsync(tenantId, businessDate, ct);

            var orders = await RevenueProfitCalculationHelper.QueryRevenueOrdersAsync(
                _context,
                tenantId,
                startUtc,
                endUtcExclusive,
                ct: ct);

            var returns = await RevenueProfitCalculationHelper.QueryConfirmedReturnsAsync(
                _context,
                tenantId,
                startUtc,
                endUtcExclusive,
                ct: ct);

            var buckets = new Dictionary<(Guid BranchId, string Channel, Guid SkuId), MutableBucket>();

            foreach (var order in orders)
            {
                var orderItems = order.Items.ToList();
                var allocatedRevenues = RevenueProfitCalculationHelper.AllocateOrderRevenueByItem(order);

                for (int i = 0; i < orderItems.Count; i++)
                {
                    var item = orderItems[i];
                    var key = (order.BranchId, order.Channel, item.SkuId);
                    if (!buckets.TryGetValue(key, out var bucket))
                    {
                        bucket = new MutableBucket();
                        buckets[key] = bucket;
                    }

                    bucket.SoldQuantity += item.BaseQuantity;
                    bucket.Revenue += allocatedRevenues[i];
                    bucket.OrderCogs += RevenueProfitCalculationHelper.CalculateOrderItemCogs(item);
                }
            }

            foreach (var salesReturn in returns)
            {
                var returnItems = salesReturn.Items;
                var allocatedReturns = RevenueProfitCalculationHelper.AllocateReturnRevenueByItem(salesReturn);
                var channel = salesReturn.OrderChannel ?? "POS";

                for (int i = 0; i < returnItems.Count; i++)
                {
                    var item = returnItems[i];
                    var key = (salesReturn.BranchId, channel, item.SkuId);
                    if (!buckets.TryGetValue(key, out var bucket))
                    {
                        bucket = new MutableBucket();
                        buckets[key] = bucket;
                    }

                    bucket.ReturnedQuantity += RevenueProfitCalculationHelper.CalculateReturnItemBaseQuantity(item);
                    bucket.ReturnAmount += allocatedReturns[i];
                    bucket.ReturnCogs += RevenueProfitCalculationHelper.CalculateReturnItemCogs(item);
                }
            }

            var existingRows = await _context.DailySalesSummaries
                .Where(s => s.TenantId == tenantId && s.BusinessDate == businessDate)
                .ToListAsync(ct);

            if (existingRows.Count > 0)
            {
                _context.DailySalesSummaries.RemoveRange(existingRows);
            }

            var nowUtc = DateTimeOffset.UtcNow;
            var newRows = new List<DailySalesSummary>();

            foreach (var (key, bucket) in buckets)
            {
                var soldQty = Math.Round(bucket.SoldQuantity, 3, MidpointRounding.AwayFromZero);
                var returnedQty = Math.Round(bucket.ReturnedQuantity, 3, MidpointRounding.AwayFromZero);
                var revenue = Math.Round(bucket.Revenue, 2, MidpointRounding.AwayFromZero);
                var returnAmount = Math.Round(bucket.ReturnAmount, 2, MidpointRounding.AwayFromZero);
                var netCogs = Math.Round(bucket.OrderCogs - bucket.ReturnCogs, 2, MidpointRounding.AwayFromZero);

                // Không ghi dòng toàn 0.
                if (soldQty == 0m && returnedQty == 0m && revenue == 0m && returnAmount == 0m && netCogs == 0m)
                {
                    continue;
                }

                newRows.Add(new DailySalesSummary
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    BusinessDate = businessDate,
                    BranchId = key.BranchId,
                    Channel = key.Channel,
                    SkuId = key.SkuId,
                    SoldQuantity = soldQty,
                    ReturnedQuantity = returnedQty,
                    Revenue = revenue,
                    ReturnAmount = returnAmount,
                    CostOfGoodsSold = netCogs,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc
                });
            }

            if (newRows.Count > 0)
            {
                _context.DailySalesSummaries.AddRange(newRows);
            }

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return newRows.Count;
        });
    }

    public async Task<RebuildDailySummaryResponseDto> RebuildRangeAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
        CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        if (!fromDate.HasValue || !toDate.HasValue)
        {
            throw new OismException("VALIDATION_ERROR", "Cần cung cấp đầy đủ ngày bắt đầu (fromDate) và ngày kết thúc (toDate).", 400);
        }

        if (fromDate.Value > toDate.Value)
        {
            throw new OismException("VALIDATION_ERROR", "Ngày bắt đầu (fromDate) không được lớn hơn ngày kết thúc (toDate).", 400);
        }

        var inclusiveDays = toDate.Value.DayNumber - fromDate.Value.DayNumber + 1;
        if (inclusiveDays > MaxRebuildRangeDays)
        {
            throw new OismException("VALIDATION_ERROR", $"Khoảng ngày tính lại tối đa {MaxRebuildRangeDays} ngày (đang chọn {inclusiveDays} ngày).", 400);
        }

        int totalRowsWritten = 0;
        for (var d = fromDate.Value; d <= toDate.Value; d = d.AddDays(1))
        {
            totalRowsWritten += await RebuildAsync(tenantId, d, ct);
        }

        return new RebuildDailySummaryResponseDto
        {
            FromDate = fromDate.Value,
            ToDate = toDate.Value,
            DaysProcessed = inclusiveDays,
            RowsWritten = totalRowsWritten
        };
    }

    public async Task<DailySalesSummaryComparisonDto> GetComparisonAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
        Guid? branchId,
        CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        DateOnly effectiveFrom;
        DateOnly effectiveTo;
        if (!fromDate.HasValue && !toDate.HasValue)
        {
            effectiveTo = DateOnly.FromDateTime(ReportTimeHelper.Today());
            effectiveFrom = effectiveTo.AddDays(-(DefaultComparisonDays - 1));
        }
        else if (fromDate.HasValue && toDate.HasValue)
        {
            effectiveFrom = fromDate.Value;
            effectiveTo = toDate.Value;
        }
        else
        {
            throw new OismException("VALIDATION_ERROR", "Cần truyền đủ cả fromDate và toDate hoặc để trống cả hai.", 400);
        }

        if (effectiveFrom > effectiveTo)
        {
            throw new OismException("VALIDATION_ERROR", "Ngày bắt đầu (fromDate) không được lớn hơn ngày kết thúc (toDate).", 400);
        }

        var filter = new ReportFilterDto
        {
            BranchId = branchId,
            StartDate = ReportTimeHelper.StartOfDayUtc(effectiveFrom.ToDateTime(TimeOnly.MinValue)),
            EndDate = ReportTimeHelper.StartOfDayUtc(effectiveTo.AddDays(1).ToDateTime(TimeOnly.MinValue))
        };

        var liveReport = await _reportService.GetRevenueProfitReportAsync(filter, isOwner: true, ct);
        var liveByDate = liveReport.DailyTrend
            .ToDictionary(d => DateOnly.ParseExact(d.Date, "yyyy-MM-dd"));

        var summaryQuery = _context.DailySalesSummaries
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.BusinessDate >= effectiveFrom && s.BusinessDate <= effectiveTo);

        if (branchId.HasValue && branchId.Value != Guid.Empty)
        {
            summaryQuery = summaryQuery.Where(s => s.BranchId == branchId.Value);
        }

        var summaryRows = await summaryQuery.ToListAsync(ct);
        var summaryByDate = summaryRows
            .GroupBy(s => s.BusinessDate)
            .ToDictionary(g => g.Key, g => g.ToList());

        var allDates = liveByDate.Keys
            .Union(summaryByDate.Keys)
            .OrderBy(d => d)
            .ToList();

        var days = new List<DailySalesSummaryDayDto>(allDates.Count);
        foreach (var date in allDates)
        {
            DailySalesSummaryMetricsDto liveMetrics;
            if (liveByDate.TryGetValue(date, out var liveTrend))
            {
                var netRev = liveTrend.NetRevenue;
                var netCogs = liveTrend.NetCogs ?? 0m;
                liveMetrics = new DailySalesSummaryMetricsDto
                {
                    Revenue = liveTrend.Revenue,
                    ReturnAmount = liveTrend.ReturnAmount,
                    NetRevenue = netRev,
                    CostOfGoodsSold = netCogs,
                    GrossProfit = liveTrend.GrossProfit ?? (netRev - netCogs)
                };
            }
            else
            {
                liveMetrics = new DailySalesSummaryMetricsDto();
            }

            DailySalesSummaryMetricsDto? summaryMetrics = null;
            DateTimeOffset? updatedAt = null;
            bool matched;
            string status;

            if (summaryByDate.TryGetValue(date, out var dayRows) && dayRows.Count > 0)
            {
                var rev = dayRows.Sum(r => r.Revenue);
                var ret = dayRows.Sum(r => r.ReturnAmount);
                var netRev = rev - ret;
                var cogs = dayRows.Sum(r => r.CostOfGoodsSold);
                var profit = netRev - cogs;

                summaryMetrics = new DailySalesSummaryMetricsDto
                {
                    Revenue = rev,
                    ReturnAmount = ret,
                    NetRevenue = netRev,
                    CostOfGoodsSold = cogs,
                    GrossProfit = profit
                };

                updatedAt = dayRows.Max(r => r.UpdatedAt ?? r.CreatedAt);

                // Bảng tổng hợp lưu numeric(20,2) và làm tròn từng dòng (chi nhánh, kênh, SKU), còn báo cáo trực tiếp
                // cộng giá vốn WAC 6 chữ số lẻ rồi mới làm tròn. Sai số làm tròn tối đa 0,005 mỗi dòng, nên cho phép
                // chênh 0,01 × số dòng; chênh lớn hơn mới là lệch thật.
                var tolerance = 0.01m * dayRows.Count;
                matched =
                    Math.Abs(summaryMetrics.Revenue - liveMetrics.Revenue) <= tolerance &&
                    Math.Abs(summaryMetrics.ReturnAmount - liveMetrics.ReturnAmount) <= tolerance &&
                    Math.Abs(summaryMetrics.CostOfGoodsSold - liveMetrics.CostOfGoodsSold) <= tolerance &&
                    Math.Abs(summaryMetrics.GrossProfit - liveMetrics.GrossProfit) <= tolerance;

                status = matched ? "Matched" : "Mismatch";
            }
            else
            {
                matched = false;
                status = "NotSummarized";
            }

            days.Add(new DailySalesSummaryDayDto
            {
                Date = date,
                Summary = summaryMetrics,
                Live = liveMetrics,
                Matched = matched,
                Status = status,
                UpdatedAt = updatedAt
            });
        }

        return new DailySalesSummaryComparisonDto
        {
            FromDate = effectiveFrom,
            ToDate = effectiveTo,
            BranchId = branchId,
            Days = days
        };
    }

    private sealed class MutableBucket
    {
        public decimal SoldQuantity { get; set; }
        public decimal ReturnedQuantity { get; set; }
        public decimal Revenue { get; set; }
        public decimal ReturnAmount { get; set; }
        public decimal OrderCogs { get; set; }
        public decimal ReturnCogs { get; set; }
    }
}
