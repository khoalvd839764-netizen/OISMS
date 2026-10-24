using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho một dòng SKU trong Đợt kiểm kê kho hàng.
/// Map vào bảng stock_count_items trong PostgreSQL schema v0.5.
/// </summary>
public class StockCountItem : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid StockCountId { get; set; }
    public Guid SkuId { get; set; }
    public decimal ExpectedQuantity { get; set; }
    public decimal? CountedQuantity { get; set; }
    public long BalanceSequenceAtSnapshot { get; set; }
    public decimal? AdjustmentUnitCost { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public StockCount? StockCount { get; set; }
    public ProductVariant? Sku { get; set; }
}
