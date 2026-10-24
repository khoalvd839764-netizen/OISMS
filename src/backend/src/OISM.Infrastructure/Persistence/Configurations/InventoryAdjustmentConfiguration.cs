using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class InventoryAdjustmentConfiguration : IEntityTypeConfiguration<InventoryAdjustment>
{
    public void Configure(EntityTypeBuilder<InventoryAdjustment> builder)
    {
        builder.ToTable("inventory_adjustments");

        builder.HasKey(a => new { a.TenantId, a.Id });

        builder.HasIndex(a => new { a.TenantId, a.Id })
            .IsUnique()
            .HasDatabaseName("uq_inventory_adjustments_1");

        builder.HasIndex(a => new { a.TenantId, a.Number })
            .IsUnique()
            .HasDatabaseName("uq_inventory_adjustments_2");

        builder.HasIndex(a => new { a.TenantId, a.Id, a.BranchId, a.WarehouseId })
            .IsUnique()
            .HasDatabaseName("uq_inventory_adjustments_3");

        builder.Property(a => a.TenantId).IsRequired();

        builder.Property(a => a.Number)
            .HasColumnName("number")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(a => a.BranchId)
            .HasColumnName("branch_id")
            .IsRequired();

        builder.Property(a => a.WarehouseId)
            .HasColumnName("warehouse_id")
            .IsRequired();

        builder.Property(a => a.Reason)
            .HasColumnName("reason")
            .IsRequired();

        builder.Property(a => a.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasDefaultValue("Draft")
            .IsRequired();

        builder.Property(a => a.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired();

        builder.Property(a => a.ConfirmedBy)
            .HasColumnName("confirmed_by")
            .IsRequired(false);

        builder.Property(a => a.ConfirmedAt)
            .HasColumnName("confirmed_at")
            .IsRequired(false);

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(a => a.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);

        // Foreign keys
        builder.HasOne(a => a.Tenant)
            .WithMany()
            .HasForeignKey(a => a.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Branch)
            .WithMany()
            .HasForeignKey(a => new { a.TenantId, a.BranchId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Warehouse)
            .WithMany()
            .HasForeignKey(a => new { a.TenantId, a.WarehouseId, a.BranchId })
            .HasPrincipalKey(w => new { w.TenantId, w.Id, w.BranchId })
            .HasConstraintName("fk_inventory_adjustments_warehouse_branch")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.CreatedByUser)
            .WithMany()
            .HasForeignKey(a => new { a.TenantId, a.CreatedBy })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.ConfirmedByUser)
            .WithMany()
            .HasForeignKey(a => new { a.TenantId, a.ConfirmedBy })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(a => a.Items)
            .WithOne(i => i.Adjustment)
            .HasForeignKey(i => new { i.TenantId, i.AdjustmentId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
