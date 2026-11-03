using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.SalesInvoices.Dtos;
using OISM.Domain.Entities;

namespace OISM.Application.Features.SalesInvoices.Services;

public class SalesInvoiceService : ISalesInvoiceService
{
    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public SalesInvoiceService(IAppDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    private Guid RequireTenantId()
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Không xác định được danh tính Tenant hợp lệ.");
        }
        return tenantId.Value;
    }

    public async Task<PagedResult<SalesInvoiceSummaryDto>> GetSalesInvoicesAsync(SalesInvoiceFilterDto filter, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var query = _context.SalesInvoices
            .AsNoTracking()
            .Include(s => s.Order)
            .Include(s => s.Branch)
            .Where(s => s.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(filter.Number))
        {
            var trimmed = filter.Number.Trim();
            query = query.Where(s => s.Number == trimmed);
        }

        if (filter.OrderId.HasValue)
        {
            query = query.Where(s => s.OrderId == filter.OrderId.Value);
        }

        if (filter.BranchId.HasValue)
        {
            query = query.Where(s => s.BranchId == filter.BranchId.Value);
        }

        if (filter.FromDate.HasValue)
        {
            var fromUtc = filter.FromDate.Value.ToUniversalTime();
            query = query.Where(s => s.IssuedAt >= fromUtc);
        }

        if (filter.ToDate.HasValue)
        {
            // toDate nửa mở: IssuedAt < toDate
            var toUtc = filter.ToDate.Value.ToUniversalTime();
            query = query.Where(s => s.IssuedAt < toUtc);
        }

        var totalCount = await query.CountAsync(ct);

        var pageIndex = Math.Max(1, filter.PageIndex);
        var pageSize = Math.Max(1, Math.Min(100, filter.PageSize));

        var items = await query
            .OrderByDescending(s => s.IssuedAt)
            .ThenByDescending(s => s.Id)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SalesInvoiceSummaryDto
            {
                Id = s.Id,
                Number = s.Number,
                OrderId = s.OrderId,
                OrderNumber = s.Order != null ? s.Order.Number : string.Empty,
                BranchId = s.BranchId,
                BranchName = !string.IsNullOrWhiteSpace(s.BranchNameSnapshot) ? s.BranchNameSnapshot : (s.Branch != null ? s.Branch.Name : string.Empty),
                Channel = s.Channel,
                CustomerName = s.CustomerNameSnapshot,
                TotalAmount = s.TotalAmount,
                IssuedByName = s.IssuedByNameSnapshot,
                IssuedAt = s.IssuedAt
            })
            .ToListAsync(ct);

        return new PagedResult<SalesInvoiceSummaryDto>
        {
            Items = items,
            PageIndex = pageIndex,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<SalesInvoiceSummaryDto> GetSalesInvoiceByIdAsync(Guid id, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var invoice = await _context.SalesInvoices
            .AsNoTracking()
            .Include(s => s.Order)
            .Include(s => s.Branch)
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == id, ct);

        if (invoice == null)
        {
            throw new OismException("SALES_INVOICE_NOT_FOUND", $"Không tìm thấy hóa đơn bán hàng với mã '{id}'.", 404);
        }

        return new SalesInvoiceSummaryDto
        {
            Id = invoice.Id,
            Number = invoice.Number,
            OrderId = invoice.OrderId,
            OrderNumber = invoice.Order != null ? invoice.Order.Number : string.Empty,
            BranchId = invoice.BranchId,
            BranchName = !string.IsNullOrWhiteSpace(invoice.BranchNameSnapshot) ? invoice.BranchNameSnapshot : (invoice.Branch != null ? invoice.Branch.Name : string.Empty),
            Channel = invoice.Channel,
            CustomerName = invoice.CustomerNameSnapshot,
            TotalAmount = invoice.TotalAmount,
            IssuedByName = invoice.IssuedByNameSnapshot,
            IssuedAt = invoice.IssuedAt
        };
    }

    public async Task<SalesInvoiceReceiptDto> GetReceiptByIdAsync(Guid id, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var invoice = await _context.SalesInvoices
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == id, ct);

        if (invoice == null)
        {
            throw new OismException("SALES_INVOICE_NOT_FOUND", $"Không tìm thấy hóa đơn bán hàng với mã '{id}'.", 404);
        }

        return await BuildReceiptDtoAsync(invoice, tenantId, ct);
    }

    public async Task<SalesInvoiceReceiptDto> GetReceiptByNumberAsync(string number, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        if (string.IsNullOrWhiteSpace(number))
        {
            throw new OismException("VALIDATION_ERROR", "Số hóa đơn không được để trống.", 400);
        }

        var trimmed = number.Trim();
        var invoice = await _context.SalesInvoices
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Number == trimmed, ct);

        if (invoice == null)
        {
            throw new OismException("SALES_INVOICE_NOT_FOUND", $"Không tìm thấy hóa đơn bán hàng với số '{number}'.", 404);
        }

        return await BuildReceiptDtoAsync(invoice, tenantId, ct);
    }

    private async Task<SalesInvoiceReceiptDto> BuildReceiptDtoAsync(SalesInvoice invoice, Guid tenantId, CancellationToken ct)
    {
        var order = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.Payment)
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Id == invoice.OrderId, ct);

        if (order == null)
        {
            throw new OismException("ORDER_NOT_FOUND", "Không tìm thấy đơn hàng tương ứng với hóa đơn.", 404);
        }

        var items = (order.Items ?? new List<OrderItem>())
            .Select(i => new PosReceiptItemDto
            {
                SkuId = i.SkuId,
                SkuCode = i.SkuCodeSnapshot,
                ProductName = i.SkuNameSnapshot,
                VariantName = null,
                UnitId = i.UnitId,
                UnitName = i.UnitNameSnapshot,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            })
            .ToList();

        decimal? amountGiven = order.Payment?.CashReceived;
        decimal? changeDue = order.Payment?.CashReceived.HasValue == true
            ? order.Payment.CashReceived.Value - invoice.TotalAmount
            : null;

        return new SalesInvoiceReceiptDto
        {
            InvoiceId = invoice.Id,
            OrderId = invoice.OrderId,
            ReceiptNumber = invoice.Number,
            OrderNumber = order.Number,
            BranchId = invoice.BranchId,
            BranchName = invoice.BranchNameSnapshot,
            BranchAddress = invoice.BranchAddressSnapshot,
            CashierName = invoice.IssuedByNameSnapshot ?? "Thu ngân",
            CustomerName = invoice.CustomerNameSnapshot,
            CustomerPhone = invoice.CustomerPhoneSnapshot,
            TotalAmount = invoice.TotalAmount,
            PaymentMethod = invoice.PaymentMethodSnapshot,
            AmountGiven = amountGiven,
            ChangeDue = changeDue,
            CreatedAt = invoice.IssuedAt,
            Items = items
        };
    }

    public async Task<DocumentTraceDto> TraceByInvoiceIdAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var invoice = await _context.SalesInvoices
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == invoiceId, ct);

        if (invoice == null)
        {
            throw new OismException("SALES_INVOICE_NOT_FOUND", $"Không tìm thấy hóa đơn bán hàng với mã '{invoiceId}'.", 404);
        }

        return await TraceByOrderIdAsync(invoice.OrderId, ct);
    }

    public async Task<DocumentTraceDto> TraceByOrderIdAsync(Guid orderId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var order = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.ConfirmedByUser)
            .Include(o => o.CreatedByUser)
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Id == orderId, ct);

        if (order == null)
        {
            throw new OismException("ORDER_NOT_FOUND", $"Không tìm thấy đơn hàng với mã '{orderId}'.", 404);
        }

        var invoice = await _context.SalesInvoices
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.OrderId == order.Id, ct);

        DocumentTraceInvoiceDto? invoiceDto = null;
        if (invoice != null)
        {
            invoiceDto = new DocumentTraceInvoiceDto
            {
                Id = invoice.Id,
                Number = invoice.Number,
                IssuedAt = invoice.IssuedAt,
                IssuedByName = invoice.IssuedByNameSnapshot,
                TotalAmount = invoice.TotalAmount
            };
        }

        Guid? actorUserId = null;
        string? actorName = null;

        if (order.ConfirmedBy.HasValue)
        {
            actorUserId = order.ConfirmedBy;
            actorName = order.ConfirmedByUser?.FullName;
        }
        else if (string.Equals(order.Channel, "POS", StringComparison.OrdinalIgnoreCase) && order.CreatedBy.HasValue)
        {
            actorUserId = order.CreatedBy;
            actorName = order.CreatedByUser?.FullName;
        }

        var trace = new DocumentTraceDto
        {
            SalesInvoice = invoiceDto,
            Source = new DocumentTraceSourceDto
            {
                Type = "Order",
                Id = order.Id,
                Number = order.Number,
                Status = order.Status,
                ActorUserId = actorUserId,
                ActorName = actorName,
                ActedAt = order.ConfirmedAt
            },
            Lines = new List<DocumentTraceLineDto>()
        };

        var orderItemIds = (order.Items ?? new List<OrderItem>()).Select(i => i.Id).ToList();
        if (orderItemIds.Count > 0)
        {
            var orderItemMap = order.Items!.ToDictionary(i => i.Id);

            var transactions = await LedgerLines(tenantId)
                .Where(t => t.OrderItemId.HasValue && orderItemIds.Contains(t.OrderItemId.Value))
                .OrderBy(t => t.SequenceNo)
                .ToListAsync(ct);

            trace.Lines = transactions
                .Select(t => MapLine(t, orderItemMap.GetValueOrDefault(t.OrderItemId!.Value)))
                .ToList();
        }

        return trace;
    }

    public async Task<DocumentTraceDto> TraceByLedgerIdAsync(Guid ledgerId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var ledger = await LedgerLines(tenantId).FirstOrDefaultAsync(t => t.Id == ledgerId, ct);

        if (ledger == null)
        {
            throw new OismException("LEDGER_ENTRY_NOT_FOUND", $"Không tìm thấy dòng sổ kho với mã '{ledgerId}'.", 404);
        }

        if (ledger.OrderItemId.HasValue)
        {
            var orderId = await _context.OrderItems
                .AsNoTracking()
                .Where(i => i.TenantId == tenantId && i.Id == ledger.OrderItemId.Value)
                .Select(i => (Guid?)i.OrderId)
                .FirstOrDefaultAsync(ct);

            if (orderId.HasValue)
            {
                return await TraceByOrderIdAsync(orderId.Value, ct);
            }
        }

        if (ledger.TransferItemId.HasValue)
        {
            var transferItem = await _context.StockTransferItems
                .AsNoTracking()
                .Include(i => i.Transfer)
                    .ThenInclude(t => t!.DispatchedByUser)
                .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == ledger.TransferItemId.Value, ct);

            if (transferItem?.Transfer != null)
            {
                var transfer = transferItem.Transfer;
                var itemIds = _context.StockTransferItems
                    .Where(i => i.TenantId == tenantId && i.TransferId == transfer.Id)
                    .Select(i => i.Id);
                var lines = await LedgerLines(tenantId)
                    .Where(t => t.TransferItemId.HasValue && itemIds.Contains(t.TransferItemId.Value))
                    .OrderBy(t => t.SequenceNo)
                    .ToListAsync(ct);

                return BuildTrace("StockTransfer", transfer.Id, transfer.Number, transfer.Status,
                    transfer.DispatchedBy, transfer.DispatchedByUser?.FullName, transfer.DispatchedAt, lines);
            }
        }

        if (ledger.AdjustmentItemId.HasValue)
        {
            var adjustmentItem = await _context.InventoryAdjustmentItems
                .AsNoTracking()
                .Include(i => i.Adjustment)
                    .ThenInclude(a => a!.ConfirmedByUser)
                .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == ledger.AdjustmentItemId.Value, ct);

            if (adjustmentItem?.Adjustment != null)
            {
                var adjustment = adjustmentItem.Adjustment;
                var itemIds = _context.InventoryAdjustmentItems
                    .Where(i => i.TenantId == tenantId && i.AdjustmentId == adjustment.Id)
                    .Select(i => i.Id);
                var lines = await LedgerLines(tenantId)
                    .Where(t => t.AdjustmentItemId.HasValue && itemIds.Contains(t.AdjustmentItemId.Value))
                    .OrderBy(t => t.SequenceNo)
                    .ToListAsync(ct);

                var linkedCount = await _context.StockCounts
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.GeneratedAdjustmentId == adjustment.Id, ct);

                return linkedCount != null
                    ? BuildTrace("StockCount", linkedCount.Id, linkedCount.Number, linkedCount.Status,
                        adjustment.ConfirmedBy, adjustment.ConfirmedByUser?.FullName, adjustment.ConfirmedAt, lines)
                    : BuildTrace("InventoryAdjustment", adjustment.Id, adjustment.Number, adjustment.Status,
                        adjustment.ConfirmedBy, adjustment.ConfirmedByUser?.FullName, adjustment.ConfirmedAt, lines);
            }
        }

        if (ledger.StockCountItemId.HasValue)
        {
            var countItem = await _context.StockCountItems
                .AsNoTracking()
                .Include(i => i.StockCount)
                    .ThenInclude(c => c!.ConfirmedByUser)
                .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == ledger.StockCountItemId.Value, ct);

            if (countItem?.StockCount != null)
            {
                var count = countItem.StockCount;
                return BuildTrace("StockCount", count.Id, count.Number, count.Status,
                    count.ConfirmedBy, count.ConfirmedByUser?.FullName, count.ConfirmedAt, new[] { ledger });
            }
        }

        if (ledger.ReceiptItemId.HasValue)
        {
            var receiptItem = await _context.GoodsReceiptItems
                .AsNoTracking()
                .Include(i => i.Receipt)
                    .ThenInclude(r => r!.ConfirmedByUser)
                .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == ledger.ReceiptItemId.Value, ct);

            if (receiptItem?.Receipt != null)
            {
                var receipt = receiptItem.Receipt;
                var itemIds = _context.GoodsReceiptItems
                    .Where(i => i.TenantId == tenantId && i.ReceiptId == receipt.Id)
                    .Select(i => i.Id);
                var lines = await LedgerLines(tenantId)
                    .Where(t => t.ReceiptItemId.HasValue && itemIds.Contains(t.ReceiptItemId.Value))
                    .OrderBy(t => t.SequenceNo)
                    .ToListAsync(ct);

                return BuildTrace("GoodsReceipt", receipt.Id, receipt.Number, receipt.Status,
                    receipt.ConfirmedBy, receipt.ConfirmedByUser?.FullName, receipt.ConfirmedAt, lines);
            }
        }

        // Trả hàng chưa có luồng (CS-01 bổ sung); dòng không rõ nguồn vẫn trả về thay vì báo lỗi.
        return BuildTrace(ledger.SalesReturnItemId.HasValue ? "SalesReturn" : "Unknown", null, null, null, null, null, null, new[] { ledger });
    }

    private IQueryable<InventoryTransaction> LedgerLines(Guid tenantId) =>
        _context.InventoryTransactions
            .AsNoTracking()
            .Include(t => t.Warehouse)
            .Include(t => t.Sku)
            .Where(t => t.TenantId == tenantId);

    private static DocumentTraceDto BuildTrace(
        string type,
        Guid? id,
        string? number,
        string? status,
        Guid? actorUserId,
        string? actorName,
        DateTimeOffset? actedAt,
        IEnumerable<InventoryTransaction> lines) => new()
        {
            SalesInvoice = null,
            Source = new DocumentTraceSourceDto
            {
                Type = type,
                Id = id,
                Number = number,
                Status = status,
                ActorUserId = actorUserId,
                ActorName = actorName,
                ActedAt = actedAt
            },
            Lines = lines.Select(t => MapLine(t)).ToList()
        };

    private static DocumentTraceLineDto MapLine(InventoryTransaction t, OrderItem? orderItem = null) => new()
    {
        LedgerId = t.Id,
        Type = t.Type,
        SkuId = t.SkuId,
        SkuCode = t.Sku?.SKU ?? string.Empty,
        WarehouseId = t.WarehouseId,
        WarehouseName = t.Warehouse?.Name ?? string.Empty,
        Quantity = t.Quantity,
        UnitCost = t.UnitCost,
        BalanceAfter = t.BalanceAfter,
        SequenceNo = t.SequenceNo,
        CreatedAt = t.CreatedAt,
        OrderItemId = t.OrderItemId,
        UnitName = orderItem?.UnitNameSnapshot,
        OrderItemQuantity = orderItem?.Quantity
    };
}
