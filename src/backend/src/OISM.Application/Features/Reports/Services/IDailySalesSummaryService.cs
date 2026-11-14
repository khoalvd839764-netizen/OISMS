using OISM.Application.Features.Reports.Dtos;

namespace OISM.Application.Features.Reports.Services;

public interface IDailySalesSummaryService
{
    /// <summary>
    /// Tổng hợp lại toàn bộ dòng trong <c>daily_sales_summaries</c> cho một tenant và một ngày kinh doanh (UTC+07)
    /// trong một giao dịch duy nhất được bảo vệ bởi <c>pg_advisory_xact_lock</c>.
    /// </summary>
    Task<int> RebuildAsync(Guid tenantId, DateOnly businessDate, CancellationToken ct = default);

    /// <summary>
    /// Tính lại tổng hợp theo khoảng ngày tối đa 31 ngày cho tenant hiện tại từ JWT.
    /// </summary>
    Task<RebuildDailySummaryResponseDto> RebuildRangeAsync(DateOnly? fromDate, DateOnly? toDate, CancellationToken ct = default);

    /// <summary>
    /// Đối soát số liệu từng ngày giữa bảng tổng hợp <c>daily_sales_summaries</c> và báo cáo trực tiếp cho Owner.
    /// </summary>
    Task<DailySalesSummaryComparisonDto> GetComparisonAsync(DateOnly? fromDate, DateOnly? toDate, Guid? branchId, CancellationToken ct = default);
}
