using Microsoft.EntityFrameworkCore;
using OISM.Domain.Entities;

namespace OISM.Application.Common.Interfaces;

/// <summary>
/// Giao diện DbContext cho tầng Application tuân thủ Clean Architecture và schema v0.5.
/// </summary>
public interface IAppDbContext
{
    IDbErrorTranslator DatabaseErrors { get; }
    IUnitOfWork UnitOfWork { get; }
    ISqlLockService Locks { get; }
    DbSet<Tenant> Tenants { get; }
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Branch> Branches { get; }
    DbSet<Warehouse> Warehouses { get; }
    DbSet<Category> Categories { get; }
    DbSet<Brand> Brands { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductVariant> ProductVariants { get; }
    DbSet<ProductVariant> Skus => ProductVariants;
    DbSet<Supplier> Suppliers { get; }
    DbSet<InventoryBalance> InventoryBalances { get; }
    DbSet<WarehouseInventoryBalance> WarehouseInventoryBalances { get; }
    DbSet<GoodsReceipt> GoodsReceipts { get; }
    DbSet<GoodsReceiptItem> GoodsReceiptItems { get; }
    DbSet<InventoryTransaction> InventoryTransactions { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<Reservation> Reservations { get; }
    DbSet<Payment> Payments { get; }
    DbSet<PaymentAccount> PaymentAccounts { get; }
    DbSet<SalesReturn> SalesReturns { get; }
    DbSet<SalesReturnItem> SalesReturnItems { get; }
    DbSet<StockTransfer> StockTransfers { get; }
    DbSet<StockTransferItem> StockTransferItems { get; }
    DbSet<StockCount> StockCounts { get; }
    DbSet<StockCountItem> StockCountItems { get; }
    DbSet<InventoryAdjustment> InventoryAdjustments { get; }
    DbSet<InventoryAdjustmentItem> InventoryAdjustmentItems { get; }
    DbSet<ShopIntegration> ShopIntegrations { get; }
    DbSet<Unit> Units { get; }
    DbSet<SkuUnit> SkuUnits { get; }
    DbSet<SkuUnitFactorHistory> SkuUnitFactorHistories { get; }
    DbSet<PriceList> PriceLists { get; }
    DbSet<PriceChangeRequest> PriceChangeRequests { get; }
    DbSet<PriceChangeRequestItem> PriceChangeRequestItems { get; }
    DbSet<SkuUnitPrice> SkuUnitPrices { get; }
    DbSet<PurchaseOrder> PurchaseOrders { get; }
    DbSet<PurchaseOrderItem> PurchaseOrderItems { get; }
    DbSet<SalesInvoice> SalesInvoices { get; }
    DbSet<StockCountSchedule> StockCountSchedules { get; }
    DbSet<StockCountPeriod> StockCountPeriods { get; }
    DbSet<StockCountApproval> StockCountApprovals { get; }

    int SaveChanges();
    int SaveChanges(bool acceptAllChangesOnSuccess);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default);

    IDisposable OpenPrivilegedScope();
    bool IsPrivilegedScopeActive { get; }
}
