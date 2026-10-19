using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items");

        builder.HasKey(i => new { i.TenantId, i.Id });

        builder.HasIndex(i => new { i.TenantId, i.Id })
            .IsUnique()
            .HasDatabaseName("uq_order_items_1");

        builder.HasIndex(i => new { i.TenantId, i.OrderId, i.SkuId })
            .IsUnique()
            .HasDatabaseName("uq_order_items_2");

        builder.HasIndex(i => new { i.TenantId, i.Id, i.OrderId, i.SkuId })
            .IsUnique()
            .HasDatabaseName("uq_order_items_3");

        builder.Property(i => i.TenantId).IsRequired();

        builder.Property(i => i.OrderId)
            .HasColumnName("order_id")
            .IsRequired();

        builder.Property(i => i.SkuId)
            .HasColumnName("sku_id")
            .IsRequired();

        builder.Property(i => i.SkuCodeSnapshot)
            .HasColumnName("sku_code_snapshot")
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(i => i.SkuNameSnapshot)
            .HasColumnName("sku_name_snapshot")
            .HasMaxLength(250)
            .IsRequired();

        builder.Property(i => i.Quantity)
            .HasColumnName("quantity")
            .HasPrecision(18, 3)
            .IsRequired();

        builder.Property(i => i.UnitPrice)
            .HasColumnName("unit_price")
            .HasPrecision(20, 2)
            .IsRequired();

        builder.Property(i => i.CostPrice)
            .HasColumnName("cost_price")
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

        builder.HasOne(i => i.Order)
            .WithMany(o => o.Items)
            .HasForeignKey(i => new { i.TenantId, i.OrderId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Sku)
            .WithMany()
            .HasForeignKey(i => new { i.TenantId, i.SkuId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
