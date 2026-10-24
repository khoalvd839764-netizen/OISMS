using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Orders.Dtos;
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
    private readonly ILogger<OrderService> _logger;

    public const decimal MaxMoneyAmount = 999_999_999_999_999_999.99m; // numeric(20,2) limit (18 integer digits, 2 decimals)
    public const decimal MaxQuantityAmount = 999_999_999_999_999.999m; // numeric(18,3) limit (15 integer digits, 3 decimals)

    public OrderService(
        IAppDbContext context,
        ICurrentTenantService currentTenantService,
        ILogger<OrderService> logger)
    {
        _context = context;
        _currentTenantService = currentTenantService;
        _logger = logger;
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
        var strategy = _context.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _context.ClearChangeTracker();

            // Mở Transaction CSDL duy nhất cho toàn bộ chuỗi xử lý
            await using var tx = await _context.BeginTransactionAsync(ct);
            try
            {
            // 5. Lấy PostgreSQL Advisory Locks theo thứ tự xác định: SKU Locks -> RequestKey Lock
            if (_context.IsRelationalDatabase)
            {
                foreach (var skuId in sortedSkuIds)
                {
                    await _context.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/' || {1}::text || '/' || {2}::text, 0));",
                        new object[] { tenantId.Value.ToString(), dto.BranchId.ToString(), skuId.ToString() },
                        ct);
                }

                await _context.ExecuteSqlRawAsync(
                    "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/request_key/' || {1}::text, 0));",
                    new object[] { tenantId.Value.ToString(), dto.RequestKey.ToString() },
                    ct);
            }

            // 6. Kiểm tra Idempotency dưới khóa
            var existingOrder = await _context.Orders
                .Include(o => o.Items)
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
                foreach (var g in dto.Items.GroupBy(i => i.SkuId))
                {
                    if (g.Select(x => Math.Round(x.UnitPrice, 2)).Distinct().Count() > 1)
                    {
                        throw new OismException("VALIDATION_ERROR", "Các dòng cùng SKU trong giỏ hàng phải có cùng đơn giá.", 400);
                    }
                }

                var existingItemSummary = existingOrder.Items
                    .GroupBy(i => i.SkuId)
                    .ToDictionary(g => g.Key, g => new
                    {
                        Quantity = g.Sum(x => x.Quantity),
                        UnitPrice = Math.Round(g.First().UnitPrice, 2)
                    });

                var requestItemSummary = dto.Items
                    .GroupBy(i => i.SkuId)
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
                        throw new IdempotencyPayloadMismatchException($"RequestKey đã được sử dụng với danh mục SKU khác (thiếu SKU '{kv.Key}').");
                    }

                    if (reqData.Quantity != kv.Value.Quantity)
                    {
                        throw new IdempotencyPayloadMismatchException($"RequestKey đã được sử dụng với số lượng mua khác cho SKU '{kv.Key}'.");
                    }

                    if (reqData.UnitPrice != kv.Value.UnitPrice)
                    {
                        throw new IdempotencyPayloadMismatchException($"RequestKey đã được sử dụng với đơn giá khác cho SKU '{kv.Key}'.");
                    }
                }

                _logger.LogInformation("Idempotent replay cho RequestKey {RequestKey}. Trả kết quả đơn {OrderNumber}.", dto.RequestKey, existingOrder.Number);

                var branchInfo = await _context.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.TenantId == tenantId.Value && b.Id == existingOrder.BranchId, ct);
                var cashierInfo = cashierUserId.HasValue
                    ? await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TenantId == tenantId.Value && u.Id == cashierUserId.Value, ct)
                    : null;

                var replayReceipt = new PosReceiptDto
                {
                    OrderId = existingOrder.Id,
                    ReceiptNumber = existingOrder.Number,
                    OrderNumber = existingOrder.Number,
                    BranchId = existingOrder.BranchId,
                    BranchName = branchInfo?.Name ?? string.Empty,
                    BranchAddress = branchInfo?.Address,
                    BranchPhone = branchInfo?.Phone,
                    CashierName = cashierInfo?.FullName ?? "Thu ngân",
                    CustomerName = existingOrder.CustomerName,
                    CustomerPhone = existingOrder.CustomerPhone,
                    TotalAmount = existingOrder.TotalAmount,
                    PaymentMethod = existingOrder.Payment?.Method ?? normalizedMethod,
                    AmountGiven = existingOrder.Payment?.CashReceived,
                    ChangeDue = existingOrder.Payment?.CashReceived.HasValue == true
                        ? existingOrder.Payment.CashReceived.Value - existingOrder.TotalAmount
                        : null,
                    CreatedAt = existingOrder.CreatedAt,
                    Items = existingOrder.Items.Select(i => new PosReceiptItemDto
                    {
                        SkuId = i.SkuId,
                        SkuCode = i.SkuCodeSnapshot,
                        ProductName = i.SkuNameSnapshot,
                        VariantName = null,
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

            // 9. Xác thực giá bán theo chính sách đã chốt (Backend luôn chốt theo giá niêm yết Catalog ProductVariant.RetailPrice)
            var groupedItems = new Dictionary<Guid, decimal>();
            foreach (var item in dto.Items)
            {
                if (groupedItems.TryGetValue(item.SkuId, out var existingQty))
                {
                    var newQty = existingQty + item.Quantity;
                    if (newQty > MaxQuantityAmount)
                    {
                        throw new OismException("VALIDATION_ERROR", $"Tổng số lượng mặt hàng SKU '{item.SkuCode}' vượt quá giới hạn tối đa cho phép của hệ thống.", 400);
                    }
                    groupedItems[item.SkuId] = newQty;
                }
                else
                {
                    groupedItems[item.SkuId] = item.Quantity;
                }
            }

            foreach (var item in dto.Items)
            {
                var catalogSku = skus[item.SkuId];
                if (item.UnitPrice != catalogSku.RetailPrice)
                {
                    throw new OismException("PRICE_MISMATCH", $"Đơn giá của mặt hàng '{catalogSku.SKU}' ({item.UnitPrice:N0}đ) không khớp với giá bán lẻ niêm yết của hệ thống ({catalogSku.RetailPrice:N0}đ).", 400);
                }
            }

            // 10. Tính toán tổng tiền đơn hàng phía Server (kiểm tra tràn số CLR và giới hạn numeric(20,2))
            decimal totalAmount = 0m;
            foreach (var kv in groupedItems)
            {
                var retailPrice = skus[kv.Key].RetailPrice;
                var qty = kv.Value;
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
            var now = DateTimeOffset.UtcNow;
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
                ConfirmedAt = now,
                CompletedAt = now,
                CreatedAt = now
            };
            _context.Orders.Add(order);

            var createdOrderItems = new List<OrderItem>();

            foreach (var kv in groupedItems)
            {
                var skuId = kv.Key;
                var qty = kv.Value;
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
                    Quantity = qty,
                    UnitPrice = Math.Round(sku.RetailPrice, 2, MidpointRounding.AwayFromZero),
                    CostPrice = Math.Round(bBal.AverageCost, 4, MidpointRounding.AwayFromZero), // Snapshot WAC
                    CreatedAt = now
                };
                _context.OrderItems.Add(orderItem);
                createdOrderItems.Add(orderItem);

                var allocs = allocationPlan[skuId];
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
                        UnitCost = bBal.AverageCost, // Snapshot WAC
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

            // 16. Lưu xuống DB
            await _context.SaveChangesAsync(ct);

            // 17. Chốt kiểm tra Deferred Constraints của PostgreSQL trước khi COMMIT
            if (_context.IsRelationalDatabase)
            {
                await _context.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE;", Array.Empty<object>(), ct);
            }

            await tx.CommitAsync(ct);

            // 18. Xây dựng DTO phản hồi quầy POS
            var cashierUser = cashierUserId.HasValue
                ? await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TenantId == tenantId.Value && u.Id == cashierUserId.Value, ct)
                : null;

            var receipt = new PosReceiptDto
            {
                OrderId = order.Id,
                ReceiptNumber = order.Number,
                OrderNumber = order.Number,
                BranchId = branch.Id,
                BranchName = branch.Name,
                BranchAddress = branch.Address,
                BranchPhone = branch.Phone,
                CashierName = cashierUser?.FullName ?? "Thu ngân",
                CustomerName = dto.CustomerName,
                CustomerPhone = dto.CustomerPhone,
                TotalAmount = totalAmount,
                PaymentMethod = normalizedMethod,
                AmountGiven = cashReceived,
                ChangeDue = changeDue,
                CreatedAt = order.CreatedAt,
                Items = createdOrderItems.Select(i => new PosReceiptItemDto
                {
                    SkuId = i.SkuId,
                    SkuCode = i.SkuCodeSnapshot,
                    ProductName = i.SkuNameSnapshot,
                    VariantName = null,
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

        // 1. Validation
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
            throw new OismException("VALIDATION_ERROR", "Đơn hàng phải có ít nhất 1 sản phẩm.", 400);
        }

        if (dto.Items.Any(i => i == null))
        {
            throw new OismException("VALIDATION_ERROR", "Dòng sản phẩm trong đơn hàng không được mang giá trị null.", 400);
        }

        if (dto.Items.Any(i => i.SkuId == Guid.Empty))
        {
            throw new OismException("VALIDATION_ERROR", "Mã biến thể SKU trong đơn hàng không được để trống.", 400);
        }

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

        // Kiểm tra tính nhất quán của đơn giá nếu có dòng trùng SKU
        foreach (var g in dto.Items.GroupBy(i => i.SkuId))
        {
            if (g.Select(x => Math.Round(x.UnitPrice, 2)).Distinct().Count() > 1)
            {
                throw new OismException("VALIDATION_ERROR", "Các dòng cùng SKU trong đơn hàng phải có cùng đơn giá.", 400);
            }
        }

        var channel = string.IsNullOrWhiteSpace(dto.Channel) ? "Admin" : dto.Channel.Trim();
        if (!string.Equals(channel, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            throw new OismException("VALIDATION_ERROR", $"Endpoint chỉ hỗ trợ tạo đơn hàng kênh 'Admin'. Kênh '{dto.Channel}' không hợp lệ.", 400);
        }
        channel = "Admin";

        // 2. Sắp xếp danh sách SkuId tăng dần để triệt tiêu deadlocks khi khóa tuần tự
        var sortedSkuIds = dto.Items.Select(i => i.SkuId).Distinct().OrderBy(id => id).ToList();

        // 3. Bao bọc trong ExecutionStrategy
        var strategy = _context.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _context.ClearChangeTracker();

            await using var tx = await _context.BeginTransactionAsync(ct);
            try
            {
                // 4. Lấy PostgreSQL Advisory Locks theo thứ tự xác định: SKU Locks -> RequestKey Lock
                if (_context.IsRelationalDatabase)
                {
                    foreach (var skuId in sortedSkuIds)
                    {
                        await _context.ExecuteSqlRawAsync(
                            "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/' || {1}::text || '/' || {2}::text, 0));",
                            new object[] { tenantId.Value.ToString(), dto.BranchId.ToString(), skuId.ToString() },
                            ct);
                    }

                    await _context.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/request_key/' || {1}::text, 0));",
                        new object[] { tenantId.Value.ToString(), dto.RequestKey.ToString() },
                        ct);
                }

                // 5. Kiểm tra Idempotency dưới khóa
                var existingOrder = await _context.Orders
                    .Include(o => o.Items)
                    .Include(o => o.Payment)
                    .FirstOrDefaultAsync(o => o.TenantId == tenantId.Value && o.RequestKey == dto.RequestKey, ct);

                if (existingOrder != null)
                {
                    // So sánh Kênh bán
                    if (!string.Equals(existingOrder.Channel, "Admin", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IdempotencyPayloadMismatchException("RequestKey đã được sử dụng cho một đơn hàng không thuộc kênh Admin.");
                    }

                    // So sánh Chi nhánh
                    if (existingOrder.BranchId != dto.BranchId)
                    {
                        throw new IdempotencyPayloadMismatchException("RequestKey đã được sử dụng với Chi nhánh khác.");
                    }

                    // So sánh thông tin khách hàng & địa chỉ giao nhận
                    var normDtoCustName = string.IsNullOrWhiteSpace(dto.CustomerName) ? null : dto.CustomerName.Trim();
                    var normDtoCustPhone = string.IsNullOrWhiteSpace(dto.CustomerPhone) ? null : dto.CustomerPhone.Trim();
                    var normDtoAddress = string.IsNullOrWhiteSpace(dto.ShippingAddress) ? null : dto.ShippingAddress.Trim();

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
                        .GroupBy(i => i.SkuId)
                        .ToDictionary(g => g.Key, g => new
                        {
                            Quantity = g.Sum(x => x.Quantity),
                            UnitPrice = Math.Round(g.First().UnitPrice, 2)
                        });

                    var requestItemSummary = dto.Items
                        .GroupBy(i => i.SkuId)
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
                            throw new IdempotencyPayloadMismatchException($"RequestKey đã được sử dụng với danh mục SKU khác (thiếu SKU '{kv.Key}').");
                        }

                        if (reqData.Quantity != kv.Value.Quantity)
                        {
                            throw new IdempotencyPayloadMismatchException($"RequestKey đã được sử dụng với số lượng mua khác cho SKU '{kv.Key}'.");
                        }

                        if (reqData.UnitPrice != kv.Value.UnitPrice)
                        {
                            throw new IdempotencyPayloadMismatchException($"RequestKey đã được sử dụng với đơn giá khác cho SKU '{kv.Key}'.");
                        }
                    }

                    _logger.LogInformation("Idempotent replay cho tạo đơn hàng trực tuyến với RequestKey {RequestKey}. Trả kết quả đơn {OrderNumber}.", dto.RequestKey, existingOrder.Number);

                    await tx.CommitAsync(ct);
                    var replayDto = await MapToOrderResponseDtoAsync(existingOrder, ct);
                    replayDto.IsReplay = true;
                    return replayDto;
                }

                // 6. Xác thực Chi nhánh
                var branch = await _context.Branches.FirstOrDefaultAsync(b => b.TenantId == tenantId.Value && b.Id == dto.BranchId, ct);
                if (branch == null || !branch.IsActive)
                {
                    throw new OismException("BRANCH_INACTIVE_OR_INVALID", $"Chi nhánh '{dto.BranchId}' không tồn tại hoặc đã ngừng hoạt động.", 404);
                }

                // 7. Tải và xác thực danh mục SKU trong Catalog
                var skus = await _context.ProductVariants
                    .Include(v => v.Product)
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

                // Gom nhóm số lượng theo SkuId
                var groupedItems = new Dictionary<Guid, decimal>();
                foreach (var item in dto.Items)
                {
                    if (groupedItems.TryGetValue(item.SkuId, out var existingQty))
                    {
                        var newQty = existingQty + item.Quantity;
                        if (newQty > MaxQuantityAmount)
                        {
                            throw new OismException("VALIDATION_ERROR", $"Tổng số lượng mặt hàng SKU '{item.SkuCode}' vượt quá giới hạn tối đa cho phép của hệ thống.", 400);
                        }
                        groupedItems[item.SkuId] = newQty;
                    }
                    else
                    {
                        groupedItems[item.SkuId] = item.Quantity;
                    }
                }

                // Kiểm tra đơn giá khớp với RetailPrice niêm yết trong Catalog
                foreach (var item in dto.Items)
                {
                    var catalogSku = skus[item.SkuId];
                    if (item.UnitPrice != catalogSku.RetailPrice)
                    {
                        throw new OismException("PRICE_MISMATCH", $"Đơn giá của mặt hàng '{catalogSku.SKU}' ({item.UnitPrice:N0}đ) không khớp với giá bán lẻ niêm yết của hệ thống ({catalogSku.RetailPrice:N0}đ).", 400);
                    }
                }

                // Tính tổng tiền đơn hàng phía Server
                decimal totalAmount = 0m;
                foreach (var kv in groupedItems)
                {
                    var retailPrice = skus[kv.Key].RetailPrice;
                    var qty = kv.Value;
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

                // 8. Kiểm tra tồn khả dụng tại Chi nhánh (InventoryBalances)
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

                // 9. Phân bổ kho theo chính sách: Tự động phân bổ tồn khả dụng qua các kho hoạt động thuộc chi nhánh
                var activeWarehouses = await _context.Warehouses
                    .Where(w => w.TenantId == tenantId.Value && w.BranchId == dto.BranchId && w.IsActive)
                    .OrderBy(w => w.CreatedAt)
                    .ToListAsync(ct);

                if (activeWarehouses.Count == 0)
                {
                    throw new OismException("NO_ACTIVE_WAREHOUSE", $"Chi nhánh '{branch.Name}' không có kho hàng nào đang hoạt động để giữ chỗ hàng.", 409);
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

                // 10. Tạo Đơn hàng ở trạng thái Reserved
                var now = DateTimeOffset.UtcNow;
                var orderId = Guid.NewGuid();
                var orderNumber = $"ORD-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..20];

                var order = new Order
                {
                    Id = orderId,
                    TenantId = tenantId.Value,
                    Number = orderNumber,
                    BranchId = dto.BranchId,
                    Channel = channel,
                    ExternalShopId = channel == "Admin" || channel == "POS" ? null : dto.ExternalShopId,
                    ExternalOrderId = channel == "Admin" || channel == "POS" ? null : dto.ExternalOrderId,
                    Status = "Reserved",
                    CustomerName = dto.CustomerName,
                    CustomerPhone = dto.CustomerPhone,
                    ShippingAddress = dto.ShippingAddress,
                    TotalAmount = totalAmount,
                    RequestKey = dto.RequestKey,
                    CreatedBy = createdByUserId,
                    ConfirmedAt = null,
                    CompletedAt = null,
                    CancelledAt = null,
                    CreatedAt = now
                };
                _context.Orders.Add(order);

                var createdOrderItems = new List<OrderItem>();
                var createdReservations = new List<Reservation>();

                foreach (var kv in groupedItems)
                {
                    var skuId = kv.Key;
                    var qty = kv.Value;
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
                        Quantity = qty,
                        UnitPrice = Math.Round(sku.RetailPrice, 2, MidpointRounding.AwayFromZero),
                        CostPrice = null, // CostPrice chỉ được snapshot khi Confirmed/Completed
                        CreatedAt = now
                    };
                    _context.OrderItems.Add(orderItem);
                    createdOrderItems.Add(orderItem);

                    var allocs = allocationPlan[skuId];
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
                            TenantId = tenantId.Value,
                            OrderId = orderId,
                            OrderItemId = orderItem.Id,
                            BranchId = dto.BranchId,
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

                if (_context.IsRelationalDatabase)
                {
                    await _context.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE;", Array.Empty<object>(), ct);
                }

                await tx.CommitAsync(ct);

                order.Items = createdOrderItems;
                order.Reservations = createdReservations;

                _logger.LogInformation("Tạo đơn hàng trực tuyến {OrderNumber} thành công ở trạng thái Reserved.", order.Number);

                return await MapToOrderResponseDtoAsync(order, ct);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _logger.LogError(ex, "Tạo đơn hàng online thất bại cho RequestKey {RequestKey}. Đã rollback giao dịch.", dto.RequestKey);
                throw;
            }
        });
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

        var strategy = _context.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _context.ClearChangeTracker();

            await using var tx = await _context.BeginTransactionAsync(ct);
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
                if (_context.IsRelationalDatabase)
                {
                    foreach (var skuId in sortedSkuIds)
                    {
                        await _context.ExecuteSqlRawAsync(
                            "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/' || {1}::text || '/' || {2}::text, 0));",
                            new object[] { tenantId.Value.ToString(), meta.BranchId.ToString(), skuId.ToString() },
                            ct);
                    }

                    await _context.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/request_key/' || {1}::text, 0));",
                        new object[] { tenantId.Value.ToString(), meta.RequestKey.ToString() },
                        ct);

                    await _context.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/order/' || {1}::text, 0));",
                        new object[] { tenantId.Value.ToString(), orderId.ToString() },
                        ct);
                }

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
                    if (totalAllocated != item.Quantity)
                    {
                        throw new OismException("INVALID_STATE_TRANSITION", $"Số lượng giữ chỗ ({totalAllocated:G29}) không khớp với số lượng yêu cầu ({item.Quantity:G29}) của dòng đơn hàng SKU '{item.SkuCodeSnapshot}'.", 409);
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

                await _context.SaveChangesAsync(ct);

                if (_context.IsRelationalDatabase)
                {
                    await _context.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE;", Array.Empty<object>(), ct);
                }

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

        var strategy = _context.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _context.ClearChangeTracker();

            await using var tx = await _context.BeginTransactionAsync(ct);
            try
            {
                // 1. Khóa theo OrderId
                if (_context.IsRelationalDatabase)
                {
                    await _context.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/order/' || {1}::text, 0));",
                        new object[] { tenantId.Value.ToString(), orderId.ToString() },
                        ct);
                }

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

                if (_context.IsRelationalDatabase)
                {
                    await _context.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE;", Array.Empty<object>(), ct);
                }

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

        var strategy = _context.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _context.ClearChangeTracker();

            await using var tx = await _context.BeginTransactionAsync(ct);
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
                if (_context.IsRelationalDatabase)
                {
                    foreach (var skuId in sortedSkuIds)
                    {
                        await _context.ExecuteSqlRawAsync(
                            "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/' || {1}::text || '/' || {2}::text, 0));",
                            new object[] { tenantId.Value.ToString(), meta.BranchId.ToString(), skuId.ToString() },
                            ct);
                    }

                    await _context.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/request_key/' || {1}::text, 0));",
                        new object[] { tenantId.Value.ToString(), meta.RequestKey.ToString() },
                        ct);

                    await _context.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/order/' || {1}::text, 0));",
                        new object[] { tenantId.Value.ToString(), orderId.ToString() },
                        ct);
                }

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

                if (_context.IsRelationalDatabase)
                {
                    await _context.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE;", Array.Empty<object>(), ct);
                }

                await tx.CommitAsync(ct);

                _logger.LogInformation("Hủy đơn hàng {OrderNumber} thành công. Đã giải phóng toàn bộ tồn giữ chỗ.", order.Number);

                return await MapToOrderResponseDtoAsync(order, ct);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _logger.LogError(ex, "Hủy đơn hàng {OrderId} thất bại. Đã rollback giao dịch.", orderId);
                throw;
            }
        });
    }

    private async Task<OrderResponseDto> MapToOrderResponseDtoAsync(Order order, CancellationToken ct)
    {
        var branchName = await _context.Branches
            .AsNoTracking()
            .Where(b => b.TenantId == order.TenantId && b.Id == order.BranchId)
            .Select(b => b.Name)
            .FirstOrDefaultAsync(ct);

        decimal? totalCogs = null;
        if (order.Items != null && order.Items.Count > 0 && order.Items.All(i => i.CostPrice.HasValue))
        {
            totalCogs = Math.Round(order.Items.Sum(i => i.Quantity * i.CostPrice!.Value), 6, MidpointRounding.AwayFromZero);
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

    public Task<OrderResponseDto?> GetOrderByIdAsync(Guid orderId, CancellationToken ct = default)
    {
        throw new NotImplementedException("Chức năng tra cứu chi tiết đơn hàng thuộc phạm vi phân công của Nhựt (BE2).");
    }

    public Task<PagedResultDto<OrderResponseDto>> GetOrdersAsync(OrderFilterRequestDto filter, CancellationToken ct = default)
    {
        throw new NotImplementedException("Chức năng tra cứu danh sách đơn hàng thuộc phạm vi phân công của Nhựt (BE2).");
    }

    public Task<int> ExpireOverdueReservationsAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException("Tác vụ quét hết hạn phiếu giữ chỗ thuộc phạm vi Task 6 (Hangfire).");
    }
}
