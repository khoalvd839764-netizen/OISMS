using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho một dòng chi tiết SKU trong Phiếu mua hàng (U01-A).
/// Map vào bảng purchase_order_items trong PostgreSQL schema.
/// </summary>
public class PurchaseOrderItem : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public Guid SkuId { get; set; }

    public Guid? UnitId { get; set; }
    public string UnitNameSnapshot { get; set; } = string.Empty;
    public decimal FactorSnapshot { get; set; } = 1m;
    public short QuantityDecimalPlaces { get; set; } = 3;
    public decimal Quantity { get; set; }
    public decimal OrderedBaseQuantity { get => Quantity * FactorSnapshot; private set { } }
    public decimal UnitPrice { get; set; }
    public decimal ReceivedBaseQuantity { get; set; } = 0m;

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    public ProductVariant? Sku { get; set; }
    public Unit? Unit { get; set; }
}
