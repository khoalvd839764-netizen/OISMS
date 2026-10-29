using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Features.Catalog.Brands;
using OISM.Application.Features.Catalog.Brands.Services;

namespace OISM.WebApi.Controllers;

[ApiController]
[Route("api/brands")]
[Authorize]
public class BrandsController : ControllerBase
{
    private readonly IBrandService _brandService;

    public BrandsController(IBrandService brandService)
    {
        _brandService = brandService;
    }

    /// <summary>
    /// Lấy danh sách Thương hiệu sản phẩm (FR-PROD-01).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetBrands(CancellationToken ct)
    {
        var brands = await _brandService.GetBrandsAsync(ct);
        return Ok(brands);
    }

    /// <summary>
    /// Tạo mới Thương hiệu sản phẩm (FR-PROD-01).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> CreateBrand([FromBody] CreateBrandRequest request, CancellationToken ct)
    {
        var validator = new CreateBrandValidator();
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        var response = await _brandService.CreateBrandAsync(request, ct);
        return StatusCode(201, response);
    }

    /// <summary>
    /// Cập nhật Thương hiệu (FR-PROD-01).
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> UpdateBrand(Guid id, [FromBody] UpdateBrandRequest request, CancellationToken ct)
    {
        var validator = new UpdateBrandValidator();
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        var response = await _brandService.UpdateBrandAsync(id, request, ct);
        if (response == null)
        {
            return NotFound(new { message = "Không tìm thấy thương hiệu." });
        }

        return Ok(response);
    }

    /// <summary>
    /// Vô hiệu hóa Thương hiệu (FR-PROD-01).
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> DeleteBrand(Guid id, CancellationToken ct)
    {
        var success = await _brandService.DeleteBrandAsync(id, ct);
        if (!success)
        {
            return NotFound(new { message = "Không tìm thấy thương hiệu." });
        }

        return Ok(new { message = "Đã vô hiệu hóa thương hiệu thành công." });
    }
}
