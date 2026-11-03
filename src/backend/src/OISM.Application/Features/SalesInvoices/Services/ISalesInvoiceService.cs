using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.SalesInvoices.Dtos;

namespace OISM.Application.Features.SalesInvoices.Services;

public interface ISalesInvoiceService
{
    Task<PagedResult<SalesInvoiceSummaryDto>> GetSalesInvoicesAsync(SalesInvoiceFilterDto filter, CancellationToken ct = default);
    Task<SalesInvoiceSummaryDto> GetSalesInvoiceByIdAsync(Guid id, CancellationToken ct = default);
    Task<SalesInvoiceReceiptDto> GetReceiptByIdAsync(Guid id, CancellationToken ct = default);
    Task<SalesInvoiceReceiptDto> GetReceiptByNumberAsync(string number, CancellationToken ct = default);
    Task<DocumentTraceDto> TraceByInvoiceIdAsync(Guid invoiceId, CancellationToken ct = default);
    Task<DocumentTraceDto> TraceByOrderIdAsync(Guid orderId, CancellationToken ct = default);
    Task<DocumentTraceDto> TraceByLedgerIdAsync(Guid ledgerId, CancellationToken ct = default);
}
