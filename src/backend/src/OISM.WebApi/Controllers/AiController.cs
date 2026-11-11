using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Ai.Dtos;
using OISM.Application.Features.Ai.Services;
using OISM.WebApi.Http;

namespace OISM.WebApi.Controllers;

/// <summary>
/// API điều phối các tác vụ phân tích dữ liệu lịch sử và dự báo gợi ý nhập hàng bằng AI (FR-AI-01).
/// </summary>
[ApiController]
[Route("api/ai")]
[Authorize]
public class AiController : ControllerBase
{
    private readonly IAiForecastingService _aiService;

    public AiController(IAiForecastingService aiService)
    {
        _aiService = aiService;
    }

    [HttpPost("chat")]
    [Authorize(Roles = "Owner,Staff")]
    [EnableRateLimiting("AiChat")]
    [ApiErrorContract(ApiErrorProfile.Ai)]
    [ProducesResponseType(typeof(AiChatResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Chat(
        [FromBody] AiChatRequest? request, [FromServices] IAiChatService chatService, CancellationToken ct)
    {
        var role = User.IsInRole("Owner") ? "Owner" : "Staff";
        return Ok(await chatService.ChatAsync(request, role, ct));
    }

    /// <summary>
    /// Danh sách model AI đang có key để chọn trong khung chat. AI tắt thì trả danh sách rỗng.
    /// </summary>
    [HttpGet("models")]
    [Authorize(Roles = "Owner,Staff")]
    [ProducesResponseType(typeof(IReadOnlyList<AiModelOption>), StatusCodes.Status200OK)]
    public IActionResult GetModels([FromServices] ILlmModelCatalog catalog) => Ok(catalog.GetAvailableModels());

    /// <summary>
    /// Lấy chuỗi dữ liệu lịch sử xuất kho theo ngày và SKU phục vụ thuật toán AI dự báo.
    /// </summary>
    [HttpGet("export-history")]
    [Authorize(Roles = "Owner,Staff")]
    [ProducesResponseType(typeof(ExportHistoryResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetExportHistory([FromQuery] ExportHistoryQueryDto query, CancellationToken ct)
    {
        var result = await _aiService.GetExportHistoryAsync(query, ct);
        return Ok(result);
    }

    /// <summary>
    /// Đề xuất kế hoạch nhập lại hàng hóa (Reorder Recommendations) dựa trên vận tốc bán và số dư tồn khả dụng.
    /// </summary>
    [HttpGet("reorder-recommendations")]
    [Authorize(Roles = "Owner,Staff")]
    [ProducesResponseType(typeof(ReorderRecommendationSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetReorderRecommendations([FromQuery] ReorderRecommendationQueryDto query, CancellationToken ct)
    {
        var result = await _aiService.GetReorderRecommendationsAsync(query, ct);
        return Ok(result);
    }
}
