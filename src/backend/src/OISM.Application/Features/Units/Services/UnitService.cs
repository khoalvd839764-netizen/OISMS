using System.Collections.Concurrent;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Units.Dtos;

namespace OISM.Application.Features.Units.Services;

public class UnitService : IUnitService
{
    private readonly ICurrentTenantService _currentTenantService;

    // Bộ nhớ lưu trữ theo TenantId bảo đảm cô lập đa gian hàng
    private static readonly ConcurrentDictionary<Guid, List<UnitDto>> _tenantUnits = new();
    private static readonly ConcurrentDictionary<Guid, List<SkuUnitDto>> _tenantSkuUnits = new();

    public UnitService(ICurrentTenantService currentTenantService)
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

    public Task<List<UnitDto>> GetUnitsAsync(CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();
        var units = _tenantUnits.GetOrAdd(tenantId, _ => new List<UnitDto>
        {
            new() { Id = Guid.NewGuid(), Name = "Cái", Description = "Đơn vị đếm cơ bản", IsActive = true },
            new() { Id = Guid.NewGuid(), Name = "Hộp", Description = "Đơn vị đóng hộp", IsActive = true },
            new() { Id = Guid.NewGuid(), Name = "Thùng", Description = "Đơn vị đóng thùng carton", IsActive = true }
        });

        return Task.FromResult(units.ToList());
    }

    public Task<UnitDto> CreateUnitAsync(CreateUnitDto dto, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();
        var list = _tenantUnits.GetOrAdd(tenantId, _ => new List<UnitDto>());

        var newUnit = new UnitDto
        {
            Id = Guid.NewGuid(),
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        lock (list)
        {
            list.Add(newUnit);
        }

        return Task.FromResult(newUnit);
    }

    public Task<List<SkuUnitDto>> GetSkuUnitsAsync(Guid skuId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();
        var list = _tenantSkuUnits.GetOrAdd(tenantId, _ => new List<SkuUnitDto>());

        lock (list)
        {
            return Task.FromResult(list.Where(u => u.SkuId == skuId).ToList());
        }
    }

    public Task<SkuUnitDto> AddSkuUnitAsync(Guid skuId, CreateSkuUnitDto dto, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();
        var unitList = _tenantUnits.GetOrAdd(tenantId, _ => new List<UnitDto>());
        var unit = unitList.FirstOrDefault(u => u.Id == dto.UnitId);

        var skuUnitList = _tenantSkuUnits.GetOrAdd(tenantId, _ => new List<SkuUnitDto>());

        var newSkuUnit = new SkuUnitDto
        {
            Id = Guid.NewGuid(),
            SkuId = skuId,
            UnitId = dto.UnitId,
            UnitName = unit?.Name ?? "Đơn vị",
            ConversionRate = dto.ConversionRate,
            Barcode = dto.Barcode,
            IsBaseUnit = dto.IsBaseUnit,
            CostPrice = dto.CostPrice,
            SellingPrice = dto.SellingPrice
        };

        lock (skuUnitList)
        {
            skuUnitList.Add(newSkuUnit);
        }

        return Task.FromResult(newSkuUnit);
    }
}
