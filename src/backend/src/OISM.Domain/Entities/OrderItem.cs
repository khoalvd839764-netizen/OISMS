using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho một dòng sản phẩm trong Đơn hàng.
/// Chụp snapshot mã, tên và giá vốn WAC tại thời điểm xác nhận.
/// Map vào bảng order_items trong PostgreSQL schema v0.5.
/// </summary>
public class OrderItem : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid OrderId { get; set; }
    public Guid SkuId { get; set; }

    public string SkuCodeSnapshot { get; set; } = string.Empty;
    public string SkuNameSnapshot { get; set; } = string.Empty;

    public Guid? UnitId { get; set; }
    public string UnitNameSnapshot { get; set; } = string.Empty;
    public decimal FactorSnapshot { get; set; } = 1m;
    public decimal BaseQuantity { get => Quantity * FactorSnapshot; private set { } }

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal? CostPrice { get; set; }

    public Guid? SkuUnitPriceId { get; set; }
    public string PriceSource { get; set; } = "Legacy";

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Order? Order { get; set; }
    public ProductVariant? Sku { get; set; }
    public Unit? Unit { get; set; }
    public SkuUnitPrice? SkuUnitPrice { get; set; }
}
