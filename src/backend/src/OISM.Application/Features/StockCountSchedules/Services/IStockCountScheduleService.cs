using OISM.Application.Features.StockCountSchedules.Dtos;

namespace OISM.Application.Features.StockCountSchedules.Services;

public interface IStockCountScheduleService
{
    Task<List<StockCountScheduleDto>> GetSchedulesAsync(CancellationToken ct = default);
    Task<StockCountScheduleDto> CreateScheduleAsync(CreateStockCountScheduleDto dto, CancellationToken ct = default);
}
