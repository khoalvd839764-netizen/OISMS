using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class InventoryBalanceConfiguration : IEntityTypeConfiguration<InventoryBalance>
{
    public void Configure(EntityTypeBuilder<InventoryBalance> builder)
    {
        builder.ToTable("inventory_balances");

        builder.HasKey(ib => new { ib.TenantId, ib.Id });

        builder.HasIndex(b => new { b.TenantId, b.Id })
            .IsUnique()
            .HasDatabaseName("uq_inventory_balances_1");

        builder.HasIndex(b => new { b.TenantId, b.BranchId, b.SkuId })
            .IsUnique()
            .HasDatabaseName("uq_inventory_balances_2");

        builder.HasIndex(b => new { b.TenantId, b.SkuId, b.BranchId })
            .HasDatabaseName("ix_inventory_balances_sku_branches");

        builder.Property(b => b.TenantId)
            .IsRequired();

        builder.Property(b => b.BranchId)
            .IsRequired();

        builder.Property(b => b.SkuId)
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

        builder.Property(b => b.AverageCost)
            .HasColumnName("average_cost")
            .HasPrecision(20, 6)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(b => b.ReorderPoint)
            .HasColumnName("reorder_point")
            .HasPrecision(18, 3)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(b => b.LastSequence)
            .HasColumnName("last_sequence")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(b => b.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(b => b.UpdatedAt)
            .IsRequired(false);

        // Foreign keys
        builder.HasOne(b => b.Tenant)
            .WithMany()
            .HasForeignKey(b => b.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Branch)
            .WithMany()
            .HasForeignKey(b => new { b.TenantId, b.BranchId })
            .HasPrincipalKey(br => new { br.TenantId, br.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Sku)
            .WithMany()
            .HasForeignKey(b => new { b.TenantId, b.SkuId })
            .HasPrincipalKey(v => new { v.TenantId, v.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
