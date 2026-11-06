using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Features.StockCounts.Dtos;
using OISM.Application.Features.StockCounts.Services;
using OISM.WebApi.Http;

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
    [ApiErrorContract(ApiErrorProfile.StockCountSchedules)]
    public async Task<IActionResult> CreateSchedule(
        [FromBody] CreateStockCountScheduleRequestDto dto,
        CancellationToken ct)
    {
        if (dto == null)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockCountSchedules, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Dữ liệu yêu cầu không được để trống.");
        }

        var userId = User.GetUserId() ?? Guid.Empty;
        var result = await _scheduleService.CreateScheduleAsync(dto, userId, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// Cập nhật cấu hình lịch kiểm kê định kỳ.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Owner")]
    [ApiErrorContract(ApiErrorProfile.StockCountSchedules)]
    public async Task<IActionResult> UpdateSchedule(
        [FromRoute] Guid id,
        [FromBody] UpdateStockCountScheduleRequestDto dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockCountSchedules, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã lịch kiểm kê không được để trống (Guid.Empty).");
        }

        if (dto == null)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockCountSchedules, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Dữ liệu yêu cầu không được để trống.");
        }

        var userId = User.GetUserId() ?? Guid.Empty;
        var result = await _scheduleService.UpdateScheduleAsync(id, dto, userId, ct);
        return Ok(result);
    }
}
