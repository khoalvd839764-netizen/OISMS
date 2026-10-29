using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Catalog.Categories;
using OISM.Domain.Entities;

namespace OISM.Application.Features.Catalog.Categories.Services;

/// <summary>
/// Hiện thực hóa nghiệp vụ Danh mục sản phẩm (FR-PROD-01).
/// </summary>
public class CategoryService : ICategoryService
{
    private readonly IAppDbContext _context;
    private readonly ISqlLockService _lockService;

    public CategoryService(IAppDbContext context, ISqlLockService lockService)
    {
        _context = context;
        _lockService = lockService;
    }

    public async Task<List<CategoryResponse>> GetCategoriesAsync(CancellationToken ct = default)
    {
        var allCategories = await _context.Categories
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        return allCategories
            .Where(c => c.ParentId == null)
            .Select(c => MapToResponse(c, allCategories))
            .ToList();
    }

    public async Task<CategoryResponse> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct = default)
    {
        if (request.ParentId.HasValue)
        {
            var parent = await _context.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.ParentId.Value, ct);
            if (parent == null)
            {
                throw new OismException("VALIDATION_ERROR", "Danh mục cha được chọn không tồn tại hoặc không thuộc cửa hàng của bạn.", 400);
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
        await _context.SaveChangesAsync(ct);

        return new CategoryResponse
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description,
            ParentId = category.ParentId,
            IsActive = category.IsActive
        };
    }

    public async Task<CategoryResponse?> UpdateCategoryAsync(Guid id, UpdateCategoryRequest request, Guid? tenantIdHint, CancellationToken ct = default)
    {
        if (request.ParentId.HasValue && request.ParentId.Value == id)
        {
            throw new OismException("VALIDATION_ERROR", "Không thể đặt danh mục làm cha của chính nó (vòng lặp quan hệ).", 400);
        }

        var tenantId = tenantIdHint;
        if (!tenantId.HasValue)
        {
            var catStub = await _context.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
            if (catStub == null)
            {
                return null;
            }
            tenantId = catStub.TenantId;
        }

        var strategy = _context.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<CategoryResponse?>(async () =>
        {
            await using var tx = await _context.BeginTransactionAsync(ct);
            try
            {
                await _lockService.LockCategoryTreeAsync(tenantId.Value, ct);

                _context.ClearChangeTracker();

                var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
                if (category == null)
                {
                    await tx.RollbackAsync(ct);
                    return null;
                }

                if (request.ParentId.HasValue)
                {
                    if (request.ParentId.Value == id)
                    {
                        throw new OismException("VALIDATION_ERROR", "Không thể đặt danh mục làm cha của chính nó (vòng lặp quan hệ).", 400);
                    }

                    var parent = await _context.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.ParentId.Value, ct);
                    if (parent == null)
                    {
                        throw new OismException("VALIDATION_ERROR", "Danh mục cha được chọn không tồn tại hoặc không thuộc cửa hàng của bạn.", 400);
                    }

                    var visited = new HashSet<Guid> { id };
                    var currentParentId = request.ParentId;
                    while (currentParentId.HasValue)
                    {
                        if (visited.Contains(currentParentId.Value))
                        {
                            throw new OismException("VALIDATION_ERROR", "Không thể đặt danh mục con/cháu làm danh mục cha (phát hiện vòng lặp đệ quy).", 400);
                        }
                        visited.Add(currentParentId.Value);
                        var curr = await _context.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == currentParentId.Value, ct);
                        currentParentId = curr?.ParentId;
                    }
                }

                category.Name = request.Name.Trim();
                category.Description = request.Description?.Trim();
                category.ParentId = request.ParentId;
                category.IsActive = request.IsActive;

                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return new CategoryResponse
                {
                    Id = category.Id,
                    Name = category.Name,
                    Description = category.Description,
                    ParentId = category.ParentId,
                    IsActive = category.IsActive
                };
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        });
    }

    public async Task<bool> DeleteCategoryAsync(Guid id, CancellationToken ct = default)
    {
        var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category == null)
        {
            return false;
        }

        category.IsActive = false;
        await _context.SaveChangesAsync(ct);
        return true;
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
