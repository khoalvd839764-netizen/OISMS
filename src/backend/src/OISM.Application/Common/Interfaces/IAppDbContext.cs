using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OISM.Domain.Entities;

namespace OISM.Application.Common.Interfaces;

/// <summary>
/// Giao diện DbContext cho tầng Application tuân thủ Clean Architecture và schema v0.5.
/// </summary>
public interface IAppDbContext
{
    bool IsRelationalDatabase { get; }
    Task<int> ExecuteSqlRawAsync(string sql, object[] parameters, CancellationToken cancellationToken = default);
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
    DbSet<StockTransfer> StockTransfers { get; }
    DbSet<StockTransferItem> StockTransferItems { get; }
    DbSet<StockCount> StockCounts { get; }
    DbSet<StockCountItem> StockCountItems { get; }
    DbSet<InventoryAdjustment> InventoryAdjustments { get; }
    DbSet<InventoryAdjustmentItem> InventoryAdjustmentItems { get; }

    int SaveChanges();
    int SaveChanges(bool acceptAllChangesOnSuccess);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
    IExecutionStrategy CreateExecutionStrategy();
    void ClearChangeTracker();

    IDisposable OpenPrivilegedScope();
    bool IsPrivilegedScopeActive { get; }
}
