using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Features.Webhooks.Dtos;
using OISM.Application.Features.Webhooks.Services;
using OISM.WebApi.Http;

namespace OISM.WebApi.Controllers;

/// <summary>
/// Bộ điều khiển tiếp nhận Webhook đa kênh TMĐT (Shopee, TikTok Shop, Lazada).
/// Cung cấp endpoint giả lập tiếp nhận đơn hàng và kích hoạt chuỗi xử lý giữ chỗ tồn kho nguyên tử.
/// </summary>
[ApiController]
[Route("api/webhooks/simulator")]
public class WebhooksController : ControllerBase
{
    private readonly IWebhookService _webhookService;

    public WebhooksController(IWebhookService webhookService)
    {
        _webhookService = webhookService;
    }

    /// <summary>
    /// Tiếp nhận đơn hàng từ kênh sàn TMĐT (Shopee, TikTok, Lazada).
    /// Xác thực qua Header 'X-Webhook-Secret' hoặc Bearer JWT (Owner/Staff).
    /// Ánh xạ chi nhánh fulfillment mặc định từ bảng cấu hình tích hợp.
    /// Khóa chống trùng: (tenant_id, channel, external_shop_id, external_order_id) và chữ ký Canonical SHA-256.
    /// </summary>
    [HttpPost("{channel}")]
    [AllowAnonymous]
    [ApiErrorContract(ApiErrorProfile.Webhooks)]
    public async Task<IActionResult> ReceiveMarketplaceWebhook(
        [FromRoute] string channel,
        [FromBody] CanonicalWebhookPayloadDto dto,
        [FromHeader(Name = "X-Webhook-Secret")] string? secretHeader,
        CancellationToken ct)
    {
        var authHeader = Request.Headers.Authorization.ToString();

        var result = await _webhookService.ProcessWebhookAsync(
            pathChannel: channel,
            dto: dto,
            secretHeader: secretHeader,
            authHeader: authHeader,
            user: User,
            ct: ct);

        if (result.Message.Contains("trước đó", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(result);
        }

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// Cho biết X-Webhook-Secret thuộc gian hàng nào để công cụ giả lập tự điền sàn và mã shop.
    /// Không tạo dữ liệu. Secret sai, kết nối tắt hoặc doanh nghiệp ngừng hoạt động trả 401.
    /// </summary>
    [HttpGet("whoami")]
    [AllowAnonymous]
    [ApiErrorContract(ApiErrorProfile.Webhooks)]
    public async Task<IActionResult> WhoAmI(
        [FromHeader(Name = "X-Webhook-Secret")] string? secretHeader,
        CancellationToken ct)
    {
        return Ok(await _webhookService.GetIdentityAsync(secretHeader, ct));
    }

    /// <summary>
    /// SKU đang bán của doanh nghiệp sở hữu secret (mã, tên, giá bán lẻ, tồn có thể bán tại chi nhánh kết nối)
    /// để công cụ giả lập cho chọn sản phẩm. Không trả giá vốn. Secret không dùng được trả 401.
    /// </summary>
    [HttpGet("catalog")]
    [AllowAnonymous]
    [ApiErrorContract(ApiErrorProfile.Webhooks)]
    public async Task<IActionResult> Catalog(
        [FromHeader(Name = "X-Webhook-Secret")] string? secretHeader,
        CancellationToken ct)
    {
        return Ok(await _webhookService.GetCatalogAsync(secretHeader, ct));
    }
}
