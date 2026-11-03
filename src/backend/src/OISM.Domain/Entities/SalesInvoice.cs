using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho Hóa đơn bán nội bộ (Sales Invoice) gắn với mỗi lần xuất bán hàng.
/// Bảng chỉ ghi (append-only), map vào bảng sales_invoices trong PostgreSQL schema v0.5.
/// </summary>
public class SalesInvoice : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public string Number { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public Guid BranchId { get; set; }
    public string Channel { get; set; } = "POS";

    public string BranchNameSnapshot { get; set; } = string.Empty;
    public string? BranchAddressSnapshot { get; set; }
    public string? CustomerNameSnapshot { get; set; }
    public string? CustomerPhoneSnapshot { get; set; }

    public Guid? IssuedBy { get; set; }
    public string? IssuedByNameSnapshot { get; set; }

    public decimal TotalAmount { get; set; }
    public string? PaymentMethodSnapshot { get; set; }

    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Order? Order { get; set; }
    public Branch? Branch { get; set; }
    public User? IssuedByUser { get; set; }
}
