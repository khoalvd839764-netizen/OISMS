using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.StockCounts.Dtos;
using OISM.Application.Features.StockCounts.Services;

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
    public async Task<IActionResult> StartPeriod([FromRoute] Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã kỳ kiểm kê không được để trống (Guid.Empty).");
        }

        try
        {
            var userId = GetCurrentUserId() ?? Guid.Empty;
            var result = await _stockCountService.StartPeriodAsync(id, userId, ct);

            if (result.IsReplay)
            {
                return Ok(result);
            }

            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, GetTitleForErrorCode(ex.ErrorCode), ex.StatusCode, ex.Message);
        }
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirst("userId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var userId) ? userId : null;
    }

    private ObjectResult CreateProblemResult(string errorCode, string title, int statusCode, string detail)
    {
        var problem = new ProblemDetails
        {
            Type = $"https://oisms.local/errors/{errorCode}",
            Title = title,
            Status = statusCode,
            Detail = detail,
            Instance = HttpContext.Request.Path,
            Extensions =
            {
                ["errorCode"] = errorCode,
                ["timestamp"] = DateTimeOffset.UtcNow
            }
        };
        return StatusCode(statusCode, problem);
    }

    private static string GetTitleForErrorCode(string errorCode) => errorCode switch
    {
        "NOT_FOUND" => "Không tìm thấy dữ liệu",
        "EMPTY_WAREHOUSE_STOCK_COUNT" => "Kho không có sản phẩm để kiểm kê",
        "VALIDATION_ERROR" => "Lỗi xác thực dữ liệu",
        "INVALID_STATE_TRANSITION" => "Chuyển trạng thái không hợp lệ",
        _ => "Lỗi xử lý nghiệp vụ"
    };
}
