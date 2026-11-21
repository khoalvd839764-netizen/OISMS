using OISM.Application.Features.Reports.Dtos;

namespace OISM.Application.Features.Reports.Services;

/// <summary>
/// Giao diện dịch vụ Báo cáo tồn kho theo kỳ (NXT) và Thẻ kho (S12/S10 - Upgrade 13).
/// </summary>
public interface IInventoryPeriodReportService
{
    /// <summary>
    /// Lấy báo cáo tồn kho theo kỳ: Đầu kỳ, Nhập, Xuất, Cuối kỳ theo Chi nhánh hoặc Toàn cửa hàng (Tất cả).
    /// </summary>
    Task<InventoryPeriodReportDto> GetInventoryPeriodReportAsync(
        InventoryPeriodFilterDto filter,
        bool? isOwner = null,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy toàn bộ SKU khớp bộ lọc (bỏ qua phân trang của người gọi) để xuất Excel; dòng tổng cộng tính trên mọi dòng.
    /// </summary>
    Task<InventoryPeriodReportDto> GetInventoryPeriodReportForExportAsync(
        InventoryPeriodFilterDto filter,
        bool? isOwner = null,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy Thẻ kho (S12-DN) hoặc Sổ chi tiết hàng hóa (S10-DN) cho một SKU tại một Chi nhánh trong kỳ.
    /// </summary>
    Task<StockCardReportDto> GetStockCardReportAsync(
        StockCardFilterDto filter,
        bool? isOwner = null,
        CancellationToken ct = default);
}
