using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.Webhooks.Dtos;
using OISM.Application.Features.Webhooks.Services;

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
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(
        IWebhookService webhookService,
        ILogger<WebhooksController> logger)
    {
        _webhookService = webhookService;
        _logger = logger;
    }

    /// <summary>
    /// Tiếp nhận đơn hàng từ kênh sàn TMĐT (Shopee, TikTok, Lazada).
    /// Xác thực qua Header 'X-Webhook-Secret' hoặc Bearer JWT (Owner/Staff).
    /// Ánh xạ chi nhánh fulfillment mặc định từ bảng cấu hình tích hợp.
    /// Khóa chống trùng: (tenant_id, channel, external_shop_id, external_order_id) và chữ ký Canonical SHA-256.
    /// </summary>
    [HttpPost("{channel}")]
    [AllowAnonymous]
    public async Task<IActionResult> ReceiveMarketplaceWebhook(
        [FromRoute] string channel,
        [FromBody] CanonicalWebhookPayloadDto dto,
        [FromHeader(Name = "X-Webhook-Secret")] string? secretHeader,
        CancellationToken ct)
    {
        var authHeader = Request.Headers.Authorization.ToString();

        try
        {
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
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, GetTitleForErrorCode(ex.ErrorCode), ex.StatusCode, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi hệ thống khi xử lý Webhook tiếp nhận đơn kênh {Channel}.", channel);
            return CreateProblemResult("INTERNAL_SERVER_ERROR", "Lỗi xử lý nội bộ", StatusCodes.Status500InternalServerError, "Đã xảy ra lỗi trong quá trình xử lý đơn hàng webhook.");
        }
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
        "CHANNEL_MISMATCH" => "Sai lệch kênh bán hàng",
        "TOTAL_AMOUNT_MISMATCH" => "Sai lệch tổng tiền đơn hàng",
        "VALIDATION_ERROR" => "Lỗi xác thực dữ liệu",
        "UNAUTHORIZED" => "Không được phép truy cập",
        "FORBIDDEN" => "Bị cấm truy cập",
        "INTEGRATION_NOT_FOUND" => "Không tìm thấy cấu hình tích hợp gian hàng",
        "BRANCH_INACTIVE_OR_INVALID" => "Chi nhánh fulfillment không hợp lệ",
        "SKU_NOT_FOUND" => "Mặt hàng biến thể không tồn tại",
        "SKU_INACTIVE" => "Mặt hàng biến thể đã ngừng hoạt động",
        "INSUFFICIENT_STOCK" => "Tồn kho khả dụng không đủ",
        "WEBHOOK_PAYLOAD_MISMATCH" => "Sai lệch nội dung đơn hàng sàn",
        "INVALID_STATE_TRANSITION" => "Chuyển trạng thái không hợp lệ",
        _ => "Lỗi xử lý nghiệp vụ"
    };
}
