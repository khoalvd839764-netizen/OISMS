using OISM.Application.Features.Reports.Dtos;

namespace OISM.Application.Features.Reports.Services;

public interface IReportService
{
    Task<RevenueProfitReportDto> GetRevenueProfitReportAsync(ReportFilterDto filter, CancellationToken ct = default);
    Task<InventoryVelocityReportDto> GetInventoryVelocityReportAsync(ReportFilterDto filter, CancellationToken ct = default);
    Task<List<StockAlertDto>> GetStockAlertsAsync(Guid? branchId, CancellationToken ct = default);
}
