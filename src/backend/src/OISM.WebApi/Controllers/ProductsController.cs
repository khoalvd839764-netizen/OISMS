using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.Catalog.Products;
using OISM.Application.Features.Catalog.Products.Services;

namespace OISM.WebApi.Controllers;

[ApiController]
[Route("api/products")]
[Authorize]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;

    public ProductsController(IProductService productService)
    {
        _productService = productService;
    }

    /// <summary>
    /// Lấy danh sách sản phẩm kèm theo danh sách biến thể (SKU), thuộc tính và giá bán (FR-PROD-02, FR-PROD-04).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetProducts([FromQuery] string? search, [FromQuery] Guid? categoryId, [FromQuery] Guid? brandId, CancellationToken ct)
    {
        var products = await _productService.GetProductsAsync(search, categoryId, brandId, ct);
        return Ok(products);
    }

    /// <summary>
    /// Lấy chi tiết một sản phẩm theo Id kèm danh sách SKU.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetProductById(Guid id, CancellationToken ct)
    {
        var response = await _productService.GetProductByIdAsync(id, ct);
        if (response == null)
        {
            return NotFound(new { message = "Không tìm thấy sản phẩm." });
        }

        return Ok(response);
    }

    /// <summary>
    /// Tạo mới Sản phẩm và các Biến thể / SKU ban đầu (FR-PROD-02).
    /// BẮT BUỘC: Kiểm tra Unique SKU trong cùng TenantId và chặn trùng tổ hợp thuộc tính.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductRequest request, CancellationToken ct)
    {
        var validator = new CreateProductValidator();
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        try
        {
            var productId = await _productService.CreateProductAsync(request, ct);
            return StatusCode(201, new { message = "Tạo sản phẩm và biến thể thành công!", productId });
        }
        catch (OismException ex) when (ex.StatusCode == 409)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (OismException ex) when (ex.StatusCode == 400)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Thêm Biến thể (SKU) mới cho Sản phẩm đã tồn tại (FR-PROD-02, BE2-05).
    /// Khóa bi quan hàng Product để chống xung đột đồng thời và chặn trùng tổ hợp thuộc tính.
    /// </summary>
    [HttpPost("{id}/variants")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> CreateVariant(Guid id, [FromBody] CreateVariantRequest request, CancellationToken ct)
    {
        var validator = new CreateVariantValidator();
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        try
        {
            var variantResponse = await _productService.CreateVariantAsync(id, request, ct);
            return StatusCode(201, variantResponse);
        }
        catch (OismException ex) when (ex.StatusCode == 404)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (OismException ex) when (ex.StatusCode == 409)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (OismException ex) when (ex.StatusCode == 400)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Cập nhật thông tin Biến thể (SKU) của Sản phẩm (BE2-05).
    /// Hỗ trợ sửa giá, thuộc tính động, barcode, mã SKU và chặn trùng tổ hợp.
    /// </summary>
    [HttpPut("{productId}/variants/{variantId}")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> UpdateVariant(Guid productId, Guid variantId, [FromBody] UpdateVariantRequest request, CancellationToken ct)
    {
        var validator = new UpdateVariantValidator();
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        try
        {
            var variantResponse = await _productService.UpdateVariantAsync(productId, variantId, request, ct);
            return Ok(variantResponse);
        }
        catch (OismException ex) when (ex.StatusCode == 404)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (OismException ex) when (ex.StatusCode == 409)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (OismException ex) when (ex.StatusCode == 400)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Cập nhật Bảng giá Biến thể SKU (Giá bán lẻ / Giá bán sỉ - BE2-06).
    /// Tách biệt hoàn toàn giá bán với Giá vốn bình quân (WAC).
    /// </summary>
    [HttpPut("{productId}/variants/{variantId}/prices")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> UpdateVariantPrices(Guid productId, Guid variantId, [FromBody] UpdateVariantPricesRequest request, CancellationToken ct)
    {
        var validator = new UpdateVariantPricesValidator();
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        var variant = await _productService.UpdateVariantPricesAsync(productId, variantId, request, ct);
        if (variant == null)
        {
            return NotFound(new { message = "Không tìm thấy biến thể của sản phẩm này." });
        }

        return Ok(variant);
    }

    /// <summary>
    /// Vô hiệu hóa Biến thể SKU an toàn (Soft delete, bảo toàn lịch sử kho - BE2-05).
    /// </summary>
    [HttpDelete("{productId}/variants/{variantId}")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> DeleteVariant(Guid productId, Guid variantId, CancellationToken ct)
    {
        var success = await _productService.DeleteVariantAsync(productId, variantId, ct);
        if (!success)
        {
            return NotFound(new { message = "Không tìm thấy biến thể của sản phẩm này." });
        }

        return Ok(new { message = "Đã vô hiệu hóa biến thể SKU an toàn thành công.", variantId, isActive = false });
    }

    /// <summary>
    /// Bật/Tắt trạng thái hoạt động của Biến thể SKU (BE2-05).
    /// </summary>
    [HttpPut("{productId}/variants/{variantId}/toggle-status")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> ToggleVariantStatus(Guid productId, Guid variantId, CancellationToken ct)
    {
        var result = await _productService.ToggleVariantStatusAsync(productId, variantId, ct);
        if (result == null)
        {
            return NotFound(new { message = "Không tìm thấy biến thể của sản phẩm này." });
        }

        return Ok(new { message = $"Đã {(result.IsActive ? "kích hoạt" : "vô hiệu hóa")} biến thể thành công.", variantId, isActive = result.IsActive });
    }

    /// <summary>
    /// Cập nhật thông tin chung của Sản phẩm.
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> UpdateProduct(Guid id, [FromBody] UpdateProductRequest request, CancellationToken ct)
    {
        var validator = new UpdateProductValidator();
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        try
        {
            var success = await _productService.UpdateProductAsync(id, request, ct);
            if (!success)
            {
                return NotFound(new { message = "Không tìm thấy sản phẩm." });
            }

            return Ok(new { message = "Cập nhật sản phẩm thành công." });
        }
        catch (OismException ex) when (ex.StatusCode == 400)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Vô hiệu hóa Sản phẩm và toàn bộ Biến thể trực thuộc.
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> DeleteProduct(Guid id, CancellationToken ct)
    {
        var success = await _productService.DeleteProductAsync(id, ct);
        if (!success)
        {
            return NotFound(new { message = "Không tìm thấy sản phẩm." });
        }

        return Ok(new { message = "Đã vô hiệu hóa sản phẩm thành công." });
    }
}
