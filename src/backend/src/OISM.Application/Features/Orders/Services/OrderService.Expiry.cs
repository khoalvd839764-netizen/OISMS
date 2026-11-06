using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Realtime.Dtos;
using OISM.Domain.Common;
using OISM.Domain.Entities;

namespace OISM.Application.Features.Orders.Services;

public partial class OrderService
{
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
                && (o.Status == OrderStatus.Draft || o.Status == OrderStatus.Reserved)
                && o.ConfirmedAt == null
                && o.CompletedAt == null
                && o.CancelledAt == null
                && (o.Payment == null || o.Payment.Status != PaymentStatus.Succeeded)
                && o.Reservations.Any(r => r.Status == ReservationStatus.Active && r.ExpiresAt <= effectiveCutoff));

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
                if (!order.CanExpireReservations)
                {
                    return false;
                }

                if (order.Reservations == null || order.Reservations.Count == 0)
                {
                    return false;
                }

                var activeReservations = order.Reservations.Where(r => r.Status == ReservationStatus.Active).ToList();
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
                        res.Expire(effectiveCutoff);
                    }
                    else
                    {
                        res.Release();
                    }
                }

                // 6. Cập nhật trạng thái đơn hàng sang Cancelled
                order.Cancel(now);

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
