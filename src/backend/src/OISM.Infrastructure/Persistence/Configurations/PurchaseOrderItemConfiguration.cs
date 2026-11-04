using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class PurchaseOrderItemConfiguration : IEntityTypeConfiguration<PurchaseOrderItem>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderItem> builder)
    {
        builder.ToTable("purchase_order_items");

        builder.HasKey(i => new { i.TenantId, i.Id });

        builder.HasIndex(i => new { i.TenantId, i.Id })
            .IsUnique()
            .HasDatabaseName("uq_purchase_order_items_1");

        builder.HasIndex(i => new { i.TenantId, i.PurchaseOrderId, i.SkuId })
            .IsUnique()
            .HasDatabaseName("uq_purchase_order_items_2");

        builder.HasIndex(i => new { i.TenantId, i.Id, i.SkuId })
            .IsUnique()
            .HasDatabaseName("uq_purchase_order_items_3");

        builder.Property(i => i.TenantId).IsRequired();

        builder.Property(i => i.PurchaseOrderId)
            .HasColumnName("purchase_order_id")
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

        builder.Property(i => i.QuantityDecimalPlaces)
            .HasColumnName("quantity_decimal_places")
            .IsRequired();

        builder.Property(i => i.Quantity)
            .HasColumnName("quantity")
            .HasPrecision(18, 3)
            .IsRequired();

        builder.Property(i => i.OrderedBaseQuantity)
            .HasColumnName("ordered_base_quantity")
            .HasPrecision(18, 3)
            .HasComputedColumnSql("quantity * factor_snapshot", stored: true);

        builder.Property(i => i.UnitPrice)
            .HasColumnName("unit_price")
            .HasPrecision(20, 6)
            .IsRequired();

        builder.Property(i => i.ReceivedBaseQuantity)
            .HasColumnName("received_base_quantity")
            .HasPrecision(18, 3)
            .HasDefaultValue(0m)
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

        builder.HasOne(i => i.PurchaseOrder)
            .WithMany(p => p.Items)
            .HasForeignKey(i => new { i.TenantId, i.PurchaseOrderId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Sku)
            .WithMany()
            .HasForeignKey(i => new { i.TenantId, i.SkuId })
            .HasPrincipalKey(s => new { s.TenantId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Unit)
            .WithMany()
            .HasForeignKey(i => new { i.TenantId, i.UnitId })
            .HasPrincipalKey(u => new { u.TenantId, u.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
