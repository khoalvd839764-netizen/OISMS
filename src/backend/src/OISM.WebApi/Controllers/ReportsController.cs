using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Features.Reports.Dtos;
using OISM.Application.Features.Reports.Services;

namespace OISM.WebApi.Controllers;

/// <summary>
/// API Báo cáo Doanh thu, Lợi nhuận gộp, Vận tốc luân chuyển kho và Cảnh báo tồn thấp (FR-REP-01, FR-REP-02, FR-REP-03).
/// Dành cho Chủ shop (Owner) và Quản lý/Nhân viên được phân quyền (Staff).
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize(Roles = "Owner,Staff")]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reportService;

    public ReportsController(IReportService reportService)
    {
        _reportService = reportService;
    }

    /// <summary>
    /// Báo cáo Doanh thu thuần, Tổng giá vốn (COGS) và Lợi nhuận gộp (Gross Profit = Revenue - COGS - FR-REP-01).
    /// Hỗ trợ phân rã theo Kênh bán (POS, Shopee, TikTok, Lazada), Chi nhánh và biểu đồ theo ngày.
    /// </summary>
    [HttpGet("revenue-profit")]
    public async Task<IActionResult> GetRevenueProfit([FromQuery] ReportFilterDto filter, CancellationToken ct)
    {
        var result = await _reportService.GetRevenueProfitReportAsync(filter, ct);
        return Ok(result);
    }

    /// <summary>
    /// Báo cáo Giá trị tồn kho hiện thời theo giá vốn WAC và Vận tốc luân chuyển hàng hóa (FR-REP-02).
    /// Thống kê Top mặt hàng bán chạy nhất (Best-sellers) và Mặt hàng luân chuyển chậm (Slow-moving).
    /// </summary>
    [HttpGet("inventory-velocity")]
    public async Task<IActionResult> GetInventoryVelocity([FromQuery] ReportFilterDto filter, CancellationToken ct)
    {
        var result = await _reportService.GetInventoryVelocityReportAsync(filter, ct);
        return Ok(result);
    }

    /// <summary>
    /// Báo cáo Cảnh báo tồn kho tối thiểu (Stock Alert - FR-REP-03).
    /// Liệt kê các SKU có Tồn khả dụng (Available = OnHand - Reserved) chạm hoặc giảm dưới điểm đặt hàng lại (ReorderPoint).
    /// Phân cấp cảnh báo: Warning (chạm ngưỡng), Critical (dưới 50%), OutOfStock (hết hàng).
    /// </summary>
    [HttpGet("stock-alerts")]
    public async Task<IActionResult> GetStockAlerts([FromQuery] Guid? branchId, CancellationToken ct)
    {
        var result = await _reportService.GetStockAlertsAsync(branchId, ct);
        return Ok(result);
    }
}
