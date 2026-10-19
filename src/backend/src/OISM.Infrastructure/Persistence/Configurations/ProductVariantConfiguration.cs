using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("skus");

        builder.HasKey(v => new { v.TenantId, v.Id });

        builder.HasIndex(v => new { v.TenantId, v.Id })
            .IsUnique()
            .HasDatabaseName("uq_skus_1");

        builder.HasIndex(v => new { v.TenantId, v.SKU })
            .IsUnique()
            .HasDatabaseName("uq_skus_2");

        builder.HasIndex(v => new { v.TenantId, v.Barcode })
            .IsUnique()
            .HasDatabaseName("uq_skus_3");

        builder.HasIndex(v => new { v.TenantId, v.ProductId })
            .HasDatabaseName("ix_skus_product");

        builder.Property(v => v.TenantId)
            .IsRequired();

        builder.Property(v => v.ProductId)
            .IsRequired();

        builder.Property(v => v.SKU)
            .HasColumnName("code")
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(v => v.AttributesJson)
            .HasColumnName("attributes")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();

        builder.Property(v => v.Unit)
            .HasColumnName("unit")
            .HasMaxLength(30)
            .HasDefaultValue("piece")
            .IsRequired();

        builder.Property(v => v.BarcodeType)
            .HasColumnName("barcode_type")
            .HasMaxLength(20)
            .IsRequired(false);

        builder.Property(v => v.Barcode)
            .HasColumnName("barcode")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(v => v.RetailPrice)
            .HasColumnName("retail_price")
            .HasPrecision(20, 2)
            .IsRequired();

        builder.Property(v => v.WholesalePrice)
            .HasColumnName("wholesale_price")
            .HasPrecision(20, 2)
            .IsRequired();

        builder.Property(v => v.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(v => v.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(v => v.UpdatedAt)
            .IsRequired(false);

        // Foreign keys
        builder.HasOne(v => v.Tenant)
            .WithMany()
            .HasForeignKey(v => v.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(v => v.Product)
            .WithMany(p => p.Variants)
            .HasForeignKey(v => new { v.TenantId, v.ProductId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Ignore unmapped/virtual properties
        builder.Ignore(v => v.Code);
        builder.Ignore(v => v.CostPrice);
        builder.Ignore(v => v.VariantName);
        builder.Ignore(v => v.Color);
        builder.Ignore(v => v.Size);
    }
}
