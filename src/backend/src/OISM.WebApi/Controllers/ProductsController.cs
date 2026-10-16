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
    /// Lấy danh sách sản phẩm kèm theo danh sách biến thể (SKU) và giá bán (FR-PROD-02, FR-PROD-04).
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
                                                        (v.Barcode != null && v.Barcode.Contains(searchLower))));
        }

        if (categoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == categoryId.Value);
        }

        if (brandId.HasValue)
        {
            query = query.Where(p => p.BrandId == brandId.Value);
        }

        var products = await query
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ProductResponse
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                CategoryId = p.CategoryId,
                CategoryName = p.Category != null ? p.Category.Name : null,
                BrandId = p.BrandId,
                BrandName = p.Brand != null ? p.Brand.Name : null,
                IsActive = p.IsActive,
                Variants = p.Variants.Select(v => new ProductVariantResponse
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
                    IsActive = v.IsActive
                }).ToList()
            })
            .ToListAsync();

        return Ok(products);
    }

    /// <summary>
    /// Lấy chi tiết một sản phẩm theo Id.
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
            Variants = product.Variants.Select(v => new ProductVariantResponse
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
                IsActive = v.IsActive
            }).ToList()
        };

        return Ok(response);
    }

    /// <summary>
    /// Tạo mới Sản phẩm và các Biến thể / SKU ban đầu (FR-PROD-02).
    /// BẮT BUỘC: Kiểm tra Unique SKU trong cùng TenantId, trả về 409 Conflict nếu trùng.
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

        // 1. Kiểm tra trùng lặp SKU trong danh sách gửi lên
        var inputSkus = request.Variants.Select(v => v.SKU.Trim().ToUpper()).ToList();
        if (inputSkus.Count != inputSkus.Distinct().Count())
        {
            return BadRequest(new { message = "Danh sách biến thể gửi lên có chứa mã SKU trùng nhau." });
        }

        // 2. Kiểm tra trùng lặp SKU với dữ liệu trong database thuộc Tenant hiện tại (NFR-TENANT / FR-PROD-02)
        var existingSku = await _context.ProductVariants
            .Where(v => inputSkus.Contains(v.SKU.ToUpper()))
            .Select(v => v.SKU)
            .FirstOrDefaultAsync();

        if (existingSku != null)
        {
            return Conflict(new { message = $"Mã SKU '{existingSku}' đã tồn tại trong cửa hàng của bạn. Vui lòng chọn mã SKU khác." });
        }

        var product = new Product
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            CategoryId = request.CategoryId,
            BrandId = request.BrandId,
            IsActive = true
        };

        foreach (var variantReq in request.Variants)
        {
            var skuFormatted = variantReq.SKU.Trim().ToUpper();
            // Nếu không nhập mã vạch, tự sinh barcode chuẩn EAN-13 (FR-PROD-03)
            var barcode = string.IsNullOrWhiteSpace(variantReq.Barcode)
                ? BarcodeHelper.GenerateEan13()
                : variantReq.Barcode.Trim();

            product.Variants.Add(new ProductVariant
            {
                SKU = skuFormatted,
                VariantName = variantReq.VariantName.Trim(),
                Barcode = barcode,
                RetailPrice = variantReq.RetailPrice,
                WholesalePrice = variantReq.WholesalePrice,
                CostPrice = 0, // Giá vốn ban đầu là 0, cập nhật khi nhập kho
                Color = variantReq.Color?.Trim(),
                Size = variantReq.Size?.Trim(),
                IsActive = true
            });
        }

        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        return StatusCode(201, new { message = "Tạo sản phẩm và biến thể thành công!", productId = product.Id });
    }

    /// <summary>
    /// Thêm Biến thể (SKU) mới cho Sản phẩm đã tồn tại (FR-PROD-02).
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

        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product == null)
        {
            return NotFound(new { message = "Không tìm thấy sản phẩm." });
        }

        var skuFormatted = request.SKU.Trim().ToUpper();

        // Kiểm tra độc quyền mã SKU trong cùng TenantId
        var skuExists = await _context.ProductVariants.AnyAsync(v => v.SKU.ToUpper() == skuFormatted);
        if (skuExists)
        {
            return Conflict(new { message = $"Mã SKU '{skuFormatted}' đã tồn tại trong cửa hàng." });
        }

        var barcode = string.IsNullOrWhiteSpace(request.Barcode)
            ? BarcodeHelper.GenerateEan13()
            : request.Barcode.Trim();

        var variant = new ProductVariant
        {
            ProductId = id,
            SKU = skuFormatted,
            VariantName = request.VariantName.Trim(),
            Barcode = barcode,
            RetailPrice = request.RetailPrice,
            WholesalePrice = request.WholesalePrice,
            CostPrice = 0,
            Color = request.Color?.Trim(),
            Size = request.Size?.Trim(),
            IsActive = true
        };

        _context.ProductVariants.Add(variant);
        await _context.SaveChangesAsync();

        var response = new ProductVariantResponse
        {
            Id = variant.Id,
            ProductId = variant.ProductId,
            SKU = variant.SKU,
            VariantName = variant.VariantName,
            Barcode = variant.Barcode,
            RetailPrice = variant.RetailPrice,
            WholesalePrice = variant.WholesalePrice,
            CostPrice = variant.CostPrice,
            Color = variant.Color,
            Size = variant.Size,
            IsActive = variant.IsActive
        };

        return StatusCode(201, response);
    }

    /// <summary>
    /// Cập nhật thông tin chung của Sản phẩm.
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> UpdateProduct(Guid id, [FromBody] UpdateProductRequest request)
    {
        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product == null)
        {
            return NotFound(new { message = "Không tìm thấy sản phẩm." });
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
    /// Vô hiệu hóa Sản phẩm.
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> DeleteProduct(Guid id)
    {
        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product == null)
        {
            return NotFound(new { message = "Không tìm thấy sản phẩm." });
        }

        product.IsActive = false;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Đã vô hiệu hóa sản phẩm thành công." });
    }
}
