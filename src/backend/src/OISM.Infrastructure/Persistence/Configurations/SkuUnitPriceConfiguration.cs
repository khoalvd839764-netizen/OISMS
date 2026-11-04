using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class SkuUnitPriceConfiguration : IEntityTypeConfiguration<SkuUnitPrice>
{
    public void Configure(EntityTypeBuilder<SkuUnitPrice> builder)
    {
        builder.ToTable("sku_unit_prices");

        builder.HasKey(p => new { p.TenantId, p.Id });

        builder.HasIndex(p => new { p.TenantId, p.Id })
            .IsUnique()
            .HasDatabaseName("uq_sku_unit_prices_1");

        builder.HasIndex(p => new { p.TenantId, p.SourceItemId })
            .IsUnique()
            .HasDatabaseName("uq_sku_unit_prices_2");

        builder.HasIndex(p => new { p.TenantId, p.PriceListId, p.SkuId, p.UnitId, p.ValidFrom })
            .IsUnique()
            .HasFilter("cancelled_at IS NULL")
            .HasDatabaseName("ux_sku_unit_prices_active_key");

        builder.Property(p => p.TenantId).IsRequired();

        builder.Property(p => p.PriceListId)
            .HasColumnName("price_list_id")
            .IsRequired();

        builder.Property(p => p.SkuId)
            .HasColumnName("sku_id")
            .IsRequired();

        builder.Property(p => p.UnitId)
            .HasColumnName("unit_id")
            .IsRequired();

        builder.Property(p => p.Price)
            .HasColumnName("price")
            .HasPrecision(20, 2)
            .IsRequired();

        builder.Property(p => p.ValidFrom)
            .HasColumnName("valid_from")
            .IsRequired();

        builder.Property(p => p.SourceItemId)
            .HasColumnName("source_item_id")
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(p => p.CancelledAt)
            .HasColumnName("cancelled_at")
            .IsRequired(false);

        builder.Property(p => p.CancelledBy)
            .HasColumnName("cancelled_by")
            .IsRequired(false);

        builder.Property(p => p.CancelReason)
            .HasColumnName("cancel_reason")
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Ignore(p => p.UpdatedAt);

        // Foreign keys
        builder.HasOne(p => p.Tenant)
            .WithMany()
            .HasForeignKey(p => p.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.PriceList)
            .WithMany(pl => pl.Prices)
            .HasForeignKey(p => new { p.TenantId, p.PriceListId })
            .HasPrincipalKey(pl => new { pl.TenantId, pl.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.SourceItem)
            .WithOne(i => i.SkuUnitPrice)
            .HasForeignKey<SkuUnitPrice>(p => new { p.TenantId, p.SourceItemId })
            .HasPrincipalKey<PriceChangeRequestItem>(i => new { i.TenantId, i.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.SkuUnit)
            .WithMany()
            .HasForeignKey(p => new { p.TenantId, p.SkuId, p.UnitId })
            .HasPrincipalKey(u => new { u.TenantId, u.SkuId, u.UnitId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Sku)
            .WithMany()
            .HasForeignKey(p => new { p.TenantId, p.SkuId })
            .HasPrincipalKey(s => new { s.TenantId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Unit)
            .WithMany()
            .HasForeignKey(p => new { p.TenantId, p.UnitId })
            .HasPrincipalKey(u => new { u.TenantId, u.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.CancelledByUser)
            .WithMany()
            .HasForeignKey(p => new { p.TenantId, p.CancelledBy })
            .HasPrincipalKey(u => new { u.TenantId, u.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
