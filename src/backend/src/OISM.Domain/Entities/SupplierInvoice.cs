using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>Hóa đơn của nhà cung cấp, gắn với một phiếu nhập kho.</summary>
public sealed class SupplierInvoice : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid GoodsReceiptId { get; set; }
    public Guid SupplierId { get; set; }
    public string InvoiceSeries { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatAmount { get; set; }
    public decimal ShippingFee { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = SupplierInvoiceStatus.Draft;
    public Guid RequestKey { get; set; }
    public string RequestPayloadHash { get; set; } = string.Empty;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public Guid? CancelledBy { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelReason { get; set; }

    public GoodsReceipt GoodsReceipt { get; set; } = null!;
    public ICollection<SupplierInvoiceItem> Items { get; set; } = new List<SupplierInvoiceItem>();
    public PurchaseVariance? Variance { get; set; }
}
