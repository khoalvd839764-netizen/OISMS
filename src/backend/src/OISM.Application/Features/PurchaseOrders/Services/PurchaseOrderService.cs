using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Catalog.Units.Services;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.PurchaseOrders.Dtos;
using OISM.Domain.Common;
using OISM.Domain.Entities;

namespace OISM.Application.Features.PurchaseOrders.Services;

public class PurchaseOrderService : IPurchaseOrderService
{
    private const decimal MaxUnitPrice = 999_999_999_999_999.999999m;
    private const int MaxCloseReasonLength = 500;
    private const int MaxNoteLength = 1000;

    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _currentTenantService;
    private readonly ISqlLockService _sqlLockService;

    public PurchaseOrderService(
        IAppDbContext context,
        ICurrentTenantService currentTenantService,
        ISqlLockService sqlLockService)
    {
        _context = context;
        _currentTenantService = currentTenantService;
        _sqlLockService = sqlLockService;
    }

    private Guid RequireTenantId()
    {
        var tenantId = _currentTenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new OismException("UNAUTHORIZED", "Yêu cầu xác thực gian hàng (TenantId hợp lệ).", 401);
        }
        return tenantId.Value;
    }

    public async Task<PurchaseOrderResponseDto> CreateAsync(
        CreatePurchaseOrderDto dto,
        Guid requestKey,
        Guid userId,
        CancellationToken ct = default)
    {
        if (dto == null)
        {
            throw new OismException("VALIDATION_ERROR", "Dữ liệu phiếu mua không được để trống.", 400);
        }

        if (requestKey == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "RequestKey không được để trống.", 400);
        }

        var tenantId = RequireTenantId();
        ValidateHeaderAndItemsBasic(dto.SupplierId, dto.BranchId, dto.Number, dto.Note, dto.Items);

        return await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            _context.UnitOfWork.ResetTracking();
            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);

            var existing = await LoadPurchaseOrderGraphQuery()
                .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.RequestKey == requestKey, ct);

            if (existing != null)
            {
                if (!PayloadMatches(existing, dto))
                {
                    throw new IdempotencyPayloadMismatchException("RequestKey đã tồn tại nhưng nội dung phiếu mua hàng không khớp.");
                }

                var replayDto = MapToResponseDto(existing);
                replayDto.IsReplay = true;
                return replayDto;
            }

            var branch = await _context.Branches.FirstOrDefaultAsync(b => b.Id == dto.BranchId, ct);
            if (branch == null)
            {
                throw new OismException("NOT_FOUND", $"Không tìm thấy Chi nhánh với ID '{dto.BranchId}'.", 404);
            }

            var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.Id == dto.SupplierId, ct);
            if (supplier == null)
            {
                throw new OismException("NOT_FOUND", $"Không tìm thấy Nhà cung cấp với ID '{dto.SupplierId}'.", 404);
            }

            var skuIds = dto.Items.Select(i => i.SkuId).Distinct().ToList();
            var existingSkus = await _context.ProductVariants
                .Include(v => v.Product)
                .Include(v => v.SkuUnits)
                    .ThenInclude(u => u.Unit)
                .Where(v => skuIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, ct);

            if (existingSkus.Count != skuIds.Count)
            {
                var missing = skuIds.Except(existingSkus.Keys).First();
                throw new OismException("NOT_FOUND", $"Mặt hàng SKU với ID '{missing}' không tồn tại trong gian hàng.", 404);
            }

            var number = string.IsNullOrWhiteSpace(dto.Number)
                ? $"PO-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}"
                : dto.Number.Trim();

            var numberExists = await _context.PurchaseOrders.AnyAsync(p => p.Number == number, ct);
            if (numberExists)
            {
                throw new OismException("VALIDATION_ERROR", $"Mã phiếu mua '{number}' đã tồn tại trong gian hàng.", 409);
            }

            var now = DateTimeOffset.UtcNow;
            var po = new PurchaseOrder
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Number = number,
                RequestKey = requestKey,
                SupplierId = dto.SupplierId,
                BranchId = dto.BranchId,
                Status = PurchaseOrderStatus.Draft,
                Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
                CreatedBy = userId,
                CreatedAt = now
            };

            foreach (var itemDto in dto.Items)
            {
                var sku = existingSkus[itemDto.SkuId];
                var resolvedUnit = SkuUnitResolver.ResolveForPurchase(sku, sku.SkuUnits, itemDto.UnitId);
                SkuUnitResolver.ComputeBaseQuantity(itemDto.Quantity, resolvedUnit);

                po.Items.Add(new PurchaseOrderItem
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    PurchaseOrderId = po.Id,
                    SkuId = itemDto.SkuId,
                    UnitId = resolvedUnit.UnitId,
                    UnitNameSnapshot = resolvedUnit.UnitName,
                    FactorSnapshot = resolvedUnit.Factor,
                    QuantityDecimalPlaces = (short)resolvedUnit.DecimalPlaces,
                    Quantity = itemDto.Quantity,
                    UnitPrice = itemDto.UnitPrice,
                    ReceivedBaseQuantity = 0m,
                    CreatedAt = now
                });
            }

            _context.PurchaseOrders.Add(po);

            try
            {
                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (_context.DatabaseErrors.IsUniqueViolation(ex, "uq_purchase_orders_3"))
            {
                await tx.RollbackAsync(ct);
                _context.UnitOfWork.ResetTracking();

                var reloaded = await LoadPurchaseOrderGraphQuery()
                    .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.RequestKey == requestKey, ct);

                if (reloaded != null)
                {
                    if (!PayloadMatches(reloaded, dto))
                    {
                        throw new IdempotencyPayloadMismatchException("RequestKey đã tồn tại nhưng nội dung phiếu mua hàng không khớp.");
                    }

                    var replayDto = MapToResponseDto(reloaded);
                    replayDto.IsReplay = true;
                    return replayDto;
                }

                throw;
            }
            catch (DbUpdateException ex) when (_context.DatabaseErrors.IsUniqueViolation(ex, "uq_purchase_orders_2"))
            {
                await tx.RollbackAsync(ct);
                throw new OismException("VALIDATION_ERROR", $"Mã phiếu mua '{number}' đã tồn tại trong gian hàng.", 409);
            }

            return await LoadAndMapResponseAsync(po.Id, ct);
        });
    }

    public async Task<PurchaseOrderResponseDto> UpdateDraftAsync(
        Guid id,
        UpdatePurchaseOrderDraftDto dto,
        Guid userId,
        CancellationToken ct = default)
    {
        if (dto == null)
        {
            throw new OismException("VALIDATION_ERROR", "Dữ liệu phiếu mua không được để trống.", 400);
        }

        var tenantId = RequireTenantId();
        ValidateHeaderAndItemsBasic(dto.SupplierId, dto.BranchId, dto.Number, dto.Note, dto.Items);

        return await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            _context.UnitOfWork.ResetTracking();
            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);

            await _sqlLockService.LockPurchaseOrderAsync(tenantId, id, ct);

            var po = await _context.PurchaseOrders
                .Include(p => p.Items)
                .FirstOrDefaultAsync(p => p.Id == id, ct);

            if (po == null)
            {
                throw new OismException("NOT_FOUND", $"Không tìm thấy phiếu mua hàng với ID '{id}'.", 404);
            }

            if (!po.IsDraft)
            {
                throw new OismException("INVALID_STATE_TRANSITION", $"Phiếu mua '{po.Number}' đang ở trạng thái '{po.Status}', chỉ được sửa khi ở trạng thái 'Draft'.", 409);
            }

            var branch = await _context.Branches.FirstOrDefaultAsync(b => b.Id == dto.BranchId, ct);
            if (branch == null)
            {
                throw new OismException("NOT_FOUND", $"Không tìm thấy Chi nhánh với ID '{dto.BranchId}'.", 404);
            }

            var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.Id == dto.SupplierId, ct);
            if (supplier == null)
            {
                throw new OismException("NOT_FOUND", $"Không tìm thấy Nhà cung cấp với ID '{dto.SupplierId}'.", 404);
            }

            var skuIds = dto.Items.Select(i => i.SkuId).Distinct().ToList();
            var existingSkus = await _context.ProductVariants
                .Include(v => v.Product)
                .Include(v => v.SkuUnits)
                    .ThenInclude(u => u.Unit)
                .Where(v => skuIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, ct);

            if (existingSkus.Count != skuIds.Count)
            {
                var missing = skuIds.Except(existingSkus.Keys).First();
                throw new OismException("NOT_FOUND", $"Mặt hàng SKU với ID '{missing}' không tồn tại trong gian hàng.", 404);
            }

            if (!string.IsNullOrWhiteSpace(dto.Number) && !string.Equals(dto.Number.Trim(), po.Number, StringComparison.Ordinal))
            {
                var newNum = dto.Number.Trim();
                var exists = await _context.PurchaseOrders.AnyAsync(p => p.Number == newNum && p.Id != po.Id, ct);
                if (exists)
                {
                    throw new OismException("VALIDATION_ERROR", $"Mã phiếu mua '{newNum}' đã tồn tại trong gian hàng.", 409);
                }
                po.Number = newNum;
            }

            var now = DateTimeOffset.UtcNow;
            po.SupplierId = dto.SupplierId;
            po.BranchId = dto.BranchId;
            po.Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
            po.UpdatedAt = now;

            // Xóa toàn bộ dòng cũ và chụp lại dòng mới
            foreach (var oldItem in po.Items.ToList())
            {
                _context.PurchaseOrderItems.Remove(oldItem);
            }
            po.Items.Clear();

            // Lưu xóa trước để tránh đụng unique constraint (tenant_id, purchase_order_id, sku_id)
            await _context.SaveChangesAsync(ct);

            foreach (var itemDto in dto.Items)
            {
                var sku = existingSkus[itemDto.SkuId];
                var resolvedUnit = SkuUnitResolver.ResolveForPurchase(sku, sku.SkuUnits, itemDto.UnitId);
                SkuUnitResolver.ComputeBaseQuantity(itemDto.Quantity, resolvedUnit);

                var newItem = new PurchaseOrderItem
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    PurchaseOrderId = po.Id,
                    SkuId = itemDto.SkuId,
                    UnitId = resolvedUnit.UnitId,
                    UnitNameSnapshot = resolvedUnit.UnitName,
                    FactorSnapshot = resolvedUnit.Factor,
                    QuantityDecimalPlaces = (short)resolvedUnit.DecimalPlaces,
                    Quantity = itemDto.Quantity,
                    UnitPrice = itemDto.UnitPrice,
                    ReceivedBaseQuantity = 0m,
                    CreatedAt = now
                };
                _context.PurchaseOrderItems.Add(newItem);
            }

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return await LoadAndMapResponseAsync(po.Id, ct);
        });
    }

    public async Task<PurchaseOrderResponseDto> ApproveAsync(
        Guid id,
        bool confirmZeroPrice,
        Guid userId,
        CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        return await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            _context.UnitOfWork.ResetTracking();
            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);

            await _sqlLockService.LockPurchaseOrderAsync(tenantId, id, ct);

            var po = await _context.PurchaseOrders
                .Include(p => p.Items)
                .FirstOrDefaultAsync(p => p.Id == id, ct);

            if (po == null)
            {
                throw new OismException("NOT_FOUND", $"Không tìm thấy phiếu mua hàng với ID '{id}'.", 404);
            }

            if (!po.IsDraft)
            {
                throw new OismException("INVALID_STATE_TRANSITION", $"Phiếu mua '{po.Number}' đang ở trạng thái '{po.Status}', chỉ được duyệt phiếu ở trạng thái 'Draft'.", 409);
            }

            if (po.Items.Count == 0)
            {
                throw new OismException("VALIDATION_ERROR", "Phiếu mua hàng phải có ít nhất 1 dòng mặt hàng trước khi duyệt.", 400);
            }

            if (po.Items.Any(i => i.UnitPrice == 0m) && !confirmZeroPrice)
            {
                throw new PurchaseOrderZeroPriceConfirmRequiredException();
            }

            var now = DateTimeOffset.UtcNow;
            po.Approve(userId, now);

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return await LoadAndMapResponseAsync(po.Id, ct);
        });
    }

    public async Task<PurchaseOrderResponseDto> CloseAsync(
        Guid id,
        string? reason,
        Guid userId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new OismException("VALIDATION_ERROR", "Bắt buộc nhập lý do đóng phiếu mua hàng.", 400);
        }

        var trimmedReason = reason.Trim();
        if (trimmedReason.Length > MaxCloseReasonLength)
        {
            throw new OismException("VALIDATION_ERROR", $"Lý do đóng phiếu mua tối đa {MaxCloseReasonLength} ký tự.", 400);
        }

        var tenantId = RequireTenantId();

        return await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            _context.UnitOfWork.ResetTracking();
            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);

            await _sqlLockService.LockPurchaseOrderAsync(tenantId, id, ct);

            var po = await _context.PurchaseOrders
                .Include(p => p.Items)
                .FirstOrDefaultAsync(p => p.Id == id, ct);

            if (po == null)
            {
                throw new OismException("NOT_FOUND", $"Không tìm thấy phiếu mua hàng với ID '{id}'.", 404);
            }

            if (!po.CanReceive)
            {
                throw new OismException("INVALID_STATE_TRANSITION", $"Phiếu mua '{po.Number}' đang ở trạng thái '{po.Status}', chỉ được đóng phiếu ở trạng thái 'Approved' hoặc 'PartiallyReceived'.", 409);
            }

            var now = DateTimeOffset.UtcNow;
            po.Close(userId, trimmedReason, now);

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return await LoadAndMapResponseAsync(po.Id, ct);
        });
    }

    public async Task<PurchaseOrderResponseDto> CancelAsync(
        Guid id,
        Guid userId,
        CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        return await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            _context.UnitOfWork.ResetTracking();
            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);

            await _sqlLockService.LockPurchaseOrderAsync(tenantId, id, ct);

            var po = await _context.PurchaseOrders
                .Include(p => p.Items)
                .FirstOrDefaultAsync(p => p.Id == id, ct);

            if (po == null)
            {
                throw new OismException("NOT_FOUND", $"Không tìm thấy phiếu mua hàng với ID '{id}'.", 404);
            }

            if (!po.IsCancellableStatus)
            {
                throw new OismException("INVALID_STATE_TRANSITION", $"Phiếu mua '{po.Number}' đang ở trạng thái '{po.Status}', chỉ được hủy khi ở trạng thái 'Draft' hoặc 'Approved' chưa nhận hàng.", 409);
            }

            if (po.HasReceivedItems)
            {
                throw new OismException("INVALID_STATE_TRANSITION", $"Phiếu mua '{po.Number}' đã phát sinh hàng nhận, không thể hủy.", 409);
            }

            var now = DateTimeOffset.UtcNow;
            po.Cancel(userId, now);

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return await LoadAndMapResponseAsync(po.Id, ct);
        });
    }

    public async Task<PagedResult<PurchaseOrderResponseDto>> GetPurchaseOrdersAsync(
        PurchaseOrderFilterDto filter,
        CancellationToken ct = default)
    {
        RequireTenantId();

        var query = LoadPurchaseOrderGraphQuery().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var status = filter.Status.Trim();
            query = query.Where(p => p.Status == status);
        }

        if (filter.SupplierId.HasValue)
        {
            query = query.Where(p => p.SupplierId == filter.SupplierId.Value);
        }

        if (filter.BranchId.HasValue)
        {
            query = query.Where(p => p.BranchId == filter.BranchId.Value);
        }

        var totalCount = await query.CountAsync(ct);

        var requestedPage = filter.Page > 1 ? filter.Page : filter.PageIndex;
        var pageIndex = Math.Max(1, requestedPage);
        var pageSize = Math.Max(1, Math.Min(100, filter.PageSize));

        var orders = await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<PurchaseOrderResponseDto>
        {
            Items = orders.Select(MapToResponseDto).ToList(),
            PageIndex = pageIndex,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<PurchaseOrderResponseDto?> GetPurchaseOrderByIdAsync(Guid id, CancellationToken ct = default)
    {
        RequireTenantId();

        var po = await LoadPurchaseOrderGraphQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        return po == null ? null : MapToResponseDto(po);
    }

    private IQueryable<PurchaseOrder> LoadPurchaseOrderGraphQuery()
    {
        return _context.PurchaseOrders
            .Include(p => p.Supplier)
            .Include(p => p.Branch)
            .Include(p => p.CreatedByUser)
            .Include(p => p.ApprovedByUser)
            .Include(p => p.ClosedByUser)
            .Include(p => p.CancelledByUser)
            .Include(p => p.Items)
                .ThenInclude(i => i.Sku)
                    .ThenInclude(s => s!.Product);
    }

    private async Task<PurchaseOrderResponseDto> LoadAndMapResponseAsync(Guid id, CancellationToken ct)
    {
        var po = await LoadPurchaseOrderGraphQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (po == null)
        {
            throw new OismException("NOT_FOUND", $"Không tìm thấy phiếu mua hàng với ID '{id}'.", 404);
        }

        return MapToResponseDto(po);
    }

    private static void ValidateHeaderAndItemsBasic(
        Guid supplierId,
        Guid branchId,
        string? number,
        string? note,
        List<CreatePurchaseOrderItemDto>? items)
    {
        if (supplierId == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "Nhà cung cấp là bắt buộc.", 400);
        }

        if (branchId == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "Chi nhánh là bắt buộc.", 400);
        }

        if (!string.IsNullOrWhiteSpace(number) && number.Trim().Length > 50)
        {
            throw new OismException("VALIDATION_ERROR", "Mã phiếu mua tối đa 50 ký tự.", 400);
        }

        if (!string.IsNullOrWhiteSpace(note) && note.Trim().Length > MaxNoteLength)
        {
            throw new OismException("VALIDATION_ERROR", $"Ghi chú phiếu mua tối đa {MaxNoteLength} ký tự.", 400);
        }

        if (items == null || items.Count == 0)
        {
            throw new OismException("VALIDATION_ERROR", "Phiếu mua phải có ít nhất 1 dòng mặt hàng.", 400);
        }

        if (items.Any(i => i == null))
        {
            throw new OismException("VALIDATION_ERROR", "Danh sách mặt hàng không được chứa phần tử rỗng.", 400);
        }

        var distinctSkus = new HashSet<Guid>();
        foreach (var item in items)
        {
            if (item.SkuId == Guid.Empty)
            {
                throw new OismException("VALIDATION_ERROR", "Mã mặt hàng SKU không hợp lệ.", 400);
            }

            if (!distinctSkus.Add(item.SkuId))
            {
                throw new OismException("VALIDATION_ERROR", "Phiếu mua không được chứa các dòng SKU trùng lặp.", 400);
            }

            if (item.Quantity <= 0m)
            {
                throw new OismException("VALIDATION_ERROR", "Số lượng đặt mua phải lớn hơn 0.", 400);
            }

            if (item.UnitPrice < 0m)
            {
                throw new OismException("VALIDATION_ERROR", "Đơn giá mua không được âm.", 400);
            }

            if (item.UnitPrice > MaxUnitPrice)
            {
                throw new OismException("VALIDATION_ERROR", "Đơn giá mua vượt quá giới hạn cho phép.", 400);
            }

            if (decimal.Round(item.UnitPrice, 6) != item.UnitPrice)
            {
                throw new OismException("VALIDATION_ERROR", "Đơn giá mua tối đa 6 chữ số thập phân.", 400);
            }
        }
    }

    private static bool PayloadMatches(PurchaseOrder existing, CreatePurchaseOrderDto incoming)
    {
        if (existing.SupplierId != incoming.SupplierId) return false;
        if (existing.BranchId != incoming.BranchId) return false;

        var incomingNote = string.IsNullOrWhiteSpace(incoming.Note) ? null : incoming.Note.Trim();
        if (!string.Equals(existing.Note, incomingNote, StringComparison.Ordinal)) return false;

        if (!string.IsNullOrWhiteSpace(incoming.Number) &&
            !string.Equals(existing.Number, incoming.Number.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        if (incoming.Items == null || existing.Items.Count != incoming.Items.Count) return false;

        foreach (var incItem in incoming.Items)
        {
            var match = existing.Items.FirstOrDefault(e =>
                e.SkuId == incItem.SkuId &&
                e.Quantity == incItem.Quantity &&
                e.UnitPrice == incItem.UnitPrice &&
                (incItem.UnitId == null
                    ? (e.UnitId == null || (e.Sku != null && e.UnitId == e.Sku.BaseUnitId) || e.FactorSnapshot == 1m)
                    : e.UnitId == incItem.UnitId));

            if (match == null) return false;
        }

        return true;
    }

    private static PurchaseOrderResponseDto MapToResponseDto(PurchaseOrder po)
    {
        var items = po.Items
            .OrderBy(i => i.CreatedAt)
            .ThenBy(i => i.Id)
            .Select(i =>
            {
                var orderedBase = i.Quantity * i.FactorSnapshot;
                var remainingBase = Math.Max(0m, orderedBase - i.ReceivedBaseQuantity);
                var remainingUnitQty = i.FactorSnapshot > 0m
                    ? decimal.Round(remainingBase / i.FactorSnapshot, 3, MidpointRounding.AwayFromZero)
                    : 0m;

                return new PurchaseOrderItemResponseDto
                {
                    Id = i.Id,
                    SkuId = i.SkuId,
                    SkuCode = i.Sku?.SKU ?? string.Empty,
                    ProductName = i.Sku?.Product?.Name ?? string.Empty,
                    UnitId = i.UnitId,
                    UnitName = i.UnitNameSnapshot,
                    FactorSnapshot = i.FactorSnapshot,
                    QuantityDecimalPlaces = i.QuantityDecimalPlaces,
                    Quantity = i.Quantity,
                    OrderedBaseQuantity = orderedBase,
                    UnitPrice = i.UnitPrice,
                    ReceivedBaseQuantity = i.ReceivedBaseQuantity,
                    RemainingBaseQuantity = remainingBase,
                    RemainingQuantity = remainingUnitQty
                };
            })
            .ToList();

        return new PurchaseOrderResponseDto
        {
            Id = po.Id,
            Number = po.Number,
            RequestKey = po.RequestKey,
            SupplierId = po.SupplierId,
            SupplierName = po.Supplier?.Name ?? string.Empty,
            BranchId = po.BranchId,
            BranchName = po.Branch?.Name ?? string.Empty,
            Status = po.Status,
            Note = po.Note,
            TotalAmount = po.Items.Sum(i => i.Quantity * i.UnitPrice),
            CreatedBy = po.CreatedBy,
            CreatedByName = po.CreatedByUser?.FullName,
            ApprovedBy = po.ApprovedBy,
            ApprovedByName = po.ApprovedByUser?.FullName,
            ApprovedAt = po.ApprovedAt,
            ClosedBy = po.ClosedBy,
            ClosedByName = po.ClosedByUser?.FullName,
            ClosedAt = po.ClosedAt,
            CloseReason = po.CloseReason,
            CancelledBy = po.CancelledBy,
            CancelledByName = po.CancelledByUser?.FullName,
            CancelledAt = po.CancelledAt,
            CreatedAt = po.CreatedAt,
            UpdatedAt = po.UpdatedAt,
            IsReplay = false,
            Items = items
        };
    }
}
