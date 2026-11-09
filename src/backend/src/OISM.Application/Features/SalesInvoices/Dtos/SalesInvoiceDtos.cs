using OISM.Application.Features.Orders.Dtos;

namespace OISM.Application.Features.SalesInvoices.Dtos;

public class SalesInvoiceSummaryDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public decimal TotalAmount { get; set; }
    public string? IssuedByName { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
}

public class SalesInvoiceReceiptDto
{
    public Guid InvoiceId { get; set; }
    public Guid OrderId { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public string? BranchName { get; set; }
    public string? BranchAddress { get; set; }
    public string? BranchPhone { get; set; }
    public string? CashierName { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public decimal TotalAmount { get; set; }
    public string? PaymentMethod { get; set; }
    public decimal? AmountGiven { get; set; }
    public decimal? ChangeDue { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<PosReceiptItemDto> Items { get; set; } = new();
}

public class SalesInvoiceFilterDto
{
    public string? Number { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? BranchId { get; set; }
    public DateTimeOffset? FromDate { get; set; }
    public DateTimeOffset? ToDate { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class DocumentTraceDto
{
    public DocumentTraceInvoiceDto? SalesInvoice { get; set; }
    public DocumentTraceSourceDto Source { get; set; } = new();
    public List<DocumentTraceLineDto> Lines { get; set; } = new();
    public List<DocumentTraceSalesReturnDto> SalesReturns { get; set; } = new();
}

public class DocumentTraceSalesReturnDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? Note { get; set; }
    public decimal TotalRefundAmount { get; set; }
    public string? RefundMethod { get; set; }
    public Guid? ConfirmedBy { get; set; }
    public string? ConfirmedByName { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<DocumentTraceLineDto> Lines { get; set; } = new();
}

public class DocumentTraceInvoiceDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; set; }
    public string? IssuedByName { get; set; }
    public decimal TotalAmount { get; set; }
}

public class DocumentTraceSourceDto
{
    public string Type { get; set; } = string.Empty;
    public Guid? Id { get; set; }
    public string? Number { get; set; }
    public string? Status { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? ActorName { get; set; }
    public DateTimeOffset? ActedAt { get; set; }
}

public class DocumentTraceLineDto
{
    public Guid LedgerId { get; set; }
    public string Type { get; set; } = string.Empty;
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal BalanceAfter { get; set; }
    public long SequenceNo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? OrderItemId { get; set; }
    public Guid? SalesReturnItemId { get; set; }
    public string? UnitName { get; set; }
    public decimal? OrderItemQuantity { get; set; }
}
