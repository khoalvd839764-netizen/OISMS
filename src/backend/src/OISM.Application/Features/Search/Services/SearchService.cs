using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;

namespace OISM.Application.Features.Search.Services;

/// <summary>
/// Hiện thực hóa dịch vụ tìm kiếm toàn hệ thống đa thực thể (CS-04).
/// Đảm bảo tính cô lập Đa người thuê (NFR-TENANT-01) và kiểm soát phân quyền RBAC.
/// </summary>
public class SearchService : ISearchService
{
    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _currentTenantService;

    public SearchService(IAppDbContext context, ICurrentTenantService currentTenantService)
    {
        _context = context;
        _currentTenantService = currentTenantService;
    }

    public async Task<GlobalSearchResponse> SearchAsync(
        string query,
        string userRole,
        int limit = 5,
        CancellationToken ct = default)
    {
        var response = new GlobalSearchResponse
        {
            Query = query?.Trim() ?? string.Empty
        };

        if (string.IsNullOrWhiteSpace(query))
        {
            return response;
        }

        var normalizedLimit = Math.Clamp(limit, 1, 20);
        var term = query.Trim().ToLowerInvariant();

        // 1. Tìm kiếm Sản phẩm & Biến thể SKU
        var skus = await _context.Skus
            .Include(s => s.Product)
            .AsNoTracking()
            .Where(s =>
                s.SKU.ToLower().Contains(term) ||
                (s.Barcode != null && s.Barcode.ToLower().Contains(term)) ||
                (s.Product != null && s.Product.Name.ToLower().Contains(term)))
            .OrderBy(s => s.SKU)
            .Take(normalizedLimit)
            .Select(s => new SearchResultItemDto
            {
                Id = s.Id,
                Type = "Product",
                Title = s.Product != null ? s.Product.Name : s.SKU,
                Subtitle = $"Mã: {s.SKU} · Giá: {s.RetailPrice:N0} ₫",
                Status = s.Product != null && s.Product.IsActive ? "Active" : "Inactive",
                Url = "/products"
            })
            .ToListAsync(ct);

        response.Products = skus;

        // 2. Tìm kiếm Đơn hàng (Orders)
        var orders = await _context.Orders
            .AsNoTracking()
            .Where(o =>
                o.Number.ToLower().Contains(term) ||
                (o.CustomerName != null && o.CustomerName.ToLower().Contains(term)) ||
                (o.CustomerPhone != null && o.CustomerPhone.Contains(term)))
            .OrderByDescending(o => o.CreatedAt)
            .Take(normalizedLimit)
            .Select(o => new SearchResultItemDto
            {
                Id = o.Id,
                Type = "Order",
                Title = $"Đơn hàng {o.Number}",
                Subtitle = $"{o.CustomerName ?? "Khách lẻ"} · {o.TotalAmount:N0} ₫",
                Status = o.Status,
                Url = "/orders"
            })
            .ToListAsync(ct);

        response.Orders = orders;

        // 3. Tìm kiếm Phiếu nhập kho (Goods Receipts)
        // RBAC: Vai trò Thu ngân (Cashier) không có quyền xem thông tin nhập kho và giá vốn nhà cung cấp
        if (!string.Equals(userRole, "Cashier", StringComparison.OrdinalIgnoreCase))
        {
            var receipts = await _context.GoodsReceipts
                .Include(g => g.Supplier)
                .AsNoTracking()
                .Where(g =>
                    g.Number.ToLower().Contains(term) ||
                    (g.Supplier != null && g.Supplier.Name.ToLower().Contains(term)))
                .OrderByDescending(g => g.CreatedAt)
                .Take(normalizedLimit)
                .Select(g => new SearchResultItemDto
                {
                    Id = g.Id,
                    Type = "GoodsReceipt",
                    Title = $"Phiếu nhập {g.Number}",
                    Subtitle = $"NCC: {(g.Supplier != null ? g.Supplier.Name : "Nội bộ")} · Trạng thái: {g.Status}",
                    Status = g.Status,
                    Url = "/inventory"
                })
                .ToListAsync(ct);

            response.GoodsReceipts = receipts;
        }

        return response;
    }
}
