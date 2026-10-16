using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OISM.Application.Common.Interfaces;
using OISM.Domain.Common;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence;

/// <summary>
/// DbContext chính của hệ thống OISMS.
/// Quản lý cô lập dữ liệu Multi-tenant tự động thông qua Global Query Filter và tự động gán Audit info.
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
    public DbSet<Role> Roles { get; set; } = null!;
    public DbSet<UserRole> UserRoles { get; set; } = null!;
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
    public DbSet<Branch> Branches { get; set; } = null!;
    public DbSet<Category> Categories { get; set; } = null!;
    public DbSet<Brand> Brands { get; set; } = null!;
    public DbSet<Product> Products { get; set; } = null!;
    public DbSet<ProductVariant> ProductVariants { get; set; } = null!;
    public DbSet<Supplier> Suppliers { get; set; } = null!;

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return Database.BeginTransactionAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // =====================================================================
        // 1. CÔ LẬP DỮ LIỆU ĐA NGƯỜI THUÊ (GLOBAL QUERY FILTER - NFR-TENANT-01)
        // Mọi truy vấn mặc định sẽ tự động lọc: WHERE tenant_id = CurrentTenantService.TenantId
        // =====================================================================
        modelBuilder.Entity<User>()
            .HasQueryFilter(e => !_currentTenantService.TenantId.HasValue || e.TenantId == _currentTenantService.TenantId);

        modelBuilder.Entity<RefreshToken>()
            .HasQueryFilter(e => !_currentTenantService.TenantId.HasValue || e.TenantId == _currentTenantService.TenantId);

        modelBuilder.Entity<Branch>()
            .HasQueryFilter(e => !_currentTenantService.TenantId.HasValue || e.TenantId == _currentTenantService.TenantId);

        modelBuilder.Entity<Category>()
            .HasQueryFilter(e => !_currentTenantService.TenantId.HasValue || e.TenantId == _currentTenantService.TenantId);

        modelBuilder.Entity<Brand>()
            .HasQueryFilter(e => !_currentTenantService.TenantId.HasValue || e.TenantId == _currentTenantService.TenantId);

        modelBuilder.Entity<Product>()
            .HasQueryFilter(e => !_currentTenantService.TenantId.HasValue || e.TenantId == _currentTenantService.TenantId);

        modelBuilder.Entity<ProductVariant>()
            .HasQueryFilter(e => !_currentTenantService.TenantId.HasValue || e.TenantId == _currentTenantService.TenantId);

        modelBuilder.Entity<Supplier>()
            .HasQueryFilter(e => !_currentTenantService.TenantId.HasValue || e.TenantId == _currentTenantService.TenantId);

        // =====================================================================
        // 2. KHÓA CHÍNH, CHỈ MỤC DUY NHẤT VÀ MỐI QUAN HỆ
        // =====================================================================

        // Khóa kép cho UserRole
        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(ur => new { ur.UserId, ur.RoleId });

            entity.HasOne(ur => ur.User)
                .WithMany(u => u.UserRoles)
                .HasForeignKey(ur => ur.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(ur => ur.Role)
                .WithMany(r => r.UserRoles)
                .HasForeignKey(ur => ur.RoleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // TenantCode phải là duy nhất trên toàn hệ thống
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasIndex(t => t.Code).IsUnique();
        });

        // RoleCode phải duy nhất
        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasIndex(r => r.Code).IsUnique();
        });

        // Email trong cùng 1 Tenant không được trùng lặp
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => new { u.TenantId, u.Email }).IsUnique();
        });

        // BẮT BUỘC THEO ĐỀ BÀI (FR-PROD-02): Mã SKU là duy nhất trong cùng 1 TenantId
        modelBuilder.Entity<ProductVariant>(entity =>
        {
            entity.HasIndex(v => new { v.TenantId, v.SKU }).IsUnique();
            entity.HasIndex(v => new { v.TenantId, v.Barcode });

            entity.Property(v => v.RetailPrice).HasPrecision(18, 2);
            entity.Property(v => v.WholesalePrice).HasPrecision(18, 2);
            entity.Property(v => v.CostPrice).HasPrecision(18, 2);
        });

        // Category cấu hình quan hệ cha - con
        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasOne(c => c.Parent)
                .WithMany(c => c.SubCategories)
                .HasForeignKey(c => c.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditAndTenantInfo();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyAuditAndTenantInfo();
        return base.SaveChanges();
    }

    private void ApplyAuditAndTenantInfo()
    {
        // 1. Tự động gán TenantId cho thực thể mới thêm
        foreach (var entry in ChangeTracker.Entries<IMustHaveTenant>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.TenantId == Guid.Empty && _currentTenantService.TenantId.HasValue)
                {
                    entry.Entity.TenantId = _currentTenantService.TenantId.Value;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(nameof(IMustHaveTenant.TenantId)).IsModified = false;
            }
        }

        // 2. Tự động cập nhật CreatedAt / UpdatedAt
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
}
