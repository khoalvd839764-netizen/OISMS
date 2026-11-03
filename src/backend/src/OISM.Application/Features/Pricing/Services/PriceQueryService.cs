using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Pricing.Dtos;

namespace OISM.Application.Features.Pricing.Services;

public class PriceQueryService : IPriceQueryService
{
    private readonly IAppDbContext _dbContext;

    public PriceQueryService(IAppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<PriceListDto>> GetPriceListsAsync(CancellationToken ct = default)
    {
        return await _dbContext.PriceLists
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Code)
            .Select(p => new PriceListDto
            {
                Id = p.Id,
                Code = p.Code,
                Name = p.Name,
                IsActive = p.IsActive
            })
            .ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<PriceChangeRequestResponse> Items, int TotalCount)> GetRequestsAsync(
        string? status,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _dbContext.PriceChangeRequests.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var st = status.Trim();
            query = query.Where(r => r.Status == st);
        }

        var totalCount = await query.CountAsync(ct);

        var list = await query
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(r => r.Items)
                .ThenInclude(i => i.PriceList)
            .Include(r => r.Items)
                .ThenInclude(i => i.Sku)
            .Include(r => r.Items)
                .ThenInclude(i => i.Unit)
            .ToListAsync(ct);

        var dtos = list.Select(MapToResponse).ToList();
        return (dtos, totalCount);
    }

    public async Task<PriceChangeRequestResponse?> GetRequestByIdAsync(Guid id, CancellationToken ct = default)
    {
        var request = await _dbContext.PriceChangeRequests
            .AsNoTracking()
            .Include(r => r.Items)
                .ThenInclude(i => i.PriceList)
            .Include(r => r.Items)
                .ThenInclude(i => i.Sku)
            .Include(r => r.Items)
                .ThenInclude(i => i.Unit)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        return request == null ? null : MapToResponse(request);
    }

    public async Task<IReadOnlyList<EffectivePriceDto>> GetEffectivePriceAsync(
        Guid skuId,
        string? priceListCode,
        DateTimeOffset? at,
        CancellationToken ct = default)
    {
        var code = string.IsNullOrWhiteSpace(priceListCode) ? "retail" : priceListCode.Trim().ToLowerInvariant();
        var effectiveAt = at ?? DateTimeOffset.UtcNow;

        var prices = await _dbContext.SkuUnitPrices
            .AsNoTracking()
            .Include(p => p.PriceList)
            .Where(p => p.SkuId == skuId &&
                        p.PriceList != null &&
                        p.PriceList.Code == code &&
                        p.CancelledAt == null &&
                        p.ValidFrom <= effectiveAt)
            .ToListAsync(ct);

        var latestPrices = prices
            .GroupBy(p => p.UnitId)
            .Select(g => g.OrderByDescending(p => p.ValidFrom).ThenByDescending(p => p.Id).First())
            .Select(p => new EffectivePriceDto
            {
                SkuUnitPriceId = p.Id,
                PriceListId = p.PriceListId,
                PriceListCode = p.PriceList?.Code ?? code,
                SkuId = p.SkuId,
                UnitId = p.UnitId,
                Price = p.Price,
                ValidFrom = p.ValidFrom
            })
            .ToList();

        return latestPrices;
    }

    public async Task<IReadOnlyList<PriceHistoryDto>> GetPriceHistoryAsync(
        Guid skuId,
        Guid? unitId,
        string? priceListCode,
        CancellationToken ct = default)
    {
        var query = _dbContext.SkuUnitPrices
            .AsNoTracking()
            .Include(p => p.PriceList)
            .Where(p => p.SkuId == skuId);

        if (unitId.HasValue && unitId.Value != Guid.Empty)
        {
            query = query.Where(p => p.UnitId == unitId.Value);
        }

        if (!string.IsNullOrWhiteSpace(priceListCode))
        {
            var code = priceListCode.Trim().ToLowerInvariant();
            query = query.Where(p => p.PriceList != null && p.PriceList.Code == code);
        }

        var allRows = await query.ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;

        var result = new List<PriceHistoryDto>();

        // Nhóm theo từng cặp (UnitId, PriceListId) để suy ra ValidTo
        var groups = allRows.GroupBy(p => (p.UnitId, p.PriceListId));

        foreach (var group in groups)
        {
            // Sắp xếp theo ValidFrom tăng dần
            var ordered = group.OrderBy(p => p.ValidFrom).ThenBy(p => p.CreatedAt).ToList();

            for (var i = 0; i < ordered.Count; i++)
            {
                var cur = ordered[i];

                // Tìm dòng kế tiếp có hiệu lực (chưa bị hủy)
                DateTimeOffset? validTo = null;
                for (var j = i + 1; j < ordered.Count; j++)
                {
                    if (ordered[j].CancelledAt == null && ordered[j].ValidFrom > cur.ValidFrom)
                    {
                        validTo = ordered[j].ValidFrom;
                        break;
                    }
                }

                var isActive = cur.CancelledAt == null &&
                               cur.ValidFrom <= now &&
                               (!validTo.HasValue || validTo.Value > now);

                result.Add(new PriceHistoryDto
                {
                    Id = cur.Id,
                    PriceListCode = cur.PriceList?.Code ?? string.Empty,
                    SkuId = cur.SkuId,
                    UnitId = cur.UnitId,
                    Price = cur.Price,
                    ValidFrom = cur.ValidFrom,
                    ValidTo = validTo,
                    IsActive = isActive,
                    CancelledAt = cur.CancelledAt,
                    CancelReason = cur.CancelReason
                });
            }
        }

        return result.OrderByDescending(p => p.ValidFrom).ThenByDescending(p => p.Id).ToList();
    }

    private static PriceChangeRequestResponse MapToResponse(Domain.Entities.PriceChangeRequest r)
    {
        return new PriceChangeRequestResponse
        {
            Id = r.Id,
            RequestKey = r.RequestKey,
            Kind = r.Kind,
            Status = r.Status,
            Reason = r.Reason,
            RequestedValidFrom = r.RequestedValidFrom,
            CreatedBy = r.CreatedBy,
            CreatedAt = r.CreatedAt,
            ReviewedBy = r.ReviewedBy,
            ReviewedAt = r.ReviewedAt,
            ReviewNote = r.ReviewNote,
            IsReplay = false,
            Items = r.Items.Select(i => new PriceChangeRequestItemDto
            {
                Id = i.Id,
                PriceListId = i.PriceListId,
                PriceListCode = i.PriceList?.Code ?? string.Empty,
                SkuId = i.SkuId,
                SkuCode = i.Sku?.Code ?? string.Empty,
                UnitId = i.UnitId,
                UnitName = i.Unit?.Name ?? string.Empty,
                NewPrice = i.NewPrice
            }).ToList()
        };
    }
}
