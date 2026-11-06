using System.Data;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Catalog.Units.Services;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Realtime;
using OISM.Domain.Entities;

namespace OISM.Application.Features.Inventory.Services;

public interface IGoodsReceiptService
{
    Task<GoodsReceiptResponseDto> CreateReceiptAsync(CreateGoodsReceiptDto dto, Guid userId, CancellationToken ct = default);
    Task<PagedResult<GoodsReceiptResponseDto>> GetReceiptsAsync(GoodsReceiptFilterDto filter, CancellationToken ct = default);
    Task<GoodsReceiptResponseDto?> GetReceiptByIdAsync(Guid id, CancellationToken ct = default);
    Task<GoodsReceiptResponseDto> ConfirmReceiptAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<GoodsReceiptResponseDto> CancelReceiptAsync(Guid id, Guid userId, CancellationToken ct = default);
}

public class GoodsReceiptService : IGoodsReceiptService
{
    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _currentTenantService;
    private readonly IWacCalculator _wacCalculator;
    private readonly ISqlLockService _sqlLockService;
    private readonly IRealtimeNotifier? _realtimeNotifier;

    public GoodsReceiptService(
        IAppDbContext context,
        ICurrentTenantService currentTenantService,
        IWacCalculator wacCalculator,
        ISqlLockService? sqlLockService = null,
        IRealtimeNotifier? realtimeNotifier = null)
    {
        _context = context;
        _currentTenantService = currentTenantService;
        _wacCalculator = wacCalculator;
        _sqlLockService = sqlLockService ?? context.Locks;
        _realtimeNotifier = realtimeNotifier;
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

    public async Task<GoodsReceiptResponseDto> CreateReceiptAsync(CreateGoodsReceiptDto dto, Guid userId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        if (dto == null)
        {
            throw new ArgumentException("Dữ liệu phiếu nhập không được để trống.");
        }

        if (dto.PurchaseOrderId == Guid.Empty)
        {
            throw new PurchaseOrderRequiredException();
        }

        // 1. Tải phiếu mua; phải là Approved hoặc PartiallyReceived
        var po = await _context.PurchaseOrders
            .Include(p => p.Branch)
            .Include(p => p.Supplier)
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == dto.PurchaseOrderId, ct);

        if (po == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy Phiếu mua hàng với ID '{dto.PurchaseOrderId}'.");
        }

        if (po.Status != "Approved" && po.Status != "PartiallyReceived")
        {
            throw new PurchaseOrderNotReceivableException(
                $"Phiếu mua '{po.Number}' đang ở trạng thái '{po.Status}', chỉ cho phép lập phiếu nhập khi ở 'Approved' hoặc 'PartiallyReceived'.");
        }

        // 2. Kho phải thuộc branch_id của phiếu mua
        var warehouse = await _context.Warehouses
            .FirstOrDefaultAsync(w => w.Id == dto.WarehouseId && w.BranchId == po.BranchId, ct);
        if (warehouse == null)
        {
            throw new InvalidOperationException($"Kho hàng ID '{dto.WarehouseId}' không tồn tại hoặc không thuộc chi nhánh của phiếu mua '{po.Number}'.");
        }

        // 3. Kiểm tra các dòng hàng
        if (dto.Items == null || dto.Items.Count == 0)
        {
            throw new InvalidOperationException("Phiếu nhập phải có ít nhất 1 dòng mặt hàng.");
        }
        if (dto.Items.Any(i => i == null))
        {
            throw new ArgumentException("Danh sách mặt hàng không được chứa phần tử rỗng (null).");
        }

        var poItemsById = po.Items.ToDictionary(i => i.Id);
        var seenPoItemIds = new HashSet<Guid>();
        var seenSkuIds = new HashSet<Guid>();

        foreach (var itemDto in dto.Items)
        {
            if (itemDto.PurchaseOrderItemId == Guid.Empty || !poItemsById.TryGetValue(itemDto.PurchaseOrderItemId, out var poItem))
            {
                throw new PurchaseOrderItemMismatchException($"Dòng phiếu mua '{itemDto.PurchaseOrderItemId}' không thuộc phiếu mua '{po.Number}'.");
            }

            if (!seenPoItemIds.Add(itemDto.PurchaseOrderItemId) || !seenSkuIds.Add(poItem.SkuId))
            {
                throw new PurchaseOrderItemMismatchException("Phiếu nhập không được chứa các dòng phiếu mua hoặc SKU trùng lặp.");
            }
        }

        // 4. Sinh mã phiếu nhập nếu chưa có
        var number = string.IsNullOrWhiteSpace(dto.Number)
            ? $"GR-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}"
            : dto.Number.Trim();

        var exists = await _context.GoodsReceipts.AnyAsync(r => r.Number == number, ct);
        if (exists)
        {
            throw DuplicateReceiptNumber(number);
        }

        var now = DateTimeOffset.UtcNow;
        var receipt = new GoodsReceipt
        {
            TenantId = tenantId,
            Number = number,
            PurchaseOrderId = po.Id,
            BranchId = po.BranchId,
            WarehouseId = dto.WarehouseId,
            SupplierId = po.SupplierId,
            Status = "Draft",
            Note = dto.Note,
            CreatedBy = userId,
            CreatedAt = now
        };

        foreach (var itemDto in dto.Items)
        {
            if (itemDto.Quantity <= 0)
            {
                throw new ArgumentException("Số lượng nhập phải lớn hơn 0.");
            }

            var poItem = poItemsById[itemDto.PurchaseOrderItemId];

            // Chép snapshot từ dòng phiếu mua; KHÔNG gọi ResolveForPurchase
            var resolvedFromSnapshot = new ResolvedSkuUnit(
                UnitId: poItem.UnitId,
                UnitName: poItem.UnitNameSnapshot,
                Factor: poItem.FactorSnapshot,
                DecimalPlaces: poItem.QuantityDecimalPlaces,
                IsBase: poItem.FactorSnapshot == 1m,
                Version: 1);

            var baseQuantity = SkuUnitResolver.ComputeBaseQuantity(itemDto.Quantity, resolvedFromSnapshot);
            var orderedBaseQuantity = poItem.Quantity * poItem.FactorSnapshot;

            // 5. Vượt lượng còn lại thì trả PURCHASE_ORDER_OVER_RECEIPT để báo sớm
            if (poItem.ReceivedBaseQuantity + baseQuantity > orderedBaseQuantity)
            {
                var remainingBase = Math.Max(0m, orderedBaseQuantity - poItem.ReceivedBaseQuantity);
                throw new PurchaseOrderOverReceiptException(
                    $"Số lượng quy đổi {baseQuantity:G29} vượt lượng còn được nhận ({remainingBase:G29}) của dòng phiếu mua.");
            }

            receipt.Items.Add(new GoodsReceiptItem
            {
                TenantId = tenantId,
                PurchaseOrderItemId = poItem.Id,
                SkuId = poItem.SkuId,
                UnitId = poItem.UnitId,
                UnitNameSnapshot = poItem.UnitNameSnapshot,
                FactorSnapshot = poItem.FactorSnapshot,
                Quantity = itemDto.Quantity,
                UnitPrice = poItem.UnitPrice,
                CreatedAt = now
            });
        }

        try
        {
            _context.GoodsReceipts.Add(receipt);
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (_context.DatabaseErrors.IsUniqueViolation(ex, "uq_goods_receipts_2"))
        {
            throw DuplicateReceiptNumber(number);
        }

        return await MapToResponseDtoAsync(receipt.Id, ct);
    }

    private static OismException DuplicateReceiptNumber(string number) =>
        new("VALIDATION_ERROR", $"Mã phiếu nhập '{number}' đã tồn tại trong gian hàng.", 409);

    public async Task<PagedResult<GoodsReceiptResponseDto>> GetReceiptsAsync(GoodsReceiptFilterDto filter, CancellationToken ct = default)
    {
        RequireTenantId();

        var query = _context.GoodsReceipts
            .Include(r => r.PurchaseOrder)
            .Include(r => r.Branch)
            .Include(r => r.Warehouse)
            .Include(r => r.Supplier)
            .Include(r => r.CreatedByUser)
            .Include(r => r.ConfirmedByUser)
            .Include(r => r.Items)
                .ThenInclude(i => i.Sku)
                    .ThenInclude(s => s!.Product)
            .AsNoTracking();

        if (filter.BranchId.HasValue)
            query = query.Where(r => r.BranchId == filter.BranchId.Value);

        if (filter.WarehouseId.HasValue)
            query = query.Where(r => r.WarehouseId == filter.WarehouseId.Value);

        if (filter.SupplierId.HasValue)
            query = query.Where(r => r.SupplierId == filter.SupplierId.Value);

        if (filter.PurchaseOrderId.HasValue)
            query = query.Where(r => r.PurchaseOrderId == filter.PurchaseOrderId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(r => r.Status == filter.Status);

        if (filter.FromDate.HasValue)
            query = query.Where(r => r.CreatedAt >= filter.FromDate.Value);

        if (filter.ToDate.HasValue)
            query = query.Where(r => r.CreatedAt <= filter.ToDate.Value);

        var totalCount = await query.CountAsync(ct);

        var pageIndex = Math.Max(1, filter.PageIndex);
        var pageSize = Math.Max(1, Math.Min(100, filter.PageSize));

        var receipts = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = receipts.Select(r => MapReceiptToDto(r)).ToList();

        return new PagedResult<GoodsReceiptResponseDto>
        {
            Items = items,
            PageIndex = pageIndex,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<GoodsReceiptResponseDto?> GetReceiptByIdAsync(Guid id, CancellationToken ct = default)
    {
        RequireTenantId();
        return await MapToResponseDtoAsync(id, ct);
    }

    public async Task<GoodsReceiptResponseDto> ConfirmReceiptAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        List<Func<Task>> pendingNotifications = new();
        var result = await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            pendingNotifications.Clear();
            _context.UnitOfWork.ResetTracking();
            await using var transaction = await _context.UnitOfWork.BeginTransactionAsync(ct);

            try
            {
                // 1. Advisory lock phiếu nhập
                await _sqlLockService.LockGoodsReceiptAsync(tenantId, id, ct);

                // 2. Tải phiếu nhập tươi mới từ DB sau khi đã có khóa chứng từ
                var receipt = await _context.GoodsReceipts
                    .Include(r => r.Items)
                    .FirstOrDefaultAsync(r => r.Id == id, ct);

                if (receipt == null)
                {
                    throw new KeyNotFoundException($"Không tìm thấy Phiếu nhập kho với ID '{id}'.");
                }

                if (receipt.Status == "Confirmed")
                {
                    throw new InvalidOperationException($"Phiếu nhập '{receipt.Number}' đã được xác nhận trước đó.");
                }

                if (receipt.Status == "Cancelled")
                {
                    throw new OismException("INVALID_STATE_TRANSITION", $"Không thể xác nhận phiếu nhập '{receipt.Number}' đã bị hủy.", 409);
                }

                if (receipt.Items.Count == 0)
                {
                    throw new InvalidOperationException("Phiếu nhập không có dòng hàng nào để xác nhận.");
                }

                // 3. PurchaseOrderId hoặc bất kỳ PurchaseOrderItemId nào trống thì trả PURCHASE_ORDER_REQUIRED
                if (!receipt.PurchaseOrderId.HasValue || receipt.PurchaseOrderId.Value == Guid.Empty ||
                    receipt.Items.Any(i => !i.PurchaseOrderItemId.HasValue || i.PurchaseOrderItemId.Value == Guid.Empty))
                {
                    throw new PurchaseOrderRequiredException($"Phiếu nhập '{receipt.Number}' chưa gắn với phiếu mua hàng hợp lệ.");
                }

                var poId = receipt.PurchaseOrderId.Value;

                // 4. LockPurchaseOrderAsync (đúng thứ tự: phiếu nhập -> phiếu mua -> SKU)
                await _sqlLockService.LockPurchaseOrderAsync(tenantId, poId, ct);

                // 5. Tải phiếu mua và các dòng SAU KHÓA
                var po = await _context.PurchaseOrders
                    .Include(p => p.Items)
                    .FirstOrDefaultAsync(p => p.Id == poId, ct);

                if (po == null)
                {
                    throw new PurchaseOrderItemMismatchException("Không tìm thấy phiếu mua hàng tương ứng với phiếu nhập.");
                }

                if (po.Status != "Approved" && po.Status != "PartiallyReceived" && po.Status != "Received")
                {
                    throw new PurchaseOrderNotReceivableException(
                        $"Phiếu mua '{po.Number}' đang ở trạng thái '{po.Status}', không thể xác nhận nhập kho.");
                }

                if (po.SupplierId != receipt.SupplierId || po.BranchId != receipt.BranchId)
                {
                    throw new PurchaseOrderItemMismatchException("Nhà cung cấp hoặc Chi nhánh trên phiếu nhập không khớp với phiếu mua.");
                }

                var poItemsById = po.Items.ToDictionary(i => i.Id);
                var seenPoItemIds = new HashSet<Guid>();

                foreach (var item in receipt.Items)
                {
                    var poItemId = item.PurchaseOrderItemId!.Value;
                    if (!seenPoItemIds.Add(poItemId) || !poItemsById.TryGetValue(poItemId, out var poItem))
                    {
                        throw new PurchaseOrderItemMismatchException($"Dòng nhập không khớp với dòng phiếu mua '{poItemId}'.");
                    }

                    if (poItem.SkuId != item.SkuId ||
                        poItem.UnitId != item.UnitId ||
                        poItem.UnitNameSnapshot != item.UnitNameSnapshot ||
                        poItem.FactorSnapshot != item.FactorSnapshot ||
                        poItem.UnitPrice != item.UnitPrice)
                    {
                        throw new PurchaseOrderItemMismatchException("Thông tin SKU, đơn vị, hệ số quy đổi hoặc đơn giá của dòng nhập không khớp với dòng phiếu mua.");
                    }

                    // 6. Kiểm tra vượt lượng còn được nhận trên từng dòng
                    var itemBaseQuantity = item.Quantity * item.FactorSnapshot;
                    var orderedBaseQuantity = poItem.Quantity * poItem.FactorSnapshot;
                    if (po.Status == "Received" || poItem.ReceivedBaseQuantity + itemBaseQuantity > orderedBaseQuantity)
                    {
                        var remainingBase = Math.Max(0m, orderedBaseQuantity - poItem.ReceivedBaseQuantity);
                        throw new PurchaseOrderOverReceiptException(
                            $"Số lượng nhận ({itemBaseQuantity:G29}) vượt quá số lượng còn được nhận ({remainingBase:G29}) trên dòng phiếu mua.");
                    }
                }

                // 7. Sắp xếp SKU theo thứ tự tăng dần cố định để phòng ngừa Deadlock giữa nhiều phiếu nhập cùng SKU
                var sortedItems = receipt.Items.OrderBy(i => i.SkuId).ToList();
                var skuIds = sortedItems.Select(i => i.SkuId).Distinct().ToList();

                await _sqlLockService.LockSkuStockAsync(tenantId, receipt.BranchId, skuIds, ct);

                // 8. Tải tồn kho hiện tại cấp Chi nhánh và cấp Kho, cộng tồn, tính WAC, ghi sổ kho
                var branchBalances = await _context.InventoryBalances
                    .Where(b => b.BranchId == receipt.BranchId && skuIds.Contains(b.SkuId))
                    .ToDictionaryAsync(b => b.SkuId, ct);

                var warehouseBalances = await _context.WarehouseInventoryBalances
                    .Where(b => b.WarehouseId == receipt.WarehouseId && skuIds.Contains(b.SkuId))
                    .ToDictionaryAsync(b => b.SkuId, ct);

                var now = DateTimeOffset.UtcNow;

                foreach (var item in sortedItems)
                {
                    if (!branchBalances.TryGetValue(item.SkuId, out var branchBalance))
                    {
                        branchBalance = new InventoryBalance
                        {
                            TenantId = tenantId,
                            BranchId = receipt.BranchId,
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
                            BranchId = receipt.BranchId,
                            WarehouseId = receipt.WarehouseId,
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
                    var baseQuantity = item.BaseQuantity;
                    var baseUnitCost = item.UnitPrice / item.FactorSnapshot;
                    var newWac = _wacCalculator.CalculateWac(oldOnHand, oldWac, baseQuantity, baseUnitCost);

                    var newSequence = branchBalance.LastSequence + 1;
                    var newBalanceAfter = oldOnHand + baseQuantity;

                    branchBalance.OnHand += baseQuantity;
                    branchBalance.AverageCost = newWac;
                    branchBalance.LastSequence = newSequence;
                    branchBalance.UpdatedAt = now;

                    warehouseBalance.OnHand += baseQuantity;
                    warehouseBalance.LastSequence = newSequence;
                    warehouseBalance.UpdatedAt = now;

                    var ledgerEntry = new InventoryTransaction
                    {
                        TenantId = tenantId,
                        BranchId = receipt.BranchId,
                        WarehouseId = receipt.WarehouseId,
                        SkuId = item.SkuId,
                        Type = "IN",
                        Quantity = baseQuantity,
                        UnitCost = decimal.Round(baseUnitCost, 6, MidpointRounding.AwayFromZero),
                        BalanceAfter = newBalanceAfter,
                        SequenceNo = newSequence,
                        ReceiptItemId = item.Id,
                        CreatedAt = now
                    };
                    _context.InventoryTransactions.Add(ledgerEntry);

                    // 9. Cộng lượng đã nhận trên dòng phiếu mua
                    var poItem = poItemsById[item.PurchaseOrderItemId!.Value];
                    poItem.ReceivedBaseQuantity += baseQuantity;
                    poItem.UpdatedAt = now;
                }

                // Cập nhật trạng thái phiếu mua: đủ mọi dòng thì Received, còn lại PartiallyReceived
                var allFullyReceived = po.Items.All(pi => pi.ReceivedBaseQuantity >= pi.Quantity * pi.FactorSnapshot);
                po.Status = allFullyReceived ? "Received" : "PartiallyReceived";
                po.UpdatedAt = now;

                // Cập nhật trạng thái phiếu nhập
                receipt.Status = "Confirmed";
                receipt.ConfirmedAt = now;
                receipt.ConfirmedBy = userId;
                receipt.UpdatedAt = now;

                // 10. SaveChanges, SET CONSTRAINTS ALL IMMEDIATE, commit
                await _context.SaveChangesAsync(ct);

                await _sqlLockService.SetConstraintsImmediateAsync(ct);

                await transaction.CommitAsync(ct);

                if (_realtimeNotifier != null)
                {
                    foreach (var item in receipt.Items)
                    {
                        var bal = await _context.InventoryBalances.AsNoTracking()
                            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.BranchId == receipt.BranchId && b.SkuId == item.SkuId, ct);
                        if (bal != null)
                        {
                            var newAvail = bal.OnHand - bal.Reserved;
                            var skuIdCapture = item.SkuId;
                            var branchIdCapture = receipt.BranchId;
                            pendingNotifications.Add(() => _realtimeNotifier.NotifyStockChangedAsync(tenantId, branchIdCapture, skuIdCapture, newAvail, ct));
                        }
                    }
                }

                return await MapToResponseDtoAsync(receipt.Id, ct);
            }
            catch (DbUpdateException dbEx) when (_context.DatabaseErrors.IsCheckViolation(dbEx, "ck_purchase_order_items_received_range"))
            {
                await transaction.RollbackAsync(ct);
                throw new PurchaseOrderOverReceiptException();
            }
            catch (DbUpdateException dbEx) when (_context.DatabaseErrors.IsUniqueViolation(dbEx))
            {
                await transaction.RollbackAsync(ct);
                _context.UnitOfWork.ResetTracking();
                var isConfirmed = await _context.GoodsReceipts.AnyAsync(r => r.Id == id && r.Status == "Confirmed", ct);
                if (isConfirmed)
                {
                    throw new OismException("INVALID_STATE_TRANSITION", $"Phiếu nhập '{id}' đã được xác nhận trước đó.", 409);
                }
                throw;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
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
                catch (Exception)
                {
                }
            }
        }

        return result;
    }

    public async Task<GoodsReceiptResponseDto> CancelReceiptAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        return await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            _context.UnitOfWork.ResetTracking();
            await using var transaction = await _context.UnitOfWork.BeginTransactionAsync(ct);

            try
            {
                // Cùng khóa phiếu nhập với xác nhận
                await _sqlLockService.LockGoodsReceiptAsync(tenantId, id, ct);

                var receipt = await _context.GoodsReceipts
                    .Include(r => r.Items)
                    .FirstOrDefaultAsync(r => r.Id == id, ct);

                if (receipt == null)
                {
                    throw new KeyNotFoundException($"Không tìm thấy Phiếu nhập kho với ID '{id}'.");
                }

                if (receipt.Status != "Draft")
                {
                    throw new OismException("INVALID_STATE_TRANSITION", $"Phiếu nhập '{receipt.Number}' đang ở trạng thái '{receipt.Status}', chỉ được hủy khi ở trạng thái 'Draft'.", 409);
                }

                receipt.Status = "Cancelled";
                receipt.UpdatedAt = DateTimeOffset.UtcNow;

                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                return await MapToResponseDtoAsync(receipt.Id, ct);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    private async Task<GoodsReceiptResponseDto> MapToResponseDtoAsync(Guid id, CancellationToken ct)
    {
        var receipt = await _context.GoodsReceipts
            .Include(r => r.PurchaseOrder)
            .Include(r => r.Branch)
            .Include(r => r.Warehouse)
            .Include(r => r.Supplier)
            .Include(r => r.CreatedByUser)
            .Include(r => r.ConfirmedByUser)
            .Include(r => r.Items)
                .ThenInclude(i => i.Sku)
                    .ThenInclude(s => s!.Product)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (receipt == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy Phiếu nhập kho với ID '{id}'.");
        }

        return MapReceiptToDto(receipt);
    }

    private static GoodsReceiptResponseDto MapReceiptToDto(GoodsReceipt receipt)
    {
        return new GoodsReceiptResponseDto
        {
            Id = receipt.Id,
            Number = receipt.Number,
            PurchaseOrderId = receipt.PurchaseOrderId,
            PurchaseOrderNumber = receipt.PurchaseOrder?.Number,
            BranchId = receipt.BranchId,
            BranchName = receipt.Branch?.Name ?? string.Empty,
            WarehouseId = receipt.WarehouseId,
            WarehouseName = receipt.Warehouse?.Name ?? string.Empty,
            SupplierId = receipt.SupplierId,
            SupplierName = receipt.Supplier?.Name ?? string.Empty,
            Status = receipt.Status,
            Note = receipt.Note,
            TotalAmount = receipt.Items.Sum(i => i.Quantity * i.UnitPrice),
            CreatedBy = receipt.CreatedBy,
            CreatedByName = receipt.CreatedByUser?.FullName,
            ConfirmedAt = receipt.ConfirmedAt,
            ConfirmedBy = receipt.ConfirmedBy,
            ConfirmedByName = receipt.ConfirmedByUser?.FullName,
            CreatedAt = receipt.CreatedAt,
            UpdatedAt = receipt.UpdatedAt,
            Items = receipt.Items.Select(i => new GoodsReceiptItemResponseDto
            {
                Id = i.Id,
                PurchaseOrderItemId = i.PurchaseOrderItemId,
                SkuId = i.SkuId,
                SkuCode = i.Sku?.SKU ?? string.Empty,
                ProductName = i.Sku?.Product?.Name ?? string.Empty,
                UnitId = i.UnitId,
                UnitName = i.UnitNameSnapshot,
                FactorSnapshot = i.FactorSnapshot,
                BaseQuantity = i.BaseQuantity,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList()
        };
    }
}
