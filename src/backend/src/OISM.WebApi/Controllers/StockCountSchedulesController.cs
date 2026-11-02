using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Features.StockCountSchedules.Dtos;
using OISM.Application.Features.StockCountSchedules.Services;

namespace OISM.WebApi.Controllers;

/// <summary>
/// API Quản lý Lịch kiểm kê định kỳ (Upgrade 02 - Lịch kiểm kê).
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

    [HttpGet]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> GetSchedules(CancellationToken ct)
    {
        var result = await _scheduleService.GetSchedulesAsync(ct);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> CreateSchedule([FromBody] CreateStockCountScheduleDto dto, CancellationToken ct)
    {
        var result = await _scheduleService.CreateScheduleAsync(dto, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }
}
