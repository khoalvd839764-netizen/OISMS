using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Features.Simulator.Dtos;
using OISM.Application.Features.Simulator.Services;
using OISM.WebApi.Http;

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

    /// <summary>
    /// Chạy mô phỏng tải đồng thời (Concurrency Simulation Runner) cho 1 SKU.
    /// Giúp kiểm chứng khả năng chịu tải và chống bán vượt (Anti-Overselling) theo tiêu chuẩn nghiệm thu Sprint 3 (NFR-PERF-02).
    /// </summary>
    [HttpPost("batch")]
    [ApiErrorContract(ApiErrorProfile.Simulator)]
    public async Task<IActionResult> RunBatchSimulation(
        [FromBody] SimulationBatchRequestDto request,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        var result = await _simulatorService.RunConcurrencySimulationAsync(request, userId, ct);
        return Ok(result);
    }
}
