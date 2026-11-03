namespace OISM.Application.Common.Interfaces;

/// <summary>
/// Hợp đồng tác vụ nền tự động sinh kỳ kiểm kê định kỳ theo lịch cho các tenant.
/// </summary>
public interface IStockCountPeriodJob
{
    Task<int> ExecuteAsync(DateTimeOffset? nowUtc = null, CancellationToken ct = default);
}
