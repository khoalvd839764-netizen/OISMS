using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class SalesReturnItemConfiguration : IEntityTypeConfiguration<SalesReturnItem>
{
    public void Configure(EntityTypeBuilder<SalesReturnItem> builder)
    {
        builder.ToTable("sales_return_items");

        builder.HasKey(i => new { i.TenantId, i.Id });

        builder.HasIndex(i => new { i.TenantId, i.Id })
            .IsUnique()
            .HasDatabaseName("uq_sales_return_items_1");

        builder.HasIndex(i => new { i.TenantId, i.SalesReturnId, i.OrderItemId })
            .IsUnique()
            .HasDatabaseName("uq_sales_return_items_2");

        builder.Property(i => i.TenantId).IsRequired();

        builder.Property(i => i.SalesReturnId)
            .HasColumnName("sales_return_id")
            .IsRequired();

        builder.Property(i => i.OrderId)
            .HasColumnName("order_id")
            .IsRequired();

        builder.Property(i => i.OrderItemId)
            .HasColumnName("order_item_id")
            .IsRequired();

        builder.Property(i => i.SkuId)
            .HasColumnName("sku_id")
            .IsRequired();

        builder.Property(i => i.Quantity)
            .HasColumnName("quantity")
            .HasPrecision(18, 3)
            .IsRequired();

        builder.Property(i => i.RefundAmount)
            .HasColumnName("refund_amount")
            .HasPrecision(20, 2)
            .IsRequired();

        builder.Property(i => i.OriginalUnitCost)
            .HasColumnName("original_unit_cost")
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

        builder.HasOne(i => i.SalesReturn)
            .WithMany(r => r.Items)
            .HasForeignKey(i => new { i.TenantId, i.SalesReturnId, i.OrderId })
            .HasPrincipalKey(r => new { r.TenantId, r.Id, r.OrderId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.OrderItem)
            .WithMany()
            .HasForeignKey(i => new { i.TenantId, i.OrderItemId, i.OrderId, i.SkuId })
            .HasPrincipalKey(oi => new { oi.TenantId, oi.Id, oi.OrderId, oi.SkuId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Sku)
            .WithMany()
            .HasForeignKey(i => new { i.TenantId, i.SkuId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
