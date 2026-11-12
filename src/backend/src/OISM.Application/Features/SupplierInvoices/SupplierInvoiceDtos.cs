namespace OISM.Application.Features.SupplierInvoices;

public class SupplierInvoiceDraftDto
{
    public string InvoiceSeries { get; init; } = string.Empty;
    public string InvoiceNumber { get; init; } = string.Empty;
    public DateOnly InvoiceDate { get; init; }
    public decimal VatAmount { get; init; }
    public decimal ShippingFee { get; init; }
    public List<SupplierInvoiceLineDto> Items { get; init; } = [];
}

public sealed class CreateSupplierInvoiceDto : SupplierInvoiceDraftDto
{
    public Guid GoodsReceiptId { get; init; }
    public Guid SupplierId { get; init; }
}

public sealed record SupplierInvoiceLineDto(Guid GoodsReceiptItemId, decimal Quantity, decimal UnitPrice);
public sealed record SupplierInvoiceDecisionDto(string? DecisionNote);
public sealed record CancelSupplierInvoiceDto(string? Reason);

public sealed class SupplierInvoiceFilterDto
{
    public Guid? SupplierId { get; init; }
    public string? Status { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public Guid? GoodsReceiptId { get; init; }
    public Guid? PurchaseOrderId { get; init; }
    public bool PendingOnly { get; init; }
    public int PageIndex { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed record SupplierInvoiceResponseDto(
    Guid Id,
    Guid GoodsReceiptId,
    Guid SupplierId,
    string InvoiceSeries,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    decimal Subtotal,
    decimal VatAmount,
    decimal ShippingFee,
    decimal TotalAmount,
    string Status,
    Guid RequestKey,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    Guid? CancelledBy,
    DateTimeOffset? CancelledAt,
    string? CancelReason,
    PurchaseVarianceDto? Variance,
    IReadOnlyList<SupplierInvoiceComparisonDto>? Comparison,
    bool IsReplay = false,
    string? SupplierName = null,
    string? GoodsReceiptNumber = null,
    string? GoodsReceiptStatus = null,
    Guid? PurchaseOrderId = null,
    string? PurchaseOrderNumber = null);

public sealed record PurchaseVarianceDto(
    Guid Id,
    string VarianceType,
    decimal TotalAmountDiff,
    string Status,
    Guid? DecidedBy,
    DateTimeOffset? DecidedAt,
    string? DecisionNote);

public sealed record SupplierInvoiceComparisonDto(
    Guid GoodsReceiptItemId,
    Guid? PurchaseOrderItemId,
    Guid SkuId,
    string UnitName,
    decimal FactorSnapshot,
    SupplierInvoiceComparisonValue PurchaseOrder,
    SupplierInvoiceComparisonValue GoodsReceipt,
    SupplierInvoiceComparisonValue? Invoice,
    decimal? ExpectedQuantity,
    decimal? ExpectedUnitPrice,
    decimal? QuantityDiff,
    decimal? PriceDiff,
    decimal? AmountDiff);

public sealed record SupplierInvoiceComparisonValue(decimal Quantity, decimal? UnitPrice);
