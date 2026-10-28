using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class StockCountItemConfiguration : IEntityTypeConfiguration<StockCountItem>
{
    public void Configure(EntityTypeBuilder<StockCountItem> builder)
    {
        builder.ToTable("stock_count_items");

        builder.HasKey(i => new { i.TenantId, i.Id });

        builder.HasIndex(i => new { i.TenantId, i.Id })
            .IsUnique()
            .HasDatabaseName("uq_stock_count_items_1");

        builder.HasIndex(i => new { i.TenantId, i.StockCountId, i.SkuId })
            .IsUnique()
            .HasDatabaseName("uq_stock_count_items_2");

        builder.Property(i => i.TenantId).IsRequired();

        builder.Property(i => i.StockCountId)
            .HasColumnName("stock_count_id")
            .IsRequired();

        builder.Property(i => i.SkuId)
            .HasColumnName("sku_id")
            .IsRequired();

        builder.Property(i => i.ExpectedQuantity)
            .HasColumnName("expected_quantity")
            .HasPrecision(18, 3)
            .IsRequired();

        builder.Property(i => i.CountedQuantity)
            .HasColumnName("counted_quantity")
            .HasPrecision(18, 3)
            .IsRequired(false);

        builder.Property(i => i.BalanceSequenceAtSnapshot)
            .HasColumnName("balance_sequence_at_snapshot")
            .IsRequired();

        builder.Property(i => i.AdjustmentUnitCost)
            .HasColumnName("adjustment_unit_cost")
            .HasPrecision(20, 6)
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

        builder.HasOne(i => i.StockCount)
            .WithMany(c => c.Items)
            .HasForeignKey(i => new { i.TenantId, i.StockCountId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Sku)
            .WithMany()
            .HasForeignKey(i => new { i.TenantId, i.SkuId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
