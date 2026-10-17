using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho Tổng tồn và Giá vốn bình quân (WAC) theo Chi nhánh và SKU.
/// Map vào bảng inventory_balances trong PostgreSQL schema v0.5.
/// </summary>
public class InventoryBalance : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public Guid SkuId { get; set; }

    public decimal OnHand { get; set; } = 0;
    public decimal Reserved { get; set; } = 0;
    public decimal AverageCost { get; set; } = 0;
    public decimal ReorderPoint { get; set; } = 0;
    public long LastSequence { get; set; } = 0;

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Branch? Branch { get; set; }
    public ProductVariant? Sku { get; set; }
}
