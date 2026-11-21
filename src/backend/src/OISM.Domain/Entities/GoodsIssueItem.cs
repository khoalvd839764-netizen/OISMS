using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho một dòng hàng trong Phiếu xuất kho 02-VT (Goods Issue Item),
/// gắn 1-1 với đúng một dòng bút toán sổ cái kho OUT bán hàng (Upgrade 13 - P1).
/// Map vào bảng goods_issue_items trong PostgreSQL schema v0.5.
/// </summary>
public class GoodsIssueItem : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid GoodsIssueId { get; set; }
    public Guid InventoryTransactionId { get; set; }
    public Guid OrderItemId { get; set; }
    public Guid SkuId { get; set; }

    public string SkuCodeSnapshot { get; set; } = string.Empty;
    public string SkuNameSnapshot { get; set; } = string.Empty;

    public Guid? UnitId { get; set; }
    public string UnitNameSnapshot { get; set; } = string.Empty;
    public decimal FactorSnapshot { get; set; } = 1m;

    public decimal Quantity { get; set; }
    public decimal BaseQuantity { get; set; }
    public decimal UnitCost { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public GoodsIssue? GoodsIssue { get; set; }
    public InventoryTransaction? InventoryTransaction { get; set; }
    public OrderItem? OrderItem { get; set; }
    public ProductVariant? Sku { get; set; }
    public Unit? Unit { get; set; }
}
