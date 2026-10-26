using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho Đơn hàng đa kênh (POS, Admin, Shopee, TikTok, Lazada).
/// Map vào bảng orders trong PostgreSQL schema v0.5.
/// </summary>
public class Order : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public string Number { get; set; } = string.Empty;
    public Guid BranchId { get; set; }

    /// <summary>
    /// Kênh bán: 'POS', 'Admin', 'Shopee', 'TikTok', 'Lazada'.
    /// </summary>
    public string Channel { get; set; } = "POS";

    public string? ExternalShopId { get; set; }
    public string? ExternalOrderId { get; set; }

    /// <summary>
    /// Trạng thái: 'Draft', 'Reserved', 'Confirmed', 'Completed', 'Cancelled'.
    /// </summary>
    public string Status { get; set; } = "Draft";

    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string? ShippingAddress { get; set; }

    public decimal TotalAmount { get; set; } = 0;

    /// <summary>
    /// RequestKey duy nhất để đảm bảo tính Idempotency khi thanh toán/retry.
    /// </summary>
    public Guid RequestKey { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Chữ ký SHA-256 của Canonical Webhook Payload đối với đơn hàng tiếp nhận từ sàn TMĐT.
    /// Null đối với đơn tạo từ POS, Admin hoặc đơn lịch sử.
    /// </summary>
    public string? WebhookPayloadSignature { get; set; }

    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Branch? Branch { get; set; }
    public User? CreatedByUser { get; set; }

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
    public Payment? Payment { get; set; }
}
