using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho Phiếu điều chỉnh cân bằng kho (Inventory Adjustment).
/// Sinh tự động sau khi chốt đợt kiểm kê có sai lệch (Variance).
/// Map vào bảng inventory_adjustments trong PostgreSQL schema v0.5.
/// </summary>
public class InventoryAdjustment : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public string Number { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public Guid WarehouseId { get; set; }
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Trạng thái điều chỉnh: 'Draft', 'Confirmed', 'Cancelled'.
    /// </summary>
    public string Status { get; set; } = "Draft";

    public Guid CreatedBy { get; set; }
    public Guid? ConfirmedBy { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Branch? Branch { get; set; }
    public Warehouse? Warehouse { get; set; }
    public User? CreatedByUser { get; set; }
    public User? ConfirmedByUser { get; set; }

    public ICollection<InventoryAdjustmentItem> Items { get; set; } = new List<InventoryAdjustmentItem>();
}
