using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Helpers;
using OISM.Application.Common.Interfaces;
using OISM.Domain.Entities;

namespace OISM.Application.Features.Catalog.Products.Services;

/// <summary>
/// Hiện thực hóa nghiệp vụ Quản lý Sản phẩm và Biến thể SKU (FR-PROD-02, FR-PROD-04, BE2-05, BE2-06).
/// </summary>
public class ProductService : IProductService
{
    private readonly IAppDbContext _context;
    private readonly ISqlLockService _lockService;

    public ProductService(IAppDbContext context, ISqlLockService lockService)
    {
        _context = context;
        _lockService = lockService;
    }

    public async Task<List<ProductResponse>> GetProductsAsync(string? search, Guid? categoryId, Guid? brandId, CancellationToken ct = default)
    {
        var query = _context.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Variants)
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
            Variants = p.Variants.Select(MapVariantToResponse).ToList()
        }).ToList();
    }

    public async Task<ProductResponse?> GetProductByIdAsync(Guid id, CancellationToken ct = default)
    {
        var product = await _context.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Variants)
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
            Variants = product.Variants.Select(MapVariantToResponse).ToList()
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

        // 4. Kiểm tra trùng lặp SKU với dữ liệu trong database thuộc Tenant hiện tại (NFR-TENANT / FR-PROD-02)
        var existingSku = await _context.ProductVariants
            .Where(v => inputSkus.Contains(v.SKU.ToUpper()))
            .Select(v => v.SKU)
            .FirstOrDefaultAsync(ct);

        if (existingSku != null)
        {
            throw new OismException("DUPLICATE_SKU", $"Mã SKU '{existingSku}' đã tồn tại trong cửa hàng của bạn. Vui lòng chọn mã SKU khác.", 409);
        }

        // 5. Kiểm tra trùng lặp Barcode với dữ liệu trong database thuộc Tenant hiện tại
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
        }

        var product = new Product
        {
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
                } while (attempts < 10 && (generatedBarcodes.Contains(barcode) || await _context.ProductVariants.AnyAsync(v => v.Barcode == barcode, ct)));
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

            var variant = new ProductVariant
            {
                SKU = skuFormatted,
                Barcode = barcode,
                RetailPrice = variantReq.RetailPrice,
                WholesalePrice = variantReq.WholesalePrice,
                CostPrice = 0,
                AttributesJson = resolvedVariant.SerializedJson,
                IsActive = true
            };

            product.Variants.Add(variant);
        }

        var strategy = _context.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.BeginTransactionAsync(ct);
            try
            {
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

            // Kiểm tra độc quyền Barcode nếu có
            if (!string.IsNullOrWhiteSpace(request.Barcode))
            {
                var barcodeTrimmed = request.Barcode.Trim();
                var barcodeExists = await _context.ProductVariants.AnyAsync(v => v.Barcode == barcodeTrimmed, ct);
                if (barcodeExists)
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
                } while (attempts < 10 && await _context.ProductVariants.AnyAsync(v => v.Barcode == barcode, ct));
            }
            else
            {
                barcode = request.Barcode.Trim();
            }

            var variant = new ProductVariant
            {
                ProductId = productId,
                SKU = skuFormatted,
                Barcode = barcode,
                RetailPrice = request.RetailPrice,
                WholesalePrice = request.WholesalePrice,
                CostPrice = 0,
                AttributesJson = newResolved.SerializedJson,
                IsActive = true
            };

            try
            {
                _context.ProductVariants.Add(variant);
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
                throw new OismException("DUPLICATE_SKU", $"Mã SKU '{skuFormatted}' đã tồn tại trong cửa hàng.", 409);
            }
        });
    }

    public async Task<ProductVariantResponse> UpdateVariantAsync(Guid productId, Guid variantId, UpdateVariantRequest request, CancellationToken ct = default)
    {
        var strategy = _context.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.BeginTransactionAsync(ct);

            await _lockService.LockProductAsync(productId, ct);

            var product = await _context.Products
                .Include(p => p.Variants)
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
                    if (barcodeExists)
                    {
                        await tx.RollbackAsync(ct);
                        throw new OismException("DUPLICATE_BARCODE", $"Mã vạch (Barcode) '{newBarcode}' đã tồn tại trong cửa hàng.", 409);
                    }
                    variant.Barcode = newBarcode;
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
                   (constraintName == "uq_skus_3" || message.Contains("uq_skus_3"));
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
