using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Features.Simulator.Dtos;
using OISM.Application.Features.Simulator.Services;

namespace OISM.WebApi.Controllers;

/// <summary>
/// API Trình Giả Lập Webhook Sàn Thương Mại Điện Tử (Shopee, TikTok Shop, Lazada - FR-SIM-01).
/// Cho phép bắn payload đơn hàng sàn vào hệ thống OISMS để kiểm thử chuẩn hóa đơn hàng và tải đồng thời.
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
    /// Giả lập tiếp nhận Webhook đơn hàng từ sàn (Shopee, TikTok, Lazada).
    /// Tự động chuẩn hóa về Mô hình Đơn hàng Hợp nhất (Canonical Order Model - FR-ORD-01) và kích hoạt giữ chỗ (Status = 'Reserved').
    /// </summary>
    [HttpPost("{channel}")]
    public async Task<IActionResult> IngestWebhookOrder(
        string channel,
        [FromBody] WebhookOrderPayloadDto payload,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var userId = GetCurrentUserId();
            var order = await _simulatorService.IngestWebhookOrderAsync(channel, payload, userId, ct);
            return Ok(order);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
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
