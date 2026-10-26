using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Features.Simulator.Dtos;
using OISM.Application.Features.Simulator.Services;

namespace OISM.WebApi.Controllers;

/// <summary>
/// API Trình Giả Lập Tải Đồng Thời (Concurrency Simulation Runner - FR-SIM-01, NFR-PERF-02).
/// Cho phép bắn nhiều request tranh chấp đồng thời để kiểm tra chống bán vượt và bảo vệ số dư tồn kho.
/// Lưu ý: Toàn bộ webhook tiếp nhận đơn sàn đi qua WebhooksController (/api/webhooks/simulator/{channel}).
/// </summary>
[ApiController]
[Route("api/webhooks/simulator")]
[Authorize(Roles = "Owner,Staff")]
public class SimulatorController : ControllerBase
{
    private readonly ISimulatorService _simulatorService;

    public SimulatorController(ISimulatorService simulatorService)
    {
        _simulatorService = simulatorService;
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirst("userId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(claim, out var userId))
        {
            return userId;
        }
        return null;
    }

    /// <summary>
    /// Chạy mô phỏng tải đồng thời (Concurrency Simulation Runner) cho 1 SKU.
    /// Giúp kiểm chứng khả năng chịu tải và chống bán vượt (Anti-Overselling) theo tiêu chuẩn nghiệm thu Sprint 3 (NFR-PERF-02).
    /// </summary>
    [HttpPost("batch")]
    public async Task<IActionResult> RunBatchSimulation(
        [FromBody] SimulationBatchRequestDto request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var userId = GetCurrentUserId();
            var result = await _simulatorService.RunConcurrencySimulationAsync(request, userId, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
