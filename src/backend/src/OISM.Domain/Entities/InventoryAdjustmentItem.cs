using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho một dòng SKU trong Phiếu điều chỉnh cân bằng kho.
/// Map vào bảng inventory_adjustment_items trong PostgreSQL schema v0.5.
/// </summary>
public class InventoryAdjustmentItem : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid AdjustmentId { get; set; }
    public Guid SkuId { get; set; }
    public decimal QuantityDelta { get; set; }
    public decimal? UnitCost { get; set; }
    public Guid? OriginalTransactionId { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public InventoryAdjustment? Adjustment { get; set; }
    public ProductVariant? Sku { get; set; }
    public InventoryTransaction? OriginalTransaction { get; set; }
}
