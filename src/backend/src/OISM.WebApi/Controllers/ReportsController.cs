using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Reports.Dtos;
using OISM.Application.Features.Reports.Services;
using OISM.WebApi.Http;

namespace OISM.WebApi.Controllers;

/// <summary>
/// API Báo cáo Doanh thu, Lợi nhuận gộp, Vận tốc luân chuyển kho, Cảnh báo tồn thấp và Tồn kho theo kỳ / Thẻ kho (Upgrade 13).
/// Dành cho Chủ shop (Owner) và Quản lý/Nhân viên được phân quyền (Staff).
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize(Roles = "Owner,Staff")]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reportService;
    private readonly IDailySalesSummaryService _dailySalesSummaryService;
    private readonly IInventoryPeriodReportService _inventoryPeriodReportService;
    private readonly IExcelExportService _excelExportService;

    public ReportsController(
        IReportService reportService,
        IDailySalesSummaryService dailySalesSummaryService,
        IInventoryPeriodReportService inventoryPeriodReportService,
        IExcelExportService excelExportService)
    {
        _reportService = reportService;
        _dailySalesSummaryService = dailySalesSummaryService;
        _inventoryPeriodReportService = inventoryPeriodReportService;
        _excelExportService = excelExportService;
    }

    /// <summary>
    /// Báo cáo Doanh thu thuần, Tổng giá vốn (COGS) và Lợi nhuận gộp (Gross Profit = Revenue - COGS - FR-REP-01).
    /// Hỗ trợ phân rã theo Kênh bán (POS, Shopee, TikTok, Lazada), Chi nhánh và biểu đồ theo ngày.
    /// </summary>
    [HttpGet("revenue-profit")]
    public async Task<IActionResult> GetRevenueProfit([FromQuery] ReportFilterDto filter, CancellationToken ct)
    {
        var isOwner = User.IsInRole("Owner");
        var result = await _reportService.GetRevenueProfitReportAsync(filter, isOwner, ct);
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

    /// <summary>
    /// Thống kê KPI tổng quan cho trang Dashboard (CS-07 / FR-REP-01).
    /// Tổng hợp Doanh thu, Đơn hàng, Lãi gộp (chỉ Owner), Giá trị tồn kho và Top 5 đơn hàng mới nhất.
    /// </summary>
    [HttpGet("dashboard-summary")]
    public async Task<IActionResult> GetDashboardSummary(CancellationToken ct)
    {
        var isOwner = User.IsInRole("Owner");
        var result = await _reportService.GetDashboardSummaryAsync(isOwner, ct);
        return Ok(result);
    }

    /// <summary>
    /// Đối soát số liệu từng ngày giữa bảng tổng hợp <c>daily_sales_summaries</c> và báo cáo trực tiếp (chỉ Owner).
    /// </summary>
    [HttpGet("daily-summaries")]
    [Authorize(Roles = "Owner")]
    [ApiErrorContract(ApiErrorProfile.Reports)]
    public async Task<IActionResult> GetDailySummaries(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] Guid? branchId,
        CancellationToken ct)
    {
        var result = await _dailySalesSummaryService.GetComparisonAsync(fromDate, toDate, branchId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Tính lại thủ công bảng tổng hợp <c>daily_sales_summaries</c> theo khoảng ngày tối đa 31 ngày (chỉ Owner).
    /// Chạy đồng bộ cả khi Hangfire đang tắt; TenantId lấy hoàn toàn từ JWT.
    /// </summary>
    [HttpPost("daily-summaries/rebuild")]
    [Authorize(Roles = "Owner")]
    [ApiErrorContract(ApiErrorProfile.Reports)]
    public async Task<IActionResult> RebuildDailySummaries(
        [FromBody] RebuildDailySummaryRequestDto? request,
        CancellationToken ct)
    {
        if (request == null)
        {
            return ApiErrorResponse.Problem(
                HttpContext,
                ApiErrorProfile.Reports,
                "VALIDATION_ERROR",
                StatusCodes.Status400BadRequest,
                "Dữ liệu yêu cầu tính lại tổng hợp ngày không được để trống.");
        }

        var result = await _dailySalesSummaryService.RebuildRangeAsync(request.FromDate, request.ToDate, ct);
        return Ok(result);
    }

    /// <summary>
    /// Báo cáo Tồn kho theo kỳ (Đầu kỳ, Nhập, Xuất, Cuối kỳ, Hàng đang chuyển - Upgrade 13 / BE-13.1).
    /// </summary>
    [HttpGet("inventory-period")]
    [ApiErrorContract(ApiErrorProfile.Reports)]
    public async Task<IActionResult> GetInventoryPeriod([FromQuery] InventoryPeriodFilterDto filter, CancellationToken ct)
    {
        var isOwner = User.IsInRole("Owner");
        var result = await _inventoryPeriodReportService.GetInventoryPeriodReportAsync(filter, isOwner, ct);
        return Ok(result);
    }

    /// <summary>
    /// Xuất file Excel (.xlsx) báo cáo Tồn kho theo kỳ (ClosedXML - Upgrade 13 / BE-13.3).
    /// </summary>
    [HttpGet("inventory-period/export-excel")]
    [ApiErrorContract(ApiErrorProfile.Reports)]
    public async Task<IActionResult> ExportInventoryPeriodExcel([FromQuery] InventoryPeriodFilterDto filter, CancellationToken ct)
    {
        var isOwner = User.IsInRole("Owner");
        var report = await _inventoryPeriodReportService.GetInventoryPeriodReportAsync(filter, isOwner, ct);
        var bytes = _excelExportService.ExportInventoryPeriodReport(report, isOwner);
        var fileName = $"BaoCaoTonTheoKy_{filter.FromDate:yyyyMMdd}_{filter.ToDate:yyyyMMdd}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    /// <summary>
    /// Thẻ kho S12-DN (Staff) hoặc Sổ chi tiết hàng hóa S10-DN (Owner) cho một SKU tại một chi nhánh (Upgrade 13 / BE-13.2).
    /// </summary>
    [HttpGet("stock-card")]
    [ApiErrorContract(ApiErrorProfile.Reports)]
    public async Task<IActionResult> GetStockCard([FromQuery] StockCardFilterDto filter, CancellationToken ct)
    {
        var isOwner = User.IsInRole("Owner");
        var result = await _inventoryPeriodReportService.GetStockCardReportAsync(filter, isOwner, ct);
        return Ok(result);
    }

    /// <summary>
    /// Xuất file Excel (.xlsx) Thẻ kho S12-DN hoặc Sổ chi tiết hàng hóa S10-DN (ClosedXML - Upgrade 13 / BE-13.3).
    /// </summary>
    [HttpGet("stock-card/export-excel")]
    [ApiErrorContract(ApiErrorProfile.Reports)]
    public async Task<IActionResult> ExportStockCardExcel([FromQuery] StockCardFilterDto filter, CancellationToken ct)
    {
        var isOwner = User.IsInRole("Owner");
        var report = await _inventoryPeriodReportService.GetStockCardReportAsync(filter, isOwner, ct);
        var bytes = _excelExportService.ExportStockCardReport(report, isOwner);
        var fileName = $"TheKho_{report.SkuCode}_{filter.FromDate:yyyyMMdd}_{filter.ToDate:yyyyMMdd}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }
}
