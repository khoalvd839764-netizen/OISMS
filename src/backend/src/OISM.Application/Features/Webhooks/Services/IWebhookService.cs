using System.Security.Claims;
using OISM.Application.Features.Webhooks.Dtos;

namespace OISM.Application.Features.Webhooks.Services;

/// <summary>
/// Giao diện dịch vụ tiếp nhận đơn hàng Webhook từ các sàn TMĐT (Shopee, TikTok Shop, Lazada).
/// </summary>
public interface IWebhookService
{
    Task<WebhookSimulationResultDto> ProcessWebhookAsync(
        string pathChannel,
        CanonicalWebhookPayloadDto dto,
        string? secretHeader,
        string? authHeader,
        ClaimsPrincipal? user,
        CancellationToken ct = default);
}
