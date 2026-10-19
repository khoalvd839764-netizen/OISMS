using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
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
    private readonly ICurrentTenantService? _currentTenantService;

    public CategoriesController(AppDbContext context, ICurrentTenantService? currentTenantService = null)
    {
        _context = context;
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

        // Nếu có chọn danh mục cha, kiểm tra danh mục cha có tồn tại và cùng Tenant không
        if (request.ParentId.HasValue)
        {
            var parent = await _context.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.ParentId.Value);
            if (parent == null)
            {
                return BadRequest(new { message = "Danh mục cha được chọn không tồn tại hoặc không thuộc cửa hàng của bạn." });
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
    /// Cập nhật Danh mục sản phẩm - Chặn tạo vòng lặp cha - con và kiểm tra cha tồn tại (FR-PROD-01).
    /// Sử dụng PostgreSQL Advisory Lock theo Tenant để bảo vệ cây danh mục trước race condition đồng thời.
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> UpdateCategory(Guid id, [FromBody] UpdateCategoryRequest request)
    {
        var validator = new UpdateCategoryValidator();
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        if (request.ParentId.HasValue && request.ParentId.Value == id)
        {
            return BadRequest(new { message = "Không thể đặt danh mục làm cha của chính nó (vòng lặp quan hệ)." });
        }

        var tenantId = GetCurrentTenantId();
        if (!tenantId.HasValue)
        {
            var catStub = await _context.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (catStub == null)
            {
                return NotFound(new { message = "Không tìm thấy danh mục." });
            }
            tenantId = catStub.TenantId;
        }

        var strategy = _context.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            try
            {
                // 1. Phải lấy khóa database trước khi đọc/kiểm tra quan hệ và giữ bảo vệ đến commit
                // Khóa phân cấp danh mục theo tenant trên PostgreSQL để chống race condition tạo vòng lặp
                if (_context.Database.IsNpgsql())
                {
                    await _context.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT pg_advisory_xact_lock(hashtextextended({tenantId.Value.ToString()} || '/category_tree', 0))");
                }

                // 2. Xóa sạch ChangeTracker để bảo đảm đọc lại dữ liệu mới nhất từ DB sau khi chờ khóa
                _context.ChangeTracker.Clear();

                // 3. Đọc lại danh mục mục tiêu cần cập nhật
                var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id);
                if (category == null)
                {
                    await tx.RollbackAsync();
                    return NotFound(new { message = "Không tìm thấy danh mục." });
                }

                // 4. Kiểm tra danh mục cha nếu có chọn
                if (request.ParentId.HasValue)
                {
                    if (request.ParentId.Value == id)
                    {
                        await tx.RollbackAsync();
                        return BadRequest(new { message = "Không thể đặt danh mục làm cha của chính nó (vòng lặp quan hệ)." });
                    }

                    // Kiểm tra danh mục cha có tồn tại và thuộc Tenant hiện tại không
                    var parent = await _context.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.ParentId.Value);
                    if (parent == null)
                    {
                        await tx.RollbackAsync();
                        return BadRequest(new { message = "Danh mục cha được chọn không tồn tại hoặc không thuộc cửa hàng của bạn." });
                    }

                    // Chặn tạo vòng lặp quan hệ cha - con nhiều cấp (duyệt tổ tiên)
                    var visited = new HashSet<Guid> { id };
                    var currentParentId = request.ParentId;
                    while (currentParentId.HasValue)
                    {
                        if (visited.Contains(currentParentId.Value))
                        {
                            await tx.RollbackAsync();
                            return BadRequest(new { message = "Không thể đặt danh mục con/cháu làm danh mục cha (phát hiện vòng lặp đệ quy)." });
                        }
                        visited.Add(currentParentId.Value);
                        var curr = await _context.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == currentParentId.Value);
                        currentParentId = curr?.ParentId;
                    }
                }

                category.Name = request.Name.Trim();
                category.Description = request.Description?.Trim();
                category.ParentId = request.ParentId;
                category.IsActive = request.IsActive;

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new CategoryResponse
                {
                    Id = category.Id,
                    Name = category.Name,
                    Description = category.Description,
                    ParentId = category.ParentId,
                    IsActive = category.IsActive
                });
            }
            catch (Exception)
            {
                await tx.RollbackAsync();
                throw;
            }
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

    private static CategoryResponse MapToResponse(Category category, List<Category> allCategories, HashSet<Guid>? visited = null)
    {
        visited ??= new HashSet<Guid>();
        visited.Add(category.Id);

        return new CategoryResponse
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description,
            ParentId = category.ParentId,
            IsActive = category.IsActive,
            SubCategories = allCategories
                .Where(sub => sub.ParentId == category.Id && !visited.Contains(sub.Id))
                .Select(sub => MapToResponse(sub, allCategories, new HashSet<Guid>(visited)))
                .ToList()
        };
    }
}
