using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Pricing.Dtos;
using OISM.Application.Features.Pricing.Services;
using OISM.Domain.Entities;

namespace OISM.Application.Features.Catalog.Units.Services;

public class UnitService : IUnitService
{
    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _currentTenantService;
    private readonly ISqlLockService _sqlLockService;
    private readonly IPriceChangeService _priceChangeService;
    private readonly ISkuPriceResolver _skuPriceResolver;

    public UnitService(
        IAppDbContext context,
        ICurrentTenantService currentTenantService,
        ISqlLockService sqlLockService,
        IPriceChangeService priceChangeService,
        ISkuPriceResolver? skuPriceResolver = null)
    {
        _context = context;
        _currentTenantService = currentTenantService;
        _sqlLockService = sqlLockService;
        _priceChangeService = priceChangeService;
        _skuPriceResolver = skuPriceResolver ?? new SkuPriceResolver(context);
    }

    private Guid RequireTenantId()
    {
        var tenantId = _currentTenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new OismException("UNAUTHORIZED", "Không xác định được TenantId hợp lệ.", 401);
        }
        return tenantId.Value;
    }

    private static bool IsValidFactor(decimal factor)
    {
        if (factor <= 0) return false;
        if (Math.Abs(factor) >= 1_000_000_000_000m) return false;
        if (decimal.Round(factor, 6) != factor) return false;
        return true;
    }

    public async Task EnsureTenantDefaultUnitsAsync(Guid tenantId, CancellationToken ct = default)
    {
        var existingCodes = await _context.Units
            .Where(u => u.TenantId == tenantId)
            .Select(u => u.Code)
            .ToListAsync(ct);

        var missing = DefaultUnits.Items
            .Where(d => !existingCodes.Contains(d.Code, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (missing.Count == 0) return;

        if (_context.UnitOfWork.HasActiveTransaction)
        {
            await _sqlLockService.LockUnitCatalogAsync(tenantId, ct);
            var currentCodes = await _context.Units
                .Where(u => u.TenantId == tenantId)
                .Select(u => u.Code)
                .ToListAsync(ct);

            var toAdd = DefaultUnits.Items
                .Where(d => !currentCodes.Contains(d.Code, StringComparer.OrdinalIgnoreCase))
                .Select(d => new Unit
                {
                    TenantId = tenantId,
                    Code = d.Code,
                    Name = d.Name,
                    Symbol = d.Symbol,
                    MeasureGroup = d.MeasureGroup,
                    DecimalPlaces = d.DecimalPlaces,
                    IsActive = true
                })
                .ToList();

            if (toAdd.Count > 0)
            {
                _context.Units.AddRange(toAdd);
                await _context.SaveChangesAsync(ct);
            }
            return;
        }

        await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);
            await _sqlLockService.LockUnitCatalogAsync(tenantId, ct);

            var currentCodes = await _context.Units
                .Where(u => u.TenantId == tenantId)
                .Select(u => u.Code)
                .ToListAsync(ct);

            var toAdd = DefaultUnits.Items
                .Where(d => !currentCodes.Contains(d.Code, StringComparer.OrdinalIgnoreCase))
                .Select(d => new Unit
                {
                    TenantId = tenantId,
                    Code = d.Code,
                    Name = d.Name,
                    Symbol = d.Symbol,
                    MeasureGroup = d.MeasureGroup,
                    DecimalPlaces = d.DecimalPlaces,
                    IsActive = true
                })
                .ToList();

            if (toAdd.Count > 0)
            {
                _context.Units.AddRange(toAdd);
                await _context.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);
        });
    }

    public async Task<List<UnitResponse>> GetUnitsAsync(CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();
        await EnsureTenantDefaultUnitsAsync(tenantId, ct);

        var units = await _context.Units
            .OrderBy(u => u.Code)
            .ToListAsync(ct);

        return units.Select(MapUnitToResponse).ToList();
    }

    public async Task<UnitResponse?> GetUnitByIdAsync(Guid id, CancellationToken ct = default)
    {
        RequireTenantId();
        var unit = await _context.Units.FirstOrDefaultAsync(u => u.Id == id, ct);
        return unit == null ? null : MapUnitToResponse(unit);
    }

    public async Task<UnitResponse> CreateUnitAsync(CreateUnitRequest request, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw new OismException("VALIDATION_ERROR", "Mã đơn vị tính không được để trống.", 400);
        }

        var normalizedCode = request.Code.Trim().ToLowerInvariant();
        if (normalizedCode.Length > 30)
        {
            throw new OismException("VALIDATION_ERROR", "Mã đơn vị tính không được quá 30 ký tự.", 400);
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100)
        {
            throw new OismException("VALIDATION_ERROR", "Tên đơn vị tính không được để trống và không quá 100 ký tự.", 400);
        }

        if (string.IsNullOrWhiteSpace(request.Symbol) || request.Symbol.Trim().Length > 20)
        {
            throw new OismException("VALIDATION_ERROR", "Ký hiệu đơn vị tính không được để trống và không quá 20 ký tự.", 400);
        }

        if (!MeasureGroups.IsValid(request.MeasureGroup))
        {
            throw new OismException("VALIDATION_ERROR", $"Nhóm đo lường '{request.MeasureGroup}' không hợp lệ.", 400);
        }

        if (request.DecimalPlaces < 0 || request.DecimalPlaces > 3)
        {
            throw new OismException("VALIDATION_ERROR", "Số chữ số thập phân cho phép từ 0 đến 3.", 400);
        }

        return await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);

            await _sqlLockService.LockUnitCatalogAsync(tenantId, ct);

            var codeExists = await _context.Units.AnyAsync(u => u.Code == normalizedCode, ct);
            if (codeExists)
            {
                await tx.RollbackAsync(ct);
                throw new OismException("UNIT_CODE_DUPLICATE", $"Mã đơn vị tính '{normalizedCode}' đã tồn tại trong cửa hàng.", 409);
            }

            var unit = new Unit
            {
                TenantId = tenantId,
                Code = normalizedCode,
                Name = request.Name.Trim(),
                Symbol = request.Symbol.Trim(),
                MeasureGroup = request.MeasureGroup,
                DecimalPlaces = request.DecimalPlaces,
                IsActive = true
            };

            _context.Units.Add(unit);
            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return MapUnitToResponse(unit);
        });
    }

    public async Task<UnitResponse> UpdateUnitAsync(Guid id, UpdateUnitRequest request, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        if (request.Code != null && (string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 30))
        {
            throw new OismException("VALIDATION_ERROR", "Mã đơn vị tính không được để trống và không quá 30 ký tự.", 400);
        }

        if (request.Name != null && (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100))
        {
            throw new OismException("VALIDATION_ERROR", "Tên đơn vị tính không được để trống và không quá 100 ký tự.", 400);
        }

        if (request.Symbol != null && (string.IsNullOrWhiteSpace(request.Symbol) || request.Symbol.Trim().Length > 20))
        {
            throw new OismException("VALIDATION_ERROR", "Ký hiệu đơn vị tính không được để trống và không quá 20 ký tự.", 400);
        }

        return await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);

            await _sqlLockService.LockUnitCatalogAsync(tenantId, ct);

            var unit = await _context.Units.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (unit == null)
            {
                await tx.RollbackAsync(ct);
                throw new OismException("UNIT_NOT_FOUND", "Không tìm thấy đơn vị tính.", 404);
            }

            var isUsedBySku = await _context.Skus.AnyAsync(s => s.BaseUnitId == id, ct) ||
                              await _context.SkuUnits.AnyAsync(u => u.UnitId == id, ct);

            // Không đổi code hoặc measure_group khi đơn vị đã được SKU dùng
            if (!string.IsNullOrWhiteSpace(request.Code))
            {
                var newCode = request.Code.Trim().ToLowerInvariant();
                if (newCode != unit.Code)
                {
                    if (isUsedBySku)
                    {
                        await tx.RollbackAsync(ct);
                        throw new OismException("UNIT_IN_USE", "Không thể thay đổi mã đơn vị khi đơn vị đã được sản phẩm sử dụng.", 409);
                    }

                    var codeExists = await _context.Units.AnyAsync(u => u.Code == newCode && u.Id != id, ct);
                    if (codeExists)
                    {
                        await tx.RollbackAsync(ct);
                        throw new OismException("UNIT_CODE_DUPLICATE", $"Mã đơn vị tính '{newCode}' đã tồn tại trong cửa hàng.", 409);
                    }
                    unit.Code = newCode;
                }
            }

            if (!string.IsNullOrWhiteSpace(request.MeasureGroup))
            {
                if (!MeasureGroups.IsValid(request.MeasureGroup))
                {
                    await tx.RollbackAsync(ct);
                    throw new OismException("VALIDATION_ERROR", $"Nhóm đo lường '{request.MeasureGroup}' không hợp lệ.", 400);
                }

                if (request.MeasureGroup != unit.MeasureGroup)
                {
                    if (isUsedBySku)
                    {
                        await tx.RollbackAsync(ct);
                        throw new OismException("UNIT_IN_USE", "Không thể thay đổi nhóm đo lường khi đơn vị đã được sản phẩm sử dụng.", 409);
                    }
                    unit.MeasureGroup = request.MeasureGroup;
                }
            }

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                unit.Name = request.Name.Trim();
            }

            if (!string.IsNullOrWhiteSpace(request.Symbol))
            {
                unit.Symbol = request.Symbol.Trim();
            }

            if (request.DecimalPlaces.HasValue)
            {
                if (request.DecimalPlaces.Value < 0 || request.DecimalPlaces.Value > 3)
                {
                    await tx.RollbackAsync(ct);
                    throw new OismException("VALIDATION_ERROR", "Số chữ số thập phân cho phép từ 0 đến 3.", 400);
                }
                unit.DecimalPlaces = request.DecimalPlaces.Value;
            }

            if (request.IsActive.HasValue)
            {
                if (!request.IsActive.Value && await _context.Skus.AnyAsync(s => s.BaseUnitId == id, ct))
                {
                    await tx.RollbackAsync(ct);
                    throw new OismException("UNIT_IN_USE", "Không thể tắt đơn vị tính đang là đơn vị gốc của sản phẩm.", 409);
                }
                unit.IsActive = request.IsActive.Value;
            }

            unit.UpdatedAt = DateTimeOffset.UtcNow;
            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return MapUnitToResponse(unit);
        });
    }

    public async Task<List<SkuUnitResponse>> GetSkuUnitsAsync(Guid productId, Guid variantId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();
        await EnsureTenantDefaultUnitsAsync(tenantId, ct);

        var variant = await _context.ProductVariants
            .Include(v => v.SkuUnits)
                .ThenInclude(u => u.Unit)
            .FirstOrDefaultAsync(v => v.Id == variantId && v.ProductId == productId, ct);

        if (variant == null)
        {
            throw new OismException("NOT_FOUND", "Không tìm thấy biến thể sản phẩm (SKU).", 404);
        }

        // Tự khởi tạo đơn vị gốc nếu variant.BaseUnitId == null
        if (!variant.BaseUnitId.HasValue)
        {
            await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
            {
                await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);
                await _sqlLockService.LockProductAsync(productId, ct);
                await _sqlLockService.LockUnitCatalogAsync(tenantId, ct);
                await EnsureBaseUnitConfiguredAsync(variant, ct);
                await tx.CommitAsync(ct);
            });
        }

        var skuUnits = await _context.SkuUnits
            .Include(u => u.Unit)
            .Where(u => u.SkuId == variantId)
            .OrderByDescending(u => u.UnitId == variant.BaseUnitId)
            .ThenBy(u => u.FactorToBase)
            .ToListAsync(ct);

        var priceKeys = skuUnits.Where(u => u.UnitId != variant.BaseUnitId).Select(u => new SkuUnitPriceKey(variantId, u.UnitId)).ToList();
        var effectivePrices = await _skuPriceResolver.ResolveAsync("retail", priceKeys, DateTimeOffset.UtcNow, ct);

        return skuUnits.Select(u => MapSkuUnitToResponse(u, variant.BaseUnitId, effectivePrices)).ToList();
    }

    public async Task<SkuUnitResponse> CreateSkuUnitAsync(Guid productId, Guid variantId, CreateSkuUnitRequest request, Guid? userId, PriceActor actor, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();
        await EnsureTenantDefaultUnitsAsync(tenantId, ct);

        if (!IsValidFactor(request.FactorToBase))
        {
            throw new OismException("INVALID_UNIT_FACTOR", "Hệ số quy đổi phải lớn hơn 0, tối đa 12 chữ số nguyên và tối đa 6 chữ số thập phân.", 400);
        }

        if (request.QuantityDecimalPlaces < 0 || request.QuantityDecimalPlaces > 3)
        {
            throw new OismException("VALIDATION_ERROR", "Số chữ số thập phân cho phép từ 0 đến 3.", 400);
        }

        if (request.SalePrice.HasValue && request.SalePrice.Value < 0)
        {
            throw new OismException("VALIDATION_ERROR", "Giá bán không được là số âm.", 400);
        }

        if (!string.IsNullOrWhiteSpace(request.Barcode) && request.Barcode.Trim().Length > 128)
        {
            throw new OismException("VALIDATION_ERROR", "Mã vạch không được vượt quá 128 ký tự.", 400);
        }

        return await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);

            // Thứ tự khóa bắt buộc: LockProductAsync -> LockUnitCatalogAsync
            await _sqlLockService.LockProductAsync(productId, ct);
            await _sqlLockService.LockUnitCatalogAsync(tenantId, ct);
            await _priceChangeService.EnsureTenantDefaultPriceListsAsync(tenantId, ct);

            var variant = await _context.ProductVariants
                .Include(v => v.BaseUnit)
                .FirstOrDefaultAsync(v => v.Id == variantId && v.ProductId == productId, ct);

            if (variant == null)
            {
                await tx.RollbackAsync(ct);
                throw new OismException("NOT_FOUND", "Không tìm thấy biến thể sản phẩm (SKU).", 404);
            }

            if (!variant.BaseUnitId.HasValue)
            {
                await EnsureBaseUnitConfiguredAsync(variant, ct);
            }

            var baseUnit = variant.BaseUnit ?? await _context.Units.FirstOrDefaultAsync(u => u.Id == variant.BaseUnitId, ct);
            if (baseUnit == null)
            {
                await tx.RollbackAsync(ct);
                throw new OismException("BASE_UNIT_NOT_FOUND", "Không tìm thấy đơn vị gốc của SKU.", 404);
            }

            var targetUnit = await _context.Units.FirstOrDefaultAsync(u => u.Id == request.UnitId, ct);
            if (targetUnit == null)
            {
                await tx.RollbackAsync(ct);
                throw new OismException("UNIT_NOT_FOUND", "Không tìm thấy đơn vị tính yêu cầu.", 404);
            }

            if (!targetUnit.IsActive)
            {
                await tx.RollbackAsync(ct);
                throw new OismException("UNIT_NOT_ALLOWED", $"Đơn vị tính '{targetUnit.Name}' đang ngừng hoạt động.", 422);
            }

            // Đơn vị giao dịch hợp lệ khi cùng measure_group với đơn vị gốc, hoặc thuộc nhóm Packaging. Ngược lại: UNIT_GROUP_INCOMPATIBLE
            if (!string.Equals(targetUnit.MeasureGroup, baseUnit.MeasureGroup, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(targetUnit.MeasureGroup, MeasureGroups.Packaging, StringComparison.OrdinalIgnoreCase))
            {
                await tx.RollbackAsync(ct);
                throw new OismException("UNIT_GROUP_INCOMPATIBLE", $"Đơn vị tính '{targetUnit.Name}' (nhóm {targetUnit.MeasureGroup}) không tương thích với đơn vị gốc '{baseUnit.Name}' (nhóm {baseUnit.MeasureGroup}).", 422);
            }

            // Đơn vị gốc không được add lại
            if (variant.BaseUnitId.HasValue && request.UnitId == variant.BaseUnitId.Value)
            {
                await tx.RollbackAsync(ct);
                throw new OismException("INVALID_OPERATION", "Đơn vị tính này đã là đơn vị gốc của SKU.", 400);
            }

            var existingSkuUnit = await _context.SkuUnits
                .FirstOrDefaultAsync(u => u.SkuId == variantId && u.UnitId == request.UnitId, ct);

            if (existingSkuUnit != null)
            {
                await tx.RollbackAsync(ct);
                throw new OismException("UNIT_ALREADY_EXISTS", $"Đơn vị tính '{targetUnit.Name}' đã được cấu hình cho SKU này.", 409);
            }

            // Kiểm tra trùng mã vạch trong tenant
            if (!string.IsNullOrWhiteSpace(request.Barcode))
            {
                var barcodeTrimmed = request.Barcode.Trim();
                var existsInSkus = await _context.ProductVariants.AnyAsync(v => v.Barcode == barcodeTrimmed, ct);
                var existsInSkuUnits = await _context.SkuUnits.AnyAsync(u => u.Barcode == barcodeTrimmed, ct);
                if (existsInSkus || existsInSkuUnits)
                {
                    await tx.RollbackAsync(ct);
                    throw new OismException("DUPLICATE_BARCODE", $"Mã vạch '{barcodeTrimmed}' đã tồn tại trong cửa hàng.", 409);
                }
            }

            // Đặt mặc định mới thì bỏ mặc định cũ và LƯU TRƯỚC trong cùng transaction
            if (request.IsDefaultSale)
            {
                var oldDefaultSale = await _context.SkuUnits
                    .Where(u => u.SkuId == variantId && u.IsDefaultSale)
                    .ToListAsync(ct);
                foreach (var item in oldDefaultSale) item.IsDefaultSale = false;
                if (oldDefaultSale.Count > 0)
                {
                    await _context.SaveChangesAsync(ct);
                }
            }

            if (request.IsDefaultPurchase)
            {
                var oldDefaultPurchase = await _context.SkuUnits
                    .Where(u => u.SkuId == variantId && u.IsDefaultPurchase)
                    .ToListAsync(ct);
                foreach (var item in oldDefaultPurchase) item.IsDefaultPurchase = false;
                if (oldDefaultPurchase.Count > 0)
                {
                    await _context.SaveChangesAsync(ct);
                }
            }

            var skuUnit = new SkuUnit
            {
                TenantId = tenantId,
                SkuId = variantId,
                UnitId = request.UnitId,
                FactorToBase = request.FactorToBase,
                CanPurchase = request.CanPurchase,
                CanSell = request.CanSell,
                IsDefaultPurchase = request.IsDefaultPurchase,
                IsDefaultSale = request.IsDefaultSale,
                Barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim(),
                QuantityDecimalPlaces = request.QuantityDecimalPlaces,
                SalePrice = request.SalePrice,
                Version = 1,
                IsActive = true,
                UpdatedBy = userId
            };

            _context.SkuUnits.Add(skuUnit);

            // Ghi lịch sử hệ số ban đầu
            var history = new SkuUnitFactorHistory
            {
                TenantId = tenantId,
                SkuUnitId = skuUnit.Id,
                OldFactor = null,
                NewFactor = skuUnit.FactorToBase,
                OldVersion = null,
                NewVersion = 1,
                ChangedBy = userId,
                ChangedAt = DateTimeOffset.UtcNow
            };
            _context.SkuUnitFactorHistories.Add(history);

            await _context.SaveChangesAsync(ct);

            if (request.SalePrice.HasValue && request.SalePrice.Value > 0)
            {
                var priceLines = new[]
                {
                    new InitialPriceLine("retail", variantId, request.UnitId, request.SalePrice.Value)
                };
                await _priceChangeService.AddInitialPricesAsync(priceLines, actor, ct);
            }

            await tx.CommitAsync(ct);

            skuUnit.Unit = targetUnit;
            var priceKey = new SkuUnitPriceKey(skuUnit.SkuId, skuUnit.UnitId);
            var effectivePrices = await _skuPriceResolver.ResolveAsync("retail", new[] { priceKey }, DateTimeOffset.UtcNow, ct);
            return MapSkuUnitToResponse(skuUnit, variant.BaseUnitId, effectivePrices);
        });
    }

    public async Task<SkuUnitResponse> UpdateSkuUnitAsync(Guid productId, Guid variantId, Guid skuUnitId, UpdateSkuUnitRequest request, Guid? userId = null, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        if (request.QuantityDecimalPlaces.HasValue && (request.QuantityDecimalPlaces.Value < 0 || request.QuantityDecimalPlaces.Value > 3))
        {
            throw new OismException("VALIDATION_ERROR", "Số chữ số thập phân cho phép từ 0 đến 3.", 400);
        }

        if (request.SalePrice.HasValue && request.SalePrice.Value < 0)
        {
            throw new OismException("VALIDATION_ERROR", "Giá bán không được là số âm.", 400);
        }

        if (!string.IsNullOrWhiteSpace(request.Barcode) && request.Barcode.Trim().Length > 128)
        {
            throw new OismException("VALIDATION_ERROR", "Mã vạch không được vượt quá 128 ký tự.", 400);
        }

        return await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);

            // Thứ tự khóa bắt buộc: LockProductAsync -> LockUnitCatalogAsync
            await _sqlLockService.LockProductAsync(productId, ct);
            await _sqlLockService.LockUnitCatalogAsync(tenantId, ct);

            var variant = await _context.ProductVariants
                .FirstOrDefaultAsync(v => v.Id == variantId && v.ProductId == productId, ct);

            if (variant == null)
            {
                await tx.RollbackAsync(ct);
                throw new OismException("NOT_FOUND", "Không tìm thấy biến thể sản phẩm (SKU).", 404);
            }

            var skuUnit = await _context.SkuUnits
                .Include(u => u.Unit)
                .FirstOrDefaultAsync(u => u.Id == skuUnitId && u.SkuId == variantId, ct);

            if (skuUnit == null)
            {
                await tx.RollbackAsync(ct);
                throw new OismException("UNIT_NOT_FOUND", "Không tìm thấy cấu hình đơn vị tính cho SKU này.", 404);
            }

            var isBase = variant.BaseUnitId.HasValue && skuUnit.UnitId == variant.BaseUnitId.Value;

            // Dòng đơn vị gốc: hệ số luôn 1, không sửa hệ số, không tắt, không đặt sale_price
            if (isBase)
            {
                if (request.FactorToBase.HasValue && request.FactorToBase.Value != 1m)
                {
                    await tx.RollbackAsync(ct);
                    throw new OismException("INVALID_UNIT_FACTOR", "Không thể sửa hệ số của đơn vị gốc.", 400);
                }

                if (request.IsActive.HasValue && !request.IsActive.Value)
                {
                    await tx.RollbackAsync(ct);
                    throw new OismException("INVALID_OPERATION", "Không thể tắt đơn vị gốc.", 400);
                }

                if (request.SalePrice.HasValue)
                {
                    await tx.RollbackAsync(ct);
                    throw new OismException("INVALID_OPERATION", "Không thể đặt giá bán cho đơn vị gốc. Đơn vị gốc sử dụng giá bán lẻ của SKU.", 400);
                }
            }
            else if (request.FactorToBase.HasValue && !IsValidFactor(request.FactorToBase.Value))
            {
                await tx.RollbackAsync(ct);
                throw new OismException("INVALID_UNIT_FACTOR", "Hệ số quy đổi phải lớn hơn 0, tối đa 12 chữ số nguyên và tối đa 6 chữ số thập phân.", 400);
            }

            var willBeActive = request.IsActive ?? skuUnit.IsActive;
            if (!willBeActive && (request.IsDefaultSale == true || request.IsDefaultPurchase == true))
            {
                await tx.RollbackAsync(ct);
                throw new OismException("INVALID_OPERATION", "Không thể đặt đơn vị đang tắt làm mặc định.", 400);
            }

            // Bật lại đơn vị: hệ số cũ có thể đã sai nghĩa nếu đơn vị gốc đã đổi trong lúc tắt, nên phải xác nhận lại
            if (!isBase && request.IsActive == true && !skuUnit.IsActive)
            {
                if (!request.FactorToBase.HasValue)
                {
                    await tx.RollbackAsync(ct);
                    throw new OismException("INVALID_UNIT_FACTOR", "Bật lại đơn vị tính phải gửi kèm hệ số quy đổi để xác nhận.", 400);
                }

                if (skuUnit.Unit is { IsActive: false })
                {
                    await tx.RollbackAsync(ct);
                    throw new OismException("UNIT_NOT_ALLOWED", $"Đơn vị tính '{skuUnit.Unit.Name}' đang ngừng hoạt động.", 422);
                }

                var currentBaseUnit = await _context.Units.FirstOrDefaultAsync(u => u.Id == variant.BaseUnitId, ct);
                if (currentBaseUnit != null && skuUnit.Unit != null &&
                    !string.Equals(skuUnit.Unit.MeasureGroup, currentBaseUnit.MeasureGroup, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(skuUnit.Unit.MeasureGroup, MeasureGroups.Packaging, StringComparison.OrdinalIgnoreCase))
                {
                    await tx.RollbackAsync(ct);
                    throw new OismException("UNIT_GROUP_INCOMPATIBLE", $"Đơn vị tính '{skuUnit.Unit.Name}' không tương thích với đơn vị gốc '{currentBaseUnit.Name}'.", 422);
                }
            }

            // Thay đổi hệ số -> version + 1 và ghi lịch sử
            if (request.FactorToBase.HasValue && request.FactorToBase.Value != skuUnit.FactorToBase)
            {
                var oldFactor = skuUnit.FactorToBase;
                var oldVersion = skuUnit.Version;
                var newVersion = oldVersion + 1;

                skuUnit.FactorToBase = request.FactorToBase.Value;
                skuUnit.Version = newVersion;

                var history = new SkuUnitFactorHistory
                {
                    TenantId = tenantId,
                    SkuUnitId = skuUnit.Id,
                    OldFactor = oldFactor,
                    NewFactor = skuUnit.FactorToBase,
                    OldVersion = oldVersion,
                    NewVersion = newVersion,
                    ChangedBy = userId,
                    ChangedAt = DateTimeOffset.UtcNow
                };
                _context.SkuUnitFactorHistories.Add(history);
            }

            // Kiểm tra trùng mã vạch
            if (request.Barcode != null)
            {
                var barcodeTrimmed = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim();
                if (barcodeTrimmed != skuUnit.Barcode && barcodeTrimmed != null)
                {
                    var existsInSkus = await _context.ProductVariants.AnyAsync(v => v.Barcode == barcodeTrimmed, ct);
                    var existsInSkuUnits = await _context.SkuUnits.AnyAsync(u => u.Barcode == barcodeTrimmed && u.Id != skuUnitId, ct);
                    if (existsInSkus || existsInSkuUnits)
                    {
                        await tx.RollbackAsync(ct);
                        throw new OismException("DUPLICATE_BARCODE", $"Mã vạch '{barcodeTrimmed}' đã tồn tại trong cửa hàng.", 409);
                    }
                }
                skuUnit.Barcode = barcodeTrimmed;
            }

            // Đặt mặc định mới thì bỏ mặc định cũ và LƯU TRƯỚC trong cùng transaction
            if (request.IsDefaultSale.HasValue)
            {
                if (request.IsDefaultSale.Value && !skuUnit.IsDefaultSale)
                {
                    var oldDefaults = await _context.SkuUnits
                        .Where(u => u.SkuId == variantId && u.IsDefaultSale && u.Id != skuUnitId)
                        .ToListAsync(ct);
                    foreach (var item in oldDefaults) item.IsDefaultSale = false;
                    if (oldDefaults.Count > 0)
                    {
                        await _context.SaveChangesAsync(ct);
                    }
                    skuUnit.IsDefaultSale = true;
                }
                else
                {
                    skuUnit.IsDefaultSale = request.IsDefaultSale.Value;
                }
            }

            if (request.IsDefaultPurchase.HasValue)
            {
                if (request.IsDefaultPurchase.Value && !skuUnit.IsDefaultPurchase)
                {
                    var oldDefaults = await _context.SkuUnits
                        .Where(u => u.SkuId == variantId && u.IsDefaultPurchase && u.Id != skuUnitId)
                        .ToListAsync(ct);
                    foreach (var item in oldDefaults) item.IsDefaultPurchase = false;
                    if (oldDefaults.Count > 0)
                    {
                        await _context.SaveChangesAsync(ct);
                    }
                    skuUnit.IsDefaultPurchase = true;
                }
                else
                {
                    skuUnit.IsDefaultPurchase = request.IsDefaultPurchase.Value;
                }
            }

            if (request.CanPurchase.HasValue) skuUnit.CanPurchase = request.CanPurchase.Value;
            if (request.CanSell.HasValue) skuUnit.CanSell = request.CanSell.Value;

            if (request.QuantityDecimalPlaces.HasValue)
            {
                skuUnit.QuantityDecimalPlaces = request.QuantityDecimalPlaces.Value;
            }

            if (request.SalePrice.HasValue && !isBase)
            {
                // Giá gửi kèm chỉ để đối chiếu; chưa có giá hiện hành thì chấp nhận 0 (frontend gửi 0 khi giá đang trống)
                var priceKey = new SkuUnitPriceKey(variantId, skuUnit.UnitId);
                var resolved = await _skuPriceResolver.ResolveAsync("retail", new[] { priceKey }, DateTimeOffset.UtcNow, ct);
                var isUnchanged = resolved.TryGetValue(priceKey, out var currentPrice)
                    ? request.SalePrice.Value == currentPrice.Price
                    : request.SalePrice.Value == 0;

                if (!isUnchanged)
                {
                    await tx.RollbackAsync(ct);
                    throw new DirectPriceUpdateNotAllowedException();
                }
            }

            if (request.IsActive.HasValue && !isBase)
            {
                skuUnit.IsActive = request.IsActive.Value;
                if (!skuUnit.IsActive)
                {
                    // Tắt đơn vị đang mặc định thì trả về mặc định cho dòng gốc
                    var needsDefaultSale = skuUnit.IsDefaultSale;
                    var needsDefaultPurchase = skuUnit.IsDefaultPurchase;

                    if (needsDefaultSale || needsDefaultPurchase)
                    {
                        if (needsDefaultSale) skuUnit.IsDefaultSale = false;
                        if (needsDefaultPurchase) skuUnit.IsDefaultPurchase = false;
                        await _context.SaveChangesAsync(ct);

                        var baseSku = await _context.SkuUnits.FirstOrDefaultAsync(u => u.SkuId == variantId && u.UnitId == variant.BaseUnitId, ct);
                        if (baseSku != null)
                        {
                            if (needsDefaultSale) baseSku.IsDefaultSale = true;
                            if (needsDefaultPurchase) baseSku.IsDefaultPurchase = true;
                        }
                    }
                }
            }

            skuUnit.UpdatedBy = userId;
            skuUnit.UpdatedAt = DateTimeOffset.UtcNow;

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            var returnPriceKey = new SkuUnitPriceKey(skuUnit.SkuId, skuUnit.UnitId);
            var effectivePrices = await _skuPriceResolver.ResolveAsync("retail", new[] { returnPriceKey }, DateTimeOffset.UtcNow, ct);
            return MapSkuUnitToResponse(skuUnit, variant.BaseUnitId, effectivePrices);
        });
    }

    public async Task DeleteSkuUnitAsync(Guid productId, Guid variantId, Guid skuUnitId, Guid? userId = null, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);

            await _sqlLockService.LockProductAsync(productId, ct);
            await _sqlLockService.LockUnitCatalogAsync(tenantId, ct);

            var variant = await _context.ProductVariants
                .FirstOrDefaultAsync(v => v.Id == variantId && v.ProductId == productId, ct);

            if (variant == null)
            {
                await tx.RollbackAsync(ct);
                throw new OismException("NOT_FOUND", "Không tìm thấy biến thể sản phẩm (SKU).", 404);
            }

            var skuUnit = await _context.SkuUnits
                .FirstOrDefaultAsync(u => u.Id == skuUnitId && u.SkuId == variantId, ct);

            if (skuUnit == null)
            {
                await tx.RollbackAsync(ct);
                throw new OismException("UNIT_NOT_FOUND", "Không tìm thấy cấu hình đơn vị tính cho SKU này.", 404);
            }

            if (variant.BaseUnitId.HasValue && skuUnit.UnitId == variant.BaseUnitId.Value)
            {
                await tx.RollbackAsync(ct);
                throw new OismException("INVALID_OPERATION", "Không thể xóa hoặc tắt đơn vị gốc.", 400);
            }

            // Tắt mềm và chuyển mặc định về dòng gốc nếu đang là mặc định
            skuUnit.IsActive = false;
            var needsDefaultSale = skuUnit.IsDefaultSale;
            var needsDefaultPurchase = skuUnit.IsDefaultPurchase;
            if (needsDefaultSale) skuUnit.IsDefaultSale = false;
            if (needsDefaultPurchase) skuUnit.IsDefaultPurchase = false;
            if (needsDefaultSale || needsDefaultPurchase)
            {
                await _context.SaveChangesAsync(ct);
                var baseSku = await _context.SkuUnits.FirstOrDefaultAsync(u => u.SkuId == variantId && u.UnitId == variant.BaseUnitId, ct);
                if (baseSku != null)
                {
                    if (needsDefaultSale) baseSku.IsDefaultSale = true;
                    if (needsDefaultPurchase) baseSku.IsDefaultPurchase = true;
                }
            }

            skuUnit.UpdatedAt = DateTimeOffset.UtcNow;
            skuUnit.UpdatedBy = userId;

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    public async Task<List<SkuUnitFactorHistoryResponse>> GetSkuUnitHistoryAsync(Guid productId, Guid variantId, Guid skuUnitId, CancellationToken ct = default)
    {
        RequireTenantId();

        var exists = await _context.SkuUnits.AnyAsync(u => u.Id == skuUnitId && u.SkuId == variantId, ct);
        if (!exists)
        {
            throw new OismException("UNIT_NOT_FOUND", "Không tìm thấy cấu hình đơn vị tính.", 404);
        }

        var history = await _context.SkuUnitFactorHistories
            .Where(h => h.SkuUnitId == skuUnitId)
            .OrderByDescending(h => h.ChangedAt)
            .ToListAsync(ct);

        return history.Select(h => new SkuUnitFactorHistoryResponse
        {
            Id = h.Id,
            SkuUnitId = h.SkuUnitId,
            OldFactor = h.OldFactor,
            NewFactor = h.NewFactor,
            OldVersion = h.OldVersion,
            NewVersion = h.NewVersion,
            ChangedBy = h.ChangedBy,
            ChangedAt = h.ChangedAt
        }).ToList();
    }

    private async Task EnsureBaseUnitConfiguredAsync(ProductVariant variant, CancellationToken ct)
    {
        await EnsureTenantDefaultUnitsAsync(variant.TenantId, ct);

        var unitCode = string.IsNullOrWhiteSpace(variant.Unit) ? "piece" : variant.Unit.Trim().ToLowerInvariant();
        var unit = await _context.Units.FirstOrDefaultAsync(u => u.Code == unitCode, ct);
        if (unit == null)
        {
            unit = new Unit
            {
                TenantId = variant.TenantId,
                Code = unitCode,
                Name = unitCode,
                Symbol = unitCode,
                MeasureGroup = MeasureGroups.Count,
                DecimalPlaces = 3,
                IsActive = true
            };
            _context.Units.Add(unit);
            await _context.SaveChangesAsync(ct);
        }

        variant.BaseUnitId = unit.Id;
        variant.Unit = unit.Code;

        var baseSkuUnit = await _context.SkuUnits.FirstOrDefaultAsync(u => u.SkuId == variant.Id && u.UnitId == unit.Id, ct);
        if (baseSkuUnit == null)
        {
            baseSkuUnit = new SkuUnit
            {
                TenantId = variant.TenantId,
                SkuId = variant.Id,
                UnitId = unit.Id,
                FactorToBase = 1m,
                CanPurchase = true,
                CanSell = true,
                IsDefaultPurchase = true,
                IsDefaultSale = true,
                QuantityDecimalPlaces = unit.DecimalPlaces,
                Version = 1,
                IsActive = true
            };
            _context.SkuUnits.Add(baseSkuUnit);
        }

        await _context.SaveChangesAsync(ct);
    }

    private static UnitResponse MapUnitToResponse(Unit u) => new()
    {
        Id = u.Id,
        Code = u.Code,
        Name = u.Name,
        Symbol = u.Symbol,
        MeasureGroup = u.MeasureGroup,
        DecimalPlaces = u.DecimalPlaces,
        IsActive = u.IsActive,
        CreatedAt = u.CreatedAt
    };

    private static SkuUnitResponse MapSkuUnitToResponse(SkuUnit u, Guid? baseUnitId, IReadOnlyDictionary<SkuUnitPriceKey, ResolvedPrice>? effectivePrices = null)
    {
        var isBase = baseUnitId.HasValue && u.UnitId == baseUnitId.Value;
        decimal? salePrice = null;
        if (!isBase && effectivePrices != null)
        {
            if (effectivePrices.TryGetValue(new SkuUnitPriceKey(u.SkuId, u.UnitId), out var p))
            {
                salePrice = p.Price;
            }
        }

        return new SkuUnitResponse
        {
            Id = u.Id,
            SkuId = u.SkuId,
            UnitId = u.UnitId,
            UnitCode = u.Unit?.Code ?? string.Empty,
            UnitName = u.Unit?.Name ?? string.Empty,
            UnitSymbol = u.Unit?.Symbol ?? string.Empty,
            MeasureGroup = u.Unit?.MeasureGroup ?? string.Empty,
            FactorToBase = u.FactorToBase,
            CanPurchase = u.CanPurchase,
            CanSell = u.CanSell,
            IsDefaultPurchase = u.IsDefaultPurchase,
            IsDefaultSale = u.IsDefaultSale,
            Barcode = u.Barcode,
            QuantityDecimalPlaces = u.QuantityDecimalPlaces,
            SalePrice = salePrice,
            Version = u.Version,
            IsActive = u.IsActive,
            IsBase = isBase
        };
    }
}
