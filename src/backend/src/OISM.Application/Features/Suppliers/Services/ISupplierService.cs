using OISM.Application.Features.Suppliers;

namespace OISM.Application.Features.Suppliers.Services;

/// <summary>
/// Giao diện xử lý nghiệp vụ Nhà cung cấp (FR-INV-02).
/// </summary>
public interface ISupplierService
{
    Task<List<SupplierResponse>> GetSuppliersAsync(string? search, CancellationToken ct = default);
    Task<SupplierResponse?> GetSupplierByIdAsync(Guid id, CancellationToken ct = default);
    Task<SupplierResponse> CreateSupplierAsync(CreateSupplierRequest request, CancellationToken ct = default);
}
