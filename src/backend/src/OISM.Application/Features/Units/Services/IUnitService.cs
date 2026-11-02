using OISM.Application.Features.Units.Dtos;

namespace OISM.Application.Features.Units.Services;

public interface IUnitService
{
    Task<List<UnitDto>> GetUnitsAsync(CancellationToken ct = default);
    Task<UnitDto> CreateUnitAsync(CreateUnitDto dto, CancellationToken ct = default);
    Task<List<SkuUnitDto>> GetSkuUnitsAsync(Guid skuId, CancellationToken ct = default);
    Task<SkuUnitDto> AddSkuUnitAsync(Guid skuId, CreateSkuUnitDto dto, CancellationToken ct = default);
}
