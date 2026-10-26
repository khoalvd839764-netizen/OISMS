using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class ShopIntegrationConfiguration : IEntityTypeConfiguration<ShopIntegration>
{
    public void Configure(EntityTypeBuilder<ShopIntegration> builder)
    {
        builder.ToTable("shop_integrations");

        builder.HasKey(si => new { si.TenantId, si.Id });

        builder.HasIndex(si => new { si.TenantId, si.Id })
            .IsUnique()
            .HasDatabaseName("uq_shop_integrations_1");

        builder.HasIndex(si => new { si.TenantId, si.Channel, si.ExternalShopId })
            .IsUnique()
            .HasDatabaseName("uq_shop_integrations_2");

        builder.HasIndex(si => si.SecretHash)
            .IsUnique()
            .HasDatabaseName("uq_shop_integrations_secret_hash");

        builder.Property(si => si.TenantId).IsRequired();

        builder.Property(si => si.Channel)
            .HasColumnName("channel")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(si => si.ExternalShopId)
            .HasColumnName("external_shop_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(si => si.BranchId)
            .HasColumnName("branch_id")
            .IsRequired();

        builder.Property(si => si.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(si => si.SecretHash)
            .HasColumnName("secret_hash")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(si => si.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(si => si.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);

        // Foreign keys
        builder.HasOne(si => si.Tenant)
            .WithMany()
            .HasForeignKey(si => si.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(si => si.Branch)
            .WithMany()
            .HasForeignKey(si => new { si.TenantId, si.BranchId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
