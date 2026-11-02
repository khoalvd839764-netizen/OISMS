using OISM.Application.Features.Pricing.Dtos;

namespace OISM.Application.Features.Pricing.Services;

public interface IPriceChangeService
{
    Task<PriceChangeRequestResponse> CreateAsync(CreatePriceChangeRequest request, Guid requestKey, PriceActor actor, CancellationToken ct = default);
    Task<PriceChangeRequestResponse> ApproveAsync(Guid id, Guid? reviewerId, CancellationToken ct = default);
    Task<PriceChangeRequestResponse> RejectAsync(Guid id, string note, Guid? reviewerId, CancellationToken ct = default);
    Task CancelScheduledPriceAsync(Guid skuUnitPriceId, string reason, Guid? userId, CancellationToken ct = default);
    Task AddInitialPricesAsync(IReadOnlyCollection<InitialPriceLine> lines, PriceActor actor, CancellationToken ct = default);
    Task EnsureTenantDefaultPriceListsAsync(Guid tenantId, CancellationToken ct = default);
}
