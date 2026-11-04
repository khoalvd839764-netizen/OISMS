using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class SkuUnitFactorHistoryConfiguration : IEntityTypeConfiguration<SkuUnitFactorHistory>
{
    public void Configure(EntityTypeBuilder<SkuUnitFactorHistory> builder)
    {
        builder.ToTable("sku_unit_factor_history");

        builder.HasKey(h => new { h.TenantId, h.Id });

        builder.HasIndex(h => new { h.TenantId, h.Id })
            .IsUnique()
            .HasDatabaseName("uq_sku_unit_factor_history_1");

        builder.Property(h => h.TenantId).IsRequired();

        builder.Property(h => h.SkuUnitId)
            .HasColumnName("sku_unit_id")
            .IsRequired();

        builder.Property(h => h.OldFactor)
            .HasColumnName("old_factor")
            .HasPrecision(18, 6)
            .IsRequired(false);

        builder.Property(h => h.NewFactor)
            .HasColumnName("new_factor")
            .HasPrecision(18, 6)
            .IsRequired();

        builder.Property(h => h.OldVersion)
            .HasColumnName("old_version")
            .IsRequired(false);

        builder.Property(h => h.NewVersion)
            .HasColumnName("new_version")
            .IsRequired();

        builder.Property(h => h.ChangedBy)
            .HasColumnName("changed_by")
            .IsRequired(false);

        builder.Property(h => h.ChangedAt)
            .HasColumnName("changed_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Ignore(h => h.CreatedAt);
        builder.Ignore(h => h.UpdatedAt);

        // Foreign keys
        builder.HasOne(h => h.Tenant)
            .WithMany()
            .HasForeignKey(h => h.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(h => h.SkuUnit)
            .WithMany(u => u.FactorHistories)
            .HasForeignKey(h => new { h.TenantId, h.SkuUnitId })
            .HasPrincipalKey(u => new { u.TenantId, u.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(h => h.ChangedByUser)
            .WithMany()
            .HasForeignKey(h => new { h.TenantId, h.ChangedBy })
            .HasPrincipalKey(u => new { u.TenantId, u.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
