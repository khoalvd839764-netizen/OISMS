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
    private const int MaxReturnReasonLength = 500;
    private const int MaxNoteLength = 1000;
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

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

    private static DateOnly GetTodayVietnam()
    {
        var nowVn = DateTimeOffset.UtcNow.ToOffset(VietnamOffset);
        return DateOnly.FromDateTime(nowVn.DateTime);
    }

    public async Task<PurchaseOrderResponseDto> CreateAsync(
        CreatePurchaseOrderDto dto,
        Guid requestKey,
        Guid userId,
        CancellationToken ct = default,
        bool isOwner = true)
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
        ValidateHeaderAndItemsBasic(dto.BranchId, dto.Number, dto.Note, dto.Items, isOwner);

        var todayVn = GetTodayVietnam();
        var orderDate = todayVn;
        var shippingFee = 0m;
        var discountAmount = 0m;
        var vatAmount = 0m;
        Guid? supplierId = null;

        if (isOwner)
        {
            if (dto.OrderDate.HasValue)
            {
                if (dto.OrderDate.Value > todayVn)
                {
                    throw new OismException("VALIDATION_ERROR", "Ngày đặt hàng không được lớn hơn ngày hiện tại.", 422);
                }
                orderDate = dto.OrderDate.Value;
            }

            shippingFee = dto.ShippingFee ?? 0m;
            discountAmount = dto.DiscountAmount ?? 0m;
            vatAmount = dto.VatAmount ?? 0m;

            ValidateFees(shippingFee, discountAmount, vatAmount);

            if (dto.SupplierId.HasValue && dto.SupplierId.Value != Guid.Empty)
            {
                supplierId = dto.SupplierId.Value;
            }
        }

        return await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            _context.UnitOfWork.ResetTracking();
            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);

            var existing = await LoadPurchaseOrderGraphQuery()
                .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.RequestKey == requestKey, ct);

            if (existing != null)
            {
                if (!PayloadMatches(existing, dto, isOwner))
                {
                    throw new IdempotencyPayloadMismatchException("RequestKey đã tồn tại nhưng nội dung phiếu mua hàng không khớp.");
                }

                var replayDto = MapToResponseDto(existing, isOwner);
                replayDto.IsReplay = true;
                return replayDto;
            }

            var branch = await _context.Branches.FirstOrDefaultAsync(b => b.Id == dto.BranchId, ct);
            if (branch == null)
            {
                throw new OismException("NOT_FOUND", $"Không tìm thấy Chi nhánh với ID '{dto.BranchId}'.", 404);
            }

            if (isOwner && supplierId.HasValue)
            {
                var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.Id == supplierId.Value, ct);
                if (supplier == null)
                {
                    throw new OismException("NOT_FOUND", $"Không tìm thấy Nhà cung cấp với ID '{supplierId.Value}'.", 404);
                }
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
                SupplierId = isOwner ? supplierId : null,
                BranchId = dto.BranchId,
                OrderDate = orderDate,
                ShippingFee = shippingFee,
                DiscountAmount = discountAmount,
                VatAmount = vatAmount,
                Status = isOwner ? PurchaseOrderStatus.PendingApproval : PurchaseOrderStatus.Draft,
                Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
                Version = 1,
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
                    UnitPrice = isOwner ? itemDto.UnitPrice : null,
                    AllocatedUnitCost = null,
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
                    if (!PayloadMatches(reloaded, dto, isOwner))
                    {
                        throw new IdempotencyPayloadMismatchException("RequestKey đã tồn tại nhưng nội dung phiếu mua hàng không khớp.");
                    }

                    var replayDto = MapToResponseDto(reloaded, isOwner);
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

            return await LoadAndMapResponseAsync(po.Id, isOwner, ct);
        });
    }

    public async Task<PurchaseOrderResponseDto> UpdateDraftAsync(
        Guid id,
        UpdatePurchaseOrderDraftDto dto,
        Guid userId,
        CancellationToken ct = default,
        bool isOwner = true)
    {
        if (dto == null)
        {
            throw new OismException("VALIDATION_ERROR", "Dữ liệu phiếu mua không được để trống.", 400);
        }

        var tenantId = RequireTenantId();
        ValidateHeaderAndItemsBasic(dto.BranchId, dto.Number, dto.Note, dto.Items, isOwner);

        var todayVn = GetTodayVietnam();
        if (isOwner)
        {
            if (dto.OrderDate.HasValue && dto.OrderDate.Value > todayVn)
            {
                throw new OismException("VALIDATION_ERROR", "Ngày đặt hàng không được lớn hơn ngày hiện tại.", 422);
            }

            ValidateFees(dto.ShippingFee ?? 0m, dto.DiscountAmount ?? 0m, dto.VatAmount ?? 0m);
        }

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

            if (!isOwner)
            {
                if (!po.IsDraft)
                {
                    throw new OismException("INVALID_STATE_TRANSITION", $"Phiếu mua '{po.Number}' đang ở trạng thái '{po.Status}', nhân viên chỉ được sửa khi ở trạng thái 'Draft'.", 409);
                }

                if (po.CreatedBy != userId)
                {
                    throw new OismException("FORBIDDEN_ACCESS", "Nhân viên chỉ được chỉnh sửa đơn đề nghị mua nháp do chính mình tạo.", 403);
                }
            }
            else
            {
                if (!po.IsPendingApproval && !po.IsDraft)
                {
                    throw new OismException("INVALID_STATE_TRANSITION", $"Phiếu mua '{po.Number}' đang ở trạng thái '{po.Status}', chỉ được sửa khi ở trạng thái 'Draft' hoặc 'PendingApproval'.", 409);
                }
            }

            if (dto.Version.HasValue && dto.Version.Value != po.Version)
            {
                throw new OismException("PURCHASE_ORDER_VERSION_MISMATCH", $"Phiên bản đơn mua không khớp (hiện tại: {po.Version}, gửi lên: {dto.Version.Value}). Vui lòng tải lại.", 409);
            }

            var branch = await _context.Branches.FirstOrDefaultAsync(b => b.Id == dto.BranchId, ct);
            if (branch == null)
            {
                throw new OismException("NOT_FOUND", $"Không tìm thấy Chi nhánh với ID '{dto.BranchId}'.", 404);
            }

            Guid? supplierId = null;
            if (isOwner && dto.SupplierId.HasValue && dto.SupplierId.Value != Guid.Empty)
            {
                var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.Id == dto.SupplierId.Value, ct);
                if (supplier == null)
                {
                    throw new OismException("NOT_FOUND", $"Không tìm thấy Nhà cung cấp với ID '{dto.SupplierId.Value}'.", 404);
                }
                supplierId = dto.SupplierId.Value;
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
            po.SupplierId = isOwner ? supplierId : null;
            po.BranchId = dto.BranchId;
            if (isOwner)
            {
                if (dto.OrderDate.HasValue)
                {
                    po.OrderDate = dto.OrderDate.Value;
                }
                if (dto.ShippingFee.HasValue)
                {
                    po.ShippingFee = dto.ShippingFee.Value;
                }
                if (dto.DiscountAmount.HasValue)
                {
                    po.DiscountAmount = dto.DiscountAmount.Value;
                }
                if (dto.VatAmount.HasValue)
                {
                    po.VatAmount = dto.VatAmount.Value;
                }
            }
            po.Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
            po.Version++;
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
                    UnitPrice = isOwner ? itemDto.UnitPrice : null,
                    AllocatedUnitCost = null,
                    ReceivedBaseQuantity = 0m,
                    CreatedAt = now
                };
                _context.PurchaseOrderItems.Add(newItem);
            }

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return await LoadAndMapResponseAsync(po.Id, isOwner, ct);
        });
    }

    public async Task<PurchaseOrderResponseDto> SubmitAsync(
        Guid id,
        Guid userId,
        CancellationToken ct = default,
        bool isOwner = false)
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
                throw new OismException("INVALID_STATE_TRANSITION", $"Phiếu mua '{po.Number}' đang ở trạng thái '{po.Status}', chỉ được gửi duyệt khi ở trạng thái 'Draft'.", 409);
            }

            if (!isOwner && po.CreatedBy != userId)
            {
                throw new OismException("FORBIDDEN_ACCESS", "Nhân viên chỉ được gửi duyệt đơn đề nghị mua nháp do chính mình tạo.", 403);
            }

            if (po.Items.Count == 0)
            {
                throw new OismException("VALIDATION_ERROR", "Phiếu mua hàng phải có ít nhất 1 dòng mặt hàng trước khi gửi duyệt.", 400);
            }

            var now = DateTimeOffset.UtcNow;
            po.Submit(now);

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return await LoadAndMapResponseAsync(po.Id, isOwner, ct);
        });
    }

    public async Task<PurchaseOrderResponseDto> ReturnAsync(
        Guid id,
        string? reason,
        Guid userId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new OismException("PURCHASE_ORDER_RETURN_REASON_REQUIRED", "Bắt buộc nhập lý do trả về đơn mua hàng.", 422);
        }

        var trimmedReason = reason.Trim();
        if (trimmedReason.Length > MaxReturnReasonLength)
        {
            throw new OismException("VALIDATION_ERROR", $"Lý do trả về đơn mua tối đa {MaxReturnReasonLength} ký tự.", 422);
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

            if (!po.IsPendingApproval)
            {
                throw new OismException("INVALID_STATE_TRANSITION", $"Phiếu mua '{po.Number}' đang ở trạng thái '{po.Status}', chỉ được trả về khi ở trạng thái 'PendingApproval'.", 409);
            }

            var now = DateTimeOffset.UtcNow;
            po.ReturnToDraft(userId, trimmedReason, now);

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return await LoadAndMapResponseAsync(po.Id, isOwner: true, ct);
        });
    }

    public async Task<PurchaseOrderResponseDto> ApproveAsync(
        Guid id,
        bool confirmZeroPrice,
        Guid userId,
        CancellationToken ct = default,
        int? version = null)
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

            if (!po.IsPendingApproval)
            {
                throw new OismException("INVALID_STATE_TRANSITION", $"Phiếu mua '{po.Number}' đang ở trạng thái '{po.Status}', chỉ được duyệt phiếu ở trạng thái 'PendingApproval'.", 409);
            }

            // Duyệt bắt buộc gửi phiên bản để Owner duyệt đúng bản đã kiểm tra (khóa lạc quan, Upgrade 12).
            if (!version.HasValue)
            {
                throw new OismException("VALIDATION_ERROR", "Bắt buộc gửi phiên bản đơn mua (version) khi duyệt.", 400);
            }

            if (version.Value != po.Version)
            {
                throw new OismException("PURCHASE_ORDER_VERSION_MISMATCH", $"Phiên bản đơn mua không khớp (hiện tại: {po.Version}, gửi lên: {version.Value}). Vui lòng tải lại trước khi duyệt.", 409);
            }

            if (po.Items.Count == 0)
            {
                throw new OismException("PURCHASE_ORDER_INCOMPLETE_FOR_APPROVAL", "Phiếu mua hàng phải có ít nhất 1 dòng mặt hàng trước khi duyệt.", 422);
            }

            if (!po.SupplierId.HasValue || po.SupplierId.Value == Guid.Empty)
            {
                throw new OismException("PURCHASE_ORDER_INCOMPLETE_FOR_APPROVAL", "Đơn mua chưa chọn nhà cung cấp, không thể duyệt.", 422);
            }

            var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.Id == po.SupplierId.Value, ct);
            if (supplier == null)
            {
                throw new OismException("NOT_FOUND", $"Không tìm thấy Nhà cung cấp với ID '{po.SupplierId.Value}'.", 404);
            }

            if (po.OrderDate > GetTodayVietnam())
            {
                throw new OismException("VALIDATION_ERROR", "Ngày đặt hàng không được lớn hơn ngày hiện tại.", 422);
            }

            if (po.Items.Any(i => !i.UnitPrice.HasValue))
            {
                throw new OismException("PURCHASE_ORDER_INCOMPLETE_FOR_APPROVAL", "Còn dòng hàng chưa nhập đơn giá mua, không thể duyệt.", 422);
            }

            if (po.Items.Any(i => i.UnitPrice!.Value < 0m))
            {
                throw new OismException("VALIDATION_ERROR", "Đơn giá mua không được âm.", 422);
            }

            if (po.Items.Any(i => i.UnitPrice!.Value == 0m) && !confirmZeroPrice)
            {
                throw new PurchaseOrderZeroPriceConfirmRequiredException();
            }

            if (po.ShippingFee < 0m || po.DiscountAmount < 0m || po.VatAmount < 0m)
            {
                throw new OismException("VALIDATION_ERROR", "Phí vận chuyển, chiết khấu và thuế VAT không được âm.", 422);
            }

            var orderedItems = po.Items
                .OrderBy(i => i.CreatedAt)
                .ThenBy(i => i.Id)
                .ToList();

            var subtotal = orderedItems.Sum(i => decimal.Round(i.Quantity * i.UnitPrice!.Value, 6, MidpointRounding.AwayFromZero));

            if (po.DiscountAmount > subtotal)
            {
                throw new OismException("PURCHASE_ORDER_DISCOUNT_EXCEEDS_SUBTOTAL", "Chiết khấu thương mại không được lớn hơn tổng tiền hàng.", 422);
            }

            var now = DateTimeOffset.UtcNow;
            var targetAllocatedTotal = subtotal + po.ShippingFee - po.DiscountAmount;
            var netAdjustment = po.ShippingFee - po.DiscountAmount;
            var totalQuantity = orderedItems.Sum(i => i.Quantity);
            var allocatedRunningTotal = 0m;

            for (var idx = 0; idx < orderedItems.Count; idx++)
            {
                var item = orderedItems[idx];
                var lineTotal = decimal.Round(item.Quantity * item.UnitPrice!.Value, 6, MidpointRounding.AwayFromZero);

                if (idx < orderedItems.Count - 1)
                {
                    decimal rawLineAllocated;
                    if (subtotal > 0m)
                    {
                        rawLineAllocated = lineTotal + (netAdjustment * lineTotal / subtotal);
                    }
                    else
                    {
                        rawLineAllocated = totalQuantity > 0m ? (po.ShippingFee * item.Quantity / totalQuantity) : 0m;
                    }

                    var allocatedUnitCost = decimal.Round(rawLineAllocated / item.Quantity, 6, MidpointRounding.AwayFromZero);
                    item.AllocatedUnitCost = allocatedUnitCost;
                    item.UpdatedAt = now;
                    allocatedRunningTotal += decimal.Round(allocatedUnitCost * item.Quantity, 6, MidpointRounding.AwayFromZero);
                }
                else
                {
                    var lastLineAllocatedTotal = targetAllocatedTotal - allocatedRunningTotal;
                    var lastAllocatedUnitCost = decimal.Round(lastLineAllocatedTotal / item.Quantity, 6, MidpointRounding.AwayFromZero);
                    if (lastAllocatedUnitCost < 0m)
                    {
                        // Chiết khấu sát tổng tiền hàng: phần làm tròn của các dòng trước vượt phần còn lại của dòng cuối.
                        throw new OismException("PURCHASE_ORDER_DISCOUNT_EXCEEDS_SUBTOTAL", "Chiết khấu thương mại quá sát tổng tiền hàng, giá vốn sau phân bổ của dòng cuối bị âm. Vui lòng giảm chiết khấu.", 422);
                    }
                    item.AllocatedUnitCost = lastAllocatedUnitCost;
                    item.UpdatedAt = now;
                }
            }

            // Lưu allocated_unit_cost trước khi chuyển trạng thái sang Approved để thỏa mãn trigger bảo vệ
            await _context.SaveChangesAsync(ct);

            po.Approve(userId, now);

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return await LoadAndMapResponseAsync(po.Id, isOwner: true, ct);
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

            return await LoadAndMapResponseAsync(po.Id, isOwner: true, ct);
        });
    }

    public async Task<PurchaseOrderResponseDto> CancelAsync(
        Guid id,
        Guid userId,
        CancellationToken ct = default,
        bool isOwner = true)
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

            if (!isOwner)
            {
                if (!po.IsDraft)
                {
                    throw new OismException("INVALID_STATE_TRANSITION", $"Phiếu mua '{po.Number}' đang ở trạng thái '{po.Status}', nhân viên chỉ được hủy khi ở trạng thái 'Draft'.", 409);
                }

                if (po.CreatedBy != userId)
                {
                    throw new OismException("FORBIDDEN_ACCESS", "Nhân viên chỉ được hủy đơn đề nghị mua nháp do chính mình tạo.", 403);
                }
            }
            else
            {
                if (!po.IsCancellableStatus)
                {
                    throw new OismException("INVALID_STATE_TRANSITION", $"Phiếu mua '{po.Number}' đang ở trạng thái '{po.Status}', chỉ được hủy khi ở trạng thái 'Draft', 'PendingApproval' hoặc 'Approved' chưa nhận hàng.", 409);
                }
            }

            if (po.HasReceivedItems)
            {
                throw new OismException("INVALID_STATE_TRANSITION", $"Phiếu mua '{po.Number}' đã phát sinh hàng nhận, không thể hủy.", 409);
            }

            var now = DateTimeOffset.UtcNow;
            po.Cancel(userId, now);

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return await LoadAndMapResponseAsync(po.Id, isOwner, ct);
        });
    }

    public async Task<PagedResult<PurchaseOrderResponseDto>> GetPurchaseOrdersAsync(
        PurchaseOrderFilterDto filter,
        CancellationToken ct = default,
        bool isOwner = true)
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

            // Staff chỉ thấy NCC từ khi đơn được duyệt: lọc theo NCC không được lộ đơn Draft/PendingApproval (Q9).
            if (!isOwner)
            {
                query = query.Where(p => p.Status != PurchaseOrderStatus.Draft && p.Status != PurchaseOrderStatus.PendingApproval);
            }
        }

        if (filter.BranchId.HasValue)
        {
            query = query.Where(p => p.BranchId == filter.BranchId.Value);
        }

        if (filter.FromDate.HasValue)
        {
            query = query.Where(p => p.CreatedAt >= filter.FromDate.Value);
        }

        if (filter.ToDate.HasValue)
        {
            query = query.Where(p => p.CreatedAt <= filter.ToDate.Value);
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
            Items = orders.Select(po => MapToResponseDto(po, isOwner)).ToList(),
            PageIndex = pageIndex,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<PurchaseOrderResponseDto?> GetPurchaseOrderByIdAsync(Guid id, CancellationToken ct = default, bool isOwner = true)
    {
        RequireTenantId();

        var po = await LoadPurchaseOrderGraphQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        return po == null ? null : MapToResponseDto(po, isOwner);
    }

    public async Task<PurchaseOrderPriceSuggestionDto> GetPriceSuggestionAsync(
        PurchaseOrderPriceSuggestionQueryDto query,
        CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        if (query == null || query.BranchId == Guid.Empty || query.SkuId == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "Bắt buộc truyền branchId và skuId hợp lệ để tra cứu gợi ý giá mua.", 400);
        }

        var sku = await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .Include(v => v.SkuUnits)
                .ThenInclude(u => u.Unit)
            .FirstOrDefaultAsync(v => v.Id == query.SkuId, ct);

        if (sku == null)
        {
            throw new OismException("NOT_FOUND", $"Không tìm thấy mặt hàng SKU với ID '{query.SkuId}'.", 404);
        }

        var resolvedUnit = SkuUnitResolver.ResolveForPurchase(sku, sku.SkuUnits, query.UnitId);

        var balance = await _context.InventoryBalances
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.BranchId == query.BranchId && b.SkuId == query.SkuId, ct);

        var currentBaseWac = balance?.AverageCost ?? 0m;
        var currentUnitWac = decimal.Round(currentBaseWac * resolvedUnit.Factor, 6, MidpointRounding.AwayFromZero);

        var result = new PurchaseOrderPriceSuggestionDto
        {
            SupplierId = query.SupplierId,
            BranchId = query.BranchId,
            SkuId = query.SkuId,
            UnitId = resolvedUnit.UnitId,
            UnitName = resolvedUnit.UnitName,
            FactorToBase = resolvedUnit.Factor,
            CurrentBaseWac = currentBaseWac,
            CurrentUnitWac = currentUnitWac
        };

        if (query.SupplierId.HasValue && query.SupplierId.Value != Guid.Empty)
        {
            var approvedStatuses = new[]
            {
                PurchaseOrderStatus.Approved,
                PurchaseOrderStatus.PartiallyReceived,
                PurchaseOrderStatus.Received,
                PurchaseOrderStatus.Closed
            };

            var latestApprovedItem = await _context.PurchaseOrderItems
                .AsNoTracking()
                .Include(i => i.PurchaseOrder)
                .Where(i => i.TenantId == tenantId
                            && i.SkuId == query.SkuId
                            && i.UnitPrice != null
                            && (resolvedUnit.UnitId == null
                                ? (i.UnitId == null || i.FactorSnapshot == 1m)
                                : i.UnitId == resolvedUnit.UnitId)
                            && i.PurchaseOrder != null
                            && i.PurchaseOrder.SupplierId == query.SupplierId.Value
                            && approvedStatuses.Contains(i.PurchaseOrder.Status))
                .OrderByDescending(i => i.PurchaseOrder!.ApprovedAt)
                .ThenByDescending(i => i.PurchaseOrder!.OrderDate)
                .ThenByDescending(i => i.PurchaseOrder!.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (latestApprovedItem?.PurchaseOrder != null)
            {
                result.SuggestedUnitPrice = latestApprovedItem.UnitPrice;
                result.LastAllocatedUnitCost = latestApprovedItem.AllocatedUnitCost;
                result.LastPurchaseOrderId = latestApprovedItem.PurchaseOrderId;
                result.LastPurchaseOrderNumber = latestApprovedItem.PurchaseOrder.Number;
                result.LastOrderDate = latestApprovedItem.PurchaseOrder.OrderDate;
                result.LastApprovedAt = latestApprovedItem.PurchaseOrder.ApprovedAt;
            }
        }

        return result;
    }

    private IQueryable<PurchaseOrder> LoadPurchaseOrderGraphQuery()
    {
        return _context.PurchaseOrders
            .Include(p => p.Supplier)
            .Include(p => p.Branch)
            .Include(p => p.CreatedByUser)
            .Include(p => p.ApprovedByUser)
            .Include(p => p.ReturnedByUser)
            .Include(p => p.ClosedByUser)
            .Include(p => p.CancelledByUser)
            .Include(p => p.Items)
                .ThenInclude(i => i.Sku)
                    .ThenInclude(s => s!.Product);
    }

    private async Task<PurchaseOrderResponseDto> LoadAndMapResponseAsync(Guid id, bool isOwner, CancellationToken ct)
    {
        var po = await LoadPurchaseOrderGraphQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (po == null)
        {
            throw new OismException("NOT_FOUND", $"Không tìm thấy phiếu mua hàng với ID '{id}'.", 404);
        }

        return MapToResponseDto(po, isOwner);
    }

    private static void ValidateFees(decimal shippingFee, decimal discountAmount, decimal vatAmount)
    {
        if (shippingFee < 0m || discountAmount < 0m || vatAmount < 0m)
        {
            throw new OismException("VALIDATION_ERROR", "Phí vận chuyển, chiết khấu thương mại và thuế VAT không được âm.", 400);
        }

        if (shippingFee > MaxUnitPrice || discountAmount > MaxUnitPrice || vatAmount > MaxUnitPrice)
        {
            throw new OismException("VALIDATION_ERROR", "Phí vận chuyển, chiết khấu hoặc thuế VAT vượt quá giới hạn cho phép.", 400);
        }

        if (decimal.Round(shippingFee, 6) != shippingFee ||
            decimal.Round(discountAmount, 6) != discountAmount ||
            decimal.Round(vatAmount, 6) != vatAmount)
        {
            throw new OismException("VALIDATION_ERROR", "Phí vận chuyển, chiết khấu và thuế VAT tối đa 6 chữ số thập phân.", 400);
        }
    }

    private static void ValidateHeaderAndItemsBasic(
        Guid branchId,
        string? number,
        string? note,
        List<CreatePurchaseOrderItemDto>? items,
        bool isOwner)
    {
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

            if (isOwner && item.UnitPrice.HasValue)
            {
                if (item.UnitPrice.Value < 0m)
                {
                    throw new OismException("VALIDATION_ERROR", "Đơn giá mua không được âm.", 400);
                }

                if (item.UnitPrice.Value > MaxUnitPrice)
                {
                    throw new OismException("VALIDATION_ERROR", "Đơn giá mua vượt quá giới hạn cho phép.", 400);
                }

                if (decimal.Round(item.UnitPrice.Value, 6) != item.UnitPrice.Value)
                {
                    throw new OismException("VALIDATION_ERROR", "Đơn giá mua tối đa 6 chữ số thập phân.", 400);
                }
            }
        }
    }

    private static bool PayloadMatches(PurchaseOrder existing, CreatePurchaseOrderDto incoming, bool isOwner)
    {
        if (existing.BranchId != incoming.BranchId) return false;

        if (isOwner)
        {
            var incomingSupplierId = incoming.SupplierId.HasValue && incoming.SupplierId.Value != Guid.Empty ? incoming.SupplierId : null;
            if (existing.SupplierId != incomingSupplierId) return false;
            if (incoming.OrderDate.HasValue && existing.OrderDate != incoming.OrderDate.Value) return false;
            if (existing.ShippingFee != (incoming.ShippingFee ?? 0m)) return false;
            if (existing.DiscountAmount != (incoming.DiscountAmount ?? 0m)) return false;
            if (existing.VatAmount != (incoming.VatAmount ?? 0m)) return false;
        }

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
            var expectedUnitPrice = isOwner ? incItem.UnitPrice : null;
            var match = existing.Items.FirstOrDefault(e =>
                e.SkuId == incItem.SkuId &&
                e.Quantity == incItem.Quantity &&
                (!isOwner || e.UnitPrice == expectedUnitPrice) &&
                (incItem.UnitId == null
                    ? (e.UnitId == null || (e.Sku != null && e.UnitId == e.Sku.BaseUnitId) || e.FactorSnapshot == 1m)
                    : e.UnitId == incItem.UnitId));

            if (match == null) return false;
        }

        return true;
    }

    private static PurchaseOrderResponseDto MapToResponseDto(PurchaseOrder po, bool isOwner)
    {
        var items = po.Items
            .OrderBy(i => i.CreatedAt)
            .ThenBy(i => i.Id)
            .Select(i =>
            {
                var orderedBase = i.Quantity * i.FactorSnapshot;
                var remainingBase = Math.Max(0m, orderedBase - i.ReceivedBaseQuantity);
                var receivedUnitQty = i.FactorSnapshot > 0m
                    ? decimal.Round(i.ReceivedBaseQuantity / i.FactorSnapshot, 3, MidpointRounding.AwayFromZero)
                    : 0m;
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
                    UnitPrice = isOwner ? i.UnitPrice : null,
                    AllocatedUnitCost = isOwner ? i.AllocatedUnitCost : null,
                    ReceivedBaseQuantity = i.ReceivedBaseQuantity,
                    RemainingBaseQuantity = remainingBase,
                    ReceivedQuantity = receivedUnitQty,
                    RemainingQuantity = remainingUnitQty
                };
            })
            .ToList();

        var hideSupplier = !isOwner && (po.Status == PurchaseOrderStatus.Draft || po.Status == PurchaseOrderStatus.PendingApproval);
        var supplierId = hideSupplier ? null : po.SupplierId;
        var supplierName = hideSupplier ? null : po.Supplier?.Name;

        decimal? subtotal = null;
        decimal? shippingFee = null;
        decimal? discountAmount = null;
        decimal? vatAmount = null;
        decimal? totalAllocatedCost = null;
        decimal? totalAmount = null;

        if (isOwner)
        {
            var rawSubtotal = po.Items
                .Where(i => i.UnitPrice.HasValue)
                .Sum(i => decimal.Round(i.Quantity * i.UnitPrice!.Value, 6, MidpointRounding.AwayFromZero));

            subtotal = rawSubtotal;
            shippingFee = po.ShippingFee;
            discountAmount = po.DiscountAmount;
            vatAmount = po.VatAmount;
            totalAllocatedCost = Math.Max(0m, rawSubtotal + po.ShippingFee - po.DiscountAmount);
            totalAmount = Math.Max(0m, rawSubtotal + po.ShippingFee - po.DiscountAmount + po.VatAmount);
        }

        return new PurchaseOrderResponseDto
        {
            Id = po.Id,
            Number = po.Number,
            RequestKey = po.RequestKey,
            SupplierId = supplierId,
            SupplierName = supplierName,
            BranchId = po.BranchId,
            BranchName = po.Branch?.Name ?? string.Empty,
            OrderDate = po.OrderDate,
            Status = po.Status,
            Note = po.Note,
            Subtotal = subtotal,
            ShippingFee = shippingFee,
            DiscountAmount = discountAmount,
            VatAmount = vatAmount,
            TotalAllocatedCost = totalAllocatedCost,
            TotalAmount = totalAmount,
            Version = po.Version,
            CreatedBy = po.CreatedBy,
            CreatedByName = po.CreatedByUser?.FullName,
            ApprovedBy = po.ApprovedBy,
            ApprovedByName = po.ApprovedByUser?.FullName,
            ApprovedAt = po.ApprovedAt,
            ReturnedBy = po.ReturnedBy,
            ReturnedByName = po.ReturnedByUser?.FullName,
            ReturnedAt = po.ReturnedAt,
            ReturnReason = po.ReturnReason,
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
