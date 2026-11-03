using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho Nhật ký xét duyệt đợt kiểm kê (OISMS-47-U03 - Append-only).
/// Map vào bảng stock_count_approvals trong PostgreSQL schema v0.5.
/// </summary>
public class StockCountApproval : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid StockCountId { get; set; }
    public string Action { get; set; } = string.Empty;
    public Guid ActorUserId { get; set; }
    public string? Reason { get; set; }
    public string Differences { get; set; } = "[]";

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public StockCount? StockCount { get; set; }
    public User? ActorUser { get; set; }
}
