using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Infrastructure.Persistence;

namespace OISM.Infrastructure.Services;

/// <summary>
/// Hiện thực hóa các thao tác khóa bi quan và advisory lock chuyên biệt cho PostgreSQL (Npgsql).
/// Sử dụng điều kiện kiểm tra provider chính xác: Database.IsNpgsql().
/// </summary>
public class SqlLockService : ISqlLockService
{
    private readonly AppDbContext _context;

    public SqlLockService(AppDbContext context)
    {
        _context = context;
    }

    public async Task LockCategoryTreeAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (_context.Database.IsNpgsql())
        {
            var tenantStr = tenantId.ToString();
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({tenantStr} || '/category_tree', 0));",
                ct);
        }
    }

    public async Task LockProductAsync(Guid productId, CancellationToken ct = default)
    {
        if (_context.Database.IsNpgsql())
        {
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM products WHERE id = {productId} FOR UPDATE",
                ct);
        }
    }

    public async Task LockRefreshTokenAsync(string tokenHash, CancellationToken ct = default)
    {
        if (_context.Database.IsNpgsql())
        {
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM refresh_tokens WHERE token_hash = {tokenHash} FOR UPDATE",
                ct);
        }
    }

    public async Task LockWarehouseAsync(Guid warehouseId, Guid tenantId, CancellationToken ct = default)
    {
        if (_context.Database.IsNpgsql())
        {
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM warehouses WHERE id = {warehouseId} AND tenant_id = {tenantId} FOR UPDATE",
                ct);
        }
    }

    public async Task LockUnitCatalogAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (_context.Database.IsNpgsql())
        {
            var tenantStr = tenantId.ToString();
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({tenantStr} || '/unit_catalog', 0));",
                ct);
        }
    }

    public async Task LockPriceChangeRequestAsync(Guid tenantId, Guid requestId, CancellationToken ct = default)
    {
        if (_context.Database.IsNpgsql())
        {
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM price_change_requests WHERE id = {requestId} AND tenant_id = {tenantId} FOR UPDATE",
                ct);
        }
    }

    public async Task LockSkuUnitPriceAsync(Guid tenantId, Guid priceId, CancellationToken ct = default)
    {
        if (_context.Database.IsNpgsql())
        {
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM sku_unit_prices WHERE id = {priceId} AND tenant_id = {tenantId} FOR UPDATE",
                ct);
        }
    }

    public async Task LockPurchaseOrderAsync(Guid tenantId, Guid purchaseOrderId, CancellationToken ct = default)
    {
        if (_context.Database.IsNpgsql())
        {
            var tenantStr = tenantId.ToString();
            var poStr = purchaseOrderId.ToString();
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({tenantStr} || '/purchase_order/' || {poStr}, 0));",
                ct);
        }
    }
}
