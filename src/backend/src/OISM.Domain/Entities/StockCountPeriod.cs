using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho Kỳ kiểm kê được sinh tự động theo lịch (OISMS-47-U03).
/// Map vào bảng stock_count_periods trong PostgreSQL schema v0.5.
/// </summary>
public class StockCountPeriod : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid ScheduleId { get; set; }
    public Guid BranchId { get; set; }
    public Guid WarehouseId { get; set; }
    public string PeriodType { get; set; } = string.Empty;
    public string PeriodKey { get; set; } = string.Empty;
    public DateTimeOffset ScheduledAt { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public Guid AssigneeUserId { get; set; }
    public string Status { get; set; } = StockCountPeriodStatus.Planned;
    public DateTimeOffset? CompletedAt { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public StockCountSchedule? Schedule { get; set; }
    public Branch? Branch { get; set; }
    public Warehouse? Warehouse { get; set; }
    public User? AssigneeUser { get; set; }

    public ICollection<StockCount> StockCounts { get; set; } = new List<StockCount>();

    public bool CanStart => Status is StockCountPeriodStatus.Planned or StockCountPeriodStatus.InProgress;

    public void Start(DateTimeOffset at)
    {
        StateTransition.Ensure(CanStart, nameof(StockCountPeriod), Status, StockCountPeriodStatus.InProgress);
        Status = StockCountPeriodStatus.InProgress;
        UpdatedAt = at;
    }

    public void Complete(DateTimeOffset at)
    {
        StateTransition.Ensure(Status == StockCountPeriodStatus.InProgress,
            nameof(StockCountPeriod), Status, StockCountPeriodStatus.Completed);
        Status = StockCountPeriodStatus.Completed;
        CompletedAt = at;
        UpdatedAt = at;
    }

    public void Reopen(DateTimeOffset at)
    {
        StateTransition.Ensure(Status == StockCountPeriodStatus.InProgress,
            nameof(StockCountPeriod), Status, StockCountPeriodStatus.Planned);
        Status = StockCountPeriodStatus.Planned;
        CompletedAt = null;
        UpdatedAt = at;
    }

    public void Cancel(DateTimeOffset at)
    {
        StateTransition.Ensure(Status == StockCountPeriodStatus.Planned,
            nameof(StockCountPeriod), Status, StockCountPeriodStatus.Cancelled);
        Status = StockCountPeriodStatus.Cancelled;
        UpdatedAt = at;
    }

    public void Reschedule(DateTimeOffset scheduledAt, DateTimeOffset dueAt, Guid assigneeId, DateTimeOffset at)
    {
        StateTransition.Ensure(Status == StockCountPeriodStatus.Cancelled && scheduledAt > at,
            nameof(StockCountPeriod), Status, StockCountPeriodStatus.Planned);
        Status = StockCountPeriodStatus.Planned;
        ScheduledAt = scheduledAt;
        DueAt = dueAt;
        AssigneeUserId = assigneeId;
        UpdatedAt = at;
    }
}
