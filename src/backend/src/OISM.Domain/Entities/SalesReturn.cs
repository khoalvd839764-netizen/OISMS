using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho Phiếu trả hàng từ khách hàng (gắn với đơn hàng gốc).
/// Map vào bảng sales_returns trong PostgreSQL schema v0.5 (CS-01).
/// </summary>
public class SalesReturn : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid RequestKey { get; set; }
    public string Number { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public Guid BranchId { get; set; }
    public Guid WarehouseId { get; set; }
    public string Status { get; set; } = SalesReturnStatus.Draft;
    public string Reason { get; set; } = string.Empty;
    public string? Note { get; set; }
    public decimal TotalRefundAmount { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? ConfirmedBy { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? RefundMethod { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Order? Order { get; set; }
    public Branch? Branch { get; set; }
    public Warehouse? Warehouse { get; set; }
    public User? Creator { get; set; }
    public User? Confirmer { get; set; }
    public ICollection<SalesReturnItem> Items { get; set; } = new List<SalesReturnItem>();

    public void Confirm(Guid userId, string? refundMethod, DateTimeOffset at)
    {
        StateTransition.Ensure(Status == SalesReturnStatus.Draft, nameof(SalesReturn), Status, SalesReturnStatus.Confirmed);
        Status = SalesReturnStatus.Confirmed;
        ConfirmedBy = userId;
        ConfirmedAt = at;
        RefundMethod = refundMethod;
        RefundedAt = refundMethod != null ? at : null;
        UpdatedAt = at;
    }

    public void Cancel(DateTimeOffset at)
    {
        StateTransition.Ensure(Status == SalesReturnStatus.Draft, nameof(SalesReturn), Status, SalesReturnStatus.Cancelled);
        Status = SalesReturnStatus.Cancelled;
        UpdatedAt = at;
    }
}
