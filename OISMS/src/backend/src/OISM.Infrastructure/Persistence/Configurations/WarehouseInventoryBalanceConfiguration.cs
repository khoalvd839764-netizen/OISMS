using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class WarehouseInventoryBalanceConfiguration : IEntityTypeConfiguration<WarehouseInventoryBalance>
{
    public void Configure(EntityTypeBuilder<WarehouseInventoryBalance> builder)
    {
        builder.ToTable("warehouse_inventory_balances");

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
            .HasColumnName("on_hand")
            .HasPrecision(18, 3)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(b => b.Reserved)
            .HasColumnName("reserved")
            .HasPrecision(18, 3)
            .HasDefaultValue(0)
            .IsRequired();

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
            .OnDelete(DeleteBehavior.Restrict);
    }
}
