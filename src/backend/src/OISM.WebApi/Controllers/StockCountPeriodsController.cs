using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Features.StockCounts.Dtos;
using OISM.Application.Features.StockCounts.Services;
using OISM.WebApi.Http;

namespace OISM.WebApi.Controllers;

/// <summary>
/// Controller quản lý các Kỳ kiểm kê định kỳ phát sinh theo lịch (FR-INV-04).
/// </summary>
[ApiController]
[Route("api/stock-count-periods")]
[Authorize]
public class StockCountPeriodsController : ControllerBase
{
    private readonly IStockCountScheduleService _scheduleService;
    private readonly IStockCountService _stockCountService;

    public StockCountPeriodsController(
        IStockCountScheduleService scheduleService,
        IStockCountService stockCountService)
    {
        _scheduleService = scheduleService;
        _stockCountService = stockCountService;
    }

    /// <summary>
    /// Tra cứu danh sách kỳ kiểm kê theo bộ lọc (kho, khoảng thời gian, trạng thái).
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> GetPeriods([FromQuery] StockCountPeriodFilterDto filter, CancellationToken ct)
    {
        var result = await _scheduleService.GetPeriodsAsync(filter, ct);
        return Ok(result);
    }

    /// <summary>
    /// Bắt đầu kỳ kiểm kê: chụp snapshot tồn kho tại thời điểm bấm và khởi tạo phiếu kiểm kê (Planned -> InProgress).
    /// Gọi lại khi kỳ đã có phiếu còn hiệu lực sẽ trả phiếu đó kèm IsReplay = true.
    /// </summary>
    [HttpPost("{id:guid}/start")]
    [Authorize(Roles = "Owner,Staff")]
    [ApiErrorContract(ApiErrorProfile.StockCountPeriods)]
    public async Task<IActionResult> StartPeriod([FromRoute] Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockCountPeriods, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã kỳ kiểm kê không được để trống (Guid.Empty).");
        }

        var userId = User.GetUserId() ?? Guid.Empty;
        var result = await _stockCountService.StartPeriodAsync(id, userId, ct);

        if (result.IsReplay)
        {
            return Ok(result);
        }

        return StatusCode(StatusCodes.Status201Created, result);
    }
}
