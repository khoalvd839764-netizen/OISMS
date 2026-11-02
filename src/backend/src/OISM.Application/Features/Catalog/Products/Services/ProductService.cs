using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Helpers;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Catalog.Units;
using OISM.Application.Features.Catalog.Units.Services;
using OISM.Domain.Entities;

namespace OISM.Application.Features.Catalog.Products.Services;

/// <summary>
/// Hiện thực hóa nghiệp vụ Quản lý Sản phẩm và Biến thể SKU (FR-PROD-02, FR-PROD-04, BE2-05, BE2-06).
/// </summary>
public class ProductService : IProductService
{
    private readonly IAppDbContext _context;
    private readonly ISqlLockService _lockService;
    private readonly ICurrentTenantService _currentTenantService;
    private readonly IUnitService _unitService;

    public ProductService(
        IAppDbContext context,
        ISqlLockService lockService,
        ICurrentTenantService currentTenantService,
        IUnitService unitService)
    {
        _context = context;
        _lockService = lockService;
        _currentTenantService = currentTenantService;
        _unitService = unitService;
    }

    public async Task<List<ProductResponse>> GetProductsAsync(string? search, Guid? categoryId, Guid? brandId, CancellationToken ct = default)
    {
        var query = _context.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Variants)
                .ThenInclude(v => v.BaseUnit)
            .Include(p => p.Variants)
                .ThenInclude(v => v.SkuUnits)
                    .ThenInclude(u => u.Unit)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(searchLower) ||
                                     p.Variants.Any(v => v.SKU.ToLower().Contains(searchLower) ||
                                                        (v.Barcode != null && v.Barcode.ToLower().Contains(searchLower))));
        }

        if (categoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == categoryId.Value);
        }

        if (brandId.HasValue)
        {
            query = query.Where(p => p.BrandId == brandId.Value);
        }

        var productList = await query
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

        return productList.Select(p => new ProductResponse
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            CategoryId = p.CategoryId,
            CategoryName = p.Category?.Name,
            BrandId = p.BrandId,
            BrandName = p.Brand?.Name,
            IsActive = p.IsActive,
            Variants = p.Variants.OrderBy(v => v.CreatedAt).ThenBy(v => v.Id).Select(MapVariantToResponse).ToList()
        }).ToList();
    }

    public async Task<ProductResponse?> GetProductByIdAsync(Guid id, CancellationToken ct = default)
    {
        var product = await _context.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Variants)
                .ThenInclude(v => v.BaseUnit)
            .Include(p => p.Variants)
                .ThenInclude(v => v.SkuUnits)
                    .ThenInclude(u => u.Unit)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (product == null)
        {
            return null;
        }

        return new ProductResponse
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            CategoryId = product.CategoryId,
            CategoryName = product.Category?.Name,
            BrandId = product.BrandId,
            BrandName = product.Brand?.Name,
            IsActive = product.IsActive,
            Variants = product.Variants.OrderBy(v => v.CreatedAt).ThenBy(v => v.Id).Select(MapVariantToResponse).ToList()
        };
    }

    public async Task<Guid> CreateProductAsync(CreateProductRequest request, CancellationToken ct = default)
    {
        if (request.CategoryId.HasValue)
        {
            var categoryExists = await _context.Categories.AnyAsync(c => c.Id == request.CategoryId.Value, ct);
            if (!categoryExists)
            {
                throw new OismException("CATEGORY_NOT_FOUND", "Danh mục được chọn không tồn tại hoặc không thuộc cửa hàng của bạn.", 400);
            }
        }

        if (request.BrandId.HasValue)
        {
            var brandExists = await _context.Brands.AnyAsync(b => b.Id == request.BrandId.Value, ct);
            if (!brandExists)
            {
                throw new OismException("BRAND_NOT_FOUND", "Thương hiệu được chọn không tồn tại hoặc không thuộc cửa hàng của bạn.", 400);
            }
        }

        if (request.Variants == null || request.Variants.Count == 0)
        {
            throw new OismException("EMPTY_VARIANTS", "Sản phẩm phải có ít nhất một biến thể.", 400);
        }

        if (request.Variants.Any(v => v == null))
        {
            throw new OismException("NULL_VARIANTS", "Danh sách biến thể không được chứa phần tử rỗng (null).", 400);
        }

        // 1. Kiểm tra trùng lặp SKU trong danh sách gửi lên
        var inputSkus = request.Variants.Select(v => v.SKU.Trim().ToUpperInvariant()).ToList();
        if (inputSkus.Count != inputSkus.Distinct().Count())
        {
            throw new OismException("DUPLICATE_INPUT_SKU", "Danh sách biến thể gửi lên có chứa mã SKU trùng nhau.", 400);
        }

        // 2. Kiểm tra trùng lặp Barcode trong danh sách gửi lên
        var incomingBarcodes = request.Variants
            .Where(v => !string.IsNullOrWhiteSpace(v.Barcode))
            .Select(v => v.Barcode!.Trim())
            .ToList();

        if (incomingBarcodes.Count != incomingBarcodes.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            throw new OismException("DUPLICATE_INPUT_BARCODE", "Danh sách biến thể gửi lên có chứa mã vạch (Barcode) trùng nhau.", 409);
        }

        // 3. Kiểm tra trùng lặp tổ hợp thuộc tính trong danh sách gửi lên
        var signatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in request.Variants)
        {
            var resolved = SkuAttributeHelper.ResolveAttributes(v.Color, v.Size, v.VariantName, v.Attributes);
            if (!signatures.Add(resolved.CombinationSignature))
            {
                throw new OismException("DUPLICATE_COMBINATION", $"Danh sách biến thể gửi lên có chứa tổ hợp thuộc tính trùng nhau: '{resolved.CombinationSignature}'.", 409);
            }
        }

        var tenantId = _currentTenantService.TenantId ?? Guid.Empty;
        var strategy = _context.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.BeginTransactionAsync(ct);
            try
            {
                await _lockService.LockUnitCatalogAsync(tenantId, ct);
                await _unitService.EnsureTenantDefaultUnitsAsync(tenantId, ct);

                // 4. Kiểm tra trùng lặp SKU với dữ liệu trong database thuộc Tenant hiện tại (NFR-TENANT / FR-PROD-02)
                var existingSku = await _context.ProductVariants
                    .Where(v => inputSkus.Contains(v.SKU.ToUpper()))
                    .Select(v => v.SKU)
                    .FirstOrDefaultAsync(ct);

                if (existingSku != null)
                {
                    throw new OismException("DUPLICATE_SKU", $"Mã SKU '{existingSku}' đã tồn tại trong cửa hàng của bạn. Vui lòng chọn mã SKU khác.", 409);
                }

                // 5. Kiểm tra trùng lặp Barcode với dữ liệu trong database thuộc Tenant hiện tại (cả skus và sku_units)
                if (incomingBarcodes.Count > 0)
                {
                    var existingBarcode = await _context.ProductVariants
                        .Where(v => v.Barcode != null && incomingBarcodes.Contains(v.Barcode))
                        .Select(v => v.Barcode)
                        .FirstOrDefaultAsync(ct);

                    if (existingBarcode != null)
                    {
                        throw new OismException("DUPLICATE_BARCODE", $"Mã vạch (Barcode) '{existingBarcode}' đã tồn tại trong cửa hàng của bạn.", 409);
                    }

                    var existingSkuUnitBarcode = await _context.SkuUnits
                        .Where(u => u.Barcode != null && incomingBarcodes.Contains(u.Barcode))
                        .Select(u => u.Barcode)
                        .FirstOrDefaultAsync(ct);

                    if (existingSkuUnitBarcode != null)
                    {
                        throw new OismException("DUPLICATE_BARCODE", $"Mã vạch (Barcode) '{existingSkuUnitBarcode}' đã tồn tại trong cửa hàng của bạn.", 409);
                    }
                }

                var product = new Product
                {
                    TenantId = tenantId,
                    Name = request.Name.Trim(),
                    Description = request.Description?.Trim(),
                    CategoryId = request.CategoryId,
                    BrandId = request.BrandId,
                    IsActive = true
                };

                var generatedBarcodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var variantReq in request.Variants)
                {
                    var skuFormatted = variantReq.SKU.Trim().ToUpperInvariant();
                    string barcode;
                    if (string.IsNullOrWhiteSpace(variantReq.Barcode))
                    {
                        var attempts = 0;
                        do
                        {
                            barcode = BarcodeHelper.GenerateEan13();
                            attempts++;
                        } while (attempts < 10 && (generatedBarcodes.Contains(barcode) || await _context.ProductVariants.AnyAsync(v => v.Barcode == barcode, ct) || await _context.SkuUnits.AnyAsync(u => u.Barcode == barcode, ct)));
                        generatedBarcodes.Add(barcode);
                    }
                    else
                    {
                        barcode = variantReq.Barcode.Trim();
                    }

                    var resolvedVariant = SkuAttributeHelper.ResolveAttributes(
                        variantReq.Color,
                        variantReq.Size,
                        variantReq.VariantName,
                        variantReq.Attributes);

                    Unit? baseUnit = null;
                    if (variantReq.BaseUnitId.HasValue)
                    {
                        baseUnit = await _context.Units.FirstOrDefaultAsync(u => u.Id == variantReq.BaseUnitId.Value, ct);
                        if (baseUnit == null)
                        {
                            throw new OismException("UNIT_NOT_FOUND", "Không tìm thấy đơn vị tính gốc yêu cầu.", 404);
                        }
                        if (!baseUnit.IsActive)
                        {
                            throw new OismException("UNIT_NOT_ALLOWED", $"Đơn vị tính '{baseUnit.Name}' đang ngừng hoạt động.", 422);
                        }
                    }
                    else
                    {
                        var targetCode = string.IsNullOrWhiteSpace(variantReq.Unit) ? "piece" : variantReq.Unit.Trim().ToLowerInvariant();
                        baseUnit = await _context.Units.FirstOrDefaultAsync(u => u.Code == targetCode, ct);
                        if (baseUnit == null)
                        {
                            baseUnit = new Unit
                            {
                                TenantId = tenantId,
                                Code = targetCode,
                                Name = targetCode,
                                Symbol = targetCode,
                                MeasureGroup = MeasureGroups.Count,
                                DecimalPlaces = 3,
                                IsActive = true
                            };
                            _context.Units.Add(baseUnit);
                            await _context.SaveChangesAsync(ct);
                        }
                    }

                    var variant = new ProductVariant
                    {
                        TenantId = tenantId,
                        SKU = skuFormatted,
                        Barcode = barcode,
                        RetailPrice = variantReq.RetailPrice,
                        WholesalePrice = variantReq.WholesalePrice,
                        CostPrice = 0,
                        AttributesJson = resolvedVariant.SerializedJson,
                        BaseUnitId = baseUnit.Id,
                        Unit = baseUnit.Code,
                        IsActive = true
                    };

                    var baseSkuUnit = new SkuUnit
                    {
                        TenantId = tenantId,
                        SkuId = variant.Id,
                        UnitId = baseUnit.Id,
                        FactorToBase = 1m,
                        CanPurchase = true,
                        CanSell = true,
                        IsDefaultPurchase = true,
                        IsDefaultSale = true,
                        QuantityDecimalPlaces = baseUnit.DecimalPlaces,
                        Version = 1,
                        IsActive = true
                    };
                    variant.SkuUnits.Add(baseSkuUnit);

                    product.Variants.Add(variant);
                }

                _context.Products.Add(product);
                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return product.Id;
            }
            catch (DbUpdateException ex) when (IsBarcodeUniqueViolation(ex))
            {
                await tx.RollbackAsync(ct);
                throw new OismException("DUPLICATE_BARCODE", "Mã vạch (Barcode) đã tồn tại trong cửa hàng.", 409);
            }
            catch (DbUpdateException ex) when (IsSkuUniqueViolation(ex))
            {
                await tx.RollbackAsync(ct);
                throw new OismException("DUPLICATE_SKU", "Mã SKU đã tồn tại trong cửa hàng.", 409);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        });
    }

    public async Task<ProductVariantResponse> CreateVariantAsync(Guid productId, CreateVariantRequest request, CancellationToken ct = default)
    {
        var strategy = _context.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.BeginTransactionAsync(ct);

            await _lockService.LockProductAsync(productId, ct);
            var tenantId = _currentTenantService.TenantId ?? Guid.Empty;
            await _lockService.LockUnitCatalogAsync(tenantId, ct);
            await _unitService.EnsureTenantDefaultUnitsAsync(tenantId, ct);

            var product = await _context.Products
                .Include(p => p.Variants)
                .FirstOrDefaultAsync(p => p.Id == productId, ct);

            if (product == null)
            {
                await tx.RollbackAsync(ct);
                throw new OismException("PRODUCT_NOT_FOUND", "Không tìm thấy sản phẩm.", 404);
            }

            var skuFormatted = request.SKU.Trim().ToUpperInvariant();

            // Kiểm tra độc quyền mã SKU trong cùng TenantId
            var skuExists = await _context.ProductVariants.AnyAsync(v => v.SKU.ToUpper() == skuFormatted, ct);
            if (skuExists)
            {
                await tx.RollbackAsync(ct);
                throw new OismException("DUPLICATE_SKU", $"Mã SKU '{skuFormatted}' đã tồn tại trong cửa hàng.", 409);
            }

            // Kiểm tra trùng tổ hợp thuộc tính với các biến thể đã có của sản phẩm
            var newResolved = SkuAttributeHelper.ResolveAttributes(request.Color, request.Size, request.VariantName, request.Attributes);
            var duplicateVariant = product.Variants.FirstOrDefault(v =>
                string.Equals(
                    SkuAttributeHelper.ResolveAttributes(v.Color, v.Size, v.VariantName, v.GetAttributes(), v.AttributesJson).CombinationSignature,
                    newResolved.CombinationSignature,
                    StringComparison.OrdinalIgnoreCase));

            if (duplicateVariant != null)
            {
                await tx.RollbackAsync(ct);
                throw new OismException("DUPLICATE_COMBINATION", $"Sản phẩm đã có biến thể mang cùng tổ hợp thuộc tính (Mã SKU '{duplicateVariant.SKU}').", 409);
            }

            // Kiểm tra độc quyền Barcode nếu có (cả skus và sku_units)
            if (!string.IsNullOrWhiteSpace(request.Barcode))
            {
                var barcodeTrimmed = request.Barcode.Trim();
                var barcodeExists = await _context.ProductVariants.AnyAsync(v => v.Barcode == barcodeTrimmed, ct);
                var skuUnitBarcodeExists = await _context.SkuUnits.AnyAsync(u => u.Barcode == barcodeTrimmed, ct);
                if (barcodeExists || skuUnitBarcodeExists)
                {
                    await tx.RollbackAsync(ct);
                    throw new OismException("DUPLICATE_BARCODE", $"Mã vạch (Barcode) '{barcodeTrimmed}' đã tồn tại trong cửa hàng.", 409);
                }
            }

            string barcode;
            if (string.IsNullOrWhiteSpace(request.Barcode))
            {
                var attempts = 0;
                do
                {
                    barcode = BarcodeHelper.GenerateEan13();
                    attempts++;
                } while (attempts < 10 && (await _context.ProductVariants.AnyAsync(v => v.Barcode == barcode, ct) || await _context.SkuUnits.AnyAsync(u => u.Barcode == barcode, ct)));
            }
            else
            {
                barcode = request.Barcode.Trim();
            }

            Unit? baseUnit = null;
            if (request.BaseUnitId.HasValue)
            {
                baseUnit = await _context.Units.FirstOrDefaultAsync(u => u.Id == request.BaseUnitId.Value, ct);
                if (baseUnit == null)
                {
                    await tx.RollbackAsync(ct);
                    throw new OismException("UNIT_NOT_FOUND", "Không tìm thấy đơn vị tính gốc yêu cầu.", 404);
                }
                if (!baseUnit.IsActive)
                {
                    await tx.RollbackAsync(ct);
                    throw new OismException("UNIT_NOT_ALLOWED", $"Đơn vị tính '{baseUnit.Name}' đang ngừng hoạt động.", 422);
                }
            }
            else
            {
                var targetCode = string.IsNullOrWhiteSpace(request.Unit) ? "piece" : request.Unit.Trim().ToLowerInvariant();
                baseUnit = await _context.Units.FirstOrDefaultAsync(u => u.Code == targetCode, ct);
                if (baseUnit == null)
                {
                    baseUnit = new Unit
                    {
                        TenantId = tenantId,
                        Code = targetCode,
                        Name = targetCode,
                        Symbol = targetCode,
                        MeasureGroup = MeasureGroups.Count,
                        DecimalPlaces = 3,
                        IsActive = true
                    };
                    _context.Units.Add(baseUnit);
                    await _context.SaveChangesAsync(ct);
                }
            }

            var variant = new ProductVariant
            {
                TenantId = tenantId,
                ProductId = productId,
                SKU = skuFormatted,
                Barcode = barcode,
                RetailPrice = request.RetailPrice,
                WholesalePrice = request.WholesalePrice,
                CostPrice = 0,
                AttributesJson = newResolved.SerializedJson,
                BaseUnitId = baseUnit.Id,
                Unit = baseUnit.Code,
                IsActive = true
            };

            var baseSkuUnit = new SkuUnit
            {
                TenantId = tenantId,
                SkuId = variant.Id,
                UnitId = baseUnit.Id,
                FactorToBase = 1m,
                CanPurchase = true,
                CanSell = true,
                IsDefaultPurchase = true,
                IsDefaultSale = true,
                QuantityDecimalPlaces = baseUnit.DecimalPlaces,
                Version = 1,
                IsActive = true
            };
            variant.SkuUnits.Add(baseSkuUnit);

            try
            {
                _context.ProductVariants.Add(variant);
                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                variant.BaseUnit = baseUnit;
                return MapVariantToResponse(variant);
            }
            catch (DbUpdateException ex) when (IsBarcodeUniqueViolation(ex))
            {
                await tx.RollbackAsync(ct);
                throw new OismException("DUPLICATE_BARCODE", "Mã vạch (Barcode) đã tồn tại trong cửa hàng.", 409);
            }
            catch (DbUpdateException ex) when (IsSkuUniqueViolation(ex))
            {
                await tx.RollbackAsync(ct);
                throw new OismException("DUPLICATE_SKU", $"Mã SKU '{skuFormatted}' đã tồn tại trong cửa hàng.", 409);
            }
        });
    }

    public async Task<ProductVariantResponse> UpdateVariantAsync(Guid productId, Guid variantId, UpdateVariantRequest request, Guid? userId = null, CancellationToken ct = default)
    {
        var strategy = _context.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.BeginTransactionAsync(ct);

            await _lockService.LockProductAsync(productId, ct);
            var tenantId = _currentTenantService.TenantId ?? Guid.Empty;
            await _lockService.LockUnitCatalogAsync(tenantId, ct);
            await _unitService.EnsureTenantDefaultUnitsAsync(tenantId, ct);

            var product = await _context.Products
                .Include(p => p.Variants)
                    .ThenInclude(v => v.BaseUnit)
                .Include(p => p.Variants)
                    .ThenInclude(v => v.SkuUnits)
                        .ThenInclude(u => u.Unit)
                .FirstOrDefaultAsync(p => p.Id == productId, ct);

            if (product == null)
            {
                await tx.RollbackAsync(ct);
                throw new OismException("PRODUCT_NOT_FOUND", "Không tìm thấy sản phẩm.", 404);
            }

            var variant = product.Variants.FirstOrDefault(v => v.Id == variantId);
            if (variant == null)
            {
                await tx.RollbackAsync(ct);
                throw new OismException("VARIANT_NOT_FOUND", "Không tìm thấy biến thể trong sản phẩm này.", 404);
            }

            // Nếu cập nhật SKU: kiểm tra độc quyền SKU trong Tenant
            if (!string.IsNullOrWhiteSpace(request.SKU))
            {
                var newSku = request.SKU.Trim().ToUpperInvariant();
                if (newSku != variant.SKU)
                {
                    var skuExists = await _context.ProductVariants.AnyAsync(v => v.SKU.ToUpper() == newSku && v.Id != variantId, ct);
                    if (skuExists)
                    {
                        await tx.RollbackAsync(ct);
                        throw new OismException("DUPLICATE_SKU", $"Mã SKU '{newSku}' đã tồn tại trong cửa hàng.", 409);
                    }
                    variant.SKU = newSku;
                }
            }

            // Resolve các thuộc tính sau khi hợp nhất từ variant.AttributesJson cũ, request.Attributes và explicit fields
            var resolved = SkuAttributeHelper.ResolveAttributes(
                request.Color,
                request.Size,
                request.VariantName,
                request.Attributes,
                variant.AttributesJson);

            var duplicate = product.Variants.FirstOrDefault(v =>
                v.Id != variantId &&
                string.Equals(
                    SkuAttributeHelper.ResolveAttributes(v.Color, v.Size, v.VariantName, v.GetAttributes(), v.AttributesJson).CombinationSignature,
                    resolved.CombinationSignature,
                    StringComparison.OrdinalIgnoreCase));

            if (duplicate != null)
            {
                await tx.RollbackAsync(ct);
                throw new OismException("DUPLICATE_COMBINATION", $"Sản phẩm đã có biến thể khác mang cùng tổ hợp thuộc tính (Mã SKU '{duplicate.SKU}').", 409);
            }

            variant.RetailPrice = request.RetailPrice;
            variant.WholesalePrice = request.WholesalePrice;
            variant.IsActive = request.IsActive;

            if (!string.IsNullOrWhiteSpace(request.Barcode))
            {
                var newBarcode = request.Barcode.Trim();
                if (newBarcode != variant.Barcode)
                {
                    var barcodeExists = await _context.ProductVariants.AnyAsync(v => v.Barcode == newBarcode && v.Id != variantId, ct);
                    var skuUnitBarcodeExists = await _context.SkuUnits.AnyAsync(u => u.Barcode == newBarcode, ct);
                    if (barcodeExists || skuUnitBarcodeExists)
                    {
                        await tx.RollbackAsync(ct);
                        throw new OismException("DUPLICATE_BARCODE", $"Mã vạch (Barcode) '{newBarcode}' đã tồn tại trong cửa hàng.", 409);
                    }
                    variant.Barcode = newBarcode;
                }
            }

            if (request.BaseUnitId.HasValue && request.BaseUnitId.Value != variant.BaseUnitId)
            {
                var hasActiveConversions = variant.SkuUnits.Any(u => u.IsActive && u.UnitId != variant.BaseUnitId);
                if (hasActiveConversions)
                {
                    await tx.RollbackAsync(ct);
                    throw new OismException("BASE_UNIT_LOCKED", "Không thể thay đổi đơn vị tính gốc khi SKU còn đơn vị quy đổi đang kích hoạt.", 409);
                }

                var hasOrderItems = await _context.OrderItems.AnyAsync(oi => oi.SkuId == variantId, ct);
                var hasReceiptItems = await _context.GoodsReceiptItems.AnyAsync(gri => gri.SkuId == variantId, ct);
                var hasInventoryTx = await _context.InventoryTransactions.AnyAsync(it => it.SkuId == variantId, ct);
                var hasStock = await _context.InventoryBalances.AnyAsync(ib => ib.SkuId == variantId && (ib.OnHand != 0 || ib.Reserved != 0), ct);

                if (hasOrderItems || hasReceiptItems || hasInventoryTx || hasStock)
                {
                    await tx.RollbackAsync(ct);
                    throw new OismException("BASE_UNIT_LOCKED", "Không thể thay đổi đơn vị tính gốc khi SKU đã có lịch sử giao dịch hoặc số dư kho.", 409);
                }

                var newBaseUnit = await _context.Units.FirstOrDefaultAsync(u => u.Id == request.BaseUnitId.Value, ct);
                if (newBaseUnit == null)
                {
                    await tx.RollbackAsync(ct);
                    throw new OismException("UNIT_NOT_FOUND", "Không tìm thấy đơn vị tính gốc yêu cầu.", 404);
                }
                if (!newBaseUnit.IsActive)
                {
                    await tx.RollbackAsync(ct);
                    throw new OismException("UNIT_NOT_ALLOWED", $"Đơn vị tính '{newBaseUnit.Name}' đang ngừng hoạt động.", 422);
                }

                var oldBaseUnitId = variant.BaseUnitId;
                variant.BaseUnitId = newBaseUnit.Id;
                variant.Unit = newBaseUnit.Code;
                variant.BaseUnit = newBaseUnit;

                var oldBaseSkuUnit = variant.SkuUnits.FirstOrDefault(u => oldBaseUnitId.HasValue && u.UnitId == oldBaseUnitId.Value);
                if (oldBaseSkuUnit != null)
                {
                    oldBaseSkuUnit.IsDefaultPurchase = false;
                    oldBaseSkuUnit.IsDefaultSale = false;
                    // Giữ dòng cũ ở trạng thái tắt vì có thể đã có lịch sử hệ số (chỉ thêm, không xóa được)
                    oldBaseSkuUnit.IsActive = false;
                    oldBaseSkuUnit.UpdatedBy = userId;
                    oldBaseSkuUnit.UpdatedAt = DateTimeOffset.UtcNow;
                    await _context.SaveChangesAsync(ct);
                }

                var newBaseSkuUnit = variant.SkuUnits.FirstOrDefault(u => u.UnitId == newBaseUnit.Id);
                if (newBaseSkuUnit == null)
                {
                    newBaseSkuUnit = new SkuUnit
                    {
                        TenantId = tenantId,
                        SkuId = variantId,
                        UnitId = newBaseUnit.Id,
                        FactorToBase = 1m,
                        CanPurchase = true,
                        CanSell = true,
                        IsDefaultPurchase = true,
                        IsDefaultSale = true,
                        QuantityDecimalPlaces = newBaseUnit.DecimalPlaces,
                        Version = 1,
                        IsActive = true
                    };
                    _context.SkuUnits.Add(newBaseSkuUnit);
                    variant.SkuUnits.Add(newBaseSkuUnit);
                }
                else
                {
                    if (newBaseSkuUnit.FactorToBase != 1m)
                    {
                        var oldVersion = newBaseSkuUnit.Version;
                        _context.SkuUnitFactorHistories.Add(new SkuUnitFactorHistory
                        {
                            TenantId = tenantId,
                            SkuUnitId = newBaseSkuUnit.Id,
                            OldFactor = newBaseSkuUnit.FactorToBase,
                            NewFactor = 1m,
                            OldVersion = oldVersion,
                            NewVersion = oldVersion + 1,
                            ChangedBy = userId,
                            ChangedAt = DateTimeOffset.UtcNow
                        });
                        newBaseSkuUnit.Version = oldVersion + 1;
                        newBaseSkuUnit.FactorToBase = 1m;
                    }
                    newBaseSkuUnit.SalePrice = null;
                    newBaseSkuUnit.CanPurchase = true;
                    newBaseSkuUnit.CanSell = true;
                    newBaseSkuUnit.UpdatedBy = userId;
                    newBaseSkuUnit.UpdatedAt = DateTimeOffset.UtcNow;
                    newBaseSkuUnit.IsDefaultPurchase = true;
                    newBaseSkuUnit.IsDefaultSale = true;
                    newBaseSkuUnit.IsActive = true;
                }
            }

            variant.AttributesJson = resolved.SerializedJson;

            try
            {
                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return MapVariantToResponse(variant);
            }
            catch (DbUpdateException ex) when (IsBarcodeUniqueViolation(ex))
            {
                await tx.RollbackAsync(ct);
                throw new OismException("DUPLICATE_BARCODE", "Mã vạch (Barcode) đã tồn tại trong cửa hàng.", 409);
            }
            catch (DbUpdateException ex) when (IsSkuUniqueViolation(ex))
            {
                await tx.RollbackAsync(ct);
                throw new OismException("DUPLICATE_SKU", "Mã SKU đã tồn tại trong cửa hàng.", 409);
            }
        });
    }

    public async Task<ProductVariantResponse?> UpdateVariantPricesAsync(Guid productId, Guid variantId, UpdateVariantPricesRequest request, CancellationToken ct = default)
    {
        var variant = await _context.ProductVariants
            .Include(v => v.BaseUnit)
            .Include(v => v.SkuUnits)
                .ThenInclude(u => u.Unit)
            .FirstOrDefaultAsync(v => v.ProductId == productId && v.Id == variantId, ct);

        if (variant == null)
        {
            return null;
        }

        variant.RetailPrice = request.RetailPrice;
        variant.WholesalePrice = request.WholesalePrice;

        await _context.SaveChangesAsync(ct);

        return MapVariantToResponse(variant);
    }

    public async Task<bool> DeleteVariantAsync(Guid productId, Guid variantId, CancellationToken ct = default)
    {
        var variant = await _context.ProductVariants
            .FirstOrDefaultAsync(v => v.ProductId == productId && v.Id == variantId, ct);

        if (variant == null)
        {
            return false;
        }

        variant.IsActive = false;
        await _context.SaveChangesAsync(ct);

        return true;
    }

    public async Task<ToggleVariantResult?> ToggleVariantStatusAsync(Guid productId, Guid variantId, CancellationToken ct = default)
    {
        var variant = await _context.ProductVariants
            .FirstOrDefaultAsync(v => v.ProductId == productId && v.Id == variantId, ct);

        if (variant == null)
        {
            return null;
        }

        variant.IsActive = !variant.IsActive;
        await _context.SaveChangesAsync(ct);

        return new ToggleVariantResult(variant.Id, variant.IsActive);
    }

    public async Task<bool> UpdateProductAsync(Guid id, UpdateProductRequest request, CancellationToken ct = default)
    {
        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product == null)
        {
            return false;
        }

        if (request.CategoryId.HasValue)
        {
            var categoryExists = await _context.Categories.AnyAsync(c => c.Id == request.CategoryId.Value, ct);
            if (!categoryExists)
            {
                throw new OismException("CATEGORY_NOT_FOUND", "Danh mục được chọn không tồn tại hoặc không thuộc cửa hàng của bạn.", 400);
            }
        }

        if (request.BrandId.HasValue)
        {
            var brandExists = await _context.Brands.AnyAsync(b => b.Id == request.BrandId.Value, ct);
            if (!brandExists)
            {
                throw new OismException("BRAND_NOT_FOUND", "Thương hiệu được chọn không tồn tại hoặc không thuộc cửa hàng của bạn.", 400);
            }
        }

        product.Name = request.Name.Trim();
        product.Description = request.Description?.Trim();
        product.CategoryId = request.CategoryId;
        product.BrandId = request.BrandId;
        product.IsActive = request.IsActive;

        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteProductAsync(Guid id, CancellationToken ct = default)
    {
        var product = await _context.Products
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (product == null)
        {
            return false;
        }

        product.IsActive = false;
        foreach (var v in product.Variants)
        {
            v.IsActive = false;
        }

        await _context.SaveChangesAsync(ct);
        return true;
    }

    private static ProductVariantResponse MapVariantToResponse(ProductVariant v)
    {
        return new ProductVariantResponse
        {
            Id = v.Id,
            ProductId = v.ProductId,
            SKU = v.SKU,
            VariantName = v.VariantName,
            Barcode = v.Barcode,
            RetailPrice = v.RetailPrice,
            WholesalePrice = v.WholesalePrice,
            CostPrice = v.CostPrice,
            Color = v.Color,
            Size = v.Size,
            Attributes = SkuAttributeHelper.ExtractAttributes(v.AttributesJson),
            BaseUnitId = v.BaseUnitId,
            BaseUnitCode = v.BaseUnit?.Code ?? v.Unit,
            BaseUnitName = v.BaseUnit?.Name ?? v.Unit,
            Units = v.SkuUnits?.Select(u => new SkuUnitResponse
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
                SalePrice = u.SalePrice,
                Version = u.Version,
                IsActive = u.IsActive,
                IsBase = v.BaseUnitId.HasValue && u.UnitId == v.BaseUnitId.Value
            }).ToList() ?? new List<SkuUnitResponse>(),
            IsActive = v.IsActive
        };
    }

    private static bool IsBarcodeUniqueViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        if (inner == null) return false;
        var type = inner.GetType();
        if (type.Name == "PostgresException")
        {
            var sqlState = type.GetProperty("SqlState")?.GetValue(inner) as string;
            var constraintName = type.GetProperty("ConstraintName")?.GetValue(inner) as string;
            var message = inner.Message;
            return sqlState == "23505" &&
                   (constraintName == "uq_skus_3" || message.Contains("uq_skus_3") ||
                    constraintName == "ux_sku_units_barcode" || message.Contains("ux_sku_units_barcode"));
        }
        return false;
    }

    private static bool IsSkuUniqueViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        if (inner == null) return false;
        var type = inner.GetType();
        if (type.Name == "PostgresException")
        {
            var sqlState = type.GetProperty("SqlState")?.GetValue(inner) as string;
            var constraintName = type.GetProperty("ConstraintName")?.GetValue(inner) as string;
            var message = inner.Message;
            return sqlState == "23505" &&
                   (constraintName == "uq_skus_2" || message.Contains("uq_skus_2"));
        }
        return false;
    }
}
