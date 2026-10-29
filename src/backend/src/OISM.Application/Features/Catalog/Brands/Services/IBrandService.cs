using OISM.Application.Features.Catalog.Brands;

namespace OISM.Application.Features.Catalog.Brands.Services;

/// <summary>
/// Giao diện xử lý nghiệp vụ Thương hiệu sản phẩm (FR-PROD-01).
/// </summary>
public interface IBrandService
{
    Task<List<BrandResponse>> GetBrandsAsync(CancellationToken ct = default);
    Task<BrandResponse> CreateBrandAsync(CreateBrandRequest request, CancellationToken ct = default);
    Task<BrandResponse?> UpdateBrandAsync(Guid id, UpdateBrandRequest request, CancellationToken ct = default);
    Task<bool> DeleteBrandAsync(Guid id, CancellationToken ct = default);
}
