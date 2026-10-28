using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class StockTransferItemConfiguration : IEntityTypeConfiguration<StockTransferItem>
{
    public void Configure(EntityTypeBuilder<StockTransferItem> builder)
    {
        builder.ToTable("stock_transfer_items");

        builder.HasKey(i => new { i.TenantId, i.Id });

        builder.HasIndex(i => new { i.TenantId, i.Id })
            .IsUnique()
            .HasDatabaseName("uq_stock_transfer_items_1");

        builder.HasIndex(i => new { i.TenantId, i.TransferId, i.SkuId })
            .IsUnique()
            .HasDatabaseName("uq_stock_transfer_items_2");

        builder.Property(i => i.TenantId).IsRequired();

        builder.Property(i => i.TransferId)
            .HasColumnName("transfer_id")
            .IsRequired();

        builder.Property(i => i.SkuId)
            .HasColumnName("sku_id")
            .IsRequired();

        builder.Property(i => i.Quantity)
            .HasColumnName("quantity")
            .HasPrecision(18, 3)
            .IsRequired();

        builder.Property(i => i.UnitCost)
            .HasColumnName("unit_cost")
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

        builder.HasOne(i => i.Transfer)
            .WithMany(t => t.Items)
            .HasForeignKey(i => new { i.TenantId, i.TransferId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Sku)
            .WithMany()
            .HasForeignKey(i => new { i.TenantId, i.SkuId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
