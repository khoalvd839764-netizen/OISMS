using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Chi tiết dòng mặt hàng trên Phiếu trả hàng.
/// Map vào bảng sales_return_items trong PostgreSQL schema v0.5 (CS-01).
/// </summary>
public class SalesReturnItem : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid SalesReturnId { get; set; }
    public Guid OrderId { get; set; }
    public Guid OrderItemId { get; set; }
    public Guid SkuId { get; set; }
    public decimal Quantity { get; set; }
    public decimal RefundAmount { get; set; }
    public decimal OriginalUnitCost { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public SalesReturn? SalesReturn { get; set; }
    public OrderItem? OrderItem { get; set; }
    public ProductVariant? Sku { get; set; }
}
