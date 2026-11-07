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

    /// <summary>
    /// Cho biết secret thuộc gian hàng nào (sàn, mã shop, chi nhánh) để công cụ giả lập tự điền.
    /// Secret sai, kết nối tắt hoặc doanh nghiệp ngừng hoạt động trả 401.
    /// </summary>
    Task<WebhookIdentityDto> GetIdentityAsync(string? secretHeader, CancellationToken ct = default);
}
