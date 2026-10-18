using System.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OISM.Application.Common.Interfaces;
using OISM.Domain.Common;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence;

/// <summary>
/// DbContext chính của hệ thống OISMS.
/// Quản lý cô lập dữ liệu Multi-tenant tự động thông qua Global Query Filter,
/// bảo vệ ghi chặt chẽ (Tenant Guard), và tự động gán Audit info.
/// </summary>
public class AppDbContext : DbContext, IAppDbContext
{
    private readonly ICurrentTenantService _currentTenantService;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ICurrentTenantService currentTenantService) : base(options)
    {
        _currentTenantService = currentTenantService;
    }

    public DbSet<Tenant> Tenants { get; set; } = null!;
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
    public DbSet<Branch> Branches { get; set; } = null!;
    public DbSet<Warehouse> Warehouses { get; set; } = null!;
    public DbSet<Category> Categories { get; set; } = null!;
    public DbSet<Brand> Brands { get; set; } = null!;
    public DbSet<Product> Products { get; set; } = null!;
    public DbSet<ProductVariant> ProductVariants { get; set; } = null!;
    public DbSet<ProductVariant> Skus => ProductVariants;
    public DbSet<Supplier> Suppliers { get; set; } = null!;
    public DbSet<InventoryBalance> InventoryBalances { get; set; } = null!;
    public DbSet<WarehouseInventoryBalance> WarehouseInventoryBalances { get; set; } = null!;
    public DbSet<GoodsReceipt> GoodsReceipts { get; set; } = null!;
    public DbSet<GoodsReceiptItem> GoodsReceiptItems { get; set; } = null!;
    public DbSet<InventoryTransaction> InventoryTransactions { get; set; } = null!;

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return Database.BeginTransactionAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Áp dụng tất cả IEntityTypeConfiguration trong assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // =====================================================================
        // GLOBAL QUERY FILTER (NFR-TENANT-01)
        // Khi không có Tenant xác thực, biểu thức trả về FALSE để ngăn rò rỉ dữ liệu chéo.
        // Luồng login / register / refresh chưa có JWT phải dùng .IgnoreQueryFilters().
        // =====================================================================
        modelBuilder.Entity<User>()
            .HasQueryFilter(e => _currentTenantService.TenantId != null && e.TenantId == (_currentTenantService.TenantId ?? Guid.Empty));

        modelBuilder.Entity<RefreshToken>()
            .HasQueryFilter(e => _currentTenantService.TenantId != null && e.TenantId == (_currentTenantService.TenantId ?? Guid.Empty));

        modelBuilder.Entity<Branch>()
            .HasQueryFilter(e => _currentTenantService.TenantId != null && e.TenantId == (_currentTenantService.TenantId ?? Guid.Empty));

        modelBuilder.Entity<Warehouse>()
            .HasQueryFilter(e => _currentTenantService.TenantId != null && e.TenantId == (_currentTenantService.TenantId ?? Guid.Empty));

        modelBuilder.Entity<Category>()
            .HasQueryFilter(e => _currentTenantService.TenantId != null && e.TenantId == (_currentTenantService.TenantId ?? Guid.Empty));

        modelBuilder.Entity<Brand>()
            .HasQueryFilter(e => _currentTenantService.TenantId != null && e.TenantId == (_currentTenantService.TenantId ?? Guid.Empty));

        modelBuilder.Entity<Product>()
            .HasQueryFilter(e => _currentTenantService.TenantId != null && e.TenantId == (_currentTenantService.TenantId ?? Guid.Empty));

        modelBuilder.Entity<ProductVariant>()
            .HasQueryFilter(e => _currentTenantService.TenantId != null && e.TenantId == (_currentTenantService.TenantId ?? Guid.Empty));

        modelBuilder.Entity<Supplier>()
            .HasQueryFilter(e => _currentTenantService.TenantId != null && e.TenantId == (_currentTenantService.TenantId ?? Guid.Empty));

        modelBuilder.Entity<InventoryBalance>()
            .HasQueryFilter(e => _currentTenantService.TenantId != null && e.TenantId == (_currentTenantService.TenantId ?? Guid.Empty));

        modelBuilder.Entity<WarehouseInventoryBalance>()
            .HasQueryFilter(e => _currentTenantService.TenantId != null && e.TenantId == (_currentTenantService.TenantId ?? Guid.Empty));

        modelBuilder.Entity<GoodsReceipt>()
            .HasQueryFilter(e => _currentTenantService.TenantId != null && e.TenantId == (_currentTenantService.TenantId ?? Guid.Empty));

        modelBuilder.Entity<GoodsReceiptItem>()
            .HasQueryFilter(e => _currentTenantService.TenantId != null && e.TenantId == (_currentTenantService.TenantId ?? Guid.Empty));

        modelBuilder.Entity<InventoryTransaction>()
            .HasQueryFilter(e => _currentTenantService.TenantId != null && e.TenantId == (_currentTenantService.TenantId ?? Guid.Empty));
    }

    private readonly AsyncLocal<bool> _isPrivilegedScopeActive = new();
    private readonly AsyncLocal<bool> _isEnforcingGuards = new();

    public bool IsPrivilegedScopeActive => _isPrivilegedScopeActive.Value;

    public IDisposable OpenPrivilegedScope()
    {
        _isPrivilegedScopeActive.Value = true;
        return new PrivilegedScope(() => _isPrivilegedScopeActive.Value = false);
    }

    private sealed class PrivilegedScope : IDisposable
    {
        private readonly Action _onDispose;
        private bool _disposed;

        public PrivilegedScope(Action onDispose) => _onDispose = onDispose;

        public void Dispose()
        {
            if (!_disposed)
            {
                _onDispose();
                _disposed = true;
            }
        }
    }

    public override int SaveChanges()
    {
        return SaveChanges(acceptAllChangesOnSuccess: true);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceTenantGuardsAndAudit();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceTenantGuardsAndAudit();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Kiểm soát nghiêm ngặt Tenant Guard khi ghi dữ liệu và tự động cập nhật Audit info.
    /// Chặn việc đổi TenantId, chặn ghi chéo Tenant, và phát hiện tham chiếu FK chéo Tenant.
    /// Mặc định chặn mọi thao tác ghi nghiệp vụ nếu thiếu tenant context, trừ khi nằm trong OpenPrivilegedScope().
    /// </summary>
    private void EnforceTenantGuardsAndAudit()
    {
        if (_isEnforcingGuards.Value) return;

        try
        {
            _isEnforcingGuards.Value = true;
            var currentTenantId = _currentTenantService.TenantId;

            // Nếu không có tenant context và KHÔNG ở trong privileged scope:
            // BẮT BUỘC chặn mọi thao tác ghi (Added, Modified, Deleted) đối với thực thể IMustHaveTenant
            if (!currentTenantId.HasValue && !IsPrivilegedScopeActive)
            {
                var hasTenantWrites = ChangeTracker.Entries<IMustHaveTenant>()
                    .Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
                if (hasTenantWrites)
                {
                    throw new SecurityException(
                        "Tenant violation: Business write operations require an authenticated tenant context. Missing TenantId.");
                }
            }

            foreach (var entry in ChangeTracker.Entries<IMustHaveTenant>())
            {
                if (currentTenantId.HasValue)
                {
                    var expectedTenantId = currentTenantId.Value;

                    if (entry.State == EntityState.Added)
                    {
                        if (entry.Entity.TenantId == Guid.Empty)
                        {
                            entry.Entity.TenantId = expectedTenantId;
                        }
                        else if (entry.Entity.TenantId != expectedTenantId)
                        {
                            throw new SecurityException(
                                $"Tenant violation: Attempted to insert {entry.Entity.GetType().Name} with TenantId '{entry.Entity.TenantId}' while authenticated as Tenant '{expectedTenantId}'.");
                        }
                    }
                    else if (entry.State == EntityState.Modified)
                    {
                        var tenantProp = entry.Property(nameof(IMustHaveTenant.TenantId));
                        if (tenantProp.IsModified && !tenantProp.OriginalValue!.Equals(tenantProp.CurrentValue))
                        {
                            throw new SecurityException(
                                $"Tenant violation: TenantId is immutable and cannot be changed on {entry.Entity.GetType().Name}.");
                        }

                        if (entry.Entity.TenantId != expectedTenantId)
                        {
                            throw new SecurityException(
                                $"Tenant violation: Attempted to modify {entry.Entity.GetType().Name} belonging to Tenant '{entry.Entity.TenantId}' while authenticated as Tenant '{expectedTenantId}'.");
                        }
                    }
                    else if (entry.State == EntityState.Deleted)
                    {
                        if (entry.Entity.TenantId != expectedTenantId)
                        {
                            throw new SecurityException(
                                $"Tenant violation: Attempted to delete {entry.Entity.GetType().Name} belonging to Tenant '{entry.Entity.TenantId}' while authenticated as Tenant '{expectedTenantId}'.");
                        }
                    }
                }
                else if (IsPrivilegedScopeActive)
                {
                    // Trong Privileged Scope (Seed hoặc Auth internal), kiểm tra thực thể có TenantId hợp lệ
                    if (entry.State == EntityState.Added)
                    {
                        if (entry.Entity.TenantId == Guid.Empty)
                        {
                            throw new SecurityException(
                                $"Tenant violation: Cannot insert {entry.Entity.GetType().Name} with empty TenantId even in privileged scope.");
                        }
                    }
                    else if (entry.State == EntityState.Modified)
                    {
                        var tenantProp = entry.Property(nameof(IMustHaveTenant.TenantId));
                        if (tenantProp.IsModified && !tenantProp.OriginalValue!.Equals(tenantProp.CurrentValue))
                        {
                            throw new SecurityException(
                                $"Tenant violation: TenantId is immutable and cannot be changed on {entry.Entity.GetType().Name}.");
                        }
                    }
                }
            }

            // Kiểm tra quan hệ Navigation: chặn gán thực thể liên kết của Tenant khác
            if (currentTenantId.HasValue)
            {
                var expectedTenantId = currentTenantId.Value;
                foreach (var entry in ChangeTracker.Entries<IMustHaveTenant>())
                {
                    if (entry.State is EntityState.Added or EntityState.Modified)
                    {
                        foreach (var navigation in entry.Navigations)
                        {
                            if (navigation.CurrentValue is IMustHaveTenant relatedTenantEntity)
                            {
                                if (relatedTenantEntity.TenantId != Guid.Empty && relatedTenantEntity.TenantId != expectedTenantId)
                                {
                                    throw new SecurityException(
                                        $"Cross-tenant reference violation: {entry.Entity.GetType().Name} references {relatedTenantEntity.GetType().Name} with TenantId '{relatedTenantEntity.TenantId}' while authenticated as Tenant '{expectedTenantId}'.");
                                }
                            }
                        }
                    }
                }
            }

            // Tự động gán CreatedAt / UpdatedAt
            foreach (var entry in ChangeTracker.Entries<BaseEntity>())
            {
                if (entry.State == EntityState.Added)
                {
                    if (entry.Entity.CreatedAt == default)
                    {
                        entry.Entity.CreatedAt = DateTimeOffset.UtcNow;
                    }
                }
                else if (entry.State == EntityState.Modified)
                {
                    entry.Entity.UpdatedAt = DateTimeOffset.UtcNow;
                    entry.Property(nameof(BaseEntity.CreatedAt)).IsModified = false;
                }
            }
        }
        finally
        {
            _isEnforcingGuards.Value = false;
        }
    }
}
