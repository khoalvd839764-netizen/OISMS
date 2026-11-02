using OISM.Application.Features.Pricing.Dtos;

namespace OISM.Application.Features.Pricing.Services;

public interface ISkuPriceResolver
{
    Task<IReadOnlyDictionary<SkuUnitPriceKey, ResolvedPrice>> ResolveAsync(
        string priceListCode,
        IReadOnlyCollection<SkuUnitPriceKey> keys,
        DateTimeOffset atUtc,
        CancellationToken ct = default);
}
