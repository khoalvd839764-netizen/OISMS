using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        builder.ToTable("brands");

        builder.HasKey(b => new { b.TenantId, b.Id });

        builder.HasIndex(b => new { b.TenantId, b.Id })
            .IsUnique()
            .HasDatabaseName("uq_brands_1");

        builder.Property(b => b.TenantId)
            .IsRequired();

        builder.Property(b => b.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(b => b.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(b => b.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(b => b.UpdatedAt)
            .IsRequired(false);

        builder.HasOne(b => b.Tenant)
            .WithMany()
            .HasForeignKey(b => b.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(b => b.Description);
    }
}
