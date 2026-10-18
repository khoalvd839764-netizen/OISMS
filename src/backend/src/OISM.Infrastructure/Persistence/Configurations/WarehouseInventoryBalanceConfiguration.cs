using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class WarehouseInventoryBalanceConfiguration : IEntityTypeConfiguration<WarehouseInventoryBalance>
{
    public void Configure(EntityTypeBuilder<WarehouseInventoryBalance> builder)
    {
        builder.ToTable("warehouse_inventory_balances");

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
            .HasColumnName("on_hand")
            .HasPrecision(18, 3)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(w => w.Reserved)
            .HasColumnName("reserved")
            .HasPrecision(18, 3)
            .HasDefaultValue(0)
            .IsRequired();

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
            .OnDelete(DeleteBehavior.Restrict);
    }
}
