using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho một Phiếu mua hàng từ Nhà cung cấp (U01-A).
/// Map vào bảng purchase_orders trong PostgreSQL schema.
/// </summary>
public class PurchaseOrder : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public string Number { get; set; } = string.Empty;
    public Guid RequestKey { get; set; }
    public Guid SupplierId { get; set; }
    public Guid BranchId { get; set; }

    /// <summary>
    /// Trạng thái phiếu mua: 'Draft', 'Approved', 'PartiallyReceived', 'Received', 'Closed', 'Cancelled'.
    /// </summary>
    public string Status { get; set; } = "Draft";

    public string? Note { get; set; }

    public Guid CreatedBy { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid? ClosedBy { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string? CloseReason { get; set; }
    public Guid? CancelledBy { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Supplier? Supplier { get; set; }
    public Branch? Branch { get; set; }
    public User? CreatedByUser { get; set; }
    public User? ApprovedByUser { get; set; }
    public User? ClosedByUser { get; set; }
    public User? CancelledByUser { get; set; }

    public ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();
}
