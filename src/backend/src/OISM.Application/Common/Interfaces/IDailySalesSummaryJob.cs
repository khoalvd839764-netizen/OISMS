namespace OISM.Application.Common.Interfaces;

/// <summary>
/// Hợp đồng tác vụ nền tự động tổng hợp báo cáo bán hàng theo ngày cho các tenant.
/// </summary>
public interface IDailySalesSummaryJob
{
    Task<int> ExecuteAsync(DateTimeOffset? nowUtc = null, CancellationToken ct = default);
}
