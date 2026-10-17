using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("warehouses");

        builder.HasKey(w => new { w.TenantId, w.Id });

        builder.HasIndex(w => new { w.TenantId, w.Id })
            .IsUnique()
            .HasDatabaseName("uq_warehouses_1");

        builder.HasIndex(w => new { w.TenantId, w.Id, w.BranchId })
            .IsUnique()
            .HasDatabaseName("uq_warehouses_2");

        builder.Property(w => w.TenantId)
            .IsRequired();

        builder.Property(w => w.BranchId)
            .IsRequired();

        builder.Property(w => w.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(w => w.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(w => w.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(w => w.UpdatedAt)
            .IsRequired(false);

        // Foreign keys
        builder.HasOne(w => w.Tenant)
            .WithMany()
            .HasForeignKey(w => w.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(w => w.Branch)
            .WithMany()
            .HasForeignKey(w => new { w.TenantId, w.BranchId })
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
