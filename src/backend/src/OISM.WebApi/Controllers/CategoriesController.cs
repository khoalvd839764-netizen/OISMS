using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Features.Catalog.Categories;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;

namespace OISM.WebApi.Controllers;

[ApiController]
[Route("api/categories")]
[Authorize]
public class CategoriesController : ControllerBase
{
    private readonly AppDbContext _context;

    public CategoriesController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Lấy danh sách danh mục theo cấu trúc phân cấp cây cha - con (FR-PROD-01).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetCategories()
    {
        var allCategories = await _context.Categories
            .OrderBy(c => c.Name)
            .ToListAsync();

        // Xây dựng cây danh mục: lấy các danh mục gốc (ParentId == null)
        var rootCategories = allCategories
            .Where(c => c.ParentId == null)
            .Select(c => MapToResponse(c, allCategories))
            .ToList();

        return Ok(rootCategories);
    }

    /// <summary>
    /// Tạo mới Danh mục sản phẩm (FR-PROD-01).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryRequest request)
    {
        var validator = new CreateCategoryValidator();
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        // Nếu có chọn danh mục cha, kiểm tra danh mục cha có tồn tại không
        if (request.ParentId.HasValue)
        {
            var parentExists = await _context.Categories.AnyAsync(c => c.Id == request.ParentId.Value);
            if (!parentExists)
            {
                return BadRequest(new { message = "Danh mục cha được chọn không tồn tại." });
            }
        }

        var category = new Category
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            ParentId = request.ParentId,
            IsActive = true
        };

        _context.Categories.Add(category);
        await _context.SaveChangesAsync();

        return StatusCode(201, new CategoryResponse
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description,
            ParentId = category.ParentId,
            IsActive = category.IsActive
        });
    }

    /// <summary>
    /// Cập nhật Danh mục sản phẩm - Chặn tạo vòng lặp cha - con (FR-PROD-01).
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> UpdateCategory(Guid id, [FromBody] UpdateCategoryRequest request)
    {
        var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id);
        if (category == null)
        {
            return NotFound(new { message = "Không tìm thấy danh mục." });
        }

        // Chặn chọn chính nó làm cha
        if (request.ParentId.HasValue && request.ParentId.Value == id)
        {
            return BadRequest(new { message = "Không thể đặt danh mục làm cha của chính nó (vòng lặp quan hệ)." });
        }

        category.Name = request.Name.Trim();
        category.Description = request.Description?.Trim();
        category.ParentId = request.ParentId;
        category.IsActive = request.IsActive;

        await _context.SaveChangesAsync();

        return Ok(new CategoryResponse
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description,
            ParentId = category.ParentId,
            IsActive = category.IsActive
        });
    }

    /// <summary>
    /// Xóa/Vô hiệu hóa danh mục sản phẩm (FR-PROD-01).
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> DeleteCategory(Guid id)
    {
        var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id);
        if (category == null)
        {
            return NotFound(new { message = "Không tìm thấy danh mục." });
        }

        category.IsActive = false;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Đã vô hiệu hóa danh mục thành công." });
    }

    private static CategoryResponse MapToResponse(Category category, List<Category> allCategories)
    {
        return new CategoryResponse
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description,
            ParentId = category.ParentId,
            IsActive = category.IsActive,
            SubCategories = allCategories
                .Where(sub => sub.ParentId == category.Id)
                .Select(sub => MapToResponse(sub, allCategories))
                .ToList()
        };
    }
}
