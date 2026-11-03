using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho cấu hình Lịch kiểm kê định kỳ theo kho (OISMS-47-U03).
/// Map vào bảng stock_count_schedules trong PostgreSQL schema v0.5.
/// </summary>
public class StockCountSchedule : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public Guid WarehouseId { get; set; }
    public string PeriodType { get; set; } = string.Empty;
    public short? DayOfMonth { get; set; }
    public TimeOnly LocalTime { get; set; }
    public short DueDays { get; set; }
    public Guid AssigneeUserId { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Branch? Branch { get; set; }
    public Warehouse? Warehouse { get; set; }
    public User? AssigneeUser { get; set; }
    public User? CreatedByUser { get; set; }
    public User? UpdatedByUser { get; set; }

    public ICollection<StockCountPeriod> Periods { get; set; } = new List<StockCountPeriod>();
}
