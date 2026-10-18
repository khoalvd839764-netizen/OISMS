using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Features.Catalog.Brands;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;

namespace OISM.WebApi.Controllers;

[ApiController]
[Route("api/brands")]
[Authorize]
public class BrandsController : ControllerBase
{
    private readonly AppDbContext _context;

    public BrandsController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Lấy danh sách Thương hiệu sản phẩm (FR-PROD-01).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetBrands()
    {
        var brands = await _context.Brands
            .OrderBy(b => b.Name)
            .Select(b => new BrandResponse
            {
                Id = b.Id,
                Name = b.Name,
                Description = b.Description,
                IsActive = b.IsActive
            })
            .ToListAsync();

        return Ok(brands);
    }

    /// <summary>
    /// Tạo mới Thương hiệu sản phẩm (FR-PROD-01).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> CreateBrand([FromBody] CreateBrandRequest request)
    {
        var validator = new CreateBrandValidator();
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        var brand = new Brand
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            IsActive = true
        };

        _context.Brands.Add(brand);
        await _context.SaveChangesAsync();

        return StatusCode(201, new BrandResponse
        {
            Id = brand.Id,
            Name = brand.Name,
            Description = brand.Description,
            IsActive = brand.IsActive
        });
    }

    /// <summary>
    /// Cập nhật Thương hiệu (FR-PROD-01).
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> UpdateBrand(Guid id, [FromBody] UpdateBrandRequest request)
    {
        var brand = await _context.Brands.FirstOrDefaultAsync(b => b.Id == id);
        if (brand == null)
        {
            return NotFound(new { message = "Không tìm thấy thương hiệu." });
        }

        brand.Name = request.Name.Trim();
        brand.Description = request.Description?.Trim();
        brand.IsActive = request.IsActive;

        await _context.SaveChangesAsync();

        return Ok(new BrandResponse
        {
            Id = brand.Id,
            Name = brand.Name,
            Description = brand.Description,
            IsActive = brand.IsActive
        });
    }

    /// <summary>
    /// Vô hiệu hóa Thương hiệu (FR-PROD-01).
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> DeleteBrand(Guid id)
    {
        var brand = await _context.Brands.FirstOrDefaultAsync(b => b.Id == id);
        if (brand == null)
        {
            return NotFound(new { message = "Không tìm thấy thương hiệu." });
        }

        brand.IsActive = false;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Đã vô hiệu hóa thương hiệu thành công." });
    }
}
