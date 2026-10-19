using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Helpers;
using OISM.Application.Features.Catalog.Products;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;

namespace OISM.WebApi.Controllers;

[ApiController]
[Route("api/products")]
[Authorize]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _context;

    public ProductsController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Lấy danh sách sản phẩm kèm theo danh sách biến thể (SKU), thuộc tính và giá bán (FR-PROD-02, FR-PROD-04).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetProducts([FromQuery] string? search, [FromQuery] Guid? categoryId, [FromQuery] Guid? brandId)
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
            .ToListAsync();

        var products = productList.Select(p => new ProductResponse
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            CategoryId = p.CategoryId,
            CategoryName = p.Category != null ? p.Category.Name : null,
            BrandId = p.BrandId,
            BrandName = p.Brand != null ? p.Brand.Name : null,
            IsActive = p.IsActive,
            Variants = p.Variants.Select(MapVariantToResponse).ToList()
        }).ToList();

        return Ok(products);
    }

    /// <summary>
    /// Lấy chi tiết một sản phẩm theo Id kèm danh sách SKU.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetProductById(Guid id)
    {
        var product = await _context.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product == null)
        {
            return NotFound(new { message = "Không tìm thấy sản phẩm." });
        }

        var response = new ProductResponse
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

        return Ok(response);
    }

    /// <summary>
    /// Tạo mới Sản phẩm và các Biến thể / SKU ban đầu (FR-PROD-02).
    /// BẮT BUỘC: Kiểm tra Unique SKU trong cùng TenantId và chặn trùng tổ hợp thuộc tính.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductRequest request)
    {
        var validator = new CreateProductValidator();
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        // Kiểm tra CategoryId và BrandId nếu có
        if (request.CategoryId.HasValue)
        {
            var categoryExists = await _context.Categories.AnyAsync(c => c.Id == request.CategoryId.Value);
            if (!categoryExists)
            {
                return BadRequest(new { message = "Danh mục được chọn không tồn tại hoặc không thuộc cửa hàng của bạn." });
            }
        }

        if (request.BrandId.HasValue)
        {
            var brandExists = await _context.Brands.AnyAsync(b => b.Id == request.BrandId.Value);
            if (!brandExists)
            {
                return BadRequest(new { message = "Thương hiệu được chọn không tồn tại hoặc không thuộc cửa hàng của bạn." });
            }
        }

        if (request.Variants == null || request.Variants.Count == 0)
        {
            return BadRequest(new { message = "Sản phẩm phải có ít nhất một biến thể." });
        }

        if (request.Variants.Any(v => v == null))
        {
            return BadRequest(new { message = "Danh sách biến thể không được chứa phần tử rỗng (null)." });
        }

        // 1. Kiểm tra trùng lặp SKU trong danh sách gửi lên
        var inputSkus = request.Variants.Select(v => v.SKU.Trim().ToUpperInvariant()).ToList();
        if (inputSkus.Count != inputSkus.Distinct().Count())
        {
            return BadRequest(new { message = "Danh sách biến thể gửi lên có chứa mã SKU trùng nhau." });
        }

        // 2. Kiểm tra trùng lặp Barcode trong danh sách gửi lên
        var incomingBarcodes = request.Variants
            .Where(v => !string.IsNullOrWhiteSpace(v.Barcode))
            .Select(v => v.Barcode!.Trim())
            .ToList();

        if (incomingBarcodes.Count != incomingBarcodes.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            return Conflict(new { message = "Danh sách biến thể gửi lên có chứa mã vạch (Barcode) trùng nhau." });
        }

        // 3. Kiểm tra trùng lặp tổ hợp thuộc tính trong danh sách gửi lên
        var signatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in request.Variants)
        {
            var resolved = SkuAttributeHelper.ResolveAttributes(v.Color, v.Size, v.VariantName, v.Attributes);
            if (!signatures.Add(resolved.CombinationSignature))
            {
                return Conflict(new { message = $"Danh sách biến thể gửi lên có chứa tổ hợp thuộc tính trùng nhau: '{resolved.CombinationSignature}'." });
            }
        }

        // 4. Kiểm tra trùng lặp SKU với dữ liệu trong database thuộc Tenant hiện tại (NFR-TENANT / FR-PROD-02)
        var existingSku = await _context.ProductVariants
            .Where(v => inputSkus.Contains(v.SKU.ToUpper()))
            .Select(v => v.SKU)
            .FirstOrDefaultAsync();

        if (existingSku != null)
        {
            return Conflict(new { message = $"Mã SKU '{existingSku}' đã tồn tại trong cửa hàng của bạn. Vui lòng chọn mã SKU khác." });
        }

        // 5. Kiểm tra trùng lặp Barcode với dữ liệu trong database thuộc Tenant hiện tại
        if (incomingBarcodes.Count > 0)
        {
            var existingBarcode = await _context.ProductVariants
                .Where(v => v.Barcode != null && incomingBarcodes.Contains(v.Barcode))
                .Select(v => v.Barcode)
                .FirstOrDefaultAsync();

            if (existingBarcode != null)
            {
                return Conflict(new { message = $"Mã vạch (Barcode) '{existingBarcode}' đã tồn tại trong cửa hàng của bạn." });
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
                } while (attempts < 10 && (generatedBarcodes.Contains(barcode) || await _context.ProductVariants.AnyAsync(v => v.Barcode == barcode)));
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

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            try
            {
                _context.Products.Add(product);
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return StatusCode(201, new { message = "Tạo sản phẩm và biến thể thành công!", productId = product.Id });
            }
            catch (DbUpdateException ex) when (IsBarcodeUniqueViolation(ex))
            {
                await tx.RollbackAsync();
                return Conflict(new { message = "Mã vạch (Barcode) đã tồn tại trong cửa hàng." });
            }
            catch (DbUpdateException ex) when (IsSkuUniqueViolation(ex))
            {
                await tx.RollbackAsync();
                return Conflict(new { message = "Mã SKU đã tồn tại trong cửa hàng." });
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        });
    }

    /// <summary>
    /// Thêm Biến thể (SKU) mới cho Sản phẩm đã tồn tại (FR-PROD-02, BE2-05).
    /// Khóa bi quan hàng Product để chống xung đột đồng thời và chặn trùng tổ hợp thuộc tính.
    /// </summary>
    [HttpPost("{id}/variants")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> CreateVariant(Guid id, [FromBody] CreateVariantRequest request)
    {
        var validator = new CreateVariantValidator();
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);

            if (_context.Database.IsNpgsql())
            {
                // Khóa bi quan dòng sản phẩm trên PostgreSQL để chặn xung đột đồng thời thêm biến thể
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT id FROM products WHERE id = {id} FOR UPDATE");
            }

            var product = await _context.Products
                .Include(p => p.Variants)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                await tx.RollbackAsync();
                return NotFound(new { message = "Không tìm thấy sản phẩm." });
            }

            var skuFormatted = request.SKU.Trim().ToUpperInvariant();

            // Kiểm tra độc quyền mã SKU trong cùng TenantId
            var skuExists = await _context.ProductVariants.AnyAsync(v => v.SKU.ToUpper() == skuFormatted);
            if (skuExists)
            {
                await tx.RollbackAsync();
                return Conflict(new { message = $"Mã SKU '{skuFormatted}' đã tồn tại trong cửa hàng." });
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
                await tx.RollbackAsync();
                return Conflict(new { message = $"Sản phẩm đã có biến thể mang cùng tổ hợp thuộc tính (Mã SKU '{duplicateVariant.SKU}')." });
            }

            // Kiểm tra độc quyền Barcode nếu có
            if (!string.IsNullOrWhiteSpace(request.Barcode))
            {
                var barcodeTrimmed = request.Barcode.Trim();
                var barcodeExists = await _context.ProductVariants.AnyAsync(v => v.Barcode == barcodeTrimmed);
                if (barcodeExists)
                {
                    await tx.RollbackAsync();
                    return Conflict(new { message = $"Mã vạch (Barcode) '{barcodeTrimmed}' đã tồn tại trong cửa hàng." });
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
                } while (attempts < 10 && await _context.ProductVariants.AnyAsync(v => v.Barcode == barcode));
            }
            else
            {
                barcode = request.Barcode.Trim();
            }

            var variant = new ProductVariant
            {
                ProductId = id,
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
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return StatusCode(201, MapVariantToResponse(variant));
            }
            catch (DbUpdateException ex) when (IsBarcodeUniqueViolation(ex))
            {
                await tx.RollbackAsync();
                return Conflict(new { message = "Mã vạch (Barcode) đã tồn tại trong cửa hàng." });
            }
            catch (DbUpdateException ex) when (IsSkuUniqueViolation(ex))
            {
                await tx.RollbackAsync();
                return Conflict(new { message = $"Mã SKU '{skuFormatted}' đã tồn tại trong cửa hàng." });
            }
        });
    }

    /// <summary>
    /// Cập nhật thông tin Biến thể (SKU) của Sản phẩm (BE2-05).
    /// Hỗ trợ sửa giá, thuộc tính động, barcode, mã SKU và chặn trùng tổ hợp.
    /// </summary>
    [HttpPut("{productId}/variants/{variantId}")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> UpdateVariant(Guid productId, Guid variantId, [FromBody] UpdateVariantRequest request)
    {
        var validator = new UpdateVariantValidator();
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);

            if (_context.Database.IsNpgsql())
            {
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT id FROM products WHERE id = {productId} FOR UPDATE");
            }

            var product = await _context.Products
                .Include(p => p.Variants)
                .FirstOrDefaultAsync(p => p.Id == productId);

            if (product == null)
            {
                await tx.RollbackAsync();
                return NotFound(new { message = "Không tìm thấy sản phẩm." });
            }

            var variant = product.Variants.FirstOrDefault(v => v.Id == variantId);
            if (variant == null)
            {
                await tx.RollbackAsync();
                return NotFound(new { message = "Không tìm thấy biến thể trong sản phẩm này." });
            }

            // Nếu cập nhật SKU: kiểm tra độc quyền SKU trong Tenant
            if (!string.IsNullOrWhiteSpace(request.SKU))
            {
                var newSku = request.SKU.Trim().ToUpperInvariant();
                if (newSku != variant.SKU)
                {
                    var skuExists = await _context.ProductVariants.AnyAsync(v => v.SKU.ToUpper() == newSku && v.Id != variantId);
                    if (skuExists)
                    {
                        await tx.RollbackAsync();
                        return Conflict(new { message = $"Mã SKU '{newSku}' đã tồn tại trong cửa hàng." });
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
                await tx.RollbackAsync();
                return Conflict(new { message = $"Sản phẩm đã có biến thể khác mang cùng tổ hợp thuộc tính (Mã SKU '{duplicate.SKU}')." });
            }

            // Cập nhật thông tin
            variant.RetailPrice = request.RetailPrice;
            variant.WholesalePrice = request.WholesalePrice;
            variant.IsActive = request.IsActive;

            if (!string.IsNullOrWhiteSpace(request.Barcode))
            {
                var newBarcode = request.Barcode.Trim();
                if (newBarcode != variant.Barcode)
                {
                    var barcodeExists = await _context.ProductVariants.AnyAsync(v => v.Barcode == newBarcode && v.Id != variantId);
                    if (barcodeExists)
                    {
                        await tx.RollbackAsync();
                        return Conflict(new { message = $"Mã vạch (Barcode) '{newBarcode}' đã tồn tại trong cửa hàng." });
                    }
                    variant.Barcode = newBarcode;
                }
            }

            variant.AttributesJson = resolved.SerializedJson;

            try
            {
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(MapVariantToResponse(variant));
            }
            catch (DbUpdateException ex) when (IsBarcodeUniqueViolation(ex))
            {
                await tx.RollbackAsync();
                return Conflict(new { message = "Mã vạch (Barcode) đã tồn tại trong cửa hàng." });
            }
            catch (DbUpdateException ex) when (IsSkuUniqueViolation(ex))
            {
                await tx.RollbackAsync();
                return Conflict(new { message = "Mã SKU đã tồn tại trong cửa hàng." });
            }
        });
    }

    /// <summary>
    /// Cập nhật Bảng giá Biến thể SKU (Giá bán lẻ / Giá bán sỉ - BE2-06).
    /// Tách biệt hoàn toàn giá bán với Giá vốn bình quân (WAC).
    /// </summary>
    [HttpPut("{productId}/variants/{variantId}/prices")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> UpdateVariantPrices(Guid productId, Guid variantId, [FromBody] UpdateVariantPricesRequest request)
    {
        var validator = new UpdateVariantPricesValidator();
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        var variant = await _context.ProductVariants
            .FirstOrDefaultAsync(v => v.ProductId == productId && v.Id == variantId);

        if (variant == null)
        {
            return NotFound(new { message = "Không tìm thấy biến thể của sản phẩm này." });
        }

        variant.RetailPrice = request.RetailPrice;
        variant.WholesalePrice = request.WholesalePrice;

        await _context.SaveChangesAsync();

        return Ok(MapVariantToResponse(variant));
    }

    /// <summary>
    /// Vô hiệu hóa Biến thể SKU an toàn (Soft delete, bảo toàn lịch sử kho - BE2-05).
    /// </summary>
    [HttpDelete("{productId}/variants/{variantId}")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> DeleteVariant(Guid productId, Guid variantId)
    {
        var variant = await _context.ProductVariants
            .FirstOrDefaultAsync(v => v.ProductId == productId && v.Id == variantId);

        if (variant == null)
        {
            return NotFound(new { message = "Không tìm thấy biến thể của sản phẩm này." });
        }

        variant.IsActive = false;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Đã vô hiệu hóa biến thể SKU an toàn thành công.", variantId, isActive = false });
    }

    /// <summary>
    /// Bật/Tắt trạng thái hoạt động của Biến thể SKU (BE2-05).
    /// </summary>
    [HttpPut("{productId}/variants/{variantId}/toggle-status")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> ToggleVariantStatus(Guid productId, Guid variantId)
    {
        var variant = await _context.ProductVariants
            .FirstOrDefaultAsync(v => v.ProductId == productId && v.Id == variantId);

        if (variant == null)
        {
            return NotFound(new { message = "Không tìm thấy biến thể của sản phẩm này." });
        }

        variant.IsActive = !variant.IsActive;
        await _context.SaveChangesAsync();

        return Ok(new { message = $"Đã {(variant.IsActive ? "kích hoạt" : "vô hiệu hóa")} biến thể thành công.", variantId, isActive = variant.IsActive });
    }

    /// <summary>
    /// Cập nhật thông tin chung của Sản phẩm.
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> UpdateProduct(Guid id, [FromBody] UpdateProductRequest request)
    {
        var validator = new UpdateProductValidator();
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product == null)
        {
            return NotFound(new { message = "Không tìm thấy sản phẩm." });
        }

        if (request.CategoryId.HasValue)
        {
            var categoryExists = await _context.Categories.AnyAsync(c => c.Id == request.CategoryId.Value);
            if (!categoryExists)
            {
                return BadRequest(new { message = "Danh mục được chọn không tồn tại hoặc không thuộc cửa hàng của bạn." });
            }
        }

        if (request.BrandId.HasValue)
        {
            var brandExists = await _context.Brands.AnyAsync(b => b.Id == request.BrandId.Value);
            if (!brandExists)
            {
                return BadRequest(new { message = "Thương hiệu được chọn không tồn tại hoặc không thuộc cửa hàng của bạn." });
            }
        }

        product.Name = request.Name.Trim();
        product.Description = request.Description?.Trim();
        product.CategoryId = request.CategoryId;
        product.BrandId = request.BrandId;
        product.IsActive = request.IsActive;

        await _context.SaveChangesAsync();

        return Ok(new { message = "Cập nhật sản phẩm thành công." });
    }

    /// <summary>
    /// Vô hiệu hóa Sản phẩm và toàn bộ Biến thể trực thuộc.
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> DeleteProduct(Guid id)
    {
        var product = await _context.Products
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product == null)
        {
            return NotFound(new { message = "Không tìm thấy sản phẩm." });
        }

        product.IsActive = false;
        foreach (var v in product.Variants)
        {
            v.IsActive = false;
        }

        await _context.SaveChangesAsync();

        return Ok(new { message = "Đã vô hiệu hóa sản phẩm thành công." });
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
