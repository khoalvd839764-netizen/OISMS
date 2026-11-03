using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.StockCounts.Dtos;
using OISM.Application.Features.StockCounts.Services;

namespace OISM.WebApi.Controllers;

/// <summary>
/// Controller quản lý cấu hình Lịch kiểm kê định kỳ theo kho (FR-INV-04).
/// </summary>
[ApiController]
[Route("api/stock-count-schedules")]
[Authorize]
public class StockCountSchedulesController : ControllerBase
{
    private readonly IStockCountScheduleService _scheduleService;

    public StockCountSchedulesController(IStockCountScheduleService scheduleService)
    {
        _scheduleService = scheduleService;
    }

    /// <summary>
    /// Tra cứu danh sách lịch kiểm kê định kỳ, tùy chọn lọc theo kho.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> GetSchedules([FromQuery] StockCountScheduleFilterRequestDto filter, CancellationToken ct)
    {
        var result = await _scheduleService.GetSchedulesAsync(filter, ct);
        return Ok(result);
    }

    /// <summary>
    /// Thiết lập cấu hình lịch kiểm kê định kỳ cho kho.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> CreateSchedule(
        [FromBody] CreateStockCountScheduleRequestDto dto,
        CancellationToken ct)
    {
        if (dto == null)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Dữ liệu yêu cầu không được để trống.");
        }

        if (!ModelState.IsValid)
        {
            var firstError = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Dữ liệu yêu cầu không hợp lệ.";
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, firstError);
        }

        try
        {
            var userId = GetCurrentUserId() ?? Guid.Empty;
            var result = await _scheduleService.CreateScheduleAsync(dto, userId, ct);
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, GetTitleForErrorCode(ex.ErrorCode), ex.StatusCode, ex.Message);
        }
    }

    /// <summary>
    /// Cập nhật cấu hình lịch kiểm kê định kỳ.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> UpdateSchedule(
        [FromRoute] Guid id,
        [FromBody] UpdateStockCountScheduleRequestDto dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã lịch kiểm kê không được để trống (Guid.Empty).");
        }

        if (dto == null)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Dữ liệu yêu cầu không được để trống.");
        }

        if (!ModelState.IsValid)
        {
            var firstError = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Dữ liệu yêu cầu không hợp lệ.";
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, firstError);
        }

        try
        {
            var userId = GetCurrentUserId() ?? Guid.Empty;
            var result = await _scheduleService.UpdateScheduleAsync(id, dto, userId, ct);
            return Ok(result);
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
        "STOCK_COUNT_SCHEDULE_DUPLICATE" => "Lịch kiểm kê bị trùng lặp",
        "NOT_FOUND" => "Không tìm thấy dữ liệu",
        "VALIDATION_ERROR" => "Lỗi xác thực dữ liệu",
        _ => "Lỗi xử lý nghiệp vụ"
    };
}
