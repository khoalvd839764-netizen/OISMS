using System.Collections.Concurrent;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.PriceLists.Dtos;

namespace OISM.Application.Features.PriceLists.Services;

public class PriceListService : IPriceListService
{
    private readonly ICurrentTenantService _currentTenantService;

    private static readonly ConcurrentDictionary<Guid, List<PriceListDto>> _tenantPriceLists = new();
    private static readonly ConcurrentDictionary<Guid, List<PriceVersionDto>> _tenantVersions = new();

    public PriceListService(ICurrentTenantService currentTenantService)
    {
        _currentTenantService = currentTenantService;
    }

    private Guid RequireTenantId()
    {
        var tenantId = _currentTenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Yêu cầu xác thực gian hàng hợp lệ.");
        }
        return tenantId.Value;
    }

    public Task<List<PriceListDto>> GetPriceListsAsync(CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();
        var lists = _tenantPriceLists.GetOrAdd(tenantId, _ => new List<PriceListDto>
        {
            new() { Id = Guid.NewGuid(), Code = "PL-RETAIL", Name = "Bảng giá bán lẻ tiêu chuẩn", Channel = "Retail", VersionsCount = 1 },
            new() { Id = Guid.NewGuid(), Code = "PL-WHOLESALE", Name = "Bảng giá bán sỉ / Đại lý", Channel = "Wholesale", VersionsCount = 1 }
        });

        return Task.FromResult(lists.ToList());
    }

    public Task<PriceListDto> CreatePriceListAsync(CreatePriceListDto dto, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();
        var lists = _tenantPriceLists.GetOrAdd(tenantId, _ => new List<PriceListDto>());

        var newList = new PriceListDto
        {
            Id = Guid.NewGuid(),
            Code = dto.Code.Trim().ToUpperInvariant(),
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            Channel = dto.Channel,
            IsActive = true,
            VersionsCount = 0,
            CreatedAt = DateTime.UtcNow
        };

        lock (lists)
        {
            lists.Add(newList);
        }

        return Task.FromResult(newList);
    }

    public Task<List<PriceVersionDto>> GetVersionsAsync(Guid priceListId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();
        var versions = _tenantVersions.GetOrAdd(tenantId, _ => new List<PriceVersionDto>());

        lock (versions)
        {
            return Task.FromResult(versions.Where(v => v.PriceListId == priceListId).ToList());
        }
    }

    public Task<PriceVersionDto> CreateVersionAsync(Guid priceListId, CreatePriceVersionDto dto, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();
        var versions = _tenantVersions.GetOrAdd(tenantId, _ => new List<PriceVersionDto>());

        var newVer = new PriceVersionDto
        {
            Id = Guid.NewGuid(),
            PriceListId = priceListId,
            VersionName = dto.VersionName.Trim(),
            EffectiveFrom = dto.EffectiveFrom,
            EffectiveTo = dto.EffectiveTo,
            Status = "Active",
            Items = dto.Items
        };

        lock (versions)
        {
            versions.Add(newVer);
        }

        // Cập nhật versions count
        var lists = _tenantPriceLists.GetOrAdd(tenantId, _ => new List<PriceListDto>());
        var targetList = lists.FirstOrDefault(l => l.Id == priceListId);
        if (targetList != null)
        {
            targetList.VersionsCount = versions.Count(v => v.PriceListId == priceListId);
        }

        return Task.FromResult(newVer);
    }
}
