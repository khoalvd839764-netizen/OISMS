using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("suppliers");

        builder.HasKey(s => new { s.TenantId, s.Id });

        builder.HasIndex(s => new { s.TenantId, s.Id })
            .IsUnique()
            .HasDatabaseName("uq_suppliers_1");

        builder.Property(s => s.TenantId)
            .IsRequired();

        builder.Property(s => s.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(s => s.Phone)
            .HasMaxLength(32)
            .IsRequired(false);

        builder.Property(s => s.Email)
            .HasMaxLength(254)
            .IsRequired(false);

        builder.Property(s => s.Address)
            .IsRequired(false);

        builder.Property(s => s.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(s => s.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(s => s.UpdatedAt)
            .IsRequired(false);

        builder.HasOne(s => s.Tenant)
            .WithMany()
            .HasForeignKey(s => s.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(s => s.Code);
        builder.Ignore(s => s.ContactName);
    }
}
