using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho một dòng Giá áp dụng theo thời gian hiệu lực (valid_from) cho SKU và Đơn vị.
/// Map vào bảng sku_unit_prices trong PostgreSQL schema.
/// </summary>
public class SkuUnitPrice : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid PriceListId { get; set; }
    public Guid SkuId { get; set; }
    public Guid UnitId { get; set; }
    public decimal Price { get; set; }
    public DateTimeOffset ValidFrom { get; set; }
    public Guid SourceItemId { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }
    public Guid? CancelledBy { get; set; }
    public string? CancelReason { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public PriceList? PriceList { get; set; }
    public ProductVariant? Sku { get; set; }
    public Unit? Unit { get; set; }
    public SkuUnit? SkuUnit { get; set; }
    public PriceChangeRequestItem? SourceItem { get; set; }
    public User? CancelledByUser { get; set; }
}
