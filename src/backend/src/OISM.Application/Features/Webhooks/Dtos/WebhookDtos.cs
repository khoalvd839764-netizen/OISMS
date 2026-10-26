using System.ComponentModel.DataAnnotations;

namespace OISM.Application.Features.Webhooks.Dtos;

public class CanonicalWebhookItemDto
{
    [Required(ErrorMessage = "Mã SKU là bắt buộc.")]
    [MaxLength(80, ErrorMessage = "Mã SKU không được vượt quá 80 ký tự.")]
    public string SkuCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số lượng mua là bắt buộc.")]
    [Range(typeof(decimal), "0.001", "999999999999999.999", ErrorMessage = "Số lượng mua phải lớn hơn 0.")]
    public decimal? Quantity { get; set; }

    [Required(ErrorMessage = "Đơn giá là bắt buộc.")]
    [Range(typeof(decimal), "0", "999999999999.99", ErrorMessage = "Đơn giá không được âm.")]
    public decimal? UnitPrice { get; set; }
}

/// <summary>
/// Hợp đồng Payload nhận đơn hợp nhất từ 3 sàn TMĐT (Shopee, TikTok Shop, Lazada).
/// Quy đổi chuẩn hóa mọi cấu trúc Webhook của đối tác về một Canonical Order Model (FR-ORD-01, FR-SIM-01).
/// 
/// Nguyên tắc bảo mật & tích hợp:
/// 1. Tenant Authentication: Webhook được xác thực qua Header HMAC signature (X-Hub-Signature-256) hoặc API Key (X-Webhook-Secret). Tuyệt đối không chấp nhận hay tin cậy TenantId từ request body.
/// 2. Branch Resolution: Chi nhánh thực hiện (BranchId) được ánh xạ từ cấu hình tích hợp shop (Channel + ExternalShopId).
/// 3. Khóa chống trùng (Deduplication Key): (tenant_id, channel, external_shop_id, external_order_id).
///    - Cùng key & cùng payload: Trả HTTP 200 OK giữ nguyên đơn đã tạo (Idempotent).
///    - Cùng key & khác payload: Trả HTTP 409 Conflict.
///    - Path Channel khác Body Channel: Trả HTTP 400 Bad Request (CHANNEL_MISMATCH).
///    - SKU không tồn tại trong Tenant: Trả HTTP 422 Unprocessable Entity (SKU_NOT_FOUND).
/// </summary>
public class CanonicalWebhookPayloadDto
{
    [Required(ErrorMessage = "Kênh bán là bắt buộc.")]
    [MaxLength(20, ErrorMessage = "Kênh bán không được vượt quá 20 ký tự.")]
    public string Channel { get; set; } = string.Empty; // 'Shopee', 'TikTok', 'Lazada'

    [Required(ErrorMessage = "Mã Shop trên sàn là bắt buộc.")]
    [MaxLength(100, ErrorMessage = "Mã Shop không được vượt quá 100 ký tự.")]
    public string ExternalShopId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mã Đơn hàng trên sàn là bắt buộc.")]
    [MaxLength(100, ErrorMessage = "Mã Đơn hàng không được vượt quá 100 ký tự.")]
    public string ExternalOrderId { get; set; } = string.Empty;

    [MaxLength(200, ErrorMessage = "Tên khách hàng không được vượt quá 200 ký tự.")]
    public string? CustomerName { get; set; }

    [MaxLength(32, ErrorMessage = "Số điện thoại không được vượt quá 32 ký tự.")]
    public string? CustomerPhone { get; set; }

    [MaxLength(500, ErrorMessage = "Địa chỉ giao hàng không được vượt quá 500 ký tự.")]
    public string? ShippingAddress { get; set; }

    [Required(ErrorMessage = "Tổng tiền đơn hàng là bắt buộc.")]
    [Range(typeof(decimal), "0", "999999999999999999.99", ErrorMessage = "Tổng tiền đơn hàng không hợp lệ.")]
    public decimal? TotalAmount { get; set; }

    [Required(ErrorMessage = "Danh sách sản phẩm là bắt buộc.")]
    [MinLength(1, ErrorMessage = "Đơn hàng phải có ít nhất 1 sản phẩm.")]
    public List<CanonicalWebhookItemDto> Items { get; set; } = new();

    [Required(ErrorMessage = "Thời gian đặt hàng là bắt buộc.")]
    public DateTimeOffset? OrderTimestamp { get; set; }
}

public class WebhookSimulationResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid? OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft"; // 'Draft', 'Reserved'
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
}
