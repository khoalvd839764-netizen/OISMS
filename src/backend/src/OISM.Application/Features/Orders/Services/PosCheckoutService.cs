using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Catalog.Units.Services;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Pricing.Dtos;
using OISM.Application.Features.Pricing.Services;
using OISM.Application.Features.Realtime;
using OISM.Application.Features.Realtime.Dtos;
using OISM.Application.Features.SalesInvoices.Services;
using OISM.Domain.Common;
using OISM.Domain.Entities;
using static OISM.Application.Features.Orders.Services.OrderLinePlanning;
using static OISM.Domain.Common.OrderLimits;

namespace OISM.Application.Features.Orders.Services;

internal sealed class PosCheckoutService(
    IAppDbContext context,
    ICurrentTenantService currentTenantService,
    ISkuPriceResolver skuPriceResolver,
    ILogger<OrderService>? logger = null,
    IRealtimeNotifier? realtimeNotifier = null) : IPosCheckoutService
{
    private readonly IAppDbContext _context = context;
    private readonly ICurrentTenantService _currentTenantService = currentTenantService;
    private readonly ISkuPriceResolver _skuPriceResolver = skuPriceResolver;
    private readonly ILogger<OrderService> _logger = logger ?? NullLogger<OrderService>.Instance;
    private readonly IRealtimeNotifier? _realtimeNotifier = realtimeNotifier;

    /// <summary>
    /// Thanh toán nhanh nguyên tử toàn giỏ hàng tại quầy POS (POST /api/pos/checkout và alias POST /api/orders/checkout).
    /// </summary>
    public async Task<PosCheckoutResponseDto> CheckoutPosAsync(PosCheckoutRequestDto dto, Guid? cashierUserId, CancellationToken ct = default)
    {
        if (dto == null)
        {
            throw new ArgumentNullException(nameof(dto));
        }

        var tenantId = _currentTenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Không xác định được TenantId hợp lệ từ ngữ cảnh xác thực.");
        }

        // 1. Validation cơ bản của DTO
        if (dto.BranchId == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "Chi nhánh không được để trống (Guid.Empty).", 400);
        }

        if (dto.RequestKey == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "RequestKey (Idempotency Key) không được để trống (Guid.Empty).", 400);
        }

        if (dto.Items == null || dto.Items.Count == 0)
        {
            throw new OismException("VALIDATION_ERROR", "Giỏ hàng phải có ít nhất 1 sản phẩm.", 400);
        }

        if (dto.Items.Any(i => i == null))
        {
            throw new OismException("VALIDATION_ERROR", "Dòng sản phẩm trong giỏ hàng không được mang giá trị null.", 400);
        }

        if (dto.Items.Any(i => i.SkuId == Guid.Empty))
        {
            throw new OismException("VALIDATION_ERROR", "Mã biến thể SKU trong giỏ hàng không được để trống.", 400);
        }

        // F-02: Kiểm tra precision/scale và magnitude theo schema limits:
        // - Quantity: numeric(18,3) -> max 999,999,999,999,999.999m (15 chữ số nguyên, 3 số thập phân)
        // - UnitPrice: numeric(20,2) -> max 999,999,999,999,999,999.99m (18 chữ số nguyên, 2 số thập phân)
        // - AmountGiven: numeric(20,2) -> max 999,999,999,999,999,999.99m (18 chữ số nguyên, 2 số thập phân)
        foreach (var item in dto.Items)
        {
            if (item.Quantity <= 0)
            {
                throw new OismException("VALIDATION_ERROR", "Số lượng mặt hàng mua phải lớn hơn 0.", 400);
            }

            if (item.Quantity > MaxQuantityAmount)
            {
                throw new OismException("VALIDATION_ERROR", $"Số lượng mặt hàng SKU '{item.SkuCode}' vượt quá giới hạn tối đa cho phép của hệ thống.", 400);
            }

            if (decimal.Round(item.Quantity, 3) != item.Quantity)
            {
                throw new OismException("VALIDATION_ERROR", $"Số lượng mặt hàng SKU '{item.SkuCode}' không được vượt quá 3 chữ số thập phân.", 400);
            }

            if (item.UnitPrice < 0)
            {
                throw new OismException("VALIDATION_ERROR", $"Đơn giá của SKU '{item.SkuCode}' không được âm.", 400);
            }

            if (item.UnitPrice > MaxMoneyAmount)
            {
                throw new OismException("VALIDATION_ERROR", $"Đơn giá của SKU '{item.SkuCode}' vượt quá giới hạn tối đa cho phép của hệ thống.", 400);
            }

            if (decimal.Round(item.UnitPrice, 2) != item.UnitPrice)
            {
                throw new OismException("VALIDATION_ERROR", $"Đơn giá của SKU '{item.SkuCode}' không được vượt quá 2 chữ số thập phân.", 400);
            }
        }

        if (dto.AmountGiven.HasValue)
        {
            if (dto.AmountGiven.Value < 0)
            {
                throw new OismException("VALIDATION_ERROR", "Số tiền khách đưa không được âm.", 400);
            }

            if (dto.AmountGiven.Value > MaxMoneyAmount)
            {
                throw new OismException("VALIDATION_ERROR", "Số tiền khách đưa vượt quá giới hạn tối đa cho phép của hệ thống.", 400);
            }

            if (decimal.Round(dto.AmountGiven.Value, 2) != dto.AmountGiven.Value)
            {
                throw new OismException("VALIDATION_ERROR", "Số tiền khách đưa không được vượt quá 2 chữ số thập phân.", 400);
            }
        }

        // 2. Chuẩn hóa Phương thức thanh toán (Hỗ trợ Cash/CASH và BankTransfer/BANK_TRANSFER_QR)
        var rawMethod = dto.PaymentMethod?.Trim();
        string normalizedMethod;
        if (string.Equals(rawMethod, "Cash", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rawMethod, "CASH", StringComparison.OrdinalIgnoreCase))
        {
            normalizedMethod = "Cash";
        }
        else if (string.Equals(rawMethod, "BankTransfer", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(rawMethod, "BANK_TRANSFER_QR", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(rawMethod, "QR", StringComparison.OrdinalIgnoreCase))
        {
            normalizedMethod = "BankTransfer";
        }
        else
        {
            throw new OismException("INVALID_PAYMENT_METHOD", $"Phương thức thanh toán '{dto.PaymentMethod}' không được hỗ trợ. Chỉ chấp nhận 'Cash' hoặc 'BankTransfer'.", 400);
        }

        // 3. Sắp xếp danh sách SkuId tăng dần để triệt tiêu deadlocks khi khóa tuần tự
        var sortedSkuIds = dto.Items.Select(i => i.SkuId).Distinct().OrderBy(id => id).ToList();

        // 4. Bao bọc toàn bộ chuỗi xử lý trong ExecutionStrategy tương thích NpgsqlRetryingExecutionStrategy
        List<Func<Task>> pendingNotifications = new();
        var result = await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            pendingNotifications.Clear();
            _context.UnitOfWork.ResetTracking();

            // Mở Transaction CSDL duy nhất cho toàn bộ chuỗi xử lý
            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);
            try
            {
            // 5. Lấy PostgreSQL Advisory Locks theo thứ tự xác định: SKU Locks -> RequestKey Lock
            await _context.Locks.LockSkuStockAsync(tenantId.Value, dto.BranchId, sortedSkuIds, ct);

            await _context.Locks.LockRequestKeyAsync(tenantId.Value, dto.RequestKey, ct);

            // 6. Kiểm tra Idempotency dưới khóa
            var existingOrder = await _context.Orders
                .Include(o => o.Items)
                    .ThenInclude(i => i.SkuUnitPrice!)
                        .ThenInclude(p => p.PriceList)
                .Include(o => o.Payment)
                .FirstOrDefaultAsync(o => o.TenantId == tenantId.Value && o.RequestKey == dto.RequestKey, ct);

            if (existingOrder != null)
            {
                // Chỉ replay một checkout POS đã hoàn tất, payment thành công. Không nhận nhầm đơn draft/online/Admin.
                if (existingOrder.Channel != "POS" ||
                    existingOrder.Status != OrderStatus.Completed ||
                    existingOrder.Payment == null ||
                    existingOrder.Payment.Status != PaymentStatus.Succeeded)
                {
                    throw new IdempotencyPayloadMismatchException("RequestKey đã được sử dụng cho một đơn hàng không phải thanh toán POS hoàn tất.");
                }

                var priceListItems = existingOrder.Items
                    .Where(i => i.PriceSource == "PriceList" && i.SkuUnitPrice != null)
                    .ToList();
                if (priceListItems.Count > 0)
                {
                    var existingCode = priceListItems.First().SkuUnitPrice?.PriceList?.Code;
                    if (existingCode != null && !string.Equals(existingCode, "retail", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IdempotencyPayloadMismatchException("RequestKey đã được sử dụng với bảng giá khác.");
                    }
                }

                // So sánh BranchId
                if (existingOrder.BranchId != dto.BranchId)
                {
                    throw new IdempotencyPayloadMismatchException("RequestKey đã được sử dụng với Chi nhánh khác.");
                }

                // So sánh PaymentMethod
                if (!string.Equals(existingOrder.Payment.Method, normalizedMethod, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IdempotencyPayloadMismatchException("RequestKey đã được sử dụng với phương thức thanh toán khác.");
                }

                // So sánh PaymentAccountId (nếu cả 2 đều có giá trị, hoặc dto có truyền nhưng khác bản lưu)
                if (dto.PaymentAccountId.HasValue && existingOrder.Payment.PaymentAccountId.HasValue &&
                    dto.PaymentAccountId.Value != existingOrder.Payment.PaymentAccountId.Value)
                {
                    throw new IdempotencyPayloadMismatchException("RequestKey đã được sử dụng với tài khoản nhận khác.");
                }

                // So sánh AmountGiven (nếu là Cash)
                if (normalizedMethod == "Cash")
                {
                    if (!dto.AmountGiven.HasValue || !existingOrder.Payment.CashReceived.HasValue ||
                        Math.Round(dto.AmountGiven.Value, 2) != Math.Round(existingOrder.Payment.CashReceived.Value, 2))
                    {
                        throw new IdempotencyPayloadMismatchException("RequestKey đã được sử dụng với số tiền khách đưa khác.");
                    }
                }
                else
                {
                    if (dto.AmountGiven.HasValue && existingOrder.Payment.CashReceived.HasValue &&
                        Math.Round(dto.AmountGiven.Value, 2) != Math.Round(existingOrder.Payment.CashReceived.Value, 2))
                    {
                        throw new IdempotencyPayloadMismatchException("RequestKey đã được sử dụng với số tiền thanh toán khác.");
                    }
                }

                // So sánh thông tin khách hàng (chuẩn hóa chuỗi rỗng / null)
                var normDtoCustName = string.IsNullOrWhiteSpace(dto.CustomerName) ? null : dto.CustomerName.Trim();
                var normDtoCustPhone = string.IsNullOrWhiteSpace(dto.CustomerPhone) ? null : dto.CustomerPhone.Trim();
                var normOrderCustName = string.IsNullOrWhiteSpace(existingOrder.CustomerName) ? null : existingOrder.CustomerName.Trim();
                var normOrderCustPhone = string.IsNullOrWhiteSpace(existingOrder.CustomerPhone) ? null : existingOrder.CustomerPhone.Trim();

                if (!string.Equals(normOrderCustName, normDtoCustName, StringComparison.Ordinal))
                {
                    throw new IdempotencyPayloadMismatchException("RequestKey đã được sử dụng với thông tin Tên khách hàng khác.");
                }
                if (!string.Equals(normOrderCustPhone, normDtoCustPhone, StringComparison.Ordinal))
                {
                    throw new IdempotencyPayloadMismatchException("RequestKey đã được sử dụng với thông tin Số điện thoại khách hàng khác.");
                }

                // So sánh danh mục sản phẩm (nhóm theo SkuId, so sánh tổng số lượng và đơn giá từng SKU)
                foreach (var g in dto.Items.GroupBy(i => (i.SkuId, i.UnitId)))
                {
                    if (g.Select(x => Math.Round(x.UnitPrice, 2)).Distinct().Count() > 1)
                    {
                        throw new OismException("VALIDATION_ERROR", "Các dòng cùng SKU và đơn vị trong giỏ hàng phải có cùng đơn giá.", 400);
                    }
                }

                var replayBaseUnits = await LoadBaseUnitsAsync(_context, existingOrder.Items.Select(i => i.SkuId).Concat(dto.Items.Select(i => i.SkuId)), ct);

                var existingItemSummary = existingOrder.Items
                    .GroupBy(i => (i.SkuId, UnitId: NormalizeUnit(i.UnitId, i.SkuId, replayBaseUnits)))
                    .ToDictionary(g => g.Key, g => new
                    {
                        Quantity = g.Sum(x => x.Quantity),
                        UnitPrice = Math.Round(g.First().UnitPrice, 2)
                    });

                var requestItemSummary = dto.Items
                    .GroupBy(i => (i.SkuId, UnitId: NormalizeUnit(i.UnitId, i.SkuId, replayBaseUnits)))
                    .ToDictionary(g => g.Key, g => new
                    {
                        Quantity = g.Sum(x => x.Quantity),
                        UnitPrice = Math.Round(g.First().UnitPrice, 2)
                    });

                if (existingItemSummary.Count != requestItemSummary.Count)
                {
                    throw new IdempotencyPayloadMismatchException("RequestKey đã được sử dụng với danh mục SKU khác (số loại mặt hàng không khớp).");
                }

                foreach (var kv in existingItemSummary)
                {
                    if (!requestItemSummary.TryGetValue(kv.Key, out var reqData))
                    {
                        throw new IdempotencyPayloadMismatchException($"RequestKey đã được sử dụng với danh mục SKU khác (thiếu SKU '{kv.Key.SkuId}').");
                    }

                    if (reqData.Quantity != kv.Value.Quantity)
                    {
                        throw new IdempotencyPayloadMismatchException($"RequestKey đã được sử dụng với số lượng mua khác cho SKU '{kv.Key.SkuId}'.");
                    }

                    if (reqData.UnitPrice != kv.Value.UnitPrice)
                    {
                        throw new IdempotencyPayloadMismatchException($"RequestKey đã được sử dụng với đơn giá khác cho SKU '{kv.Key.SkuId}'.");
                    }
                }

                _logger.LogInformation("Idempotent replay cho RequestKey {RequestKey}. Trả kết quả đơn {OrderNumber}.", dto.RequestKey, existingOrder.Number);

                var existingInvoice = await _context.SalesInvoices.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.TenantId == tenantId.Value && s.OrderId == existingOrder.Id, ct);

                var branchInfo = await _context.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.TenantId == tenantId.Value && b.Id == existingOrder.BranchId, ct);
                var cashierInfo = cashierUserId.HasValue
                    ? await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TenantId == tenantId.Value && u.Id == cashierUserId.Value, ct)
                    : null;

                var replayReceipt = new PosReceiptDto
                {
                    OrderId = existingOrder.Id,
                    ReceiptNumber = existingInvoice?.Number ?? existingOrder.Number,
                    OrderNumber = existingOrder.Number,
                    BranchId = existingOrder.BranchId,
                    BranchName = existingInvoice != null ? existingInvoice.BranchNameSnapshot : (branchInfo?.Name ?? string.Empty),
                    BranchAddress = existingInvoice != null ? existingInvoice.BranchAddressSnapshot : branchInfo?.Address,
                    BranchPhone = branchInfo?.Phone,
                    CashierName = existingInvoice?.IssuedByNameSnapshot ?? cashierInfo?.FullName ?? "Thu ngân",
                    CustomerName = existingInvoice?.CustomerNameSnapshot ?? existingOrder.CustomerName,
                    CustomerPhone = existingInvoice?.CustomerPhoneSnapshot ?? existingOrder.CustomerPhone,
                    TotalAmount = existingInvoice?.TotalAmount ?? existingOrder.TotalAmount,
                    PaymentMethod = existingInvoice?.PaymentMethodSnapshot ?? existingOrder.Payment?.Method ?? normalizedMethod,
                    AmountGiven = existingOrder.Payment?.CashReceived,
                    ChangeDue = existingOrder.Payment?.CashReceived.HasValue == true
                        ? existingOrder.Payment.CashReceived.Value - existingOrder.TotalAmount
                        : null,
                    TransferReference = existingOrder.Payment?.TransferReference,
                    ReceiverBankBin = existingOrder.Payment?.ReceiverBankBin,
                    ReceiverAccountNumber = existingOrder.Payment?.ReceiverAccountNumber,
                    ReceiverAccountName = existingOrder.Payment?.ReceiverAccountName,
                    CreatedAt = existingInvoice?.IssuedAt ?? existingOrder.CreatedAt,
                    Items = existingOrder.Items.Select(i => new PosReceiptItemDto
                    {
                        SkuId = i.SkuId,
                        SkuCode = i.SkuCodeSnapshot,
                        ProductName = i.SkuNameSnapshot,
                        VariantName = null,
                        UnitId = i.UnitId,
                        UnitName = i.UnitNameSnapshot,
                        Quantity = i.Quantity,
                        UnitPrice = i.UnitPrice
                    }).ToList()
                };

                await tx.CommitAsync(ct);

                return new PosCheckoutResponseDto
                {
                    Success = true,
                    Message = "Thanh toán đơn hàng thành công (Idempotent replay).",
                    OrderId = existingOrder.Id,
                    OrderNumber = existingOrder.Number,
                    TotalAmount = existingOrder.TotalAmount,
                    AmountGiven = existingOrder.Payment?.CashReceived,
                    ChangeDue = existingOrder.Payment?.CashReceived.HasValue == true
                        ? existingOrder.Payment.CashReceived.Value - existingOrder.TotalAmount
                        : null,
                    PaymentMethod = existingOrder.Payment?.Method ?? normalizedMethod,
                    CheckoutAt = existingOrder.CompletedAt ?? existingOrder.CreatedAt,
                    Receipt = replayReceipt
                };
            }

            // 7. Xác thực Chi nhánh
            var branch = await _context.Branches.FirstOrDefaultAsync(b => b.TenantId == tenantId.Value && b.Id == dto.BranchId, ct);
            if (branch == null || !branch.IsActive)
            {
                throw new OismException("BRANCH_INACTIVE_OR_INVALID", $"Chi nhánh '{dto.BranchId}' không tồn tại hoặc đã ngừng hoạt động.", 404);
            }

            // 8. Tải và xác thực danh mục SKU trong Catalog
            var skus = await _context.ProductVariants
                .Include(v => v.Product)
                .Include(v => v.SkuUnits)
                    .ThenInclude(u => u.Unit)
                .Where(v => v.TenantId == tenantId.Value && sortedSkuIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, ct);

            if (skus.Count != sortedSkuIds.Count)
            {
                var missingSkuId = sortedSkuIds.First(id => !skus.ContainsKey(id));
                throw new OismException("SKU_NOT_FOUND", $"Mặt hàng biến thể SKU '{missingSkuId}' không tồn tại trong hệ thống.", 404);
            }

            foreach (var sku in skus.Values)
            {
                if (!sku.IsActive || sku.Product == null || !sku.Product.IsActive)
                {
                    throw new OismException("SKU_INACTIVE", $"Mặt hàng SKU '{sku.SKU}' hoặc sản phẩm liên quan đang ngừng hoạt động.", 422);
                }
            }

            // 9. Phân giải đơn vị, gom dòng theo (SKU, đơn vị) và xác thực giá bán theo giá niêm yết của đúng đơn vị đó
            var priceKeys = new HashSet<SkuUnitPriceKey>();
            foreach (var item in dto.Items)
            {
                var sku = skus[item.SkuId];
                var resolved = SkuUnitResolver.ResolveForSale(sku, sku.SkuUnits, item.UnitId);
                if (!resolved.UnitId.HasValue)
                {
                    throw new OismException("UNIT_PRICE_NOT_CONFIGURED", $"Đơn vị của mặt hàng '{sku.SKU}' chưa được cấu hình đơn vị gốc.", 422);
                }
                priceKeys.Add(new SkuUnitPriceKey(item.SkuId, resolved.UnitId.Value));
            }

            var now = DateTimeOffset.UtcNow;
            var resolvedPrices = await _skuPriceResolver.ResolveAsync("retail", priceKeys, now, ct);

            var orderLines = BuildOrderLines(
                dto.Items.Select(i => new OrderLineInput(i.SkuId, i.UnitId, i.Quantity, i.UnitPrice)),
                skus,
                trustRequestedPrice: false,
                resolvedPrices: resolvedPrices);
            var groupedItems = SumBaseQuantityBySku(orderLines);

            // 10. Tính toán tổng tiền đơn hàng phía Server (kiểm tra tràn số CLR và giới hạn numeric(20,2))
            decimal totalAmount = 0m;
            foreach (var line in orderLines)
            {
                var retailPrice = line.UnitPrice;
                var qty = line.Quantity;
                try
                {
                    checked
                    {
                        var lineTotal = qty * retailPrice;
                        if (lineTotal > MaxMoneyAmount)
                        {
                            throw new OismException("VALIDATION_ERROR", "Tổng tiền của dòng hàng vượt quá giới hạn tối đa cho phép của hệ thống.", 400);
                        }
                        totalAmount += lineTotal;
                        if (totalAmount > MaxMoneyAmount)
                        {
                            throw new OismException("VALIDATION_ERROR", "Tổng tiền đơn hàng vượt quá giới hạn tối đa cho phép của hệ thống.", 400);
                        }
                    }
                }
                catch (OverflowException)
                {
                    throw new OismException("VALIDATION_ERROR", "Tổng tiền đơn hàng vượt quá giới hạn tối đa cho phép của hệ thống.", 400);
                }
            }
            totalAmount = Math.Round(totalAmount, 2, MidpointRounding.AwayFromZero);
            if (totalAmount > MaxMoneyAmount)
            {
                throw new OismException("VALIDATION_ERROR", "Tổng tiền đơn hàng vượt quá giới hạn tối đa cho phép của hệ thống.", 400);
            }

            // 11. Xác thực tiền thanh toán
            decimal? cashReceived = null;
            decimal? changeDue = null;
            if (normalizedMethod == "Cash")
            {
                if (!dto.AmountGiven.HasValue || dto.AmountGiven.Value < totalAmount)
                {
                    throw new OismException("INSUFFICIENT_CASH_RECEIVED", $"Số tiền khách đưa ({(dto.AmountGiven.HasValue ? dto.AmountGiven.Value : 0):N0}đ) không đủ để thanh toán tổng tiền đơn ({totalAmount:N0}đ).", 400);
                }
                cashReceived = Math.Round(dto.AmountGiven.Value, 2, MidpointRounding.AwayFromZero);
                if (cashReceived.Value > MaxMoneyAmount)
                {
                    throw new OismException("VALIDATION_ERROR", "Số tiền khách đưa vượt quá giới hạn tối đa cho phép của hệ thống.", 400);
                }

                try
                {
                    checked
                    {
                        var rawChange = cashReceived.Value - totalAmount;
                        if (rawChange > MaxMoneyAmount)
                        {
                            throw new OismException("VALIDATION_ERROR", "Tiền thừa trả khách vượt quá giới hạn tối đa cho phép của hệ thống.", 400);
                        }
                        changeDue = Math.Round(rawChange, 2, MidpointRounding.AwayFromZero);
                    }
                }
                catch (OverflowException)
                {
                    throw new OismException("VALIDATION_ERROR", "Tiền thừa trả khách vượt quá giới hạn tối đa cho phép của hệ thống.", 400);
                }
            }

            // 12. Kiểm tra tồn kho tại Chi nhánh (InventoryBalances)
            var branchBalances = await _context.InventoryBalances
                .Where(b => b.TenantId == tenantId.Value && b.BranchId == dto.BranchId && sortedSkuIds.Contains(b.SkuId))
                .ToDictionaryAsync(b => b.SkuId, ct);

            foreach (var kv in groupedItems)
            {
                var skuId = kv.Key;
                var requestedQty = kv.Value;

                if (!branchBalances.TryGetValue(skuId, out var bBal))
                {
                    throw new InsufficientStockException(skus[skuId].SKU, 0, requestedQty);
                }

                var available = Math.Max(0, bBal.OnHand - bBal.Reserved);
                if (available < requestedQty)
                {
                    throw new InsufficientStockException(skus[skuId].SKU, available, requestedQty);
                }
            }

            // 13. Phân bổ kho theo chính sách đã chốt: Tự động phân bổ tồn khả dụng qua các kho hoạt động thuộc chi nhánh
            var activeWarehouses = await _context.Warehouses
                .Where(w => w.TenantId == tenantId.Value && w.BranchId == dto.BranchId && w.IsActive)
                .OrderBy(w => w.CreatedAt)
                .ToListAsync(ct);

            if (activeWarehouses.Count == 0)
            {
                throw new OismException("NO_ACTIVE_WAREHOUSE", $"Chi nhánh '{branch.Name}' không có kho hàng nào đang hoạt động để xuất hàng.", 409);
            }

            var warehouseIds = activeWarehouses.Select(w => w.Id).ToList();

            var whBalances = await _context.WarehouseInventoryBalances
                .Where(wb => wb.TenantId == tenantId.Value && wb.BranchId == dto.BranchId && warehouseIds.Contains(wb.WarehouseId) && sortedSkuIds.Contains(wb.SkuId))
                .ToListAsync(ct);

            var allocationPlan = new Dictionary<Guid, List<(Warehouse Warehouse, WarehouseInventoryBalance Balance, decimal Quantity)>>();

            foreach (var kv in groupedItems)
            {
                var skuId = kv.Key;
                var requestedQty = kv.Value;

                var skuWhBalances = whBalances.Where(wb => wb.SkuId == skuId).ToList();
                var skuWhAvailable = skuWhBalances.Sum(wb => Math.Max(0, wb.OnHand - wb.Reserved));

                if (skuWhAvailable < requestedQty)
                {
                    throw new InsufficientStockException(skus[skuId].SKU, skuWhAvailable, requestedQty);
                }

                var skuAllocations = new List<(Warehouse, WarehouseInventoryBalance, decimal)>();

                // Ưu tiên kho có sẵn đủ số lượng yêu cầu
                var singleWh = skuWhBalances.FirstOrDefault(wb => Math.Max(0, wb.OnHand - wb.Reserved) >= requestedQty);
                if (singleWh != null)
                {
                    var wh = activeWarehouses.First(w => w.Id == singleWh.WarehouseId);
                    skuAllocations.Add((wh, singleWh, requestedQty));
                }
                else
                {
                    // Phân bổ lần lượt qua các kho có tồn khả dụng
                    decimal remaining = requestedQty;
                    foreach (var wb in skuWhBalances.Where(wb => Math.Max(0, wb.OnHand - wb.Reserved) > 0).OrderByDescending(wb => wb.OnHand - wb.Reserved))
                    {
                        var avail = Math.Max(0, wb.OnHand - wb.Reserved);
                        var take = Math.Min(remaining, avail);
                        var wh = activeWarehouses.First(w => w.Id == wb.WarehouseId);
                        skuAllocations.Add((wh, wb, take));
                        remaining -= take;
                        if (remaining <= 0) break;
                    }

                    if (remaining > 0)
                    {
                        throw new InsufficientStockException(skus[skuId].SKU, skuWhAvailable, requestedQty);
                    }
                }

                allocationPlan[skuId] = skuAllocations;
            }

            // 14. Tạo đơn hàng và ghi sổ cái trong CSDL
            var orderId = Guid.NewGuid();
            var orderNumber = $"POS-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..20];

            // Snapshot TotalCogs
            decimal totalCogs = 0;
            foreach (var kv in groupedItems)
            {
                try
                {
                    checked
                    {
                        totalCogs += kv.Value * branchBalances[kv.Key].AverageCost;
                    }
                }
                catch (OverflowException)
                {
                    totalCogs = MaxMoneyAmount;
                }
            }
            if (totalCogs > MaxMoneyAmount)
            {
                totalCogs = MaxMoneyAmount;
            }

            var order = new Order
            {
                Id = orderId,
                TenantId = tenantId.Value,
                Number = orderNumber,
                BranchId = dto.BranchId,
                Channel = "POS",
                CustomerName = dto.CustomerName,
                CustomerPhone = dto.CustomerPhone,
                TotalAmount = totalAmount,
                RequestKey = dto.RequestKey,
                CreatedBy = cashierUserId,
                CreatedAt = now
            };
            order.CompleteAtCheckout(cashierUserId, now);
            _context.Orders.Add(order);

            var createdOrderItems = new List<OrderItem>();

            foreach (var line in orderLines)
            {
                var skuId = line.SkuId;
                var sku = skus[skuId];
                var bBal = branchBalances[skuId];

                var orderItem = new OrderItem
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId.Value,
                    OrderId = orderId,
                    SkuId = skuId,
                    SkuCodeSnapshot = sku.SKU,
                    SkuNameSnapshot = string.IsNullOrWhiteSpace(sku.VariantName) ? sku.Product?.Name ?? sku.SKU : $"{sku.Product?.Name ?? sku.SKU} - {sku.VariantName}",
                    UnitId = line.Unit.UnitId,
                    UnitNameSnapshot = line.Unit.UnitName,
                    FactorSnapshot = line.Unit.Factor,
                    Quantity = line.Quantity,
                    UnitPrice = Math.Round(line.UnitPrice, 2, MidpointRounding.AwayFromZero),
                    CostPrice = Math.Round(bBal.AverageCost, 6, MidpointRounding.AwayFromZero), // Snapshot WAC theo đơn vị gốc
                    SkuUnitPriceId = line.SkuUnitPriceId,
                    PriceSource = line.PriceSource,
                    CreatedAt = now
                };
                _context.OrderItems.Add(orderItem);
                createdOrderItems.Add(orderItem);

                var allocs = TakeAllocations(allocationPlan[skuId], line.BaseQuantity);
                foreach (var alloc in allocs)
                {
                    // 1. Trừ tồn vật lý kho
                    alloc.Balance.OnHand -= alloc.Quantity;
                    var nextSeq = bBal.LastSequence + 1;
                    bBal.LastSequence = nextSeq;
                    alloc.Balance.LastSequence = nextSeq;
                    alloc.Balance.UpdatedAt = now;

                    // 2. Tạo Reservation trạng thái Consumed
                    var reservation = new Reservation
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId.Value,
                        OrderId = orderId,
                        OrderItemId = orderItem.Id,
                        BranchId = dto.BranchId,
                        WarehouseId = alloc.Warehouse.Id,
                        SkuId = skuId,
                        Quantity = alloc.Quantity,
                        ExpiresAt = now.AddMinutes(15),
                        CreatedAt = now
                    };
                    reservation.Consume(now);
                    _context.Reservations.Add(reservation);

                    // 3. Ghi bút toán Sổ cái OUT (không gán ReferenceId vì đây là generated column)
                    var ledger = new InventoryTransaction
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId.Value,
                        BranchId = dto.BranchId,
                        WarehouseId = alloc.Warehouse.Id,
                        SkuId = skuId,
                        Type = "OUT",
                        Quantity = alloc.Quantity,
                        UnitCost = Math.Round(bBal.AverageCost, 6, MidpointRounding.AwayFromZero), // Snapshot WAC
                        BalanceAfter = bBal.OnHand - alloc.Quantity,
                        SequenceNo = nextSeq,
                        OrderItemId = orderItem.Id,
                        CreatedAt = now
                    };
                    _context.InventoryTransactions.Add(ledger);

                    // Cập nhật số dư lũy kế tại chi nhánh
                    bBal.OnHand -= alloc.Quantity;
                }

                bBal.UpdatedAt = now;
            }

            // 15. Xác định tài khoản nhận tiền (nếu là BankTransfer) và tạo bản ghi thanh toán Payment (Succeeded)
            PaymentAccount? selectedAccount = null;
            if (normalizedMethod == "BankTransfer")
            {
                if (dto.PaymentAccountId.HasValue)
                {
                    selectedAccount = await _context.PaymentAccounts
                        .FirstOrDefaultAsync(a => a.TenantId == tenantId.Value && a.Id == dto.PaymentAccountId.Value, ct);

                    if (selectedAccount == null || !selectedAccount.IsActive)
                    {
                        throw new OismException("PAYMENT_ACCOUNT_INACTIVE", "Tài khoản nhận tiền đã bị ngừng hoạt động hoặc không tồn tại.", 400);
                    }
                }
                else
                {
                    selectedAccount = await _context.PaymentAccounts
                        .FirstOrDefaultAsync(a => a.TenantId == tenantId.Value && a.IsActive && a.IsDefault, ct);

                    if (selectedAccount == null)
                    {
                        selectedAccount = await _context.PaymentAccounts
                            .FirstOrDefaultAsync(a => a.TenantId == tenantId.Value && a.IsActive, ct);
                    }
                }
            }

            var payment = new Payment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId.Value,
                OrderId = orderId,
                Method = normalizedMethod,
                Amount = totalAmount,
                CashReceived = cashReceived,
                TransferReference = normalizedMethod == "BankTransfer" ? (dto.TransferReference?.Trim()) : null,
                ReceiverBankBin = selectedAccount?.BankBin,
                ReceiverAccountNumber = selectedAccount?.AccountNumber,
                ReceiverAccountName = selectedAccount?.AccountName,
                PaymentAccountId = selectedAccount?.Id,
                CreatedAt = now
            };
            payment.Succeed(now);
            _context.Payments.Add(payment);

            var cashierUser = cashierUserId.HasValue
                ? await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TenantId == tenantId.Value && u.Id == cashierUserId.Value, ct)
                : null;

            var invoice = SalesInvoiceFactory.Create(order, branch, cashierUser, normalizedMethod, now);
            _context.SalesInvoices.Add(invoice);

            // 16. Lưu xuống DB
            await _context.SaveChangesAsync(ct);

            // 17. Chốt kiểm tra Deferred Constraints của PostgreSQL trước khi COMMIT
            await _context.Locks.SetConstraintsImmediateAsync(ct);

            await tx.CommitAsync(ct);


            if (_realtimeNotifier != null)
            {
                foreach (var kv in groupedItems)
                {
                    var skuId = kv.Key;
                    var bBal = branchBalances[skuId];
                    var sku = skus[skuId];
                    var prevAvail = bBal.OnHand + kv.Value - bBal.Reserved;
                    var newAvail = bBal.OnHand - bBal.Reserved;
                    var skuCodeCapture = sku.SKU;
                    var branchIdCapture = dto.BranchId;
                    var tidCapture = tenantId.Value;
                    var reorderPointCapture = bBal.ReorderPoint;

                    pendingNotifications.Add(() => _realtimeNotifier.NotifyStockChangedAsync(tidCapture, branchIdCapture, skuId, newAvail, ct));

                    if (prevAvail > reorderPointCapture && newAvail <= reorderPointCapture)
                    {
                        pendingNotifications.Add(() => _realtimeNotifier.NotifyLowStockAlertAsync(tidCapture, branchIdCapture, skuId, skuCodeCapture, newAvail, reorderPointCapture, ct));
                    }
                }
            }

            // 18. Xây dựng DTO phản hồi quầy POS
            var receipt = new PosReceiptDto
            {
                OrderId = order.Id,
                ReceiptNumber = invoice.Number,
                OrderNumber = order.Number,
                BranchId = branch.Id,
                BranchName = invoice.BranchNameSnapshot,
                BranchAddress = invoice.BranchAddressSnapshot,
                BranchPhone = branch.Phone,
                CashierName = invoice.IssuedByNameSnapshot ?? cashierUser?.FullName ?? "Thu ngân",
                CustomerName = invoice.CustomerNameSnapshot ?? dto.CustomerName,
                CustomerPhone = invoice.CustomerPhoneSnapshot ?? dto.CustomerPhone,
                TotalAmount = totalAmount,
                PaymentMethod = invoice.PaymentMethodSnapshot ?? normalizedMethod,
                AmountGiven = cashReceived,
                ChangeDue = changeDue,
                TransferReference = payment.TransferReference,
                ReceiverBankBin = payment.ReceiverBankBin,
                ReceiverBankName = selectedAccount?.BankName ?? selectedAccount?.BankShortName,
                ReceiverAccountNumber = payment.ReceiverAccountNumber,
                ReceiverAccountName = payment.ReceiverAccountName,
                CreatedAt = invoice.IssuedAt,
                Items = createdOrderItems.Select(i => new PosReceiptItemDto
                {
                    SkuId = i.SkuId,
                    SkuCode = i.SkuCodeSnapshot,
                    ProductName = i.SkuNameSnapshot,
                    VariantName = null,
                    UnitId = i.UnitId,
                    UnitName = i.UnitNameSnapshot,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice
                }).ToList()
            };

            return new PosCheckoutResponseDto
            {
                Success = true,
                Message = "Thanh toán đơn hàng thành công.",
                OrderId = order.Id,
                OrderNumber = order.Number,
                TotalAmount = totalAmount,
                AmountGiven = cashReceived,
                ChangeDue = changeDue,
                PaymentMethod = normalizedMethod,
                CheckoutAt = order.CompletedAt ?? order.CreatedAt,
                Receipt = receipt
            };
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Thanh toán POS thất bại cho RequestKey {RequestKey}. Đã rollback giao dịch.", dto.RequestKey);
            throw;
        }
        });

        if (pendingNotifications.Count > 0)
        {
            foreach (var notifyAction in pendingNotifications)
            {
                try
                {
                    await notifyAction();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi phát sự kiện Realtime sau commit POS checkout.");
                }
            }
        }

        return result;
    }

    // =========================================================================
    // Triển khai Vòng đời Đơn hàng Trực tuyến ngoài POS (Task 3: Online Order Lifecycle)
    // =========================================================================
}
