using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho một dòng SKU trong Phiếu chuyển kho nội bộ.
/// Map vào bảng stock_transfer_items trong PostgreSQL schema v0.5.
/// </summary>
public class StockTransferItem : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid TransferId { get; set; }
    public Guid SkuId { get; set; }
    public decimal Quantity { get; set; }
    public decimal? UnitCost { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public StockTransfer? Transfer { get; set; }
    public ProductVariant? Sku { get; set; }
}
