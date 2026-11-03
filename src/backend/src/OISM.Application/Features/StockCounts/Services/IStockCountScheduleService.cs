using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.StockCounts.Dtos;

namespace OISM.Application.Features.StockCounts.Services;

/// <summary>
/// Dịch vụ quản lý Lịch kiểm kê định kỳ và sinh Kỳ kiểm kê theo kho (OISMS-47-U03).
/// </summary>
public interface IStockCountScheduleService
{
    Task<PagedResultDto<StockCountScheduleResponseDto>> GetSchedulesAsync(StockCountScheduleFilterRequestDto filter, CancellationToken ct = default);
    Task<StockCountScheduleResponseDto> CreateScheduleAsync(CreateStockCountScheduleRequestDto dto, Guid userId, CancellationToken ct = default);
    Task<StockCountScheduleResponseDto> UpdateScheduleAsync(Guid id, UpdateStockCountScheduleRequestDto dto, Guid userId, CancellationToken ct = default);
    Task<PagedResultDto<StockCountPeriodResponseDto>> GetPeriodsAsync(StockCountPeriodFilterRequestDto filter, CancellationToken ct = default);
    Task<int> GeneratePeriodsAsync(DateTimeOffset? nowUtc = null, CancellationToken ct = default);
}
