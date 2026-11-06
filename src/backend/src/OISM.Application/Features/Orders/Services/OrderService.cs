using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Catalog.Units.Services;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Pricing.Dtos;
using OISM.Application.Features.Pricing.Services;
using OISM.Application.Features.Realtime;
using OISM.Application.Features.Realtime.Dtos;
using OISM.Application.Features.SalesInvoices.Services;
using OISM.Domain.Entities;

namespace OISM.Application.Features.Orders.Services;

/// <summary>
/// Triển khai dịch vụ Đơn hàng đa kênh và Thanh toán nhanh tại quầy POS (Order & POS Core Engine).
/// Tuân thủ Clean Architecture, giao dịch nguyên tử, cơ chế khóa hàng tuần tự và idempotency.
/// </summary>
public class OrderService : IOrderService, IPosCheckoutService
{
    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _currentTenantService;
    private readonly ISkuPriceResolver _skuPriceResolver;
    private readonly ILogger<OrderService> _logger;

    public const decimal MaxMoneyAmount = 999_999_999_999_999_999.99m; // numeric(20,2) limit (18 integer digits, 2 decimals)
    public const decimal MaxQuantityAmount = 999_999_999_999_999.999m; // numeric(18,3) limit (15 integer digits, 3 decimals)

    private readonly IRealtimeNotifier? _realtimeNotifier;

    public OrderService(
        IAppDbContext context,
        ICurrentTenantService currentTenantService,
        ISkuPriceResolver skuPriceResolver,
        ILogger<OrderService>? logger = null,
        IRealtimeNotifier? realtimeNotifier = null)
    {
        _context = context;
        _currentTenantService = currentTenantService;
        _skuPriceResolver = skuPriceResolver;
        _logger = logger ?? NullLogger<OrderService>.Instance;
        _realtimeNotifier = realtimeNotifier;
    }

