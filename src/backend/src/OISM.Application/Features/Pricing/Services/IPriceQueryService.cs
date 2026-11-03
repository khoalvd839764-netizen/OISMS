using OISM.Application.Features.Pricing.Dtos;

namespace OISM.Application.Features.Pricing.Services;

public interface IPriceQueryService
{
    Task<IReadOnlyList<PriceListDto>> GetPriceListsAsync(CancellationToken ct = default);
    Task<(IReadOnlyList<PriceChangeRequestResponse> Items, int TotalCount)> GetRequestsAsync(string? status, int page, int pageSize, CancellationToken ct = default);
    Task<PriceChangeRequestResponse?> GetRequestByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<EffectivePriceDto>> GetEffectivePriceAsync(Guid skuId, string? priceListCode, DateTimeOffset? at, CancellationToken ct = default);
    Task<IReadOnlyList<PriceHistoryDto>> GetPriceHistoryAsync(Guid skuId, Guid? unitId, string? priceListCode, CancellationToken ct = default);
}
