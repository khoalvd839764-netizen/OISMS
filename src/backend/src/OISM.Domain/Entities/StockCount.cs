using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho Đợt kiểm kê kho hàng (Stocktake / Inventory Audit).
/// Map vào bảng stock_counts trong PostgreSQL schema v0.5.
/// </summary>
public class StockCount : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public string Number { get; set; } = string.Empty;
    public Guid? RequestKey { get; set; }
    public string? PayloadSignature { get; set; }
    public Guid BranchId { get; set; }
    public Guid WarehouseId { get; set; }

    /// <summary>
    /// Trạng thái kiểm kê: 'Draft', 'Counting', 'PendingApproval', 'Confirmed', 'Cancelled'.
    /// </summary>
    public string Status { get; set; } = StockCountStatus.Draft;

    public string CountType { get; set; } = "AdHoc";
    public string? AdHocReason { get; set; }
    public Guid? PeriodId { get; set; }
    public Guid? SubmittedBy { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }

    public Guid CreatedBy { get; set; }
    public Guid? ConfirmedBy { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? Note { get; set; }
    public Guid? GeneratedAdjustmentId { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Branch? Branch { get; set; }
    public Warehouse? Warehouse { get; set; }
    public StockCountPeriod? Period { get; set; }
    public User? SubmittedByUser { get; set; }
    public User? CreatedByUser { get; set; }
    public User? ConfirmedByUser { get; set; }
    public InventoryAdjustment? GeneratedAdjustment { get; set; }

    public ICollection<StockCountItem> Items { get; set; } = new List<StockCountItem>();
    public ICollection<StockCountApproval> Approvals { get; set; } = new List<StockCountApproval>();

    public bool CanRecordCounts => Status is StockCountStatus.Draft or StockCountStatus.Counting;
    public bool IsCounting => Status == StockCountStatus.Counting;
    public bool IsPendingApproval => Status == StockCountStatus.PendingApproval;
    public bool CanCancel => Status is StockCountStatus.Draft or StockCountStatus.Counting or StockCountStatus.PendingApproval;

    public void StartCounting(DateTimeOffset at)
    {
        StateTransition.Ensure(CanRecordCounts, nameof(StockCount), Status, StockCountStatus.Counting);
        Status = StockCountStatus.Counting;
        UpdatedAt = at;
    }

    public void Submit(Guid userId, DateTimeOffset at)
    {
        StateTransition.Ensure(IsCounting, nameof(StockCount), Status, StockCountStatus.PendingApproval);
        Status = StockCountStatus.PendingApproval;
        SubmittedBy = userId;
        SubmittedAt = at;
        UpdatedAt = at;
    }

    public void RequestRecount(DateTimeOffset at)
    {
        StateTransition.Ensure(IsPendingApproval, nameof(StockCount), Status, StockCountStatus.Counting);
        Status = StockCountStatus.Counting;
        SubmittedBy = null;
        SubmittedAt = null;
        UpdatedAt = at;
    }

    public void Confirm(Guid userId, Guid adjustmentId, DateTimeOffset at)
    {
        StateTransition.Ensure(IsPendingApproval, nameof(StockCount), Status, StockCountStatus.Confirmed);
        Status = StockCountStatus.Confirmed;
        ConfirmedBy = userId;
        ConfirmedAt = at;
        GeneratedAdjustmentId = adjustmentId;
        UpdatedAt = at;
    }

    public void Cancel(DateTimeOffset at)
    {
        StateTransition.Ensure(CanCancel, nameof(StockCount), Status, StockCountStatus.Cancelled);
        Status = StockCountStatus.Cancelled;
        UpdatedAt = at;
    }
}
