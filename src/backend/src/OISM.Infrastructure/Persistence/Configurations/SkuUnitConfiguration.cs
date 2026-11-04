using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class SkuUnitConfiguration : IEntityTypeConfiguration<SkuUnit>
{
    public void Configure(EntityTypeBuilder<SkuUnit> builder)
    {
        builder.ToTable("sku_units");

        builder.HasKey(u => new { u.TenantId, u.Id });

        builder.HasIndex(u => new { u.TenantId, u.Id })
            .IsUnique()
            .HasDatabaseName("uq_sku_units_1");

        builder.HasIndex(u => new { u.TenantId, u.SkuId, u.UnitId })
            .IsUnique()
            .HasDatabaseName("uq_sku_units_2");

        builder.HasIndex(u => new { u.TenantId, u.SkuId })
            .IsUnique()
            .HasFilter("is_default_sale AND is_active")
            .HasDatabaseName("ux_sku_units_default_sale");

        builder.HasIndex(u => new { u.TenantId, u.SkuId })
            .IsUnique()
            .HasFilter("is_default_purchase AND is_active")
            .HasDatabaseName("ux_sku_units_default_purchase");

        builder.HasIndex(u => new { u.TenantId, u.Barcode })
            .IsUnique()
            .HasFilter("barcode IS NOT NULL")
            .HasDatabaseName("ux_sku_units_barcode");

        builder.Property(u => u.TenantId).IsRequired();

        builder.Property(u => u.SkuId)
            .HasColumnName("sku_id")
            .IsRequired();

        builder.Property(u => u.UnitId)
            .HasColumnName("unit_id")
            .IsRequired();

        builder.Property(u => u.FactorToBase)
            .HasColumnName("factor_to_base")
            .HasPrecision(18, 6)
            .IsRequired();

        builder.Property(u => u.CanPurchase)
            .HasColumnName("can_purchase")
            .IsRequired();

        builder.Property(u => u.CanSell)
            .HasColumnName("can_sell")
            .IsRequired();

        builder.Property(u => u.IsDefaultPurchase)
            .HasColumnName("is_default_purchase")
            .IsRequired();

        builder.Property(u => u.IsDefaultSale)
            .HasColumnName("is_default_sale")
            .IsRequired();

        builder.Property(u => u.Barcode)
            .HasColumnName("barcode")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(u => u.QuantityDecimalPlaces)
            .HasColumnName("quantity_decimal_places")
            .IsRequired();

        builder.Property(u => u.SalePrice)
            .HasColumnName("sale_price")
            .HasPrecision(20, 2)
            .IsRequired(false);

        builder.Property(u => u.Version)
            .HasColumnName("version")
            .IsRequired();

        builder.Property(u => u.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(u => u.UpdatedBy)
            .HasColumnName("updated_by")
            .IsRequired(false);

        builder.Property(u => u.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(u => u.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);

        // Foreign keys
        builder.HasOne(u => u.Tenant)
            .WithMany()
            .HasForeignKey(u => u.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(u => u.Sku)
            .WithMany(s => s.SkuUnits)
            .HasForeignKey(u => new { u.TenantId, u.SkuId })
            .HasPrincipalKey(s => new { s.TenantId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(u => u.Unit)
            .WithMany(un => un.SkuUnits)
            .HasForeignKey(u => new { u.TenantId, u.UnitId })
            .HasPrincipalKey(un => new { un.TenantId, un.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(u => u.UpdatedByUser)
            .WithMany()
            .HasForeignKey(u => new { u.TenantId, u.UpdatedBy })
            .HasPrincipalKey(usr => new { usr.TenantId, usr.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
