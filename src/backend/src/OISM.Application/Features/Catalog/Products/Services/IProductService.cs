using OISM.Application.Features.Catalog.Products;
using OISM.Application.Features.Pricing.Dtos;

namespace OISM.Application.Features.Catalog.Products.Services;

public record ToggleVariantResult(Guid VariantId, bool IsActive);

/// <summary>
/// Giao diện xử lý nghiệp vụ Quản lý Sản phẩm và Biến thể SKU (FR-PROD-02, FR-PROD-04, BE2-05, BE2-06).
/// </summary>
public interface IProductService
{
    Task<List<ProductResponse>> GetProductsAsync(string? search, Guid? categoryId, Guid? brandId, CancellationToken ct = default);
    Task<ProductResponse?> GetProductByIdAsync(Guid id, CancellationToken ct = default);
    Task<Guid> CreateProductAsync(CreateProductRequest request, PriceActor actor, CancellationToken ct = default);
    Task<ProductVariantResponse> CreateVariantAsync(Guid productId, CreateVariantRequest request, PriceActor actor, CancellationToken ct = default);
    Task<ProductVariantResponse> UpdateVariantAsync(Guid productId, Guid variantId, UpdateVariantRequest request, Guid? userId = null, CancellationToken ct = default);
    Task<bool> DeleteVariantAsync(Guid productId, Guid variantId, CancellationToken ct = default);
    Task<ToggleVariantResult?> ToggleVariantStatusAsync(Guid productId, Guid variantId, CancellationToken ct = default);
    Task<bool> UpdateProductAsync(Guid id, UpdateProductRequest request, CancellationToken ct = default);
    Task<bool> DeleteProductAsync(Guid id, CancellationToken ct = default);
}
