using Microsoft.EntityFrameworkCore;
using OISM.Application.Common;
using OISM.Domain.Common;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence;

/// <summary>
/// DbContext chính của hệ thống OISM.
/// Quản lý các DbSet, cấu hình quan hệ cơ sở dữ liệu và tự động cô lập dữ liệu Multi-tenant.
/// </summary>
public class AppDbContext : DbContext
{
    private readonly ICurrentTenantService _currentTenantService;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ICurrentTenantService currentTenantService) : base(options)
    {
        _currentTenantService = currentTenantService;
    }

    // =========================================================================
    // DANH SÁCH CÁC BẢNG (DBSETS)
    // =========================================================================
    public DbSet<Tenant> Tenants { get; set; } = null!;
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Role> Roles { get; set; } = null!;
    public DbSet<UserRole> UserRoles { get; set; } = null!;
    public DbSet<Branch> Branches { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ---------------------------------------------------------------------
        // 1. CẤU HÌNH BỘ LỌC TOÀN CỤC CÔ LẬP DỮ LIỆU MULTI-TENANT (GLOBAL QUERY FILTER)
        // Yêu cầu NFR-TENANT-01: Chỉ truy vấn dữ liệu thuộc về Tenant hiện tại.
        // ---------------------------------------------------------------------
        modelBuilder.Entity<User>()
            .HasQueryFilter(e => e.TenantId == _currentTenantService.TenantId);

        modelBuilder.Entity<Branch>()
            .HasQueryFilter(e => e.TenantId == _currentTenantService.TenantId);

        // ---------------------------------------------------------------------
        // 2. CẤU HÌNH KHÓA CHÍNH VÀ RÀNG BUỘC (CONSTRAINTS)
        // ---------------------------------------------------------------------
        
        // Khóa chính kép cho bảng liên kết N-N UserRole
        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(ur => new { ur.UserId, ur.RoleId });

            entity.HasOne(ur => ur.User)
                  .WithMany(u => u.UserRoles)
                  .HasForeignKey(ur => ur.UserId)
                  .IsRequired(false)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(ur => ur.Role)
                  .WithMany(r => r.UserRoles)
                  .HasForeignKey(ur => ur.RoleId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // Đảm bảo mã Code cửa hàng là duy nhất
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasIndex(t => t.Code).IsUnique();
        });

        // Đảm bảo mã Code vai trò là duy nhất
        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasIndex(r => r.Code).IsUnique();
        });

        // Đảm bảo không trùng Email trong cùng 1 Tenant (uq_users_tenant_email)
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => new { u.TenantId, u.Email }).IsUnique();
        });
    }

    /// <summary>
    /// Ghi đè hàm SaveChangesAsync để tự động đóng dấu TenantId và thời gian CreatedAt/UpdatedAt khi lưu dữ liệu
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditAndTenantInfo();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Ghi đè hàm SaveChanges đồng bộ để đảm bảo dữ liệu luôn được đóng dấu TenantId và Audit Time
    /// </summary>
    public override int SaveChanges()
    {
        ApplyAuditAndTenantInfo();
        return base.SaveChanges();
    }

    /// <summary>
    /// Quét các thực thể trong ChangeTracker để tự động gán TenantId và thời gian khởi tạo/cập nhật
    /// </summary>
    private void ApplyAuditAndTenantInfo()
    {
        // 1. Quét các thực thể đang thêm mới (EntityState.Added) kế thừa IMustHaveTenant
        foreach (var entry in ChangeTracker.Entries<IMustHaveTenant>())
        {
            if (entry.State == EntityState.Added)
            {
                // Nếu TenantId chưa có giá trị (bằng Guid.Empty) và TenantId hiện tại có giá trị
                if (entry.Entity.TenantId == Guid.Empty && _currentTenantService.TenantId.HasValue)
                {
                    entry.Entity.TenantId = _currentTenantService.TenantId.Value;
                }
            }
        }

        // 2. Quét các thực thể kế thừa BaseEntity để tự động gán thời gian CreatedAt / UpdatedAt
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
            }
        }
    }
}
