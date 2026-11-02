using OISM.Application.Features.PriceLists.Dtos;

namespace OISM.Application.Features.PriceLists.Services;

public interface IPriceListService
{
    Task<List<PriceListDto>> GetPriceListsAsync(CancellationToken ct = default);
    Task<PriceListDto> CreatePriceListAsync(CreatePriceListDto dto, CancellationToken ct = default);
    Task<List<PriceVersionDto>> GetVersionsAsync(Guid priceListId, CancellationToken ct = default);
    Task<PriceVersionDto> CreateVersionAsync(Guid priceListId, CreatePriceVersionDto dto, CancellationToken ct = default);
}
