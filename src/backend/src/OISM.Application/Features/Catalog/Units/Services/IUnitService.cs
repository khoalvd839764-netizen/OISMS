namespace OISM.Application.Features.Catalog.Units.Services;

public interface IUnitService
{
    Task<List<UnitResponse>> GetUnitsAsync(CancellationToken ct = default);
    Task<UnitResponse?> GetUnitByIdAsync(Guid id, CancellationToken ct = default);
    Task<UnitResponse> CreateUnitAsync(CreateUnitRequest request, CancellationToken ct = default);
    Task<UnitResponse> UpdateUnitAsync(Guid id, UpdateUnitRequest request, CancellationToken ct = default);

    Task<List<SkuUnitResponse>> GetSkuUnitsAsync(Guid productId, Guid variantId, CancellationToken ct = default);
    Task<SkuUnitResponse> CreateSkuUnitAsync(Guid productId, Guid variantId, CreateSkuUnitRequest request, Guid? userId = null, CancellationToken ct = default);
    Task<SkuUnitResponse> UpdateSkuUnitAsync(Guid productId, Guid variantId, Guid skuUnitId, UpdateSkuUnitRequest request, Guid? userId = null, CancellationToken ct = default);
    Task DeleteSkuUnitAsync(Guid productId, Guid variantId, Guid skuUnitId, Guid? userId = null, CancellationToken ct = default);
    Task<List<SkuUnitFactorHistoryResponse>> GetSkuUnitHistoryAsync(Guid productId, Guid variantId, Guid skuUnitId, CancellationToken ct = default);

    Task EnsureTenantDefaultUnitsAsync(Guid tenantId, CancellationToken ct = default);
}
