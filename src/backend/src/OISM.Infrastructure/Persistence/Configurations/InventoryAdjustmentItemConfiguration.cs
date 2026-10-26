using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class InventoryAdjustmentItemConfiguration : IEntityTypeConfiguration<InventoryAdjustmentItem>
{
    public void Configure(EntityTypeBuilder<InventoryAdjustmentItem> builder)
    {
        builder.ToTable("inventory_adjustment_items");

        builder.HasKey(i => new { i.TenantId, i.Id });

        builder.HasIndex(i => new { i.TenantId, i.Id })
            .IsUnique()
            .HasDatabaseName("uq_inventory_adjustment_items_1");

        builder.HasIndex(i => new { i.TenantId, i.AdjustmentId, i.SkuId })
            .IsUnique()
            .HasDatabaseName("uq_inventory_adjustment_items_2");

        builder.Property(i => i.TenantId).IsRequired();

        builder.Property(i => i.AdjustmentId)
            .HasColumnName("adjustment_id")
            .IsRequired();

        builder.Property(i => i.SkuId)
            .HasColumnName("sku_id")
            .IsRequired();

        builder.Property(i => i.QuantityDelta)
            .HasColumnName("quantity_delta")
            .HasPrecision(18, 3)
            .IsRequired();

        builder.Property(i => i.UnitCost)
            .HasColumnName("unit_cost")
            .HasPrecision(20, 6)
            .IsRequired(false);

        builder.Property(i => i.OriginalTransactionId)
            .HasColumnName("original_transaction_id")
            .IsRequired(false);

        builder.Property(i => i.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(i => i.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);

        // Foreign keys
        builder.HasOne(i => i.Tenant)
            .WithMany()
            .HasForeignKey(i => i.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Adjustment)
            .WithMany(a => a.Items)
            .HasForeignKey(i => new { i.TenantId, i.AdjustmentId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Sku)
            .WithMany()
            .HasForeignKey(i => new { i.TenantId, i.SkuId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.OriginalTransaction)
            .WithMany()
            .HasForeignKey(i => new { i.TenantId, i.OriginalTransactionId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
