using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class PriceChangeRequestItemConfiguration : IEntityTypeConfiguration<PriceChangeRequestItem>
{
    public void Configure(EntityTypeBuilder<PriceChangeRequestItem> builder)
    {
        builder.ToTable("price_change_request_items");

        builder.HasKey(i => new { i.TenantId, i.Id });

        builder.HasIndex(i => new { i.TenantId, i.Id })
            .IsUnique()
            .HasDatabaseName("uq_price_change_request_items_1");

        builder.HasIndex(i => new { i.TenantId, i.RequestId, i.PriceListId, i.SkuId, i.UnitId })
            .IsUnique()
            .HasDatabaseName("uq_price_change_request_items_2");

        builder.Property(i => i.TenantId).IsRequired();

        builder.Property(i => i.RequestId)
            .HasColumnName("request_id")
            .IsRequired();

        builder.Property(i => i.PriceListId)
            .HasColumnName("price_list_id")
            .IsRequired();

        builder.Property(i => i.SkuId)
            .HasColumnName("sku_id")
            .IsRequired();

        builder.Property(i => i.UnitId)
            .HasColumnName("unit_id")
            .IsRequired();

        builder.Property(i => i.NewPrice)
            .HasColumnName("new_price")
            .HasPrecision(20, 2)
            .IsRequired();

        builder.Property(i => i.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Ignore(i => i.UpdatedAt);

        // Foreign keys
        builder.HasOne(i => i.Tenant)
            .WithMany()
            .HasForeignKey(i => i.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Request)
            .WithMany(r => r.Items)
            .HasForeignKey(i => new { i.TenantId, i.RequestId })
            .HasPrincipalKey(r => new { r.TenantId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.PriceList)
            .WithMany(p => p.Items)
            .HasForeignKey(i => new { i.TenantId, i.PriceListId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.SkuUnit)
            .WithMany()
            .HasForeignKey(i => new { i.TenantId, i.SkuId, i.UnitId })
            .HasPrincipalKey(u => new { u.TenantId, u.SkuId, u.UnitId })
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
