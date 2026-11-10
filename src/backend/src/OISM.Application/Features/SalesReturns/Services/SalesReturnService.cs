using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Inventory.Services;
using OISM.Application.Features.Realtime;
using OISM.Application.Features.SalesReturns.Dtos;
using OISM.Domain.Common;
using OISM.Domain.Entities;

namespace OISM.Application.Features.SalesReturns.Services;

public class SalesReturnService : ISalesReturnService
{
    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _currentTenantService;
    private readonly IWacCalculator _wacCalculator;
    private readonly IRealtimeNotifier? _realtimeNotifier;

    public SalesReturnService(
        IAppDbContext context,
        ICurrentTenantService currentTenantService,
        IWacCalculator wacCalculator,
        IRealtimeNotifier? realtimeNotifier = null)
    {
        _context = context;
        _currentTenantService = currentTenantService;
        _wacCalculator = wacCalculator;
        _realtimeNotifier = realtimeNotifier;
    }

    private Guid RequireTenantId()
    {
        var tenantId = _currentTenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Không xác định được TenantId trong ngữ cảnh yêu cầu.");
        }
        return tenantId.Value;
    }

    public async Task<List<ReturnableItemDto>> GetReturnableItemsAsync(Guid orderId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var order = await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Id == orderId, ct);

        if (order == null)
        {
            throw new OismException("ORDER_NOT_FOUND", $"Không tìm thấy đơn hàng '{orderId}'.", 404);
        }

        if (order.Status != OrderStatus.Confirmed && order.Status != OrderStatus.Completed)
        {
            throw new OismException("ORDER_NOT_RETURNABLE", $"Đơn hàng '{order.Number}' chưa xuất kho hoặc đã hủy, không thể trả hàng.", 409);
        }

        var returnedMap = await _context.SalesReturnItems
            .Where(i => i.TenantId == tenantId && i.OrderId == orderId && i.SalesReturn!.Status == SalesReturnStatus.Confirmed)
            .GroupBy(i => i.OrderItemId)
            .Select(g => new { OrderItemId = g.Key, TotalQuantity = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.OrderItemId, x => x.TotalQuantity, ct);

        var result = new List<ReturnableItemDto>();
        foreach (var item in order.Items)
        {
            var returned = returnedMap.TryGetValue(item.Id, out var qty) ? qty : 0m;
            var returnable = Math.Max(0m, item.Quantity - returned);
            result.Add(new ReturnableItemDto
            {
                OrderItemId = item.Id,
                SkuId = item.SkuId,
                SkuCode = item.SkuCodeSnapshot,
                SkuName = item.SkuNameSnapshot,
                UnitId = item.UnitId,
                UnitName = item.UnitNameSnapshot,
                FactorSnapshot = item.FactorSnapshot,
                SoldQuantity = item.Quantity,
                ReturnedQuantity = returned,
                ReturnableQuantity = returnable,
                UnitPrice = item.UnitPrice,
                OriginalUnitCost = item.CostPrice ?? 0m
            });
        }

        return result;
    }

    public async Task<(SalesReturnResponseDto Dto, bool IsReplay)> CreateAsync(
        CreateSalesReturnDto dto, Guid requestKey, Guid userId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        if (requestKey == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "RequestKey không được để trống.", 400);
        }

        return await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            _context.UnitOfWork.ResetTracking();
            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);

            await _context.Locks.LockRequestKeyAsync(tenantId, requestKey, ct);

            // Kiểm tra Idempotency
            var existing = await _context.SalesReturns
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.RequestKey == requestKey, ct);

            if (existing != null)
            {
                var payloadMatches = existing.OrderId == dto.OrderId &&
                                     existing.WarehouseId == dto.WarehouseId &&
                                     string.Equals(existing.Reason.Trim(), (dto.Reason ?? string.Empty).Trim(), StringComparison.Ordinal) &&
                                     string.Equals((existing.Note ?? string.Empty).Trim(), (dto.Note ?? string.Empty).Trim(), StringComparison.Ordinal) &&
                                     existing.Items.Count == (dto.Items?.Count ?? 0);

                if (payloadMatches && dto.Items != null)
                {
                    var existingItemsByOi = existing.Items.ToDictionary(i => i.OrderItemId);
                    foreach (var dItem in dto.Items)
                    {
                        if (!existingItemsByOi.TryGetValue(dItem.OrderItemId, out var eItem) ||
                            eItem.Quantity != dItem.Quantity)
                        {
                            payloadMatches = false;
                            break;
                        }

                        if (dItem.RefundAmount.HasValue && eItem.RefundAmount != dItem.RefundAmount.Value)
                        {
                            payloadMatches = false;
                            break;
                        }
                    }
                }

                if (!payloadMatches)
                {
                    throw new OismException("IDEMPOTENCY_PAYLOAD_MISMATCH", "RequestKey đã được sử dụng với nội dung phiếu trả khác.", 409);
                }

                await tx.RollbackAsync(ct);
                var existingDto = await MapToDtoAsync(existing.Id, ct);
                return (existingDto, true);
            }

            // Kiểm tra tính hợp lệ dữ liệu
            if (dto.Items == null || dto.Items.Count == 0)
            {
                throw new OismException("VALIDATION_ERROR", "Phiếu trả hàng phải có ít nhất một dòng mặt hàng.", 400);
            }

            if (string.IsNullOrWhiteSpace(dto.Reason))
            {
                throw new OismException("VALIDATION_ERROR", "Lý do trả hàng là bắt buộc.", 400);
            }

            var order = await _context.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Id == dto.OrderId, ct);

            if (order == null)
            {
                throw new OismException("ORDER_NOT_FOUND", $"Không tìm thấy đơn hàng '{dto.OrderId}'.", 404);
            }

            if (order.Status != OrderStatus.Confirmed && order.Status != OrderStatus.Completed)
            {
                throw new OismException("ORDER_NOT_RETURNABLE", $"Đơn hàng '{order.Number}' chưa xuất kho hoặc đã hủy, không thể trả hàng.", 409);
            }

            var warehouse = await _context.Warehouses
                .FirstOrDefaultAsync(w => w.TenantId == tenantId && w.Id == dto.WarehouseId, ct);

            if (warehouse == null || warehouse.BranchId != order.BranchId)
            {
                throw new OismException("RETURN_WAREHOUSE_MISMATCH", $"Kho nhận không thuộc chi nhánh của đơn hàng '{order.Number}'.", 422);
            }

            var isMarketplace = order.Channel is "Shopee" or "TikTok" or "Lazada";

            if (dto.Items.Select(i => i.OrderItemId).Distinct().Count() != dto.Items.Count)
            {
                throw new OismException("VALIDATION_ERROR", "Danh sách mặt hàng chứa dòng đơn hàng trùng lặp.", 400);
            }

            var orderItemsById = order.Items.ToDictionary(i => i.Id);
            var itemsToCreate = new List<(OrderItem OrderItem, decimal Quantity, decimal RefundAmount)>();

            foreach (var itemDto in dto.Items)
            {
                if (itemDto.Quantity <= 0)
                {
                    throw new OismException("VALIDATION_ERROR", "Số lượng trả phải lớn hơn 0.", 400);
                }

                // Khớp numeric(18,3) của sales_return_items.quantity và numeric(20,2) của refund_amount, tránh PostgreSQL làm tròn ngầm.
                if (decimal.Round(itemDto.Quantity, 3) != itemDto.Quantity)
                {
                    throw new OismException("VALIDATION_ERROR", "Số lượng trả không được vượt quá 3 chữ số thập phân.", 400);
                }

                if (itemDto.RefundAmount.HasValue && decimal.Round(itemDto.RefundAmount.Value, 2) != itemDto.RefundAmount.Value)
                {
                    throw new OismException("VALIDATION_ERROR", "Tiền hoàn không được vượt quá 2 chữ số thập phân.", 400);
                }

                if (!orderItemsById.TryGetValue(itemDto.OrderItemId, out var orderItem))
                {
                    throw new OismException("VALIDATION_ERROR", $"Dòng đơn hàng '{itemDto.OrderItemId}' không thuộc đơn hàng '{order.Number}'.", 400);
                }

                var alreadyReturned = await _context.SalesReturnItems
                    .Where(sri => sri.TenantId == tenantId && sri.OrderItemId == itemDto.OrderItemId && sri.SalesReturn!.Status == SalesReturnStatus.Confirmed)
                    .SumAsync(sri => sri.Quantity, ct);

                var returnableQty = orderItem.Quantity - alreadyReturned;
                if (itemDto.Quantity > returnableQty)
                {
                    throw new OismException("RETURN_QUANTITY_EXCEEDED", $"Số lượng trả ({itemDto.Quantity}) vượt quá số lượng còn được trả ({returnableQty}) của mặt hàng '{orderItem.SkuNameSnapshot}'.", 422);
                }

                decimal lineRefund;
                if (isMarketplace)
                {
                    if (itemDto.RefundAmount.HasValue && itemDto.RefundAmount.Value > 0)
                    {
                        throw new OismException("REFUND_AMOUNT_EXCEEDED", "Đơn hàng từ sàn TMĐT bắt buộc tiền hoàn = 0.", 422);
                    }
                    lineRefund = 0m;
                }
                else
                {
                    lineRefund = itemDto.RefundAmount ?? (itemDto.Quantity * orderItem.UnitPrice);
                    if (lineRefund < 0)
                    {
                        throw new OismException("VALIDATION_ERROR", "Tiền hoàn không được âm.", 400);
                    }
                    var maxLineRefund = itemDto.Quantity * orderItem.UnitPrice;
                    if (lineRefund > maxLineRefund)
                    {
                        throw new OismException("REFUND_AMOUNT_EXCEEDED", $"Tiền hoàn ({lineRefund:N0}) vượt quá giá bán của dòng ({maxLineRefund:N0}).", 422);
                    }
                }

                itemsToCreate.Add((orderItem, itemDto.Quantity, lineRefund));
            }

            var totalRefund = isMarketplace ? 0m : itemsToCreate.Sum(i => i.RefundAmount);
            if (!isMarketplace)
            {
                var alreadyRefunded = await _context.SalesReturns
                    .Where(sr => sr.TenantId == tenantId && sr.OrderId == order.Id && sr.Status == SalesReturnStatus.Confirmed)
                    .SumAsync(sr => sr.TotalRefundAmount, ct);

                if (alreadyRefunded + totalRefund > order.TotalAmount)
                {
                    throw new OismException("REFUND_AMOUNT_EXCEEDED", $"Tổng tiền hoàn ({alreadyRefunded + totalRefund:N0}) vượt quá tổng giá trị đơn hàng ({order.TotalAmount:N0}).", 422);
                }
            }

            await _context.Locks.LockSalesReturnNumberAsync(tenantId, ct);
            var number = await GenerateReturnNumberAsync(tenantId, ct);

            var now = DateTimeOffset.UtcNow;
            var salesReturn = new SalesReturn
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                RequestKey = requestKey,
                Number = number,
                OrderId = order.Id,
                BranchId = order.BranchId,
                WarehouseId = dto.WarehouseId,
                Status = SalesReturnStatus.Draft,
                Reason = dto.Reason.Trim(),
                Note = dto.Note?.Trim(),
                TotalRefundAmount = totalRefund,
                CreatedBy = userId,
                CreatedAt = now
            };

            foreach (var item in itemsToCreate)
            {
                salesReturn.Items.Add(new SalesReturnItem
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    SalesReturnId = salesReturn.Id,
                    OrderId = order.Id,
                    OrderItemId = item.OrderItem.Id,
                    SkuId = item.OrderItem.SkuId,
                    Quantity = item.Quantity,
                    RefundAmount = item.RefundAmount,
                    OriginalUnitCost = item.OrderItem.CostPrice ?? 0m,
                    CreatedAt = now
                });
            }

            _context.SalesReturns.Add(salesReturn);
            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            var createdDto = await MapToDtoAsync(salesReturn.Id, ct);
            return (createdDto, false);
        });
    }

    public async Task<SalesReturnResponseDto> ConfirmAsync(
        Guid id, string? refundMethod, Guid userId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        List<Func<Task>> pendingNotifications = new();

        return await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            pendingNotifications.Clear();
            _context.UnitOfWork.ResetTracking();
            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);

            // 1. Đọc phiếu trả để lấy thông tin khóa
            var initialReturn = await _context.SalesReturns
                .Include(r => r.Items)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);

            if (initialReturn == null)
            {
                throw new OismException("SALES_RETURN_NOT_FOUND", $"Không tìm thấy phiếu trả hàng '{id}'.", 404);
            }

            if (initialReturn.Status == SalesReturnStatus.Confirmed)
            {
                await tx.RollbackAsync(ct);
                return await MapToDtoAsync(initialReturn.Id, ct);
            }

            if (initialReturn.Status == SalesReturnStatus.Cancelled)
            {
                throw new OismException("INVALID_STATE_TRANSITION", $"Phiếu trả hàng '{initialReturn.Number}' đã bị hủy, không thể xác nhận.", 409);
            }

            var sortedSkuIds = initialReturn.Items.Select(i => i.SkuId).Distinct().Order().ToList();

            // 2. KHÓA THEO ĐÚNG THỨ TỰ AC-04 CHỈ ĐỊNH:
            // LockSkuStockAsync (SKU đã sắp xếp, chi nhánh của đơn)
            // → LockRequestKeyAsync(return.RequestKey)
            // → LockOrderAsync
            // → LockSalesReturnAsync
            // → LockBranchInventoryRowsAsync
            // → LockWarehouseInventoryRowsAsync (kho nhận)
            await _context.Locks.LockSkuStockAsync(tenantId, initialReturn.BranchId, sortedSkuIds, ct);
            await _context.Locks.LockRequestKeyAsync(tenantId, initialReturn.RequestKey, ct);
            await _context.Locks.LockOrderAsync(tenantId, initialReturn.OrderId, ct);
            await _context.Locks.LockSalesReturnAsync(tenantId, initialReturn.Id, ct);
            await _context.Locks.LockBranchInventoryRowsAsync(tenantId, initialReturn.BranchId, sortedSkuIds, ct);
            await _context.Locks.LockWarehouseInventoryRowsAsync(tenantId, initialReturn.BranchId, initialReturn.WarehouseId, sortedSkuIds, ct);

            // 3. Tải lại phiếu trả tươi mới dưới khóa
            _context.UnitOfWork.ResetTracking();
            var returnTicket = await _context.SalesReturns
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);

            if (returnTicket == null)
            {
                throw new OismException("SALES_RETURN_NOT_FOUND", $"Không tìm thấy phiếu trả hàng '{id}'.", 404);
            }

            if (returnTicket.Status == SalesReturnStatus.Confirmed)
            {
                await tx.RollbackAsync(ct);
                return await MapToDtoAsync(returnTicket.Id, ct);
            }

            if (returnTicket.Status == SalesReturnStatus.Cancelled)
            {
                throw new OismException("INVALID_STATE_TRANSITION", $"Phiếu trả hàng '{returnTicket.Number}' đã bị hủy, không thể xác nhận.", 409);
            }

            // 4. Tải đơn hàng gốc
            var order = await _context.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == returnTicket.OrderId && o.TenantId == tenantId, ct);

            if (order == null)
            {
                throw new OismException("ORDER_NOT_FOUND", "Không tìm thấy đơn hàng gốc.", 404);
            }

            if (order.Status != OrderStatus.Confirmed && order.Status != OrderStatus.Completed)
            {
                throw new OismException("ORDER_NOT_RETURNABLE", $"Đơn hàng '{order.Number}' không ở trạng thái hợp lệ để trả hàng.", 409);
            }

            var isMarketplace = order.Channel is "Shopee" or "TikTok" or "Lazada";

            // 5. Kiểm tra lại số lượng còn được trả dưới khóa
            var orderItemsById = order.Items.ToDictionary(i => i.Id);
            foreach (var item in returnTicket.Items)
            {
                if (!orderItemsById.TryGetValue(item.OrderItemId, out var orderItem))
                {
                    throw new OismException("VALIDATION_ERROR", $"Dòng đơn hàng '{item.OrderItemId}' không tồn tại.", 400);
                }

                var alreadyReturned = await _context.SalesReturnItems
                    .Where(i => i.TenantId == tenantId && i.OrderItemId == item.OrderItemId && i.SalesReturnId != returnTicket.Id && i.SalesReturn!.Status == SalesReturnStatus.Confirmed)
                    .SumAsync(i => i.Quantity, ct);

                if (alreadyReturned + item.Quantity > orderItem.Quantity)
                {
                    var remaining = Math.Max(0m, orderItem.Quantity - alreadyReturned);
                    throw new OismException("RETURN_QUANTITY_EXCEEDED", $"Số lượng trả ({item.Quantity}) vượt quá số lượng còn được trả ({remaining}) của mặt hàng '{orderItem.SkuNameSnapshot}'.", 422);
                }
            }

            // 5b. Kiểm tra lại tổng tiền hoàn của đơn dưới khóa (các phiếu Draft khác có thể đã được xác nhận sau khi phiếu này được tạo)
            if (!isMarketplace && returnTicket.TotalRefundAmount > 0)
            {
                var refundedByOthers = await _context.SalesReturns
                    .Where(sr => sr.TenantId == tenantId && sr.OrderId == order.Id && sr.Id != returnTicket.Id && sr.Status == SalesReturnStatus.Confirmed)
                    .SumAsync(sr => sr.TotalRefundAmount, ct);

                if (refundedByOthers + returnTicket.TotalRefundAmount > order.TotalAmount)
                {
                    throw new OismException("REFUND_AMOUNT_EXCEEDED", $"Tổng tiền hoàn ({refundedByOthers + returnTicket.TotalRefundAmount:N0}) vượt quá tổng giá trị đơn hàng ({order.TotalAmount:N0}).", 422);
                }
            }

            // 6. Xử lý phương thức hoàn tiền
            string? normalizedRefundMethod;
            if (isMarketplace)
            {
                normalizedRefundMethod = null;
            }
            else
            {
                if (returnTicket.TotalRefundAmount > 0)
                {
                    if (string.IsNullOrWhiteSpace(refundMethod) || (refundMethod != "Cash" && refundMethod != "BankTransfer"))
                    {
                        throw new OismException("VALIDATION_ERROR", "Phương thức hoàn tiền bắt buộc là 'Cash' hoặc 'BankTransfer'.", 400);
                    }
                    normalizedRefundMethod = refundMethod;
                }
                else
                {
                    normalizedRefundMethod = refundMethod is "Cash" or "BankTransfer" ? refundMethod : null;
                }
            }

            // 7. Tải tồn kho chi nhánh & kho, tăng tồn, tính lại WAC, ghi sổ IN
            var branchBalances = await _context.InventoryBalances
                .Where(b => b.TenantId == tenantId && b.BranchId == returnTicket.BranchId && sortedSkuIds.Contains(b.SkuId))
                .ToDictionaryAsync(b => b.SkuId, ct);

            var warehouseBalances = await _context.WarehouseInventoryBalances
                .Where(b => b.TenantId == tenantId && b.WarehouseId == returnTicket.WarehouseId && sortedSkuIds.Contains(b.SkuId))
                .ToDictionaryAsync(b => b.SkuId, ct);

            var now = DateTimeOffset.UtcNow;
            var sortedItems = returnTicket.Items.OrderBy(i => i.SkuId).ToList();

            foreach (var item in sortedItems)
            {
                var orderItem = orderItemsById[item.OrderItemId];
                var baseQty = item.Quantity * orderItem.FactorSnapshot;

                if (!branchBalances.TryGetValue(item.SkuId, out var branchBalance))
                {
                    branchBalance = new InventoryBalance
                    {
                        TenantId = tenantId,
                        BranchId = returnTicket.BranchId,
                        SkuId = item.SkuId,
                        OnHand = 0,
                        Reserved = 0,
                        AverageCost = 0,
                        LastSequence = 0,
                        CreatedAt = now
                    };
                    _context.InventoryBalances.Add(branchBalance);
                    branchBalances[item.SkuId] = branchBalance;
                }

                if (!warehouseBalances.TryGetValue(item.SkuId, out var warehouseBalance))
                {
                    warehouseBalance = new WarehouseInventoryBalance
                    {
                        TenantId = tenantId,
                        BranchId = returnTicket.BranchId,
                        WarehouseId = returnTicket.WarehouseId,
                        SkuId = item.SkuId,
                        OnHand = 0,
                        Reserved = 0,
                        LastSequence = 0,
                        CreatedAt = now
                    };
                    _context.WarehouseInventoryBalances.Add(warehouseBalance);
                    warehouseBalances[item.SkuId] = warehouseBalance;
                }

                var oldOnHand = branchBalance.OnHand;
                var oldWac = branchBalance.AverageCost;
                var originalUnitCost = item.OriginalUnitCost;
                var newWac = _wacCalculator.CalculateWac(oldOnHand, oldWac, baseQty, originalUnitCost);

                var newSequence = branchBalance.LastSequence + 1;
                var newBalanceAfter = oldOnHand + baseQty;

                branchBalance.OnHand += baseQty;
                branchBalance.AverageCost = newWac;
                branchBalance.LastSequence = newSequence;
                branchBalance.UpdatedAt = now;

                warehouseBalance.OnHand += baseQty;
                warehouseBalance.LastSequence = newSequence;
                warehouseBalance.UpdatedAt = now;

                var txEntry = new InventoryTransaction
                {
                    TenantId = tenantId,
                    BranchId = returnTicket.BranchId,
                    WarehouseId = returnTicket.WarehouseId,
                    SkuId = item.SkuId,
                    Type = "IN",
                    Quantity = baseQty,
                    UnitCost = decimal.Round(originalUnitCost, 6, MidpointRounding.AwayFromZero),
                    BalanceAfter = newBalanceAfter,
                    SequenceNo = newSequence,
                    SalesReturnItemId = item.Id,
                    CreatedAt = now
                };
                _context.InventoryTransactions.Add(txEntry);
            }

            // 8. Chuyển trạng thái phiếu trả sang Confirmed
            returnTicket.Confirm(userId, normalizedRefundMethod, now);

            // 9. Lưu thay đổi, SET CONSTRAINTS ALL IMMEDIATE, commit
            await _context.SaveChangesAsync(ct);
            await _context.Locks.SetConstraintsImmediateAsync(ct);
            await tx.CommitAsync(ct);

            // 10. Phát thông báo realtime sau khi commit thành công
            if (_realtimeNotifier != null)
            {
                foreach (var skuId in sortedSkuIds)
                {
                    var bal = await _context.InventoryBalances.AsNoTracking()
                        .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.BranchId == returnTicket.BranchId && b.SkuId == skuId, ct);
                    if (bal != null)
                    {
                        var newAvail = bal.OnHand - bal.Reserved;
                        var sId = skuId;
                        var bId = returnTicket.BranchId;
                        pendingNotifications.Add(() => _realtimeNotifier.NotifyStockChangedAsync(tenantId, bId, sId, newAvail, ct));
                    }
                }

                foreach (var notify in pendingNotifications)
                {
                    try { await notify(); } catch { /* Ignore realtime delivery failures */ }
                }
            }

            return await MapToDtoAsync(returnTicket.Id, ct);
        });
    }

    public async Task<SalesReturnResponseDto> CancelAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        return await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            _context.UnitOfWork.ResetTracking();
            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);

            var initialReturn = await _context.SalesReturns
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);

            if (initialReturn == null)
            {
                throw new OismException("SALES_RETURN_NOT_FOUND", $"Không tìm thấy phiếu trả hàng '{id}'.", 404);
            }

            // Cùng thứ tự khóa với ConfirmAsync (Order → SalesReturn) để hủy và xác nhận chạy tuần tự.
            await _context.Locks.LockOrderAsync(tenantId, initialReturn.OrderId, ct);
            await _context.Locks.LockSalesReturnAsync(tenantId, initialReturn.Id, ct);

            _context.UnitOfWork.ResetTracking();
            var returnTicket = await _context.SalesReturns
                .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);

            if (returnTicket == null)
            {
                throw new OismException("SALES_RETURN_NOT_FOUND", $"Không tìm thấy phiếu trả hàng '{id}'.", 404);
            }

            if (returnTicket.Status != SalesReturnStatus.Draft)
            {
                throw new OismException("INVALID_STATE_TRANSITION", $"Phiếu trả hàng ở trạng thái '{returnTicket.Status}' không thể hủy. Chỉ áp dụng cho phiếu Nháp (Draft).", 409);
            }

            returnTicket.Cancel(DateTimeOffset.UtcNow);
            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return await MapToDtoAsync(returnTicket.Id, ct);
        });
    }

    public async Task<PagedResult<SalesReturnResponseDto>> GetListAsync(SalesReturnFilterDto filter, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var query = _context.SalesReturns
            .Include(r => r.Branch)
            .Include(r => r.Warehouse)
            .Include(r => r.Order)
            .Include(r => r.Items)
                .ThenInclude(i => i.OrderItem)
            .Include(r => r.Items)
                .ThenInclude(i => i.Sku)
            .Include(r => r.Creator)
            .Include(r => r.Confirmer)
            .Where(r => r.TenantId == tenantId);

        if (filter.BranchId.HasValue)
            query = query.Where(r => r.BranchId == filter.BranchId.Value);

        if (filter.WarehouseId.HasValue)
            query = query.Where(r => r.WarehouseId == filter.WarehouseId.Value);

        if (filter.OrderId.HasValue)
            query = query.Where(r => r.OrderId == filter.OrderId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(r => r.Status == filter.Status);

        if (filter.FromDate.HasValue)
            query = query.Where(r => r.CreatedAt >= filter.FromDate.Value);

        if (filter.ToDate.HasValue)
            query = query.Where(r => r.CreatedAt < filter.ToDate.Value);

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.Trim().ToLower();
            query = query.Where(r => r.Number.ToLower().Contains(term) || r.Order!.Number.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(ct);

        var pageIndex = Math.Max(1, filter.PageIndex);
        var pageSize = Math.Max(1, Math.Min(100, filter.PageSize));

        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<SalesReturnResponseDto>
        {
            Items = items.Select(MapToResponseDto).ToList(),
            PageIndex = pageIndex,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<SalesReturnResponseDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        RequireTenantId();
        return await MapToDtoAsync(id, ct);
    }

    private async Task<SalesReturnResponseDto> MapToDtoAsync(Guid id, CancellationToken ct)
    {
        var tenantId = RequireTenantId();

        var returnTicket = await _context.SalesReturns
            .Include(r => r.Branch)
            .Include(r => r.Warehouse)
            .Include(r => r.Order)
            .Include(r => r.Items)
                .ThenInclude(i => i.OrderItem)
            .Include(r => r.Items)
                .ThenInclude(i => i.Sku)
            .Include(r => r.Creator)
            .Include(r => r.Confirmer)
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);

        if (returnTicket == null)
        {
            throw new OismException("SALES_RETURN_NOT_FOUND", $"Không tìm thấy phiếu trả hàng '{id}'.", 404);
        }

        return MapToResponseDto(returnTicket);
    }

    private static SalesReturnResponseDto MapToResponseDto(SalesReturn r)
    {
        return new SalesReturnResponseDto
        {
            Id = r.Id,
            RequestKey = r.RequestKey,
            Number = r.Number,
            OrderId = r.OrderId,
            OrderNumber = r.Order?.Number ?? string.Empty,
            OrderChannel = r.Order?.Channel ?? string.Empty,
            BranchId = r.BranchId,
            BranchName = r.Branch?.Name ?? string.Empty,
            WarehouseId = r.WarehouseId,
            WarehouseName = r.Warehouse?.Name ?? string.Empty,
            Status = r.Status,
            Reason = r.Reason,
            Note = r.Note,
            TotalRefundAmount = r.TotalRefundAmount,
            RefundMethod = r.RefundMethod,
            RefundedAt = r.RefundedAt,
            CreatedBy = r.CreatedBy,
            CreatedByName = r.Creator?.DisplayName,
            ConfirmedBy = r.ConfirmedBy,
            ConfirmedByName = r.Confirmer?.DisplayName,
            ConfirmedAt = r.ConfirmedAt,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt,
            Items = r.Items.Select(i =>
            {
                var factor = i.OrderItem?.FactorSnapshot ?? 1m;
                return new SalesReturnItemResponseDto
                {
                    Id = i.Id,
                    SalesReturnId = i.SalesReturnId,
                    OrderId = i.OrderId,
                    OrderItemId = i.OrderItemId,
                    SkuId = i.SkuId,
                    SkuCode = i.OrderItem?.SkuCodeSnapshot ?? i.Sku?.SKU ?? string.Empty,
                    SkuName = i.OrderItem?.SkuNameSnapshot ?? string.Empty,
                    UnitId = i.OrderItem?.UnitId,
                    UnitName = i.OrderItem?.UnitNameSnapshot ?? string.Empty,
                    FactorSnapshot = factor,
                    Quantity = i.Quantity,
                    BaseQuantity = i.Quantity * factor,
                    RefundAmount = i.RefundAmount,
                    OriginalUnitCost = i.OriginalUnitCost
                };
            }).ToList()
        };
    }

    private async Task<string> GenerateReturnNumberAsync(Guid tenantId, CancellationToken ct)
    {
        // Ngày nghiệp vụ theo UTC+07:00
        var dateStr = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).ToString("yyyyMMdd");
        var prefix = $"RT-{dateStr}-";

        var maxNumber = await _context.SalesReturns
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.Number.StartsWith(prefix))
            .OrderByDescending(r => r.Number)
            .Select(r => r.Number)
            .FirstOrDefaultAsync(ct);

        int nextSeq = 1;
        if (!string.IsNullOrEmpty(maxNumber) && maxNumber.Length >= prefix.Length)
        {
            var suffix = maxNumber[prefix.Length..];
            if (int.TryParse(suffix, out int parsed))
            {
                nextSeq = parsed + 1;
            }
        }

        return $"{prefix}{nextSeq:D4}";
    }
}
