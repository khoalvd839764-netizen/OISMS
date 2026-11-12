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

    public async Task LockSkuStockAsync(Guid tenantId, Guid branchId, IEnumerable<Guid> skuIds, CancellationToken ct = default)
    {
        foreach (var skuId in skuIds.Distinct().Order())
        {
            // Match the advisory key used by the stock_lock trigger.
            await ExecutePostgresCommandAsync(
                "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/' || {1}::text || '/' || {2}::text, 0));",
                ct, tenantId.ToString(), branchId.ToString(), skuId.ToString());
        }
    }

    public Task LockRequestKeyAsync(Guid tenantId, Guid requestKey, CancellationToken ct = default) =>
        ExecutePostgresCommandAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/request_key/' || {1}::text, 0));",
            ct, tenantId.ToString(), requestKey.ToString());

    public Task LockSupplierInvoiceAsync(Guid tenantId, Guid invoiceId, CancellationToken ct = default) =>
        ExecutePostgresCommandAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/supplier_invoice/' || {1}::text, 0));",
            ct, tenantId.ToString(), invoiceId.ToString());

    public Task LockGoodsReceiptAsync(Guid tenantId, Guid receiptId, CancellationToken ct = default) =>
        ExecutePostgresCommandAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/goods_receipt/' || {1}::text, 0));",
            ct, tenantId.ToString(), receiptId.ToString());

    public Task LockStockTransferAsync(Guid tenantId, Guid transferId, CancellationToken ct = default) =>
        ExecutePostgresCommandAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/transfer/' || {1}::text, 0));",
            ct, tenantId.ToString(), transferId.ToString());

    public Task LockStockTransferNumberAsync(Guid tenantId, CancellationToken ct = default) =>
        ExecutePostgresCommandAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/stock_transfer_number', 0));",
            ct, tenantId.ToString());

    public Task LockStockCountScheduleAsync(Guid tenantId, Guid warehouseId, string periodType, CancellationToken ct = default) =>
        ExecutePostgresCommandAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/stock_count_schedule/' || {1}::text || '/' || {2}::text, 0));",
            ct, tenantId.ToString(), warehouseId.ToString(), periodType);

    public Task LockStockCountPeriodGenerationAsync(Guid tenantId, CancellationToken ct = default) =>
        ExecutePostgresCommandAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/stock_count_period_generate', 0));",
            ct, tenantId.ToString());

    public Task LockStockCountPeriodAsync(Guid tenantId, Guid periodId, CancellationToken ct = default) =>
        ExecutePostgresCommandAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/stock_count_period/' || {1}::text, 0));",
            ct, tenantId.ToString(), periodId.ToString());

    public Task LockStockCountNumberAsync(Guid tenantId, CancellationToken ct = default) =>
        ExecutePostgresCommandAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/stock_count_number', 0));",
            ct, tenantId.ToString());

    public Task LockStockCountAsync(Guid tenantId, Guid stockCountId, CancellationToken ct = default) =>
        ExecutePostgresCommandAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/stock_count/' || {1}::text, 0));",
            ct, tenantId.ToString(), stockCountId.ToString());

    public Task LockInventoryAdjustmentNumberAsync(Guid tenantId, CancellationToken ct = default) =>
        ExecutePostgresCommandAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/inventory_adjustment_number', 0));",
            ct, tenantId.ToString());

    public Task LockExternalOrderAsync(Guid tenantId, string channel, string shopId, string externalOrderId, CancellationToken ct = default) =>
        ExecutePostgresCommandAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/external_order/' || {1}::text || '/' || {2}::text || '/' || {3}::text, 0));",
            ct, tenantId.ToString(), channel, shopId, externalOrderId);

    public Task LockOrderAsync(Guid tenantId, Guid orderId, CancellationToken ct = default) =>
        ExecutePostgresCommandAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/order/' || {1}::text, 0));",
            ct, tenantId.ToString(), orderId.ToString());

    public Task LockOrderRowAsync(Guid tenantId, Guid orderId, CancellationToken ct = default) =>
        ExecutePostgresCommandAsync(
            "SELECT id FROM orders WHERE tenant_id = {0} AND id = {1} FOR UPDATE;",
            ct, tenantId, orderId);

    public Task LockOrderReservationRowsAsync(Guid tenantId, Guid orderId, CancellationToken ct = default) =>
        ExecutePostgresCommandAsync(
            "SELECT id FROM reservations WHERE tenant_id = {0} AND order_id = {1} FOR UPDATE;",
            ct, tenantId, orderId);

    public async Task LockBranchInventoryRowsAsync(Guid tenantId, Guid branchId, IEnumerable<Guid> skuIds, CancellationToken ct = default)
    {
        foreach (var skuId in skuIds.Distinct().Order())
        {
            await ExecutePostgresCommandAsync(
                "SELECT id FROM inventory_balances WHERE tenant_id = {0} AND branch_id = {1} AND sku_id = {2} FOR UPDATE;",
                ct, tenantId, branchId, skuId);
        }
    }

    public async Task LockWarehouseInventoryRowsAsync(
        Guid tenantId, Guid branchId, Guid warehouseId, IEnumerable<Guid> skuIds, CancellationToken ct = default)
    {
        foreach (var skuId in skuIds.Distinct().Order())
        {
            await ExecutePostgresCommandAsync(
                "SELECT id FROM warehouse_inventory_balances WHERE tenant_id = {0} AND branch_id = {1} AND warehouse_id = {2} AND sku_id = {3} FOR UPDATE;",
                ct, tenantId, branchId, warehouseId, skuId);
        }
    }

    public Task SetConstraintsImmediateAsync(CancellationToken ct = default) =>
        ExecutePostgresCommandAsync("SET CONSTRAINTS ALL IMMEDIATE;", ct);

    private Task ExecutePostgresCommandAsync(string sql, CancellationToken ct, params object[] parameters) =>
        _context.Database.IsNpgsql()
            ? _context.Database.ExecuteSqlRawAsync(sql, parameters, ct)
            : Task.CompletedTask;

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

    public async Task LockPaymentAccountsAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (_context.Database.IsNpgsql())
        {
            var tenantStr = tenantId.ToString();
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({tenantStr} || '/payment_accounts', 0));",
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

    public Task LockSalesReturnAsync(Guid tenantId, Guid returnId, CancellationToken ct = default) =>
        ExecutePostgresCommandAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/sales_return/' || {1}::text, 0));",
            ct, tenantId.ToString(), returnId.ToString());

    public Task LockSalesReturnNumberAsync(Guid tenantId, CancellationToken ct = default) =>
        ExecutePostgresCommandAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/sales_return_number', 0));",
            ct, tenantId.ToString());
}
