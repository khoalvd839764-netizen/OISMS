using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>Dòng hóa đơn theo đơn vị mua và số liệu đối chiếu chụp khi gửi.</summary>
public sealed class SupplierInvoiceItem : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid GoodsReceiptItemId { get; set; }
    public Guid SkuId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal? ExpectedQuantity { get; set; }
    public decimal? ExpectedUnitPrice { get; set; }
    public decimal? QuantityDiff { get; set; }
    public decimal? PriceDiff { get; set; }
    public decimal? AmountDiff { get; set; }
}
