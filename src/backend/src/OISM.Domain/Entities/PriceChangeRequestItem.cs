using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho một dòng chi tiết trong Phiếu thay đổi giá (append-only).
/// Map vào bảng price_change_request_items trong PostgreSQL schema.
/// </summary>
public class PriceChangeRequestItem : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid RequestId { get; set; }
    public Guid PriceListId { get; set; }
    public Guid SkuId { get; set; }
    public Guid UnitId { get; set; }
    public decimal NewPrice { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public PriceChangeRequest? Request { get; set; }
    public PriceList? PriceList { get; set; }
    public ProductVariant? Sku { get; set; }
    public Unit? Unit { get; set; }
    public SkuUnit? SkuUnit { get; set; }
    public SkuUnitPrice? SkuUnitPrice { get; set; }
}
