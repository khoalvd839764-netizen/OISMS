using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho cấu hình giá bán (lẻ/sỉ) gắn với dòng đơn mua hàng (Upgrade 12 Gói 4).
/// Map vào bảng purchase_order_sale_price_configs trong PostgreSQL schema.
/// </summary>
public class PurchaseOrderSalePriceConfig : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid PurchaseOrderItemId { get; set; }
    public Guid UnitId { get; set; }
    public Guid PriceListId { get; set; }
    public string Mode { get; set; } = "KEEP"; // 'KEEP', 'WAC_MARGIN', 'FIXED'
    public decimal? MarginPercent { get; set; }
    public decimal? FixedPrice { get; set; }
    public string? PriceSource { get; set; } // 'SUPPLIER', 'OWNER'
    public string ApplyMode { get; set; } = "ON_RECEIPT"; // 'ON_RECEIPT', 'FROM_DATE'
    public DateTimeOffset? ApplyFrom { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public Guid? PriceChangeRequestId { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public PurchaseOrderItem? PurchaseOrderItem { get; set; }
    public Unit? Unit { get; set; }
    public PriceList? PriceList { get; set; }
    public PriceChangeRequest? PriceChangeRequest { get; set; }
}
