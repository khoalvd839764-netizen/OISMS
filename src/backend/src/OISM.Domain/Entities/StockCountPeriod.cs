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
    public string Status { get; set; } = "Planned";
    public DateTimeOffset? CompletedAt { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public StockCountSchedule? Schedule { get; set; }
    public Branch? Branch { get; set; }
    public Warehouse? Warehouse { get; set; }
    public User? AssigneeUser { get; set; }

    public ICollection<StockCount> StockCounts { get; set; } = new List<StockCount>();
}
