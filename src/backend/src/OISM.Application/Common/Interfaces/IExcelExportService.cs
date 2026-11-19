using OISM.Application.Features.Reports.Dtos;

namespace OISM.Application.Common.Interfaces;

/// <summary>
/// Dịch vụ xuất file bảng tính Excel (.xlsx) cho báo cáo và chứng từ (Upgrade 13).
/// Tuân thủ quy tắc che giấu thông tin giá trị/giá vốn đối với tài khoản nhân viên (Staff).
/// </summary>
public interface IExcelExportService
{
    /// <summary>
    /// Xuất báo cáo Tồn kho theo kỳ (NXT) ra định dạng .xlsx.
    /// </summary>
    byte[] ExportInventoryPeriodReport(InventoryPeriodReportDto report, bool isOwner);

    /// <summary>
    /// Xuất Thẻ kho S12-DN (Staff) hoặc Sổ chi tiết hàng hóa S10-DN (Owner) ra định dạng .xlsx.
    /// </summary>
    byte[] ExportStockCardReport(StockCardReportDto report, bool isOwner);
}
