using OISM.Application.Features.Catalog.Categories;

namespace OISM.Application.Features.Catalog.Categories.Services;

/// <summary>
/// Giao diện xử lý nghiệp vụ Danh mục sản phẩm (FR-PROD-01).
/// </summary>
public interface ICategoryService
{
    Task<List<CategoryResponse>> GetCategoriesAsync(CancellationToken ct = default);
    Task<CategoryResponse> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct = default);
    Task<CategoryResponse?> UpdateCategoryAsync(Guid id, UpdateCategoryRequest request, Guid? tenantIdHint, CancellationToken ct = default);
    Task<bool> DeleteCategoryAsync(Guid id, CancellationToken ct = default);
}
