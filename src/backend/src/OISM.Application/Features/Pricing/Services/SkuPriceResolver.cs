using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Pricing.Dtos;

namespace OISM.Application.Features.Pricing.Services;

public class SkuPriceResolver : ISkuPriceResolver
{
    private readonly IAppDbContext _dbContext;

    public SkuPriceResolver(IAppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyDictionary<SkuUnitPriceKey, ResolvedPrice>> ResolveAsync(
        string priceListCode,
        IReadOnlyCollection<SkuUnitPriceKey> keys,
        DateTimeOffset atUtc,
        CancellationToken ct = default)
    {
        if (keys == null || keys.Count == 0)
        {
            return new Dictionary<SkuUnitPriceKey, ResolvedPrice>();
        }

        var normalizedCode = priceListCode?.Trim().ToLowerInvariant() ?? string.Empty;
        var skuIds = keys.Select(k => k.SkuId).Distinct().ToList();

        // LINQ query thuần túy, tương thích cả InMemory và Npgsql
        var candidatePrices = await _dbContext.SkuUnitPrices
            .AsNoTracking()
            .Where(p => p.PriceList != null &&
                        p.PriceList.Code == normalizedCode &&
                        skuIds.Contains(p.SkuId) &&
                        p.CancelledAt == null &&
                        p.ValidFrom <= atUtc)
            .Select(p => new
            {
                p.Id,
                p.SkuId,
                p.UnitId,
                p.Price,
                p.ValidFrom
            })
            .ToListAsync(ct);

        var keySet = new HashSet<SkuUnitPriceKey>(keys);
        var result = candidatePrices
            .Where(p => keySet.Contains(new SkuUnitPriceKey(p.SkuId, p.UnitId)))
            .GroupBy(p => new SkuUnitPriceKey(p.SkuId, p.UnitId))
            .Select(g => g.OrderByDescending(p => p.ValidFrom).ThenByDescending(p => p.Id).First())
            .ToDictionary(
                p => new SkuUnitPriceKey(p.SkuId, p.UnitId),
                p => new ResolvedPrice(p.Id, p.Price, p.ValidFrom)
            );

        return result;
    }
}
