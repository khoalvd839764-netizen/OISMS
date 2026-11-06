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
    public string Status { get; set; } = PurchaseOrderStatus.Draft;

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

    public bool IsDraft => Status == PurchaseOrderStatus.Draft;
    public bool CanReceive => Status is PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived;
    public bool IsCancellableStatus => Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Approved;
    public bool HasReceivedItems => Items.Any(item => item.ReceivedBaseQuantity > 0m);

    public void Approve(Guid userId, DateTimeOffset at)
    {
        StateTransition.Ensure(IsDraft, nameof(PurchaseOrder), Status, PurchaseOrderStatus.Approved);
        Status = PurchaseOrderStatus.Approved;
        ApprovedBy = userId;
        ApprovedAt = at;
        UpdatedAt = at;
    }

    public void UpdateReceivingStatus(DateTimeOffset at)
    {
        var next = Items.All(item => item.ReceivedBaseQuantity >= item.Quantity * item.FactorSnapshot)
            ? PurchaseOrderStatus.Received
            : PurchaseOrderStatus.PartiallyReceived;
        StateTransition.Ensure(CanReceive, nameof(PurchaseOrder), Status, next);
        Status = next;
        UpdatedAt = at;
    }

    public void Close(Guid userId, string reason, DateTimeOffset at)
    {
        StateTransition.Ensure(CanReceive && !string.IsNullOrWhiteSpace(reason),
            nameof(PurchaseOrder), Status, PurchaseOrderStatus.Closed);
        Status = PurchaseOrderStatus.Closed;
        ClosedBy = userId;
        ClosedAt = at;
        CloseReason = reason;
        UpdatedAt = at;
    }

    public void Cancel(Guid userId, DateTimeOffset at)
    {
        StateTransition.Ensure(IsCancellableStatus && !HasReceivedItems,
            nameof(PurchaseOrder), Status, PurchaseOrderStatus.Cancelled);
        Status = PurchaseOrderStatus.Cancelled;
        CancelledBy = userId;
        CancelledAt = at;
        UpdatedAt = at;
    }
}
