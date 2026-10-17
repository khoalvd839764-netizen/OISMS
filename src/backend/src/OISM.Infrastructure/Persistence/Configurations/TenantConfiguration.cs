using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(t => t.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(t => t.UpdatedAt)
            .IsRequired(false);

        builder.Ignore(t => t.Code);
    }
}
