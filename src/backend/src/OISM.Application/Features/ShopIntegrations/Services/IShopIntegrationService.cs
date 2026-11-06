namespace OISM.Application.Features.ShopIntegrations.Services;

/// <summary>
/// Quản lý kết nối gian hàng sàn TMĐT (shop_integrations) của Tenant hiện tại.
/// </summary>
public interface IShopIntegrationService
{
    Task<List<ShopIntegrationResponse>> GetIntegrationsAsync(CancellationToken ct = default);

    Task<ShopIntegrationResponse?> GetIntegrationByIdAsync(Guid id, CancellationToken ct = default);

    Task<ShopIntegrationSecretResponse> CreateIntegrationAsync(CreateShopIntegrationRequest request, CancellationToken ct = default);

    Task<ShopIntegrationResponse?> UpdateIntegrationAsync(Guid id, UpdateShopIntegrationRequest request, CancellationToken ct = default);

    Task<ShopIntegrationSecretResponse?> RotateSecretAsync(Guid id, CancellationToken ct = default);
}
