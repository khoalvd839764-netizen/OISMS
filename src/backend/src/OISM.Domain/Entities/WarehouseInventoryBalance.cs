using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Tồn vật lý và giữ chỗ riêng mỗi Kho / SKU.
/// Map vào bảng warehouse_inventory_balances trong PostgreSQL schema v0.5.
/// </summary>
public class WarehouseInventoryBalance : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid SkuId { get; set; }

    public decimal OnHand { get; set; } = 0;
    public decimal Reserved { get; set; } = 0;
    public long LastSequence { get; set; } = 0;

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Branch? Branch { get; set; }
    public Warehouse? Warehouse { get; set; }
    public ProductVariant? Sku { get; set; }
}
