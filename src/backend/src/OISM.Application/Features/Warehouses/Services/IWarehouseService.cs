using OISM.Application.Features.Warehouses;

namespace OISM.Application.Features.Warehouses.Services;

public record WarehouseToggleResult(Guid Id, bool IsActive, string Message);

/// <summary>
/// Giao diện xử lý nghiệp vụ Kho hàng (BE2-08, FR-AUTH-04).
/// </summary>
public interface IWarehouseService
{
    Task<List<WarehouseResponse>> GetWarehousesAsync(Guid? branchId, CancellationToken ct = default);
    Task<WarehouseResponse?> GetWarehouseByIdAsync(Guid id, CancellationToken ct = default);
    Task<WarehouseResponse> CreateWarehouseAsync(CreateWarehouseRequest request, CancellationToken ct = default);
    Task<WarehouseResponse?> UpdateWarehouseAsync(Guid id, UpdateWarehouseRequest request, Guid? tenantIdHint, CancellationToken ct = default);
    Task<WarehouseToggleResult?> ToggleStatusAsync(Guid id, Guid? tenantIdHint, CancellationToken ct = default);
}
