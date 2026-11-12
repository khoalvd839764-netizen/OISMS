using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Domain.Common;
using OISM.Domain.Entities;

namespace OISM.Application.Features.SupplierInvoices;

public sealed class SupplierInvoiceService(
    IAppDbContext context,
    ICurrentTenantService currentTenant,
    ISqlLockService locks)
{
    private const decimal MaxQuantity = 999_999_999_999_999.999m;
    private const decimal MaxUnitPrice = 99_999_999_999_999.999999m;
    private const decimal MaxAmount = 999_999_999_999_999.999999999m;

    public async Task<PagedResult<SupplierInvoiceResponseDto>> ListAsync(
        SupplierInvoiceFilterDto filter, CancellationToken ct)
    {
        RequireTenant();
        if (filter.FromDate > filter.ToDate)
            throw Validation("Ngày bắt đầu phải trước hoặc bằng ngày kết thúc.");

        var query = context.SupplierInvoices.AsNoTracking().Include(i => i.Variance).AsQueryable();
        if (filter.SupplierId is { } supplierId)
            query = query.Where(i => i.SupplierId == supplierId);
        if (filter.GoodsReceiptId is { } receiptId)
            query = query.Where(i => i.GoodsReceiptId == receiptId);
        if (filter.PendingOnly)
            query = query.Where(i => i.Status == SupplierInvoiceStatus.PendingApproval);
        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(i => i.Status == filter.Status);
        if (filter.FromDate is { } from)
            query = query.Where(i => i.InvoiceDate >= from);
        if (filter.ToDate is { } to)
            query = query.Where(i => i.InvoiceDate <= to);

        var count = await query.CountAsync(ct);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var pageIndex = Math.Clamp(filter.PageIndex, 1, int.MaxValue / pageSize);
        var invoices = await query.OrderByDescending(i => i.InvoiceDate).ThenBy(i => i.Id)
            .Skip((pageIndex - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return new PagedResult<SupplierInvoiceResponseDto>
        {
            Items = invoices.Select(i => Map(i, includeComparison: false)).ToList(),
            PageIndex = pageIndex,
            PageSize = pageSize,
            TotalCount = count
        };
    }

    public async Task<SupplierInvoiceResponseDto> GetAsync(Guid id, CancellationToken ct)
    {
        RequireTenant();
        var invoice = await InvoiceGraph().AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw NotFound("Không tìm thấy hóa đơn nhà cung cấp.");
        return Map(invoice);
    }

    public Task<SupplierInvoiceResponseDto> CreateAsync(
        CreateSupplierInvoiceDto dto, Guid requestKey, Guid userId, CancellationToken ct)
    {
        var tenantId = RequireTenant();
        if (requestKey == Guid.Empty || dto.GoodsReceiptId == Guid.Empty || dto.SupplierId == Guid.Empty)
            throw Validation("Bắt buộc cung cấp Idempotency-Key, phiếu nhập và nhà cung cấp hợp lệ.");

        var draft = ValidateDraft(dto);
        var hash = ComputePayloadHash(dto, draft);
        var invoiceId = Guid.NewGuid();
        return WriteAsync(async () =>
        {
            await locks.LockGoodsReceiptAsync(tenantId, dto.GoodsReceiptId, ct);
            // Serializes reuse of one key even when the competing request names a different receipt.
            await locks.LockRequestKeyAsync(tenantId, requestKey, ct);
            var existing = await context.SupplierInvoices.AsNoTracking()
                .Where(i => i.RequestKey == requestKey)
                .Select(i => new { i.Id, i.RequestPayloadHash })
                .SingleOrDefaultAsync(ct);
            if (existing != null)
            {
                if (existing.RequestPayloadHash != hash)
                    throw new IdempotencyPayloadMismatchException("Idempotency-Key đã được dùng cho nội dung hóa đơn khác.");

                await locks.LockSupplierInvoiceAsync(tenantId, existing.Id, ct);
                var replay = await InvoiceGraph().SingleAsync(i => i.Id == existing.Id, ct);
                return Map(replay) with { IsReplay = true };
            }

            await locks.LockSupplierInvoiceAsync(tenantId, invoiceId, ct);
            var receipt = await context.GoodsReceipts.Include(r => r.Items).ThenInclude(i => i.PurchaseOrderItem)
                .SingleOrDefaultAsync(r => r.Id == dto.GoodsReceiptId, ct)
                ?? throw NotFound("Không tìm thấy phiếu nhập kho.");
            EnsureReceiptUsable(receipt, dto.SupplierId);

            if (await context.SupplierInvoices.AnyAsync(i => i.GoodsReceiptId == receipt.Id &&
                i.Status != SupplierInvoiceStatus.Cancelled && i.Status != SupplierInvoiceStatus.Rejected, ct))
            {
                throw AlreadyExists();
            }

            var invoice = new SupplierInvoice
            {
                Id = invoiceId,
                TenantId = tenantId,
                GoodsReceiptId = receipt.Id,
                GoodsReceipt = receipt,
                SupplierId = dto.SupplierId,
                RequestKey = requestKey,
                RequestPayloadHash = hash,
                CreatedBy = userId
            };
            ApplyDraft(invoice, draft);
            context.SupplierInvoices.Add(invoice);
            await context.SaveChangesAsync(ct);
            return Map(invoice);
        }, ct);
    }

    public Task<SupplierInvoiceResponseDto> UpdateAsync(Guid id, SupplierInvoiceDraftDto dto, CancellationToken ct) =>
        MutateAsync(id, async invoice =>
        {
            EnsureState(invoice, SupplierInvoiceStatus.Draft);
            EnsureReceiptUsable(invoice.GoodsReceipt, invoice.SupplierId);
            var draft = ValidateDraft(dto);
            var retained = draft.Items.Select(i => i.GoodsReceiptItemId).ToHashSet();
            foreach (var item in invoice.Items.Where(i => !retained.Contains(i.GoodsReceiptItemId)).ToList())
            {
                invoice.Items.Remove(item);
                context.SupplierInvoiceItems.Remove(item);
            }

            ApplyDraft(invoice, draft, trackNewItems: true);
            invoice.UpdatedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(ct);
        }, ct);

    public Task<SupplierInvoiceResponseDto> SubmitAsync(Guid id, CancellationToken ct) =>
        MutateAsync(id, async invoice =>
        {
            EnsureState(invoice, SupplierInvoiceStatus.Draft);
            EnsureReceiptUsable(invoice.GoodsReceipt, invoice.SupplierId);
            var receiptItems = invoice.GoodsReceipt.Items.ToDictionary(i => i.Id);
            foreach (var line in invoice.Items)
            {
                var receiptItem = receiptItems[line.GoodsReceiptItemId];
                line.ExpectedQuantity = receiptItem.Quantity;
                line.ExpectedUnitPrice = receiptItem.PurchaseOrderItem!.UnitPrice;
                line.QuantityDiff = line.Quantity - line.ExpectedQuantity;
                line.PriceDiff = line.UnitPrice - line.ExpectedUnitPrice;
                line.AmountDiff = AmountDifference(line, receiptItem);
            }

            var billedIds = invoice.Items.Select(i => i.GoodsReceiptItemId).ToHashSet();
            var missing = receiptItems.Values.Where(i => !billedIds.Contains(i.Id)).ToList();
            var hasVariance = missing.Count > 0 || invoice.Items.Any(i => i.QuantityDiff != 0m || i.PriceDiff != 0m);
            var now = DateTimeOffset.UtcNow;

            // The DB permits snapshot writes only while the header is still Draft.
            await context.SaveChangesAsync(ct);
            invoice.Status = hasVariance ? SupplierInvoiceStatus.PendingApproval : SupplierInvoiceStatus.Matched;
            invoice.SubmittedAt = now;
            invoice.UpdatedAt = now;
            if (hasVariance)
            {
                invoice.Variance = new PurchaseVariance
                {
                    TenantId = invoice.TenantId,
                    InvoiceId = invoice.Id,
                    GoodsReceiptId = invoice.GoodsReceiptId,
                    VarianceType = invoice.GoodsReceipt.IsDraft ? PurchaseVarianceType.AtReceipt : PurchaseVarianceType.AfterReceipt,
                    TotalAmountDiff = SumDifferences(invoice.Items, missing),
                    CreatedAt = now
                };
                context.PurchaseVariances.Add(invoice.Variance);
            }

            await context.SaveChangesAsync(ct);
        }, ct);

    public Task<SupplierInvoiceResponseDto> DecideAsync(
        Guid id, bool approve, string? note, Guid userId, CancellationToken ct) =>
        MutateAsync(id, async invoice =>
        {
            EnsureState(invoice, SupplierInvoiceStatus.PendingApproval);
            var variance = invoice.Variance;
            if (variance?.Status != PurchaseVarianceStatus.Pending)
                throw InvalidState("Hóa đơn không có chênh lệch đang chờ duyệt.");

            var decisionNote = ValidateNote(note, required: !approve);
            var now = DateTimeOffset.UtcNow;
            invoice.Status = approve ? SupplierInvoiceStatus.Approved : SupplierInvoiceStatus.Rejected;
            invoice.UpdatedAt = now;
            variance.Status = approve ? PurchaseVarianceStatus.Approved : PurchaseVarianceStatus.Rejected;
            variance.DecidedBy = userId;
            variance.DecidedAt = now;
            variance.DecisionNote = decisionNote;
            variance.UpdatedAt = now;

            if (!approve && variance.VarianceType == PurchaseVarianceType.AtReceipt && invoice.GoodsReceipt.IsDraft)
                invoice.GoodsReceipt.Cancel(now);

            await context.SaveChangesAsync(ct);
        }, ct);

    public Task<SupplierInvoiceResponseDto> CancelAsync(Guid id, string? reason, Guid userId, CancellationToken ct) =>
        MutateAsync(id, async invoice =>
        {
            if (invoice.Status is not (SupplierInvoiceStatus.Draft or SupplierInvoiceStatus.Matched or SupplierInvoiceStatus.Approved))
                throw InvalidState("Chỉ được hủy hóa đơn ở trạng thái Draft, Matched hoặc Approved.");

            invoice.CancelReason = ValidateNote(reason, required: true);
            var now = DateTimeOffset.UtcNow;
            invoice.Status = SupplierInvoiceStatus.Cancelled;
            invoice.CancelledBy = userId;
            invoice.CancelledAt = now;
            invoice.UpdatedAt = now;
            await context.SaveChangesAsync(ct);
        }, ct);

    private Task<SupplierInvoiceResponseDto> MutateAsync(Guid id, Func<SupplierInvoice, Task> mutate, CancellationToken ct)
    {
        var tenantId = RequireTenant();
        return WriteAsync(async () =>
        {
            // The receipt ID is immutable. Load the rest of the document only after both locks.
            var receiptId = await context.SupplierInvoices.Where(i => i.Id == id)
                .Select(i => (Guid?)i.GoodsReceiptId).SingleOrDefaultAsync(ct)
                ?? throw NotFound("Không tìm thấy hóa đơn nhà cung cấp.");
            await locks.LockGoodsReceiptAsync(tenantId, receiptId, ct);
            await locks.LockSupplierInvoiceAsync(tenantId, id, ct);
            var invoice = await InvoiceGraph().SingleAsync(i => i.Id == id, ct);
            await mutate(invoice);
            return Map(invoice);
        }, ct);
    }

    private Task<SupplierInvoiceResponseDto> WriteAsync(Func<Task<SupplierInvoiceResponseDto>> action, CancellationToken ct) =>
        context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            context.UnitOfWork.ResetTracking();
            await using var transaction = await context.UnitOfWork.BeginTransactionAsync(ct);
            try
            {
                var response = await action();
                await transaction.CommitAsync(ct);
                return response;
            }
            catch (DbUpdateException ex) when (context.DatabaseErrors.IsUniqueViolation(ex, "uq_supplier_invoices_number"))
            {
                throw new OismException("SUPPLIER_INVOICE_DUPLICATE", "Ký hiệu và số hóa đơn đã tồn tại cho nhà cung cấp.", 409);
            }
            catch (DbUpdateException ex) when (context.DatabaseErrors.IsUniqueViolation(ex, "uq_supplier_invoices_active_receipt"))
            {
                throw AlreadyExists();
            }
        });

    private IQueryable<SupplierInvoice> InvoiceGraph() =>
        context.SupplierInvoices.Include(i => i.Items).Include(i => i.Variance)
            .Include(i => i.GoodsReceipt).ThenInclude(r => r.Items).ThenInclude(i => i.PurchaseOrderItem);

    private void ApplyDraft(SupplierInvoice invoice, ValidatedDraft draft, bool trackNewItems = false)
    {
        var receiptItems = invoice.GoodsReceipt.Items.ToDictionary(i => i.Id);
        if (draft.Items.Any(i => !receiptItems.ContainsKey(i.GoodsReceiptItemId)))
            throw ReceiptMismatch("Dòng hàng hóa đơn phải thuộc phiếu nhập đã liên kết.");

        invoice.InvoiceSeries = draft.Series;
        invoice.InvoiceNumber = draft.Number;
        invoice.InvoiceDate = draft.Date;
        invoice.Subtotal = draft.Subtotal;
        invoice.VatAmount = draft.VatAmount;
        invoice.ShippingFee = draft.ShippingFee;
        invoice.TotalAmount = draft.TotalAmount;

        var existing = invoice.Items.ToDictionary(i => i.GoodsReceiptItemId);
        foreach (var line in draft.Items)
        {
            var isNew = !existing.TryGetValue(line.GoodsReceiptItemId, out var item);
            item ??= new SupplierInvoiceItem
            {
                TenantId = invoice.TenantId,
                InvoiceId = invoice.Id,
                GoodsReceiptItemId = line.GoodsReceiptItemId,
                SkuId = receiptItems[line.GoodsReceiptItemId].SkuId
            };
            item.Quantity = line.Quantity;
            item.UnitPrice = line.UnitPrice;
            if (isNew)
            {
                invoice.Items.Add(item);
                if (trackNewItems)
                    context.SupplierInvoiceItems.Add(item);
            }
        }
    }

    private static ValidatedDraft ValidateDraft(SupplierInvoiceDraftDto dto)
    {
        var series = dto.InvoiceSeries?.Trim() ?? string.Empty;
        var number = dto.InvoiceNumber?.Trim() ?? string.Empty;
        if (series.Length is < 1 or > 50 || number.Length is < 1 or > 50 ||
            dto.InvoiceDate == default || dto.VatAmount < 0 || dto.ShippingFee < 0 ||
            dto.VatAmount != decimal.Round(dto.VatAmount, 6) || dto.ShippingFee != decimal.Round(dto.ShippingFee, 6))
        {
            throw Validation("Ký hiệu, số, ngày hóa đơn, tiền thuế hoặc phí vận chuyển không hợp lệ.");
        }
        if (dto.Items is not { Count: > 0 } ||
            dto.Items.Any(i => i is null || i.GoodsReceiptItemId == Guid.Empty ||
                i.Quantity is < 0m or > MaxQuantity || i.Quantity != decimal.Round(i.Quantity, 3) ||
                i.UnitPrice is < 0m or > MaxUnitPrice || i.UnitPrice != decimal.Round(i.UnitPrice, 6)) ||
            dto.Items.Select(i => i.GoodsReceiptItemId).Distinct().Count() != dto.Items.Count)
        {
            throw Validation("Cần ít nhất một dòng hợp lệ, không lặp dòng nhập; lượng tối đa 3 và giá tối đa 6 chữ số thập phân.");
        }

        try
        {
            var subtotal = dto.Items.Sum(i => i.Quantity * i.UnitPrice);
            var total = subtotal + dto.VatAmount + dto.ShippingFee;
            if (total > MaxAmount)
                throw Validation("Số tiền hóa đơn vượt giới hạn hỗ trợ.");
            return new ValidatedDraft(series, number, dto.InvoiceDate, dto.VatAmount, dto.ShippingFee,
                subtotal, total, dto.Items);
        }
        catch (OverflowException)
        {
            throw Validation("Số tiền hóa đơn vượt giới hạn hỗ trợ.");
        }
    }

    private static string ComputePayloadHash(CreateSupplierInvoiceDto dto, ValidatedDraft draft)
    {
        static string Number(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);

        var payload = new
        {
            dto.GoodsReceiptId, dto.SupplierId, draft.Series, draft.Number, draft.Date,
            VatAmount = Number(draft.VatAmount),
            ShippingFee = Number(draft.ShippingFee),
            Items = draft.Items.OrderBy(i => i.GoodsReceiptItemId)
                .Select(i => new { i.GoodsReceiptItemId, Quantity = Number(i.Quantity), UnitPrice = Number(i.UnitPrice) })
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload)));
    }

    private static SupplierInvoiceResponseDto Map(SupplierInvoice invoice, bool includeComparison = true)
    {
        var variance = invoice.Variance;
        var varianceDto = variance == null ? null : new PurchaseVarianceDto(variance.Id, variance.VarianceType,
            variance.TotalAmountDiff, variance.Status, variance.DecidedBy, variance.DecidedAt, variance.DecisionNote);
        return new SupplierInvoiceResponseDto(invoice.Id, invoice.GoodsReceiptId, invoice.SupplierId, invoice.InvoiceSeries,
            invoice.InvoiceNumber, invoice.InvoiceDate, invoice.Subtotal, invoice.VatAmount, invoice.ShippingFee,
            invoice.TotalAmount, invoice.Status, invoice.RequestKey, invoice.CreatedBy, invoice.CreatedAt, invoice.SubmittedAt,
            invoice.CancelledBy, invoice.CancelledAt, invoice.CancelReason, varianceDto,
            includeComparison ? Compare(invoice) : null);
    }

    private static List<SupplierInvoiceComparisonDto> Compare(SupplierInvoice invoice)
    {
        var lines = invoice.Items.ToDictionary(i => i.GoodsReceiptItemId);
        return invoice.GoodsReceipt.Items.OrderBy(r => r.Id).Select(receipt =>
        {
            lines.TryGetValue(receipt.Id, out var line);
            var po = receipt.PurchaseOrderItem!;
            var missingAfterSubmit = line == null && invoice.SubmittedAt.HasValue;
            return new SupplierInvoiceComparisonDto(receipt.Id, receipt.PurchaseOrderItemId, receipt.SkuId,
                receipt.UnitNameSnapshot, receipt.FactorSnapshot,
                new SupplierInvoiceComparisonValue(po.Quantity, po.UnitPrice),
                new SupplierInvoiceComparisonValue(receipt.Quantity, receipt.UnitPrice),
                line == null ? null : new SupplierInvoiceComparisonValue(line.Quantity, line.UnitPrice),
                missingAfterSubmit ? receipt.Quantity : line?.ExpectedQuantity,
                missingAfterSubmit ? po.UnitPrice : line?.ExpectedUnitPrice,
                missingAfterSubmit ? -receipt.Quantity : line?.QuantityDiff,
                line?.PriceDiff,
                missingAfterSubmit ? AmountDifference(null, receipt) : line?.AmountDiff);
        }).ToList();
    }

    private static decimal AmountDifference(SupplierInvoiceItem? line, GoodsReceiptItem receipt)
    {
        try
        {
            return (line == null ? 0m : line.Quantity * line.UnitPrice)
                - receipt.Quantity * receipt.PurchaseOrderItem!.UnitPrice;
        }
        catch (OverflowException)
        {
            throw Validation("Số tiền đối chiếu vượt giới hạn hỗ trợ.");
        }
    }

    private static decimal SumDifferences(IEnumerable<SupplierInvoiceItem> lines, IEnumerable<GoodsReceiptItem> missing)
    {
        try
        {
            return lines.Sum(i => i.AmountDiff!.Value) + missing.Sum(i => AmountDifference(null, i));
        }
        catch (OverflowException)
        {
            throw Validation("Tổng chênh lệch vượt giới hạn hỗ trợ.");
        }
    }

    private Guid RequireTenant() =>
        currentTenant.TenantId is { } id && id != Guid.Empty
            ? id
            : throw new OismException("UNAUTHORIZED", "Yêu cầu TenantId hợp lệ từ token.", 401);

    private static void EnsureReceiptUsable(GoodsReceipt receipt, Guid supplierId)
    {
        if (receipt.Status == GoodsReceiptStatus.Cancelled)
            throw InvalidState("Phiếu nhập đã bị hủy.");
        if (receipt.SupplierId != supplierId || !receipt.PurchaseOrderId.HasValue || receipt.Items.Count == 0 ||
            receipt.Items.Any(i => i.PurchaseOrderItem?.PurchaseOrderId != receipt.PurchaseOrderId))
        {
            throw ReceiptMismatch("Nhà cung cấp hoặc dòng phiếu mua không khớp với phiếu nhập.");
        }
    }

    private static void EnsureState(SupplierInvoice invoice, string expected)
    {
        if (invoice.Status != expected)
            throw InvalidState($"Hóa đơn đang ở trạng thái '{invoice.Status}', yêu cầu '{expected}'.");
    }

    private static string? ValidateNote(string? note, bool required)
    {
        var value = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if ((required && value == null) || value?.Length > 500)
            throw Validation("Lý do phải hợp lệ và tối đa 500 ký tự.");
        return value;
    }

    private static OismException Validation(string message) => new("VALIDATION_ERROR", message, 400);
    private static OismException NotFound(string message) => new("NOT_FOUND", message, 404);
    private static OismException InvalidState(string message) => new("SUPPLIER_INVOICE_INVALID_STATE", message, 409);
    private static OismException ReceiptMismatch(string message) => new("SUPPLIER_INVOICE_RECEIPT_MISMATCH", message, 422);
    private static OismException AlreadyExists() =>
        new("SUPPLIER_INVOICE_ALREADY_EXISTS", "Phiếu nhập đã có hóa đơn còn hiệu lực.", 409);

    private sealed record ValidatedDraft(string Series, string Number, DateOnly Date, decimal VatAmount,
        decimal ShippingFee, decimal Subtotal, decimal TotalAmount, IReadOnlyList<SupplierInvoiceLineDto> Items);
}
