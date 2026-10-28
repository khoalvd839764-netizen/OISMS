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
    /// Trạng thái kiểm kê: 'Draft', 'Counting', 'Confirmed', 'Cancelled'.
    /// </summary>
    public string Status { get; set; } = "Draft";

    public Guid CreatedBy { get; set; }
    public Guid? ConfirmedBy { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? Note { get; set; }
    public Guid? GeneratedAdjustmentId { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Branch? Branch { get; set; }
    public Warehouse? Warehouse { get; set; }
    public User? CreatedByUser { get; set; }
    public User? ConfirmedByUser { get; set; }
    public InventoryAdjustment? GeneratedAdjustment { get; set; }

    public ICollection<StockCountItem> Items { get; set; } = new List<StockCountItem>();
}