    public OrderService(
        IAppDbContext context,
        ICurrentTenantService currentTenantService,
        ILogger<OrderService>? logger = null,
        IRealtimeNotifier? realtimeNotifier = null)
        : this(context, currentTenantService, new SkuPriceResolver(context), logger, realtimeNotifier)
    {
    }

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
                    existingOrder.Status != "Completed" ||
                    existingOrder.Payment == null ||
                    existingOrder.Payment.Status != "Succeeded")
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

                var replayBaseUnits = await LoadBaseUnitsAsync(existingOrder.Items.Select(i => i.SkuId).Concat(dto.Items.Select(i => i.SkuId)), ct);

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
                Status = "Completed",
                CustomerName = dto.CustomerName,
                CustomerPhone = dto.CustomerPhone,
                TotalAmount = totalAmount,
                RequestKey = dto.RequestKey,
                CreatedBy = cashierUserId,
                ConfirmedBy = cashierUserId,
                ConfirmedAt = now,
                CompletedAt = now,
                CreatedAt = now
            };
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
                        Status = "Consumed",
                        ExpiresAt = now.AddMinutes(15),
                        CreatedAt = now
                    };
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

            // 15. Tạo bản ghi thanh toán Payment (Succeeded)
            var payment = new Payment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId.Value,
                OrderId = orderId,
                Method = normalizedMethod,
                Status = "Succeeded",
                Amount = totalAmount,
                CashReceived = cashReceived,
                PaidAt = now,
                CreatedAt = now
            };
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

    public async Task<OrderResponseDto> CreateOrderAsync(CreateOrderRequestDto dto, Guid? createdByUserId, CancellationToken ct = default)
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

        var channel = string.IsNullOrWhiteSpace(dto.Channel) ? "Admin" : dto.Channel.Trim();
        if (!string.Equals(channel, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            throw new OismException("VALIDATION_ERROR", $"Endpoint chỉ hỗ trợ tạo đơn hàng kênh 'Admin'. Kênh '{dto.Channel}' không hợp lệ.", 400);
        }

        var coreReq = new InternalOrderCoreRequest(
            TenantId: tenantId.Value,
            BranchId: dto.BranchId,
            Channel: "Admin",
            ExternalShopId: null,
            ExternalOrderId: null,
            WebhookPayloadSignature: null,
            RequestKey: dto.RequestKey,
            CustomerName: dto.CustomerName,
            CustomerPhone: dto.CustomerPhone,
            ShippingAddress: dto.ShippingAddress,
            ExpectedTotalAmount: null,
            CreatedByUserId: createdByUserId,
            IsTrustedMarketplace: false,
            Items: dto.Items?.Select(i => new InternalOrderCoreItem(i.SkuId, i.SkuCode, i.Quantity, i.UnitPrice, i.UnitId)).ToList() ?? new(),
            PriceListCode: !string.IsNullOrWhiteSpace(dto.PriceListCode) ? dto.PriceListCode : "retail");

        return await CreateReservedOrderCoreAsync(coreReq, ct);
    }

    public async Task<OrderResponseDto> CreateMarketplaceReservedOrderAsync(InternalMarketplaceOrderContext context, CancellationToken ct = default)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var coreReq = new InternalOrderCoreRequest(
            TenantId: context.TenantId,
            BranchId: context.BranchId,
            Channel: context.Channel,
            ExternalShopId: context.ExternalShopId,
            ExternalOrderId: context.ExternalOrderId,
            WebhookPayloadSignature: context.WebhookPayloadSignature,
            RequestKey: context.RequestKey,
            CustomerName: context.CustomerName,
            CustomerPhone: context.CustomerPhone,
            ShippingAddress: context.ShippingAddress,
            ExpectedTotalAmount: context.ExpectedTotalAmount,
            CreatedByUserId: context.CreatedByUserId,
            IsTrustedMarketplace: true,
            Items: context.Items.Select(i => new InternalOrderCoreItem(null, i.SkuCode, i.Quantity, i.UnitPrice)).ToList(),
            PriceListCode: "retail");

        return await CreateReservedOrderCoreAsync(coreReq, ct);
    }

    private record InternalOrderCoreRequest(
        Guid TenantId,
        Guid BranchId,
        string Channel,
        string? ExternalShopId,
        string? ExternalOrderId,
        string? WebhookPayloadSignature,
        Guid RequestKey,
        string? CustomerName,
        string? CustomerPhone,
        string? ShippingAddress,
        decimal? ExpectedTotalAmount,
        Guid? CreatedByUserId,
        bool IsTrustedMarketplace,
        List<InternalOrderCoreItem> Items,
        string PriceListCode = "retail");

    private record InternalOrderCoreItem(
        Guid? SkuId,
        string? SkuCode,
        decimal Quantity,
        decimal UnitPrice,
        Guid? UnitId = null);

    private sealed record OrderLineInput(Guid SkuId, Guid? UnitId, decimal Quantity, decimal UnitPrice);

    private sealed class OrderLinePlan
    {
        public required Guid SkuId { get; init; }
        public required ResolvedSkuUnit Unit { get; init; }
        public required decimal UnitPrice { get; init; }
        public decimal Quantity { get; set; }
        public decimal BaseQuantity { get; set; }
        public Guid? SkuUnitPriceId { get; set; }
        public required string PriceSource { get; init; }
    }

    /// <summary>
    /// Phân giải đơn vị, gom các dòng cùng (SKU, đơn vị) và tính lượng gốc. Hệ số chỉ lấy từ cấu hình server, không nhận từ client.
    /// </summary>
    private static List<OrderLinePlan> BuildOrderLines(
        IEnumerable<OrderLineInput> items,
        IReadOnlyDictionary<Guid, ProductVariant> skus,
        bool trustRequestedPrice,
        IReadOnlyDictionary<SkuUnitPriceKey, ResolvedPrice>? resolvedPrices = null)
    {
        var itemsList = items.ToList();
        var resolvedUnits = new Dictionary<OrderLineInput, ResolvedSkuUnit>();

        foreach (var item in itemsList)
        {
            var sku = skus[item.SkuId];
            var resolved = SkuUnitResolver.ResolveForSale(sku, sku.SkuUnits, item.UnitId);
            if (!trustRequestedPrice && !resolved.UnitId.HasValue)
            {
                throw new OismException("UNIT_PRICE_NOT_CONFIGURED", $"Đơn vị của mặt hàng '{sku.SKU}' chưa được cấu hình đơn vị gốc.", 422);
            }
            resolvedUnits[item] = resolved;
        }

        if (!trustRequestedPrice)
        {
            var mismatches = new List<PriceMismatchItem>();

            foreach (var item in itemsList)
            {
                var sku = skus[item.SkuId];
                var resolved = resolvedUnits[item];
                var key = new SkuUnitPriceKey(item.SkuId, resolved.UnitId!.Value);

                if (resolvedPrices == null || !resolvedPrices.TryGetValue(key, out var price))
                {
                    throw new OismException("UNIT_PRICE_NOT_CONFIGURED", $"Đơn vị '{resolved.UnitName}' của mặt hàng '{sku.SKU}' chưa được cấu hình giá bán.", 422);
                }

                if (item.UnitPrice != price.Price)
                {
                    mismatches.Add(new PriceMismatchItem(item.SkuId, sku.SKU, resolved.UnitId, price.Price));
                }
            }

            if (mismatches.Count > 0)
            {
                var distinctMismatches = mismatches
                    .GroupBy(m => (m.SkuId, m.UnitId))
                    .Select(g => g.First())
                    .ToList();
                throw new PriceMismatchException("Đơn giá của mặt hàng trong giỏ hàng không khớp với giá niêm yết của hệ thống.", distinctMismatches);
            }
        }

        var lines = new List<OrderLinePlan>();
        foreach (var item in itemsList)
        {
            var sku = skus[item.SkuId];
            var resolved = resolvedUnits[item];

            Guid? skuUnitPriceId = null;
            string priceSource = "Marketplace";
            decimal finalUnitPrice = item.UnitPrice;

            if (!trustRequestedPrice)
            {
                var key = new SkuUnitPriceKey(item.SkuId, resolved.UnitId!.Value);
                var price = resolvedPrices![key];
                skuUnitPriceId = price.SkuUnitPriceId;
                priceSource = "PriceList";
                finalUnitPrice = price.Price;
            }

            var line = lines.FirstOrDefault(l => l.SkuId == item.SkuId && l.Unit.UnitId == resolved.UnitId);
            if (line == null)
            {
                lines.Add(new OrderLinePlan
                {
                    SkuId = item.SkuId,
                    Unit = resolved,
                    UnitPrice = finalUnitPrice,
                    Quantity = item.Quantity,
                    SkuUnitPriceId = skuUnitPriceId,
                    PriceSource = priceSource
                });
                continue;
            }

            line.Quantity += item.Quantity;
            if (line.Quantity > MaxQuantityAmount)
            {
                throw new OismException("VALIDATION_ERROR", $"Tổng số lượng mặt hàng SKU '{sku.SKU}' vượt quá giới hạn tối đa cho phép của hệ thống.", 400);
            }
        }

        foreach (var line in lines)
        {
            line.BaseQuantity = SkuUnitResolver.ComputeBaseQuantity(line.Quantity, line.Unit);
        }

        return lines;
    }

    private static Dictionary<Guid, decimal> SumBaseQuantityBySku(IEnumerable<OrderLinePlan> lines)
    {
        var totals = new Dictionary<Guid, decimal>();
        foreach (var line in lines)
        {
            var total = totals.GetValueOrDefault(line.SkuId) + line.BaseQuantity;
            if (total > MaxQuantityAmount)
            {
                throw new OismException("VALIDATION_ERROR", $"Tổng số lượng mặt hàng SKU '{line.SkuId}' vượt quá giới hạn tối đa cho phép của hệ thống.", 400);
            }
            totals[line.SkuId] = total;
        }
        return totals;
    }

    /// <summary>
    /// Chia phân bổ kho của một SKU cho từng dòng đơn; phần đã lấy được trừ khỏi danh sách còn lại.
    /// </summary>
    private static List<(Warehouse Warehouse, WarehouseInventoryBalance Balance, decimal Quantity)> TakeAllocations(
        List<(Warehouse Warehouse, WarehouseInventoryBalance Balance, decimal Quantity)> remaining,
        decimal quantity)
    {
        var taken = new List<(Warehouse Warehouse, WarehouseInventoryBalance Balance, decimal Quantity)>();
        for (var i = 0; i < remaining.Count && quantity > 0; i++)
        {
            var entry = remaining[i];
            var take = Math.Min(entry.Quantity, quantity);
            if (take <= 0) continue;

            taken.Add((entry.Warehouse, entry.Balance, take));
            remaining[i] = (entry.Warehouse, entry.Balance, entry.Quantity - take);
            quantity -= take;
        }
        return taken;
    }

    private static Guid? NormalizeUnit(Guid? unitId, Guid skuId, IReadOnlyDictionary<Guid, Guid?> baseUnits)
    {
        return !unitId.HasValue || (baseUnits.TryGetValue(skuId, out var baseUnitId) && unitId == baseUnitId) ? null : unitId;
    }

    private async Task<Dictionary<Guid, Guid?>> LoadBaseUnitsAsync(IEnumerable<Guid> skuIds, CancellationToken ct)
    {
        var ids = skuIds.Distinct().ToList();
        return await _context.ProductVariants
            .AsNoTracking()
            .Where(v => ids.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.BaseUnitId, ct);
    }

    private static void ValidateAdminOrderReplayMatch(Order existingOrder, InternalOrderCoreRequest req, IReadOnlyDictionary<Guid, Guid?> baseUnits)
    {
        // So sánh Kênh bán
        if (!string.Equals(existingOrder.Channel, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            throw new IdempotencyPayloadMismatchException("RequestKey đã được sử dụng cho một đơn hàng không thuộc kênh Admin.");
        }

        // So sánh Chi nhánh
        if (existingOrder.BranchId != req.BranchId)
        {
            throw new IdempotencyPayloadMismatchException("RequestKey đã được sử dụng với Chi nhánh khác.");
        }

        // So sánh thông tin khách hàng & địa chỉ giao nhận
        var normDtoCustName = string.IsNullOrWhiteSpace(req.CustomerName) ? null : req.CustomerName.Trim();
        var normDtoCustPhone = string.IsNullOrWhiteSpace(req.CustomerPhone) ? null : req.CustomerPhone.Trim();
        var normDtoAddress = string.IsNullOrWhiteSpace(req.ShippingAddress) ? null : req.ShippingAddress.Trim();

        var normOrderCustName = string.IsNullOrWhiteSpace(existingOrder.CustomerName) ? null : existingOrder.CustomerName.Trim();
        var normOrderCustPhone = string.IsNullOrWhiteSpace(existingOrder.CustomerPhone) ? null : existingOrder.CustomerPhone.Trim();
        var normOrderAddress = string.IsNullOrWhiteSpace(existingOrder.ShippingAddress) ? null : existingOrder.ShippingAddress.Trim();

        if (!string.Equals(normOrderCustName, normDtoCustName, StringComparison.Ordinal))
        {
            throw new IdempotencyPayloadMismatchException("RequestKey đã được sử dụng với thông tin Tên khách hàng khác.");
        }
        if (!string.Equals(normOrderCustPhone, normDtoCustPhone, StringComparison.Ordinal))
        {
            throw new IdempotencyPayloadMismatchException("RequestKey đã được sử dụng với thông tin Số điện thoại khách hàng khác.");
        }
        if (!string.Equals(normOrderAddress, normDtoAddress, StringComparison.Ordinal))
        {
            throw new IdempotencyPayloadMismatchException("RequestKey đã được sử dụng với thông tin Địa chỉ giao hàng khác.");
        }

        // So sánh danh mục SKU
        var existingItemSummary = existingOrder.Items
            .GroupBy(i => (i.SkuId, UnitId: NormalizeUnit(i.UnitId, i.SkuId, baseUnits)))
            .ToDictionary(g => g.Key, g => new
            {
                Quantity = g.Sum(x => x.Quantity),
                UnitPrice = Math.Round(g.First().UnitPrice, 2)
            });

        var requestItemSummary = req.Items
            .GroupBy(i => (SkuId: i.SkuId!.Value, UnitId: NormalizeUnit(i.UnitId, i.SkuId!.Value, baseUnits)))
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

        // So sánh Bảng giá
        var priceListItems = existingOrder.Items
            .Where(i => i.PriceSource == "PriceList" && i.SkuUnitPrice != null)
            .ToList();
        if (priceListItems.Count > 0)
        {
            var existingPriceListCode = priceListItems.First().SkuUnitPrice?.PriceList?.Code;
            var reqPriceListCode = !string.IsNullOrWhiteSpace(req.PriceListCode) ? req.PriceListCode : "retail";
            if (existingPriceListCode != null && !string.Equals(existingPriceListCode, reqPriceListCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new IdempotencyPayloadMismatchException("RequestKey đã được sử dụng với bảng giá khác.");
            }
        }
    }

    private async Task<OrderResponseDto> CreateReservedOrderCoreAsync(InternalOrderCoreRequest req, CancellationToken ct)
    {
        var tenantId = req.TenantId;

        // 1. Validation
        if (req.BranchId == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "Chi nhánh không được để trống (Guid.Empty).", 400);
        }

        if (req.RequestKey == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "RequestKey (Idempotency Key) không được để trống (Guid.Empty).", 400);
        }

        if (req.Items == null || req.Items.Count == 0)
        {
            throw new OismException("VALIDATION_ERROR", "Đơn hàng phải có ít nhất 1 sản phẩm.", 400);
        }

        if (req.Items.Any(i => i == null))
        {
            throw new OismException("VALIDATION_ERROR", "Dòng sản phẩm trong đơn hàng không được mang giá trị null.", 400);
        }

        if (!req.IsTrustedMarketplace && req.Items.Any(i => !i.SkuId.HasValue || i.SkuId.Value == Guid.Empty))
        {
            throw new OismException("VALIDATION_ERROR", "Mã biến thể SKU trong đơn hàng không được để trống.", 400);
        }

        if (req.IsTrustedMarketplace && req.Items.Any(i => string.IsNullOrWhiteSpace(i.SkuCode)))
        {
            throw new OismException("VALIDATION_ERROR", "Mã SKU mặt hàng trong đơn không được để trống.", 400);
        }

        foreach (var item in req.Items)
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

        // Kiểm tra tính nhất quán đơn giá nếu có dòng trùng SKU
        if (!req.IsTrustedMarketplace)
        {
            foreach (var g in req.Items.GroupBy(i => (i.SkuId, i.UnitId)))
            {
                if (g.Select(x => Math.Round(x.UnitPrice, 2)).Distinct().Count() > 1)
                {
                    throw new OismException("VALIDATION_ERROR", "Các dòng cùng SKU trong đơn hàng phải có cùng đơn giá.", 400);
                }
            }
        }
        else
        {
            foreach (var g in req.Items.GroupBy(i => i.SkuCode!.Trim().ToUpperInvariant()))
            {
                if (g.Select(x => Math.Round(x.UnitPrice, 2)).Distinct().Count() > 1)
                {
                    throw new OismException("VALIDATION_ERROR", "Các dòng cùng SKU trong đơn hàng phải có cùng đơn giá.", 400);
                }
            }
        }

        // 2. Fast Replay Check TRƯỚC khi kiểm tra trạng thái Branch / SKU Active / Catalog RetailPrice
        if (!req.IsTrustedMarketplace)
        {
            var existingOrderByKey = await _context.Orders
                .Include(o => o.Items)
                    .ThenInclude(i => i.SkuUnitPrice!)
                        .ThenInclude(p => p.PriceList)
                .Include(o => o.Payment)
                .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.RequestKey == req.RequestKey, ct);

            if (existingOrderByKey != null)
            {
                ValidateAdminOrderReplayMatch(existingOrderByKey, req, await LoadBaseUnitsAsync(existingOrderByKey.Items.Select(i => i.SkuId).Concat(req.Items.Select(i => i.SkuId!.Value)), ct));
                _logger.LogInformation("Idempotent fast replay cho tạo đơn hàng trực tuyến với RequestKey {RequestKey}. Trả kết quả đơn {OrderNumber}.", req.RequestKey, existingOrderByKey.Number);
                var replayDto = await MapToOrderResponseDtoAsync(existingOrderByKey, ct);
                replayDto.IsReplay = true;
                return replayDto;
            }
        }
        else
        {
            var existingMarketplaceOrder = await _context.Orders
                .Include(o => o.Items)
                .Include(o => o.Payment)
                .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Channel == req.Channel && o.ExternalShopId == req.ExternalShopId && o.ExternalOrderId == req.ExternalOrderId, ct);

            if (existingMarketplaceOrder != null)
            {
                if (string.IsNullOrWhiteSpace(existingMarketplaceOrder.WebhookPayloadSignature))
                {
                    throw new OismException("WEBHOOK_PAYLOAD_MISMATCH", "Đơn hàng sàn lịch sử không có chữ ký để đối soát payload.", 409);
                }

                if (!string.Equals(existingMarketplaceOrder.WebhookPayloadSignature, req.WebhookPayloadSignature, StringComparison.Ordinal))
                {
                    throw new OismException("WEBHOOK_PAYLOAD_MISMATCH", "Payload không khớp với đơn hàng TMĐT đã tiếp nhận trước đó cùng mã định danh sàn.", 409);
                }

                var replayDto = await MapToOrderResponseDtoAsync(existingMarketplaceOrder, ct);
                replayDto.IsReplay = true;
                return replayDto;
            }
        }

        // 3. Xác thực Chi nhánh fulfillment (kiểm tra đối với đơn hàng MỚI)
        var branch = await _context.Branches
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == req.BranchId, ct);
        if (branch == null || !branch.IsActive)
        {
            throw new OismException("BRANCH_INACTIVE_OR_INVALID", $"Chi nhánh '{req.BranchId}' không tồn tại hoặc đã ngừng hoạt động.", 404);
        }

        // 4. Tra cứu SKU trong Catalog
        Dictionary<Guid, ProductVariant> skus;
        Dictionary<string, ProductVariant> skusByCode = new(StringComparer.OrdinalIgnoreCase);

        if (!req.IsTrustedMarketplace)
        {
            var requestedSkuIds = req.Items.Select(i => i.SkuId!.Value).Distinct().ToList();
            skus = await _context.ProductVariants
                .Include(v => v.Product)
                .Include(v => v.SkuUnits)
                    .ThenInclude(u => u.Unit)
                .Where(v => v.TenantId == tenantId && requestedSkuIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, ct);

            if (skus.Count != requestedSkuIds.Count)
            {
                var missingSkuId = requestedSkuIds.First(id => !skus.ContainsKey(id));
                throw new OismException("SKU_NOT_FOUND", $"Mặt hàng biến thể SKU '{missingSkuId}' không tồn tại trong hệ thống.", 404);
            }
        }
        else
        {
            var requestedSkuCodes = req.Items.Select(i => i.SkuCode!.Trim().ToUpperInvariant()).Distinct().ToList();
            var skuList = await _context.ProductVariants
                .Include(v => v.Product)
                .Include(v => v.SkuUnits)
                    .ThenInclude(u => u.Unit)
                .Where(v => v.TenantId == tenantId && requestedSkuCodes.Contains(v.SKU.ToUpper()))
                .ToListAsync(ct);

            skusByCode = skuList.ToDictionary(v => v.SKU.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase);

            if (skusByCode.Count != requestedSkuCodes.Count)
            {
                var missingCode = requestedSkuCodes.First(c => !skusByCode.ContainsKey(c));
                throw new OismException("SKU_NOT_FOUND", $"Mặt hàng biến thể SKU '{missingCode}' không tồn tại trong hệ thống.", 422);
            }

            skus = skusByCode.Values.ToDictionary(v => v.Id);
        }

        foreach (var sku in skus.Values)
        {
            if (!sku.IsActive || sku.Product == null || !sku.Product.IsActive)
            {
                throw new OismException("SKU_INACTIVE", $"Mặt hàng SKU '{sku.SKU}' hoặc sản phẩm liên quan đang ngừng hoạt động.", 422);
            }
        }

        var priceListCode = !string.IsNullOrWhiteSpace(req.PriceListCode) ? req.PriceListCode.Trim().ToLowerInvariant() : "retail";
        IReadOnlyDictionary<SkuUnitPriceKey, ResolvedPrice>? resolvedPrices = null;
        if (!req.IsTrustedMarketplace)
        {
            if (priceListCode != "retail" && priceListCode != "wholesale")
            {
                throw new PriceListNotFoundException(priceListCode);
            }

            var priceKeys = new HashSet<SkuUnitPriceKey>();
            foreach (var item in req.Items)
            {
                var skuId = item.SkuId!.Value;
                var sku = skus[skuId];
                var resolved = SkuUnitResolver.ResolveForSale(sku, sku.SkuUnits, item.UnitId);
                if (!resolved.UnitId.HasValue)
                {
                    throw new OismException("UNIT_PRICE_NOT_CONFIGURED", $"Đơn vị của mặt hàng '{sku.SKU}' chưa được cấu hình đơn vị gốc.", 422);
                }
                priceKeys.Add(new SkuUnitPriceKey(skuId, resolved.UnitId.Value));
            }

            resolvedPrices = await _skuPriceResolver.ResolveAsync(priceListCode, priceKeys, DateTimeOffset.UtcNow, ct);
        }

        // Phân giải đơn vị, gom dòng theo (SKU, đơn vị); Admin kiểm giá theo niêm yết của đúng đơn vị, sàn TMĐT giữ giá từ payload
        var orderLines = BuildOrderLines(
            req.Items.Select(i => new OrderLineInput(
                !req.IsTrustedMarketplace ? i.SkuId!.Value : skusByCode[i.SkuCode!.Trim().ToUpperInvariant()].Id,
                i.UnitId,
                i.Quantity,
                i.UnitPrice)),
            skus,
            trustRequestedPrice: req.IsTrustedMarketplace,
            resolvedPrices: resolvedPrices);
        var groupedItems = SumBaseQuantityBySku(orderLines);

        // Tính tổng tiền đơn hàng phía Server
        decimal totalAmount = 0m;
        foreach (var line in orderLines)
        {
            var unitPrice = line.UnitPrice;
            var qty = line.Quantity;
            try
            {
                checked
                {
                    var lineTotal = qty * unitPrice;
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

        if (req.IsTrustedMarketplace && req.ExpectedTotalAmount.HasValue)
        {
            if (Math.Round(totalAmount, 2) != Math.Round(req.ExpectedTotalAmount.Value, 2))
            {
                throw new OismException("TOTAL_AMOUNT_MISMATCH", $"Tổng tiền đơn hàng không khớp (Server tính: {totalAmount:N2}, Payload gửi: {req.ExpectedTotalAmount.Value:N2}).", 400);
            }
        }

        // 3. Sắp xếp danh sách SkuId tăng dần để triệt tiêu deadlocks khi khóa tuần tự
        var sortedSkuIds = skus.Values.Select(v => v.Id).Distinct().OrderBy(id => id).ToList();

        // 4. Bao bọc trong ExecutionStrategy
        List<Func<Task>> pendingNotifications = new();
        var executionResult = await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            pendingNotifications.Clear();
            _context.UnitOfWork.ResetTracking();

            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);
            try
            {
                // 5. Lấy PostgreSQL Advisory Locks theo thứ tự xác định: SKU Locks -> RequestKey / External Order Lock
                await _context.Locks.LockSkuStockAsync(tenantId, req.BranchId, sortedSkuIds, ct);

                if (!req.IsTrustedMarketplace)
                {
                    await _context.Locks.LockRequestKeyAsync(tenantId, req.RequestKey, ct);
                }
                else
                {
                    await _context.Locks.LockExternalOrderAsync(tenantId, req.Channel, req.ExternalShopId ?? string.Empty, req.ExternalOrderId ?? string.Empty, ct);
                }

                // 6. Kiểm tra Idempotency dưới khóa
                if (!req.IsTrustedMarketplace)
                {
                    var existingOrder = await _context.Orders
                        .Include(o => o.Items)
                            .ThenInclude(i => i.SkuUnitPrice!)
                                .ThenInclude(p => p.PriceList)
                        .Include(o => o.Payment)
                        .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.RequestKey == req.RequestKey, ct);

                    if (existingOrder != null)
                    {
                        ValidateAdminOrderReplayMatch(existingOrder, req, await LoadBaseUnitsAsync(existingOrder.Items.Select(i => i.SkuId).Concat(req.Items.Select(i => i.SkuId!.Value)), ct));
                        _logger.LogInformation("Idempotent replay dưới khóa cho tạo đơn hàng trực tuyến với RequestKey {RequestKey}. Trả kết quả đơn {OrderNumber}.", req.RequestKey, existingOrder.Number);
                        await tx.CommitAsync(ct);
                        var replayDto = await MapToOrderResponseDtoAsync(existingOrder, ct);
                        replayDto.IsReplay = true;
                        return replayDto;
                    }
                }
                else
                {
                    var existingOrder = await _context.Orders
                        .Include(o => o.Items)
                        .Include(o => o.Payment)
                        .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Channel == req.Channel && o.ExternalShopId == req.ExternalShopId && o.ExternalOrderId == req.ExternalOrderId, ct);

                    if (existingOrder != null)
                    {
                        if (string.IsNullOrWhiteSpace(existingOrder.WebhookPayloadSignature))
                        {
                            throw new OismException("WEBHOOK_PAYLOAD_MISMATCH", "Đơn hàng sàn lịch sử không có chữ ký để đối soát payload.", 409);
                        }

                        if (!string.Equals(existingOrder.WebhookPayloadSignature, req.WebhookPayloadSignature, StringComparison.Ordinal))
                        {
                            throw new OismException("WEBHOOK_PAYLOAD_MISMATCH", "Payload không khớp với đơn hàng TMĐT đã tiếp nhận trước đó cùng mã định danh sàn.", 409);
                        }

                        await tx.CommitAsync(ct);
                        var replayDto = await MapToOrderResponseDtoAsync(existingOrder, ct);
                        replayDto.IsReplay = true;
                        return replayDto;
                    }
                }

                // 6b. Đọc lại giá dưới khóa SKU: giá mới có thể đã có hiệu lực trong lúc đơn đợi khóa
                if (!req.IsTrustedMarketplace)
                {
                    var lockedPrices = await _skuPriceResolver.ResolveAsync(
                        priceListCode,
                        orderLines.Select(l => new SkuUnitPriceKey(l.SkuId, l.Unit.UnitId!.Value)).ToHashSet(),
                        DateTimeOffset.UtcNow,
                        ct);

                    var changedPrices = new List<PriceMismatchItem>();
                    foreach (var line in orderLines)
                    {
                        if (!lockedPrices.TryGetValue(new SkuUnitPriceKey(line.SkuId, line.Unit.UnitId!.Value), out var lockedPrice))
                        {
                            throw new OismException("UNIT_PRICE_NOT_CONFIGURED", $"Đơn vị '{line.Unit.UnitName}' của mặt hàng '{skus[line.SkuId].SKU}' chưa được cấu hình giá bán.", 422);
                        }

                        if (lockedPrice.Price != line.UnitPrice)
                        {
                            changedPrices.Add(new PriceMismatchItem(line.SkuId, skus[line.SkuId].SKU, line.Unit.UnitId, lockedPrice.Price));
                            continue;
                        }

                        line.SkuUnitPriceId = lockedPrice.SkuUnitPriceId;
                    }

                    if (changedPrices.Count > 0)
                    {
                        throw new PriceMismatchException("Đơn giá của mặt hàng trong giỏ hàng không khớp với giá niêm yết của hệ thống.", changedPrices);
                    }
                }

                // 7. Xác thực Chi nhánh
                var branch = await _context.Branches.FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == req.BranchId, ct);
                if (branch == null || !branch.IsActive)
                {
                    throw new OismException("BRANCH_INACTIVE_OR_INVALID", $"Chi nhánh '{req.BranchId}' không tồn tại hoặc đã ngừng hoạt động.", 404);
                }

                // 8. Kiểm tra tồn khả dụng tại Chi nhánh (InventoryBalances)
                var branchBalances = await _context.InventoryBalances
                    .Where(b => b.TenantId == tenantId && b.BranchId == req.BranchId && sortedSkuIds.Contains(b.SkuId))
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

                // 9. Phân bổ kho theo chính sách: Tự động phân bổ tồn khả dụng qua các kho hoạt động thuộc chi nhánh
                var activeWarehouses = await _context.Warehouses
                    .Where(w => w.TenantId == tenantId && w.BranchId == req.BranchId && w.IsActive)
                    .OrderBy(w => w.CreatedAt)
                    .ToListAsync(ct);

                if (activeWarehouses.Count == 0)
                {
                    throw new OismException("NO_ACTIVE_WAREHOUSE", $"Chi nhánh '{branch.Name}' không có kho hàng nào đang hoạt động để giữ chỗ hàng.", 409);
                }

                var warehouseIds = activeWarehouses.Select(w => w.Id).ToList();
                var whBalances = await _context.WarehouseInventoryBalances
                    .Where(wb => wb.TenantId == tenantId && wb.BranchId == req.BranchId && warehouseIds.Contains(wb.WarehouseId) && sortedSkuIds.Contains(wb.SkuId))
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

                // 10. Tạo Đơn hàng ở trạng thái Reserved
                var now = DateTimeOffset.UtcNow;
                var orderId = Guid.NewGuid();
                var prefix = req.Channel switch
                {
                    "Admin" => "ORD",
                    "Shopee" => "SHP",
                    "TikTok" => "TTK",
                    "Lazada" => "LZD",
                    _ => "ORD"
                };
                var orderNumber = $"{prefix}-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..20];

                var order = new Order
                {
                    Id = orderId,
                    TenantId = tenantId,
                    Number = orderNumber,
                    BranchId = req.BranchId,
                    Channel = req.Channel,
                    ExternalShopId = req.Channel == "Admin" || req.Channel == "POS" ? null : req.ExternalShopId,
                    ExternalOrderId = req.Channel == "Admin" || req.Channel == "POS" ? null : req.ExternalOrderId,
                    WebhookPayloadSignature = req.WebhookPayloadSignature,
                    Status = "Reserved",
                    CustomerName = req.CustomerName,
                    CustomerPhone = req.CustomerPhone,
                    ShippingAddress = req.ShippingAddress,
                    TotalAmount = totalAmount,
                    RequestKey = req.RequestKey,
                    CreatedBy = req.CreatedByUserId,
                    ConfirmedAt = null,
                    CompletedAt = null,
                    CancelledAt = null,
                    CreatedAt = now
                };
                _context.Orders.Add(order);

                var createdOrderItems = new List<OrderItem>();
                var createdReservations = new List<Reservation>();

                foreach (var line in orderLines)
                {
                    var skuId = line.SkuId;
                    var sku = skus[skuId];
                    var bBal = branchBalances[skuId];
                    var unitPrice = line.UnitPrice;

                    var orderItem = new OrderItem
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        OrderId = orderId,
                        SkuId = skuId,
                        SkuCodeSnapshot = sku.SKU,
                        SkuNameSnapshot = string.IsNullOrWhiteSpace(sku.VariantName) ? sku.Product?.Name ?? sku.SKU : $"{sku.Product?.Name ?? sku.SKU} - {sku.VariantName}",
                        UnitId = line.Unit.UnitId,
                        UnitNameSnapshot = line.Unit.UnitName,
                        FactorSnapshot = line.Unit.Factor,
                        Quantity = line.Quantity,
                        UnitPrice = Math.Round(unitPrice, 2, MidpointRounding.AwayFromZero),
                        CostPrice = null, // CostPrice chỉ được snapshot khi Confirmed/Completed
                        SkuUnitPriceId = line.SkuUnitPriceId,
                        PriceSource = line.PriceSource,
                        CreatedAt = now
                    };
                    _context.OrderItems.Add(orderItem);
                    createdOrderItems.Add(orderItem);

                    var allocs = TakeAllocations(allocationPlan[skuId], line.BaseQuantity);
                    foreach (var alloc in allocs)
                    {
                        // Tăng Reserved ở cấp kho và cấp chi nhánh; OnHand không đổi
                        alloc.Balance.Reserved += alloc.Quantity;
                        alloc.Balance.UpdatedAt = now;

                        bBal.Reserved += alloc.Quantity;
                        bBal.UpdatedAt = now;

                        var reservation = new Reservation
                        {
                            Id = Guid.NewGuid(),
                            TenantId = tenantId,
                            OrderId = orderId,
                            OrderItemId = orderItem.Id,
                            BranchId = req.BranchId,
                            WarehouseId = alloc.Warehouse.Id,
                            SkuId = skuId,
                            Quantity = alloc.Quantity,
                            Status = "Active",
                            ExpiresAt = now.AddMinutes(15),
                            CreatedAt = now
                        };
                        _context.Reservations.Add(reservation);
                        createdReservations.Add(reservation);
                    }
                }

                // Tuyệt đối không ghi Sổ cái (Ledger) và không tạo Payment tại bước tạo đơn Reserved
                await _context.SaveChangesAsync(ct);

                await _context.Locks.SetConstraintsImmediateAsync(ct);

                await tx.CommitAsync(ct);

                order.Items = createdOrderItems;
                order.Reservations = createdReservations;

                _logger.LogInformation("Tạo đơn hàng trực tuyến {OrderNumber} thành công ở trạng thái Reserved.", order.Number);

                // 11. Phát thông báo thời gian thực sau khi Transaction commit thành công
                // 11. Thu tháº­p thÃ´ng bÃ¡o thá»i gian thá»±c sau khi Transaction commit thÃ nh cÃ´ng
                if (_realtimeNotifier != null)
                {
                    var orderSummary = new OrderSummaryRealtimeDto
                    {
                        OrderId = order.Id,
                        OrderNumber = order.Number,
                        BranchId = order.BranchId,
                        BranchName = branch.Name,
                        Channel = order.Channel,
                        Status = order.Status,
                        TotalAmount = order.TotalAmount,
                        TotalItems = order.Items.Count,
                        CustomerName = order.CustomerName,
                        CreatedAt = order.CreatedAt
                    };

                    pendingNotifications.Add(() => _realtimeNotifier.NotifyNewOrderAsync(tenantId, orderSummary, ct));

                    foreach (var kv in groupedItems)
                    {
                        var skuId = kv.Key;
                        var bBal = branchBalances[skuId];
                        var sku = skus[skuId];
                        var prevAvail = bBal.OnHand - (bBal.Reserved - kv.Value);
                        var newAvail = bBal.OnHand - bBal.Reserved;

                        pendingNotifications.Add(() => _realtimeNotifier.NotifyStockChangedAsync(tenantId, req.BranchId, skuId, newAvail, ct));

                        if (prevAvail > bBal.ReorderPoint && newAvail <= bBal.ReorderPoint)
                        {
                            pendingNotifications.Add(() => _realtimeNotifier.NotifyLowStockAlertAsync(tenantId, req.BranchId, skuId, sku.SKU, newAvail, bBal.ReorderPoint, ct));
                        }
                    }
                }
                return await MapToOrderResponseDtoAsync(order, ct);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _logger.LogError(ex, "Tạo đơn hàng online thất bại cho RequestKey {RequestKey}. Đã rollback giao dịch.", req.RequestKey);
                throw;
            }
        });

        // 12. PhÃ¡t thÃ´ng bÃ¡o thá»i gian thá»±c sau khi Execution Strategy thÃ nh cÃ´ng toÃ n diá»‡n
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
                    _logger.LogError(ex, "Lá»—i khi phÃ¡t sá»± kiá»‡n Realtime sau commit táº¡o Ä‘Æ¡n thÃ nh cÃ´ng.");
                }
            }
        }

        return executionResult;
    }


    public async Task<OrderResponseDto> ConfirmOrderAsync(Guid orderId, ConfirmOrderRequestDto dto, Guid? confirmedByUserId, CancellationToken ct = default)
    {
        var tenantId = _currentTenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Không xác định được TenantId hợp lệ từ ngữ cảnh xác thực.");
        }

        if (orderId == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "Mã đơn hàng không được để trống (Guid.Empty).", 400);
        }

        return await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            _context.UnitOfWork.ResetTracking();

            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);
            try
            {
                // 1. Pre-read để lấy metadata cần thiết cho việc khóa
                var meta = await _context.Orders
                    .Where(o => o.TenantId == tenantId.Value && o.Id == orderId)
                    .Select(o => new
                    {
                        o.BranchId,
                        o.RequestKey,
                        SkuIds = o.Items.Select(i => i.SkuId).ToList()
                    })
                    .FirstOrDefaultAsync(ct);

                if (meta == null)
                {
                    throw new OismException("ORDER_NOT_FOUND", $"Không tìm thấy đơn hàng với mã '{orderId}'.", 404);
                }

                var sortedSkuIds = meta.SkuIds.Distinct().OrderBy(id => id).ToList();

                // 2. Lấy khóa theo thứ tự phân cấp: SKU Locks -> RequestKey Lock -> Order Lock
                await _context.Locks.LockSkuStockAsync(tenantId.Value, meta.BranchId, sortedSkuIds, ct);

                await _context.Locks.LockRequestKeyAsync(tenantId.Value, meta.RequestKey, ct);

                await _context.Locks.LockOrderAsync(tenantId.Value, orderId, ct);

                // 3. Đọc dữ liệu đơn hàng dưới khóa
                var order = await _context.Orders
                    .Include(o => o.Items)
                    .Include(o => o.Reservations)
                    .Include(o => o.Payment)
                    .FirstOrDefaultAsync(o => o.TenantId == tenantId.Value && o.Id == orderId, ct);

                if (order == null)
                {
                    throw new OismException("ORDER_NOT_FOUND", $"Không tìm thấy đơn hàng với mã '{orderId}'.", 404);
                }

                if (string.Equals(order.Channel, "POS", StringComparison.OrdinalIgnoreCase))
                {
                    throw new OismException("INVALID_STATE_TRANSITION", "Đơn hàng tại quầy POS không hỗ trợ xác nhận lại qua quy trình trực tuyến.", 409);
                }

                if (order.Status != "Reserved")
                {
                    throw new OismException("INVALID_STATE_TRANSITION", $"Không thể xác nhận đơn hàng ở trạng thái '{order.Status}'. Đơn hàng phải ở trạng thái 'Reserved'.", 409);
                }

                var now = DateTimeOffset.UtcNow;

                // 4. Kiểm tra toàn bộ phiếu giữ chỗ (Reservations)
                if (order.Reservations == null || order.Reservations.Count == 0)
                {
                    throw new OismException("RESERVATION_EXPIRED", "Đơn hàng không có phiếu giữ chỗ hợp lệ.", 409);
                }

                foreach (var res in order.Reservations)
                {
                    if (res.Status != "Active" || res.ExpiresAt <= now)
                    {
                        throw new OismException("RESERVATION_EXPIRED", "Phiếu giữ chỗ của đơn hàng đã hết hạn hoặc không còn hiệu lực. Vui lòng tạo lại đơn hàng mới.", 409);
                    }
                }

                // 4b. Kiểm tra tính toàn vẹn và mức độ phân bổ giữ chỗ cho từng dòng sản phẩm (T3-F03)
                var reservationsByItem = order.Reservations.GroupBy(r => r.OrderItemId).ToDictionary(g => g.Key, g => g.ToList());
                var orderItemIds = order.Items.Select(i => i.Id).ToHashSet();

                if (order.Reservations.Any(r => !orderItemIds.Contains(r.OrderItemId)))
                {
                    throw new OismException("INVALID_STATE_TRANSITION", "Tồn tại phiếu giữ chỗ không khớp với các dòng sản phẩm của đơn hàng.", 409);
                }

                foreach (var item in order.Items)
                {
                    if (!reservationsByItem.TryGetValue(item.Id, out var itemResList) || itemResList.Count == 0)
                    {
                        throw new OismException("INVALID_STATE_TRANSITION", $"Dòng đơn hàng SKU '{item.SkuCodeSnapshot}' chưa có phiếu giữ chỗ tồn kho.", 409);
                    }

                    foreach (var res in itemResList)
                    {
                        if (res.SkuId != item.SkuId || res.BranchId != order.BranchId)
                        {
                            throw new OismException("INVALID_STATE_TRANSITION", $"Phiếu giữ chỗ của SKU '{item.SkuCodeSnapshot}' không khớp thông tin SKU hoặc Chi nhánh của đơn hàng.", 409);
                        }
                    }

                    var totalAllocated = itemResList.Sum(r => r.Quantity);
                    if (totalAllocated != item.BaseQuantity)
                    {
                        throw new OismException("INVALID_STATE_TRANSITION", $"Số lượng giữ chỗ ({totalAllocated:G29}) không khớp với lượng gốc yêu cầu ({item.BaseQuantity:G29}) của dòng đơn hàng SKU '{item.SkuCodeSnapshot}'.", 409);
                    }
                }

                // 5. Tải số dư tồn kho chi nhánh và các kho liên quan
                var branchBalances = await _context.InventoryBalances
                    .Where(b => b.TenantId == tenantId.Value && b.BranchId == order.BranchId && sortedSkuIds.Contains(b.SkuId))
                    .ToDictionaryAsync(b => b.SkuId, ct);

                var resWarehouseIds = order.Reservations.Select(r => r.WarehouseId).Distinct().ToList();
                var whBalances = await _context.WarehouseInventoryBalances
                    .Where(wb => wb.TenantId == tenantId.Value && wb.BranchId == order.BranchId && resWarehouseIds.Contains(wb.WarehouseId) && sortedSkuIds.Contains(wb.SkuId))
                    .ToListAsync(ct);

                // 6. Snapshot WAC CostPrice vào các dòng đơn hàng (order_items) theo đúng precision numeric(20,6) (T3-F02)
                var snapshotWacBySku = new Dictionary<Guid, decimal>();
                foreach (var item in order.Items)
                {
                    if (!branchBalances.TryGetValue(item.SkuId, out var bBal))
                    {
                        throw new OismException("INSUFFICIENT_STOCK", $"Không tìm thấy số dư tồn kho chi nhánh cho SKU '{item.SkuCodeSnapshot}'.", 409);
                    }

                    var snapshotWac = Math.Round(bBal.AverageCost, 6, MidpointRounding.AwayFromZero);
                    item.CostPrice = snapshotWac;
                    snapshotWacBySku[item.SkuId] = snapshotWac;
                }

                // 7. Chuyển Reservation sang Consumed, trừ OnHand & Reserved, ghi Sổ cái OUT
                foreach (var res in order.Reservations)
                {
                    var whBal = whBalances.FirstOrDefault(wb => wb.WarehouseId == res.WarehouseId && wb.SkuId == res.SkuId);
                    if (whBal == null)
                    {
                        throw new OismException("INSUFFICIENT_STOCK", $"Không tìm thấy số dư tồn kho tại kho '{res.WarehouseId}' cho SKU '{res.SkuId}'.", 409);
                    }

                    var bBal = branchBalances[res.SkuId];

                    // Trừ tồn vật lý và giảm tồn giữ chỗ ở cả 2 cấp
                    whBal.OnHand -= res.Quantity;
                    whBal.Reserved -= res.Quantity;

                    var nextSeq = bBal.LastSequence + 1;
                    bBal.LastSequence = nextSeq;
                    whBal.LastSequence = nextSeq;

                    whBal.UpdatedAt = now;

                    bBal.OnHand -= res.Quantity;
                    bBal.Reserved -= res.Quantity;
                    bBal.UpdatedAt = now;

                    // Chuyển reservation sang Consumed
                    res.Status = "Consumed";

                    // Ghi bút toán Sổ cái OUT với đúng precision numeric(20,6) snapshot (T3-F02)
                    var unitCostSnapshot = snapshotWacBySku.TryGetValue(res.SkuId, out var wac)
                        ? wac
                        : Math.Round(bBal.AverageCost, 6, MidpointRounding.AwayFromZero);

                    var ledger = new InventoryTransaction
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId.Value,
                        BranchId = order.BranchId,
                        WarehouseId = res.WarehouseId,
                        SkuId = res.SkuId,
                        Type = "OUT",
                        Quantity = res.Quantity,
                        UnitCost = unitCostSnapshot,
                        BalanceAfter = bBal.OnHand,
                        SequenceNo = nextSeq,
                        OrderItemId = res.OrderItemId,
                        CreatedAt = now
                    };
                    _context.InventoryTransactions.Add(ledger);
                }

                // 8. Cập nhật trạng thái đơn hàng sang Confirmed
                order.Status = "Confirmed";
                order.ConfirmedAt = now;
                order.ConfirmedBy = confirmedByUserId;

                var branch = await _context.Branches.AsNoTracking()
                    .FirstOrDefaultAsync(b => b.TenantId == tenantId.Value && b.Id == order.BranchId, ct);
                var confirmedByUser = confirmedByUserId.HasValue
                    ? await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TenantId == tenantId.Value && u.Id == confirmedByUserId.Value, ct)
                    : null;
                var paymentMethod = order.Payment?.Method;

                var invoice = SalesInvoiceFactory.Create(order, branch, confirmedByUser, paymentMethod, now);
                _context.SalesInvoices.Add(invoice);

                await _context.SaveChangesAsync(ct);

                await _context.Locks.SetConstraintsImmediateAsync(ct);

                await tx.CommitAsync(ct);

                _logger.LogInformation("Xác nhận đơn hàng online {OrderNumber} thành công. Đã snapshot giá vốn và xuất kho.", order.Number);

                return await MapToOrderResponseDtoAsync(order, ct);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _logger.LogError(ex, "Xác nhận đơn hàng {OrderId} thất bại. Đã rollback giao dịch.", orderId);
                throw;
            }
        });
    }

    public async Task<OrderResponseDto> CompleteOrderAsync(Guid orderId, CompleteOrderRequestDto dto, Guid? completedByUserId, CancellationToken ct = default)
    {
        var tenantId = _currentTenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Không xác định được TenantId hợp lệ từ ngữ cảnh xác thực.");
        }

        if (orderId == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "Mã đơn hàng không được để trống (Guid.Empty).", 400);
        }

        return await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            _context.UnitOfWork.ResetTracking();

            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);
            try
            {
                // 1. Khóa theo OrderId
                await _context.Locks.LockOrderAsync(tenantId.Value, orderId, ct);

                // 2. Đọc đơn hàng dưới khóa
                var order = await _context.Orders
                    .Include(o => o.Items)
                    .Include(o => o.Payment)
                    .FirstOrDefaultAsync(o => o.TenantId == tenantId.Value && o.Id == orderId, ct);

                if (order == null)
                {
                    throw new OismException("ORDER_NOT_FOUND", $"Không tìm thấy đơn hàng với mã '{orderId}'.", 404);
                }

                if (string.Equals(order.Channel, "POS", StringComparison.OrdinalIgnoreCase))
                {
                    throw new OismException("INVALID_STATE_TRANSITION", "Đơn hàng tại quầy POS không hỗ trợ hoàn tất lại qua quy trình trực tuyến.", 409);
                }

                // Idempotent replay: Nếu đơn đã Completed, giữ nguyên CompletedAt và trả 200 OK
                if (order.Status == "Completed")
                {
                    _logger.LogInformation("Idempotent replay hoàn tất đơn hàng online {OrderNumber}.", order.Number);
                    await tx.CommitAsync(ct);
                    return await MapToOrderResponseDtoAsync(order, ct);
                }

                if (order.Status != "Confirmed")
                {
                    throw new OismException("INVALID_STATE_TRANSITION", $"Không thể hoàn tất đơn hàng ở trạng thái '{order.Status}'. Đơn hàng phải ở trạng thái 'Confirmed'.", 409);
                }

                var now = DateTimeOffset.UtcNow;
                order.Status = "Completed";
                order.CompletedAt = now;

                // Tuyệt đối không thay đổi tồn kho, không ghi ledger OUT lần thứ hai
                await _context.SaveChangesAsync(ct);

                await _context.Locks.SetConstraintsImmediateAsync(ct);

                await tx.CommitAsync(ct);

                _logger.LogInformation("Hoàn tất đơn hàng online {OrderNumber} thành công.", order.Number);
                return await MapToOrderResponseDtoAsync(order, ct);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _logger.LogError(ex, "Hoàn tất đơn hàng {OrderId} thất bại. Đã rollback giao dịch.", orderId);
                throw;
            }
        });
    }

    public async Task<OrderResponseDto> CancelOrderAsync(Guid orderId, CancelOrderRequestDto dto, Guid? cancelledByUserId, CancellationToken ct = default)
    {
        var tenantId = _currentTenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Không xác định được TenantId hợp lệ từ ngữ cảnh xác thực.");
        }

        if (orderId == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "Mã đơn hàng không được để trống (Guid.Empty).", 400);
        }

        if (dto == null || string.IsNullOrWhiteSpace(dto.Reason))
        {
            throw new OismException("VALIDATION_ERROR", "Lý do hủy đơn không được để trống.", 400);
        }

        List<Func<Task>> pendingNotifications = new();
        var result = await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            pendingNotifications.Clear();
            _context.UnitOfWork.ResetTracking();

            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);
            try
            {
                // 1. Pre-read để lấy metadata khóa
                var meta = await _context.Orders
                    .Where(o => o.TenantId == tenantId.Value && o.Id == orderId)
                    .Select(o => new
                    {
                        o.BranchId,
                        o.RequestKey,
                        ItemSkuIds = o.Items.Select(i => i.SkuId).ToList(),
                        ResSkuIds = o.Reservations.Select(r => r.SkuId).ToList()
                    })
                    .FirstOrDefaultAsync(ct);

                if (meta == null)
                {
                    throw new OismException("ORDER_NOT_FOUND", $"Không tìm thấy đơn hàng với mã '{orderId}'.", 404);
                }

                var sortedSkuIds = meta.ItemSkuIds.Concat(meta.ResSkuIds).Distinct().OrderBy(id => id).ToList();

                // 2. Khóa advisory locks: SKU Locks -> RequestKey Lock -> Order Lock
                await _context.Locks.LockSkuStockAsync(tenantId.Value, meta.BranchId, sortedSkuIds, ct);

                await _context.Locks.LockRequestKeyAsync(tenantId.Value, meta.RequestKey, ct);

                await _context.Locks.LockOrderAsync(tenantId.Value, orderId, ct);

                // 3. Đọc dữ liệu đơn hàng dưới khóa
                var order = await _context.Orders
                    .Include(o => o.Items)
                    .Include(o => o.Reservations)
                    .Include(o => o.Payment)
                    .FirstOrDefaultAsync(o => o.TenantId == tenantId.Value && o.Id == orderId, ct);

                if (order == null)
                {
                    throw new OismException("ORDER_NOT_FOUND", $"Không tìm thấy đơn hàng với mã '{orderId}'.", 404);
                }

                if (order.Status != "Draft" && order.Status != "Reserved")
                {
                    throw new OismException("INVALID_STATE_TRANSITION", $"Không thể hủy đơn hàng ở trạng thái '{order.Status}'. Chỉ cho phép hủy đơn ở trạng thái 'Draft' hoặc 'Reserved'.", 409);
                }

                if (string.Equals(order.Channel, "POS", StringComparison.OrdinalIgnoreCase))
                {
                    bool isLegacyUnpaidDraft = order.Status == "Draft" && (order.Payment == null || order.Payment.Status != "Succeeded") && order.ConfirmedAt == null;
                    if (!isLegacyUnpaidDraft)
                    {
                        throw new OismException("INVALID_STATE_TRANSITION", "Đơn hàng tại quầy POS đã thanh toán không hỗ trợ hủy qua quy trình trực tuyến.", 409);
                    }
                }

                var now = DateTimeOffset.UtcNow;

                // 4. Giải phóng toàn bộ phiếu giữ chỗ Active của đơn hàng (kể cả đã quá hạn TTL)
                if (order.Reservations != null && order.Reservations.Count > 0)
                {
                    var activeReservations = order.Reservations.Where(r => r.Status == "Active").ToList();
                    if (activeReservations.Count > 0)
                    {
                        var branchBalances = await _context.InventoryBalances
                            .Where(b => b.TenantId == tenantId.Value && b.BranchId == order.BranchId && sortedSkuIds.Contains(b.SkuId))
                            .ToDictionaryAsync(b => b.SkuId, ct);

                        var resWarehouseIds = activeReservations.Select(r => r.WarehouseId).Distinct().ToList();
                        var whBalances = await _context.WarehouseInventoryBalances
                            .Where(wb => wb.TenantId == tenantId.Value && wb.BranchId == order.BranchId && resWarehouseIds.Contains(wb.WarehouseId) && sortedSkuIds.Contains(wb.SkuId))
                            .ToListAsync(ct);

                        foreach (var res in activeReservations)
                        {
                            var whBal = whBalances.FirstOrDefault(wb => wb.WarehouseId == res.WarehouseId && wb.SkuId == res.SkuId);
                            if (whBal != null)
                            {
                                whBal.Reserved -= res.Quantity;
                                whBal.UpdatedAt = now;
                            }

                            if (branchBalances.TryGetValue(res.SkuId, out var bBal))
                            {
                                bBal.Reserved -= res.Quantity;
                                bBal.UpdatedAt = now;
                            }

                            res.Status = "Released";
                        }
                    }
                }

                // 5. Cập nhật trạng thái đơn hàng sang Cancelled
                order.Status = "Cancelled";
                order.CancelledAt = now;

                // Không thay đổi OnHand, không ghi ledger OUT
                await _context.SaveChangesAsync(ct);

                await _context.Locks.SetConstraintsImmediateAsync(ct);

                await tx.CommitAsync(ct);

                _logger.LogInformation("Hủy đơn hàng {OrderNumber} thành công. Đã giải phóng toàn bộ tồn giữ chỗ.", order.Number);

                
                if (_realtimeNotifier != null && order.Reservations != null)
                {
                    try
                    {
                        var releasedSkus = order.Reservations
                            .Where(r => r.Status == "Released")
                            .GroupBy(r => r.SkuId)
                            .ToList();

                        if (releasedSkus.Count > 0)
                        {
                            var branchBalances = await _context.InventoryBalances
                                .AsNoTracking()
                                .Where(b => b.TenantId == tenantId.Value && b.BranchId == order.BranchId && releasedSkus.Select(g => g.Key).Contains(b.SkuId))
                                .ToDictionaryAsync(b => b.SkuId, ct);

                            foreach (var g in releasedSkus)
                            {
                                if (branchBalances.TryGetValue(g.Key, out var bBal))
                                {
                                    var newAvail = bBal.OnHand - bBal.Reserved;
                                    var skuIdCapture = g.Key;
                                    var branchIdCapture = order.BranchId;
                                    var tidCapture = tenantId.Value;
                                    pendingNotifications.Add(() => _realtimeNotifier.NotifyStockChangedAsync(tidCapture, branchIdCapture, skuIdCapture, newAvail, ct));
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi khi chuẩn bị sự kiện Realtime sau commit hủy đơn {OrderNumber}.", order.Number);
                    }
                }

                return await MapToOrderResponseDtoAsync(order, ct);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _logger.LogError(ex, "Hủy đơn hàng {OrderId} thất bại. Đã rollback giao dịch.", orderId);
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
                    _logger.LogError(ex, "Lỗi khi phát sự kiện Realtime sau commit hủy đơn {OrderId}.", orderId);
                }
            }
        }

        return result;
    }

    private async Task<OrderResponseDto> MapToOrderResponseDtoAsync(Order order, CancellationToken ct)
    {
        var branchName = await _context.Branches
            .AsNoTracking()
            .Where(b => b.TenantId == order.TenantId && b.Id == order.BranchId)
            .Select(b => b.Name)
            .FirstOrDefaultAsync(ct);

        var invoiceInfo = await _context.SalesInvoices
            .AsNoTracking()
            .Where(s => s.TenantId == order.TenantId && s.OrderId == order.Id)
            .Select(s => new { s.Id, s.Number })
            .FirstOrDefaultAsync(ct);

        decimal? totalCogs = null;
        if (order.Items != null && order.Items.Count > 0 && order.Items.All(i => i.CostPrice.HasValue))
        {
            totalCogs = Math.Round(order.Items.Sum(i => i.BaseQuantity * i.CostPrice!.Value), 6, MidpointRounding.AwayFromZero);
            if (totalCogs > MaxMoneyAmount)
            {
                totalCogs = MaxMoneyAmount;
            }
        }

        return new OrderResponseDto
        {
            Id = order.Id,
            Number = order.Number,
            BranchId = order.BranchId,
            BranchName = branchName,
            Channel = order.Channel,
            ExternalShopId = order.ExternalShopId,
            ExternalOrderId = order.ExternalOrderId,
            Status = order.Status,
            CustomerName = order.CustomerName,
            CustomerPhone = order.CustomerPhone,
            ShippingAddress = order.ShippingAddress,
            TotalAmount = order.TotalAmount,
            TotalCogs = totalCogs,
            RequestKey = order.RequestKey,
            CreatedBy = order.CreatedBy,
            ConfirmedBy = order.ConfirmedBy,
            SalesInvoiceId = invoiceInfo?.Id,
            SalesInvoiceNumber = invoiceInfo?.Number,
            ConfirmedAt = order.ConfirmedAt,
            CompletedAt = order.CompletedAt,
            CancelledAt = order.CancelledAt,
            CreatedAt = order.CreatedAt,
            Items = order.Items?.Select(i => new OrderItemResponseDto
            {
                Id = i.Id,
                SkuId = i.SkuId,
                SkuCodeSnapshot = i.SkuCodeSnapshot,
                SkuNameSnapshot = i.SkuNameSnapshot,
                UnitId = i.UnitId,
                UnitName = i.UnitNameSnapshot,
                FactorSnapshot = i.FactorSnapshot,
                BaseQuantity = i.BaseQuantity,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                CostPrice = i.CostPrice
            }).ToList() ?? new List<OrderItemResponseDto>(),
            Payment = order.Payment != null ? new PaymentResponseDto
            {
                Id = order.Payment.Id,
                Method = order.Payment.Method,
                Status = order.Payment.Status,
                Amount = order.Payment.Amount,
                CashReceived = order.Payment.CashReceived,
                PaidAt = order.Payment.PaidAt
            } : null
        };
    }

    public async Task<OrderResponseDto?> GetOrderByIdAsync(Guid orderId, CancellationToken ct = default)
    {
        var tenantId = _currentTenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Yêu cầu xác thực gian hàng (TenantId hợp lệ).");
        }

        var order = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.Payment)
            .FirstOrDefaultAsync(o => o.TenantId == tenantId.Value && o.Id == orderId, ct);

        if (order == null)
        {
            return null;
        }

        return await MapToOrderResponseDtoAsync(order, ct);
    }

    public Task<PagedResultDto<OrderResponseDto>> GetOrdersAsync(OrderFilterRequestDto filter, CancellationToken ct = default)
    {
        throw new NotImplementedException("Chức năng tra cứu danh sách đơn hàng thuộc phạm vi phân công của Nhựt (BE2) sẽ hoàn thiện tại T02.");
    }

    public async Task<PagedResult<OrderSummaryDto>> GetOrdersAsync(OrderFilterDto filter, CancellationToken ct = default)
    {
        var tenantId = _currentTenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Yêu cầu xác thực gian hàng (TenantId hợp lệ).");
        }

        var query = _context.Orders
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var statusTrim = filter.Status.Trim();
            query = query.Where(o => o.Status == statusTrim);
        }

        if (!string.IsNullOrWhiteSpace(filter.Channel))
        {
            var channelTrim = filter.Channel.Trim();
            query = query.Where(o => o.Channel == channelTrim);
        }

        if (filter.BranchId.HasValue && filter.BranchId.Value != Guid.Empty)
        {
            query = query.Where(o => o.BranchId == filter.BranchId.Value);
        }

        if (filter.FromDate.HasValue)
        {
            query = query.Where(o => o.CreatedAt >= filter.FromDate.Value);
        }

        if (filter.ToDate.HasValue)
        {
            query = query.Where(o => o.CreatedAt <= filter.ToDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLower();
            query = query.Where(o =>
                o.Number.ToLower().Contains(search) ||
                (o.CustomerName != null && o.CustomerName.ToLower().Contains(search)) ||
                (o.CustomerPhone != null && o.CustomerPhone.Contains(search)) ||
                (o.ExternalOrderId != null && o.ExternalOrderId.ToLower().Contains(search)));
        }

        var totalCount = await query.CountAsync(ct);

        var pageIndex = Math.Max(1, filter.PageIndex);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new OrderSummaryDto
            {
                Id = o.Id,
                Number = o.Number,
                BranchId = o.BranchId,
                BranchName = o.Branch != null ? o.Branch.Name : null,
                Channel = o.Channel,
                ExternalShopId = o.ExternalShopId,
                ExternalOrderId = o.ExternalOrderId,
                Status = o.Status,
                CustomerName = o.CustomerName,
                CustomerPhone = o.CustomerPhone,
                TotalAmount = o.TotalAmount,
                ItemCount = o.Items.Count,
                CreatedAt = o.CreatedAt,
                ConfirmedAt = o.ConfirmedAt,
                CompletedAt = o.CompletedAt,
                CancelledAt = o.CancelledAt
            })
            .ToListAsync(ct);

        return new PagedResult<OrderSummaryDto>
        {
            Items = orders,
            PageIndex = pageIndex,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<OrderDetailDto?> GetOrderDetailByIdAsync(Guid id, CancellationToken ct = default)
    {
        var tenantId = _currentTenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Yêu cầu xác thực gian hàng (TenantId hợp lệ).");
        }

        var order = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Branch)
            .Include(o => o.Items)
                .ThenInclude(i => i.Sku)
                    .ThenInclude(s => s!.Product)
            .FirstOrDefaultAsync(o => o.Id == id && o.TenantId == tenantId.Value, ct);

        if (order == null)
        {
            return null;
        }

        var invoice = await _context.SalesInvoices
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId.Value && s.OrderId == order.Id)
            .Select(s => new { s.Id, s.Number })
            .FirstOrDefaultAsync(ct);

        var detail = MapToDetailDto(order);
        detail.ConfirmedBy = order.ConfirmedBy;
        detail.SalesInvoiceId = invoice?.Id;
        detail.SalesInvoiceNumber = invoice?.Number;
        return detail;
    }

    public async Task<OrderDetailDto> CreateOrderAsync(CreateOrderDto dto, Guid? userId, CancellationToken ct = default)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        if (dto.Items == null || dto.Items.Count == 0)
        {
            throw new ArgumentException("Đơn hàng phải chứa ít nhất một mặt hàng.");
        }

        var req = new CreateOrderRequestDto
        {
            BranchId = dto.BranchId,
            Channel = dto.Channel,
            CustomerName = dto.CustomerName,
            CustomerPhone = dto.CustomerPhone,
            ShippingAddress = dto.ShippingAddress,
            RequestKey = (dto.RequestKey.HasValue && dto.RequestKey.Value != Guid.Empty) ? dto.RequestKey.Value : Guid.NewGuid(),
            Items = dto.Items.Select(i => new OrderItemRequestDto
            {
                SkuId = i.SkuId,
                UnitId = i.UnitId,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList()
        };

        var response = await CreateOrderAsync(req, userId, ct);
        var detail = await GetOrderDetailByIdAsync(response.Id, ct);
        return detail ?? throw new InvalidOperationException("Không thể tải lại chi tiết đơn hàng sau khi tạo.");
    }

    public async Task<OrderDetailDto> ConfirmOrderAsync(Guid id, Guid? userId, CancellationToken ct = default)
    {
        var response = await ConfirmOrderAsync(id, new ConfirmOrderRequestDto(), userId, ct);
        var detail = await GetOrderDetailByIdAsync(response.Id, ct);
        return detail ?? throw new InvalidOperationException("Không thể tải lại chi tiết đơn hàng sau khi xác nhận.");
    }

    private Guid RequireTenantId()
    {
        var tenantId = _currentTenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Yêu cầu xác thực gian hàng (TenantId hợp lệ).");
        }
        return tenantId.Value;
    }

    public async Task<OrderDetailDto> ProcessOrderAsync(Guid id, Guid? userId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var order = await _context.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id && o.TenantId == tenantId, ct);

        if (order == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy Đơn hàng với ID '{id}'.");
        }

        if (order.Status == "Cancelled" || order.Status == "Completed")
        {
            throw new OismException("INVALID_STATE_TRANSITION", $"Không thể xử lý đóng gói đối với đơn hàng ở trạng thái '{order.Status}'.", 409);
        }

        if (order.Status == "Confirmed")
        {
            var existing = await GetOrderDetailByIdAsync(id, ct);
            return existing ?? throw new InvalidOperationException("Không thể tải chi tiết đơn hàng.");
        }

        if (order.Status != "Reserved")
        {
            throw new OismException("INVALID_STATE_TRANSITION", $"Đơn hàng ở trạng thái '{order.Status}' không thể chuyển sang xử lý đóng gói. Yêu cầu trạng thái 'Reserved'.", 409);
        }

        try
        {
            var response = await ConfirmOrderAsync(id, new ConfirmOrderRequestDto(), userId, ct);
            var detail = await GetOrderDetailByIdAsync(response.Id, ct);
            return detail ?? throw new InvalidOperationException("Không thể tải lại chi tiết đơn hàng sau khi xử lý.");
        }
        catch (OismException ex) when (ex.ErrorCode == "INVALID_STATE_TRANSITION")
        {
            var reloaded = await _context.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id && o.TenantId == tenantId, ct);
            if (reloaded?.Status == "Confirmed")
            {
                var detail = await GetOrderDetailByIdAsync(id, ct);
                if (detail != null)
                {
                    return detail;
                }
            }
            throw;
        }
    }

    public async Task<OrderDetailDto> CompleteOrderAsync(Guid id, Guid? userId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == id && o.TenantId == tenantId, ct);

        if (order == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy Đơn hàng với ID '{id}'.");
        }

        var response = await CompleteOrderAsync(id, new CompleteOrderRequestDto(), userId, ct);
        var detail = await GetOrderDetailByIdAsync(response.Id, ct);
        return detail ?? throw new InvalidOperationException("Không thể tải lại chi tiết đơn hàng sau khi hoàn tất.");
    }

    public async Task<OrderDetailDto> CancelOrderAsync(Guid id, Guid? userId, CancellationToken ct = default)
    {
        var response = await CancelOrderAsync(id, new CancelOrderRequestDto { Reason = "Huỷ đơn theo yêu cầu" }, userId, ct);
        var detail = await GetOrderDetailByIdAsync(response.Id, ct);
        return detail ?? throw new InvalidOperationException("Không thể tải lại chi tiết đơn hàng sau khi hủy.");
    }

    private static OrderDetailDto MapToDetailDto(Order order, string? branchName = null)
    {
        return new OrderDetailDto
        {
            Id = order.Id,
            Number = order.Number,
            BranchId = order.BranchId,
            BranchName = branchName ?? order.Branch?.Name,
            Channel = order.Channel,
            ExternalShopId = order.ExternalShopId,
            ExternalOrderId = order.ExternalOrderId,
            Status = order.Status,
            CustomerName = order.CustomerName,
            CustomerPhone = order.CustomerPhone,
            ShippingAddress = order.ShippingAddress,
            TotalAmount = order.TotalAmount,
            RequestKey = order.RequestKey,
            ItemCount = order.Items?.Count ?? 0,
            CreatedAt = order.CreatedAt,
            ConfirmedBy = order.ConfirmedBy,
            ConfirmedAt = order.ConfirmedAt,
            CompletedAt = order.CompletedAt,
            CancelledAt = order.CancelledAt,
            Items = order.Items?.Select(i => new OrderItemDto
            {
                Id = i.Id,
                SkuId = i.SkuId,
                SkuCode = i.SkuCodeSnapshot ?? i.Sku?.SKU ?? string.Empty,
                SkuName = i.SkuNameSnapshot ?? i.Sku?.Product?.Name ?? string.Empty,
                UnitId = i.UnitId,
                UnitName = i.UnitNameSnapshot,
                FactorSnapshot = i.FactorSnapshot,
                BaseQuantity = i.BaseQuantity,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                CostPrice = i.CostPrice
            }).ToList() ?? new List<OrderItemDto>()
        };
    }

    private static readonly ConcurrentDictionary<Guid, (DateTimeOffset LastCreatedAt, Guid LastOrderId)> _tenantCursors = new();
    private static BatchExecutionStats _lastExecutionStats = new();
    private BatchExecutionStats _instanceStats = new();

    /// <summary>
    /// Reset toàn bộ cursor của các tenant (dùng cho test suite hoặc khi re-initialize).
    /// </summary>
    public static void ResetCursors()
    {
        _tenantCursors.Clear();
        _lastExecutionStats = new BatchExecutionStats();
    }

    /// <summary>
    /// Thống kê chi tiết kết quả thực thi lượt quét quá hạn gần nhất trên instance hiện tại (scoped theo tenant/invocation).
    /// Giúp Hangfire job phân biệt rõ: thành công trọn vẹn, một phần, no-op hợp lệ, hay thất bại toàn bộ mà không bị race conditions giữa các tenant.
    /// </summary>
    public BatchExecutionStats InstanceExecutionStats => _instanceStats;

    BatchExecutionStats IOrderService.LastExecutionStats => _instanceStats;

    /// <summary>
    /// Thống kê toàn cục tĩnh hỗ trợ tương thích ngược cho test runner/logging.
    /// </summary>
    public static BatchExecutionStats LastExecutionStats => _lastExecutionStats;

    public static void ResetTenantCursor(Guid tenantId)
    {
        _tenantCursors.TryRemove(tenantId, out _);
    }

    /// <summary>
    /// Tác vụ nền quét và tự động hủy các đơn hàng giữ chỗ quá hạn TTL (15 phút) của Tenant hiện tại.
    /// Giải phóng toàn bộ phiếu giữ chỗ Active, cập nhật tồn kho hai cấp và phát thông báo StockLevelChanged.
    /// </summary>
    /// <param name="cutoffUtc">Mốc thời gian so sánh (mặc định DateTimeOffset.UtcNow nếu null).</param>
    /// <param name="ct">CancellationToken.</param>
    /// <returns>Số lượng đơn hàng mới đã được tự động hủy thành công và commit vào CSDL.</returns>
    public async Task<int> ExpireOverdueReservationsAsync(DateTimeOffset? cutoffUtc = null, CancellationToken ct = default)
    {
        var tenantId = _currentTenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Không xác định được TenantId hợp lệ từ ngữ cảnh thực thi.");
        }

        var effectiveCutoff = cutoffUtc ?? DateTimeOffset.UtcNow;
        int batchSize = 50;

        // 1. Quét danh sách Order candidate có phiếu giữ chỗ Active quá hạn (Bounded keyset cursor pagination)
        var baseQuery = _context.Orders
            .Where(o => o.TenantId == tenantId.Value
                && (o.Status == "Draft" || o.Status == "Reserved")
                && o.ConfirmedAt == null
                && o.CompletedAt == null
                && o.CancelledAt == null
                && (o.Payment == null || o.Payment.Status != "Succeeded")
                && o.Reservations.Any(r => r.Status == "Active" && r.ExpiresAt <= effectiveCutoff));

        List<(Guid Id, DateTimeOffset CreatedAt)> candidateOrders;

        if (_tenantCursors.TryGetValue(tenantId.Value, out var cursor))
        {
            var rawList = await baseQuery
                .Where(o => o.CreatedAt > cursor.LastCreatedAt || (o.CreatedAt == cursor.LastCreatedAt && o.Id > cursor.LastOrderId))
                .OrderBy(o => o.CreatedAt)
                .ThenBy(o => o.Id)
                .Select(o => new { o.Id, o.CreatedAt })
                .Take(batchSize)
                .ToListAsync(ct);

            candidateOrders = rawList.Select(x => (x.Id, x.CreatedAt)).ToList();

            if (candidateOrders.Count < batchSize)
            {
                // Quay vòng lại đầu tập dữ liệu (wrap-around) để lấy thêm cho đủ batchSize nếu còn đơn hợp lệ ở đầu
                var existingIds = candidateOrders.Select(c => c.Id).ToHashSet();
                var wrapRaw = await baseQuery
                    .OrderBy(o => o.CreatedAt)
                    .ThenBy(o => o.Id)
                    .Select(o => new { o.Id, o.CreatedAt })
                    .Take(batchSize)
                    .ToListAsync(ct);

                foreach (var w in wrapRaw)
                {
                    if (existingIds.Add(w.Id))
                    {
                        candidateOrders.Add((w.Id, w.CreatedAt));
                        if (candidateOrders.Count >= batchSize) break;
                    }
                }
            }
        }
        else
        {
            var rawList = await baseQuery
                .OrderBy(o => o.CreatedAt)
                .ThenBy(o => o.Id)
                .Select(o => new { o.Id, o.CreatedAt })
                .Take(batchSize)
                .ToListAsync(ct);

            candidateOrders = rawList.Select(x => (x.Id, x.CreatedAt)).ToList();
        }

        if (candidateOrders.Count > 0)
        {
            var last = candidateOrders.Last();
            _tenantCursors[tenantId.Value] = (last.CreatedAt, last.Id);
        }
        else
        {
            _tenantCursors.TryRemove(tenantId.Value, out _);
            _instanceStats = new BatchExecutionStats();
            _lastExecutionStats = _instanceStats;
            return 0;
        }

        int scannedCount = 0;
        int cancelledCount = 0;
        int skippedCount = 0;
        int failedCount = 0;
        List<Exception> errors = new();

        // 2. Xử lý từng đơn hàng độc lập; lỗi của một đơn không ngăn cản các đơn khác
        foreach (var candidate in candidateOrders)
        {
            ct.ThrowIfCancellationRequested();
            scannedCount++;
            try
            {
                var cancelled = await ExpireSingleOrderAsync(tenantId.Value, candidate.Id, effectiveCutoff, ct);
                if (cancelled)
                {
                    cancelledCount++;
                }
                else
                {
                    skippedCount++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failedCount++;
                errors.Add(ex);
                _logger.LogError(ex, "Lỗi khi tự động hủy đơn giữ chỗ quá hạn '{OrderId}' của Tenant '{TenantId}'. Bỏ qua đơn này và tiếp tục xử lý các đơn khác.", candidate.Id, tenantId.Value);
            }
        }

        _instanceStats = new BatchExecutionStats
        {
            Scanned = scannedCount,
            Cancelled = cancelledCount,
            Skipped = skippedCount,
            Failed = failedCount,
            Errors = errors
        };
        _lastExecutionStats = _instanceStats;
        return cancelledCount;
    }

    /// <summary>
    /// Xử lý tự động hủy 1 đơn hàng quá hạn giữ chỗ trong một Transaction PostgreSQL độc lập.
    /// Áp dụng đầy đủ thứ tự khóa chuẩn (SKU locks -> RequestKey lock -> Order lock -> Row locks FOR UPDATE).
    /// </summary>
    internal async Task<bool> ExpireSingleOrderAsync(Guid tenantId, Guid orderId, DateTimeOffset effectiveCutoff, CancellationToken ct)
    {
        List<Func<Task>> pendingNotifications = new();
        var result = await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            pendingNotifications.Clear();
            _context.UnitOfWork.ResetTracking();

            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);
            try
            {
                // 1. Pre-read để lấy metadata khóa
                var meta = await _context.Orders
                    .Where(o => o.TenantId == tenantId && o.Id == orderId)
                    .Select(o => new
                    {
                        o.BranchId,
                        o.RequestKey,
                        ItemSkuIds = o.Items.Select(i => i.SkuId).ToList(),
                        ResSkuIds = o.Reservations.Select(r => r.SkuId).ToList()
                    })
                    .FirstOrDefaultAsync(ct);

                if (meta == null)
                {
                    return false;
                }

                var sortedSkuIds = meta.ItemSkuIds.Concat(meta.ResSkuIds).Distinct().OrderBy(id => id).ToList();

                // 2. Khóa advisory locks theo thứ tự chuẩn: SKU Locks -> RequestKey Lock -> Order Lock
                await _context.Locks.LockSkuStockAsync(tenantId, meta.BranchId, sortedSkuIds, ct);

                await _context.Locks.LockRequestKeyAsync(tenantId, meta.RequestKey, ct);

                await _context.Locks.LockOrderAsync(tenantId, orderId, ct);

                // Khóa dòng FOR UPDATE: orders, reservations, inventory_balances
                await _context.Locks.LockOrderRowAsync(tenantId, orderId, ct);

                await _context.Locks.LockOrderReservationRowsAsync(tenantId, orderId, ct);

                await _context.Locks.LockBranchInventoryRowsAsync(tenantId, meta.BranchId, sortedSkuIds, ct);

                // 3. Đọc dữ liệu đơn hàng dưới khóa
                var order = await _context.Orders
                    .Include(o => o.Items)
                    .Include(o => o.Reservations)
                    .Include(o => o.Payment)
                    .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Id == orderId, ct);

                if (order == null)
                {
                    return false;
                }

                // Kiểm tra tính hợp lệ của đơn hàng trước khi hủy
                if (order.Status != "Draft" && order.Status != "Reserved")
                {
                    return false;
                }

                if (order.ConfirmedAt != null || order.CompletedAt != null || order.CancelledAt != null)
                {
                    return false;
                }

                if (order.Payment != null && order.Payment.Status == "Succeeded")
                {
                    return false;
                }

                if (string.Equals(order.Channel, "POS", StringComparison.OrdinalIgnoreCase))
                {
                    bool isLegacyUnpaidDraft = order.Status == "Draft" && (order.Payment == null || order.Payment.Status != "Succeeded") && order.ConfirmedAt == null;
                    if (!isLegacyUnpaidDraft)
                    {
                        return false;
                    }
                }

                if (order.Reservations == null || order.Reservations.Count == 0)
                {
                    return false;
                }

                var activeReservations = order.Reservations.Where(r => r.Status == "Active").ToList();
                if (activeReservations.Count == 0)
                {
                    return false;
                }

                // Chỉ hủy khi có ít nhất một phiếu Active quá hạn: ExpiresAt <= effectiveCutoff
                bool hasOverdueActive = activeReservations.Any(r => r.ExpiresAt <= effectiveCutoff);
                if (!hasOverdueActive)
                {
                    return false;
                }

                var now = DateTimeOffset.UtcNow;

                // 4. Khóa dòng warehouse_inventory_balances theo thứ tự warehouseId, skuId
                var resWarehouseIds = activeReservations.Select(r => r.WarehouseId).Distinct().OrderBy(id => id).ToList();
                foreach (var whId in resWarehouseIds)
                {
                    await _context.Locks.LockWarehouseInventoryRowsAsync(tenantId, meta.BranchId, whId, sortedSkuIds, ct);
                }

                var branchBalances = await _context.InventoryBalances
                    .Where(b => b.TenantId == tenantId && b.BranchId == order.BranchId && sortedSkuIds.Contains(b.SkuId))
                    .ToDictionaryAsync(b => b.SkuId, ct);

                var whBalances = await _context.WarehouseInventoryBalances
                    .Where(wb => wb.TenantId == tenantId && wb.BranchId == order.BranchId && resWarehouseIds.Contains(wb.WarehouseId) && sortedSkuIds.Contains(wb.SkuId))
                    .ToListAsync(ct);

                // 5. Giải phóng toàn bộ phiếu giữ chỗ Active:
                // - Active hết hạn (ExpiresAt <= effectiveCutoff) -> Status = "Expired"
                // - Active còn hạn cùng giỏ (ExpiresAt > effectiveCutoff) -> Status = "Released"
                foreach (var res in activeReservations)
                {
                    var whBal = whBalances.FirstOrDefault(wb => wb.WarehouseId == res.WarehouseId && wb.SkuId == res.SkuId);
                    if (whBal == null || whBal.Reserved < res.Quantity)
                    {
                        throw new OismException("INCONSISTENT_STOCK", $"Số dư tồn giữ chỗ tại kho '{res.WarehouseId}' không đủ hoặc không tìm thấy để giải phóng cho SKU '{res.SkuId}'.", 409);
                    }

                    if (!branchBalances.TryGetValue(res.SkuId, out var bBal) || bBal.Reserved < res.Quantity)
                    {
                        throw new OismException("INCONSISTENT_STOCK", $"Số dư tồn giữ chỗ chi nhánh không đủ hoặc không tìm thấy để giải phóng cho SKU '{res.SkuId}'.", 409);
                    }

                    whBal.Reserved -= res.Quantity;
                    whBal.UpdatedAt = now;

                    bBal.Reserved -= res.Quantity;
                    bBal.UpdatedAt = now;

                    if (res.ExpiresAt <= effectiveCutoff)
                    {
                        res.Status = "Expired";
                    }
                    else
                    {
                        res.Status = "Released";
                    }
                }

                // 6. Cập nhật trạng thái đơn hàng sang Cancelled
                order.Status = "Cancelled";
                order.CancelledAt = now;

                await _context.SaveChangesAsync(ct);

                await _context.Locks.SetConstraintsImmediateAsync(ct);

                await tx.CommitAsync(ct);

                _logger.LogInformation("Tự động hủy đơn hàng quá hạn giữ chỗ {OrderNumber} thành công.", order.Number);

                // 7. Chuẩn bị sự kiện Realtime sau commit thành công (nhóm theo SKU để không trùng lặp)
                if (_realtimeNotifier != null)
                {
                    try
                    {
                        var releasedSkuIds = activeReservations.Select(r => r.SkuId).Distinct().ToList();
                        foreach (var skuId in releasedSkuIds)
                        {
                            if (branchBalances.TryGetValue(skuId, out var bBal))
                            {
                                var newAvail = bBal.OnHand - bBal.Reserved;
                                var skuIdCapture = skuId;
                                var branchIdCapture = order.BranchId;
                                var tidCapture = tenantId;
                                pendingNotifications.Add(() => _realtimeNotifier.NotifyStockChangedAsync(tidCapture, branchIdCapture, skuIdCapture, newAvail, ct));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi khi chuẩn bị sự kiện Realtime sau commit tự động hủy đơn {OrderNumber}.", order.Number);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _logger.LogError(ex, "Tự động hủy đơn hàng quá hạn {OrderId} thất bại. Đã rollback giao dịch.", orderId);
                throw;
            }
        });

        // Phát sự kiện SignalR ngoài transaction và ngoài execution strategy delegate
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
                    _logger.LogError(ex, "Lỗi khi phát sự kiện Realtime sau commit tự động hủy đơn {OrderId}.", orderId);
                }
            }
        }

        return result;
    }
}

/// <summary>
/// Thống kê chi tiết kết quả xử lý batch của tác vụ tự động hủy đơn giữ chỗ quá hạn.
/// </summary>
public class BatchExecutionStats
{
    public int Scanned { get; set; }
    public int Cancelled { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public List<Exception> Errors { get; set; } = new();
}
