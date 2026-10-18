using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Dòng chi tiết SKU trong Phiếu nhập kho (Goods Receipt Item).
/// Map vào bảng goods_receipt_items trong PostgreSQL schema v0.5.
/// </summary>
public class GoodsReceiptItem : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid ReceiptId { get; set; }
    public Guid SkuId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public GoodsReceipt? Receipt { get; set; }
    public ProductVariant? Sku { get; set; }
}
