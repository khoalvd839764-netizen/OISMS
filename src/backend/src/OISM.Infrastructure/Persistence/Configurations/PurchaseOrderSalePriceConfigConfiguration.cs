using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class PurchaseOrderSalePriceConfigConfiguration : IEntityTypeConfiguration<PurchaseOrderSalePriceConfig>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderSalePriceConfig> builder)
    {
        builder.ToTable("purchase_order_sale_price_configs");

        builder.HasKey(c => new { c.TenantId, c.Id });

        builder.HasIndex(c => new { c.TenantId, c.Id })
            .IsUnique()
            .HasDatabaseName("uq_po_sale_price_configs_1");

        builder.HasIndex(c => new { c.TenantId, c.PurchaseOrderItemId, c.UnitId, c.PriceListId })
            .IsUnique()
            .HasDatabaseName("uq_po_sale_price_configs_item_unit_pricelist");

        builder.Property(c => c.TenantId).IsRequired();

        builder.Property(c => c.PurchaseOrderItemId)
            .HasColumnName("purchase_order_item_id")
            .IsRequired();

        builder.Property(c => c.UnitId)
            .HasColumnName("unit_id")
            .IsRequired();

        builder.Property(c => c.PriceListId)
            .HasColumnName("price_list_id")
            .IsRequired();

        builder.Property(c => c.Mode)
            .HasColumnName("mode")
            .HasMaxLength(20)
            .HasDefaultValue("KEEP")
            .IsRequired();

        builder.Property(c => c.MarginPercent)
            .HasColumnName("margin_percent")
            .HasPrecision(7, 4)
            .IsRequired(false);

        builder.Property(c => c.FixedPrice)
            .HasColumnName("fixed_price")
            .HasPrecision(20, 6)
            .IsRequired(false);

        builder.Property(c => c.PriceSource)
            .HasColumnName("price_source")
            .HasMaxLength(20)
            .IsRequired(false);

        builder.Property(c => c.ApplyMode)
            .HasColumnName("apply_mode")
            .HasMaxLength(20)
            .HasDefaultValue("ON_RECEIPT")
            .IsRequired();

        builder.Property(c => c.ApplyFrom)
            .HasColumnName("apply_from")
            .IsRequired(false);

        builder.Property(c => c.AppliedAt)
            .HasColumnName("applied_at")
            .IsRequired(false);

        builder.Property(c => c.PriceChangeRequestId)
            .HasColumnName("price_change_request_id")
            .IsRequired(false);

        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);

        // Foreign keys
        builder.HasOne(c => c.Tenant)
            .WithMany()
            .HasForeignKey(c => c.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.PurchaseOrderItem)
            .WithMany(i => i.SalePriceConfigs)
            .HasForeignKey(c => new { c.TenantId, c.PurchaseOrderItemId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Unit)
            .WithMany()
            .HasForeignKey(c => new { c.TenantId, c.UnitId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.PriceList)
            .WithMany()
            .HasForeignKey(c => new { c.TenantId, c.PriceListId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.PriceChangeRequest)
            .WithMany()
            .HasForeignKey(c => new { c.TenantId, c.PriceChangeRequestId })
            .OnDelete(DeleteBehavior.SetNull);
    }
}
