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
<<<<<<< HEAD
        builder.Property(i => i.ReceiptId).IsRequired();
        builder.Property(i => i.SkuId).IsRequired();
=======

        builder.Property(i => i.ReceiptId)
            .HasColumnName("receipt_id")
            .IsRequired();

        builder.Property(i => i.SkuId)
            .HasColumnName("sku_id")
            .IsRequired();
>>>>>>> 2cc35c255b5ed4a5ecafb66a14f858702cf6e168

        builder.Property(i => i.Quantity)
            .HasColumnName("quantity")
            .HasPrecision(18, 3)
            .IsRequired();

        builder.Property(i => i.UnitPrice)
            .HasColumnName("unit_price")
            .HasPrecision(20, 6)
            .IsRequired();

        builder.Property(i => i.CreatedAt)
<<<<<<< HEAD
=======
            .HasColumnName("created_at")
>>>>>>> 2cc35c255b5ed4a5ecafb66a14f858702cf6e168
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(i => i.UpdatedAt)
<<<<<<< HEAD
            .IsRequired(false);

=======
            .HasColumnName("updated_at")
            .IsRequired(false);

        // Foreign keys
>>>>>>> 2cc35c255b5ed4a5ecafb66a14f858702cf6e168
        builder.HasOne(i => i.Tenant)
            .WithMany()
            .HasForeignKey(i => i.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

<<<<<<< HEAD
        builder.HasOne(i => i.Sku)
            .WithMany()
            .HasForeignKey(i => new { i.TenantId, i.SkuId })
            .HasPrincipalKey(v => new { v.TenantId, v.Id })
=======
        builder.HasOne(i => i.Receipt)
            .WithMany(r => r.Items)
            .HasForeignKey(i => new { i.TenantId, i.ReceiptId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Sku)
            .WithMany()
            .HasForeignKey(i => new { i.TenantId, i.SkuId })
>>>>>>> 2cc35c255b5ed4a5ecafb66a14f858702cf6e168
            .OnDelete(DeleteBehavior.Restrict);
    }
}
