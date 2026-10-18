using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class WarehouseInventoryBalanceConfiguration : IEntityTypeConfiguration<WarehouseInventoryBalance>
{
    public void Configure(EntityTypeBuilder<WarehouseInventoryBalance> builder)
    {
        builder.ToTable("warehouse_inventory_balances");

<<<<<<< HEAD
        builder.HasKey(w => new { w.TenantId, w.Id });

        builder.HasIndex(w => new { w.TenantId, w.Id })
            .IsUnique()
            .HasDatabaseName("uq_warehouse_inventory_balances_1");

        builder.HasIndex(w => new { w.TenantId, w.WarehouseId, w.SkuId })
            .IsUnique()
            .HasDatabaseName("uq_warehouse_inventory_balances_2");

        builder.Property(w => w.TenantId).IsRequired();
        builder.Property(w => w.BranchId).IsRequired();
        builder.Property(w => w.WarehouseId).IsRequired();
        builder.Property(w => w.SkuId).IsRequired();

        builder.Property(w => w.OnHand)
=======
        builder.HasKey(b => new { b.TenantId, b.Id });

        builder.HasIndex(b => new { b.TenantId, b.Id })
            .IsUnique()
            .HasDatabaseName("uq_warehouse_inventory_balances_1");

        builder.HasIndex(b => new { b.TenantId, b.WarehouseId, b.SkuId })
            .IsUnique()
            .HasDatabaseName("uq_warehouse_inventory_balances_2");

        builder.Property(b => b.TenantId).IsRequired();

        builder.Property(b => b.BranchId)
            .HasColumnName("branch_id")
            .IsRequired();

        builder.Property(b => b.WarehouseId)
            .HasColumnName("warehouse_id")
            .IsRequired();

        builder.Property(b => b.SkuId)
            .HasColumnName("sku_id")
            .IsRequired();

        builder.Property(b => b.OnHand)
>>>>>>> 2cc35c255b5ed4a5ecafb66a14f858702cf6e168
            .HasColumnName("on_hand")
            .HasPrecision(18, 3)
            .HasDefaultValue(0)
            .IsRequired();

<<<<<<< HEAD
        builder.Property(w => w.Reserved)
=======
        builder.Property(b => b.Reserved)
>>>>>>> 2cc35c255b5ed4a5ecafb66a14f858702cf6e168
            .HasColumnName("reserved")
            .HasPrecision(18, 3)
            .HasDefaultValue(0)
            .IsRequired();

<<<<<<< HEAD
        builder.Property(w => w.LastSequence)
            .HasColumnName("last_sequence")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(w => w.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(w => w.UpdatedAt)
            .IsRequired(false);

        builder.HasOne(w => w.Tenant)
            .WithMany()
            .HasForeignKey(w => w.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(w => w.Branch)
            .WithMany()
            .HasForeignKey(w => new { w.TenantId, w.BranchId })
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(w => w.Warehouse)
            .WithMany()
            .HasForeignKey(w => new { w.TenantId, w.WarehouseId })
            .HasPrincipalKey(wh => new { wh.TenantId, wh.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(w => w.Sku)
            .WithMany()
            .HasForeignKey(w => new { w.TenantId, w.SkuId })
            .HasPrincipalKey(v => new { v.TenantId, v.Id })
=======
        builder.Property(b => b.LastSequence)
            .HasColumnName("last_sequence")
            .HasDefaultValue(0L)
            .IsRequired();

        builder.Property(b => b.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(b => b.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);

        // Foreign keys
        builder.HasOne(b => b.Tenant)
            .WithMany()
            .HasForeignKey(b => b.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Branch)
            .WithMany()
            .HasForeignKey(b => new { b.TenantId, b.BranchId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Warehouse)
            .WithMany()
            .HasForeignKey(b => new { b.TenantId, b.WarehouseId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Sku)
            .WithMany()
            .HasForeignKey(b => new { b.TenantId, b.SkuId })
>>>>>>> 2cc35c255b5ed4a5ecafb66a14f858702cf6e168
            .OnDelete(DeleteBehavior.Restrict);
    }
}
