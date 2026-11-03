namespace OISM.Application.Features.Search.Services;

/// <summary>
/// Giao diện xử lý tìm kiếm toàn hệ thống đa thực thể (CS-04).
/// Hỗ trợ tìm kiếm SKU, Đơn hàng, Phiếu nhập kho theo quyền và Tenant.
/// </summary>
public interface ISearchService
{
    Task<GlobalSearchResponse> SearchAsync(string query, string userRole, int limit = 5, CancellationToken ct = default);
}
