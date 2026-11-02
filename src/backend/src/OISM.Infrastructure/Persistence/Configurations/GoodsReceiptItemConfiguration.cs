using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class GoodsReceiptItemConfiguration : IEntityTypeConfiguration<GoodsReceiptItem>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptItem> builder)
    {
        builder.ToTable("goods_receipt_items");

        builder.HasKey(i => new { i.TenantId, i.Id });

        builder.HasIndex(i => new { i.TenantId, i.Id })
            .IsUnique()
            .HasDatabaseName("uq_goods_receipt_items_1");

        builder.HasIndex(i => new { i.TenantId, i.ReceiptId, i.SkuId })
            .IsUnique()
            .HasDatabaseName("uq_goods_receipt_items_2");

        builder.Property(i => i.TenantId).IsRequired();

        builder.Property(i => i.ReceiptId)
            .HasColumnName("receipt_id")
            .IsRequired();

        builder.Property(i => i.SkuId)
            .HasColumnName("sku_id")
            .IsRequired();

        builder.Property(i => i.UnitId)
            .HasColumnName("unit_id")
            .IsRequired(false);

        builder.Property(i => i.UnitNameSnapshot)
            .HasColumnName("unit_name_snapshot")
            .HasMaxLength(100)
            .HasDefaultValue("")
            .IsRequired();

        builder.Property(i => i.FactorSnapshot)
            .HasColumnName("factor_snapshot")
            .HasPrecision(18, 6)
            .HasDefaultValue(1m)
            .IsRequired();

        builder.Property(i => i.BaseQuantity)
            .HasColumnName("base_quantity")
            .HasPrecision(18, 3)
            .HasComputedColumnSql("quantity * factor_snapshot", stored: true);

        builder.Property(i => i.Quantity)
            .HasColumnName("quantity")
            .HasPrecision(18, 3)
            .IsRequired();

        builder.Property(i => i.UnitPrice)
            .HasColumnName("unit_price")
            .HasPrecision(20, 6)
            .IsRequired();

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

        builder.HasOne(i => i.Receipt)
            .WithMany(r => r.Items)
            .HasForeignKey(i => new { i.TenantId, i.ReceiptId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Sku)
            .WithMany()
            .HasForeignKey(i => new { i.TenantId, i.SkuId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Unit)
            .WithMany()
            .HasForeignKey(i => new { i.TenantId, i.UnitId })
            .HasPrincipalKey(u => new { u.TenantId, u.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
