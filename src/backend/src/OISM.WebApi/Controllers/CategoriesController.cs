using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Catalog.Categories;
using OISM.Application.Features.Catalog.Categories.Services;

namespace OISM.WebApi.Controllers;

[ApiController]
[Route("api/categories")]
[Authorize]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryService _categoryService;
    private readonly ICurrentTenantService? _currentTenantService;

    public CategoriesController(ICategoryService categoryService, ICurrentTenantService? currentTenantService = null)
    {
        _categoryService = categoryService;
        _currentTenantService = currentTenantService;
    }

    private Guid? GetCurrentTenantId()
    {
        var claim = User.FindFirst("tenantId")?.Value;
        if (!string.IsNullOrEmpty(claim) && Guid.TryParse(claim, out var tid))
        {
            return tid;
        }
        return _currentTenantService?.TenantId;
    }

    /// <summary>
    /// Lấy danh sách danh mục theo cấu trúc phân cấp cây cha - con (FR-PROD-01).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetCategories(CancellationToken ct)
    {
        var rootCategories = await _categoryService.GetCategoriesAsync(ct);
        return Ok(rootCategories);
    }

    /// <summary>
    /// Tạo mới Danh mục sản phẩm (FR-PROD-01).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryRequest request, CancellationToken ct)
    {
        var validator = new CreateCategoryValidator();
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        try
        {
            var response = await _categoryService.CreateCategoryAsync(request, ct);
            return StatusCode(201, response);
        }
        catch (OismException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Cập nhật Danh mục sản phẩm - Chặn tạo vòng lặp cha - con và kiểm tra cha tồn tại (FR-PROD-01).
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> UpdateCategory(Guid id, [FromBody] UpdateCategoryRequest request, CancellationToken ct)
    {
        var validator = new UpdateCategoryValidator();
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        try
        {
            var tenantId = GetCurrentTenantId();
            var response = await _categoryService.UpdateCategoryAsync(id, request, tenantId, ct);
            if (response == null)
            {
                return NotFound(new { message = "Không tìm thấy danh mục." });
            }

            return Ok(response);
        }
        catch (OismException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Xóa/Vô hiệu hóa danh mục sản phẩm (FR-PROD-01).
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken ct)
    {
        var success = await _categoryService.DeleteCategoryAsync(id, ct);
        if (!success)
        {
            return NotFound(new { message = "Không tìm thấy danh mục." });
        }

        return Ok(new { message = "Đã vô hiệu hóa danh mục thành công." });
    }
}
