using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
<<<<<<< HEAD
/// Dòng chi tiết SKU trong Phiếu nhập kho (Goods Receipt Item).
=======
/// Đại diện cho một dòng chi tiết SKU trong Phiếu nhập kho.
>>>>>>> 2cc35c255b5ed4a5ecafb66a14f858702cf6e168
/// Map vào bảng goods_receipt_items trong PostgreSQL schema v0.5.
/// </summary>
public class GoodsReceiptItem : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid ReceiptId { get; set; }
    public Guid SkuId { get; set; }
<<<<<<< HEAD
    public decimal Quantity { get; set; }
=======

    /// <summary>
    /// Số lượng nhập (numeric(18,3) > 0).
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// Đơn giá nhập thực tế (numeric(20,6) >= 0).
    /// </summary>
>>>>>>> 2cc35c255b5ed4a5ecafb66a14f858702cf6e168
    public decimal UnitPrice { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public GoodsReceipt? Receipt { get; set; }
    public ProductVariant? Sku { get; set; }
}
