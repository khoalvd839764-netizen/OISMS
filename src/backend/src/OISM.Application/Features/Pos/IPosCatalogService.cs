namespace OISM.Application.Features.Pos;

/// <summary>
/// Giao diện xử lý nghiệp vụ tra cứu danh mục tại quầy POS (FR-POS-01, BE2-07).
/// </summary>
public interface IPosCatalogService
{
    Task<List<PosProductSearchResponse>> SearchAsync(string query, Guid? branchId, CancellationToken ct = default);
    Task<PosProductSearchResponse?> GetByBarcodeAsync(string barcode, Guid? branchId, CancellationToken ct = default);
}
