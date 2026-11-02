using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> builder)
    {
        builder.ToTable("units");

        builder.HasKey(u => new { u.TenantId, u.Id });

        builder.HasIndex(u => new { u.TenantId, u.Id })
            .IsUnique()
            .HasDatabaseName("uq_units_1");

        builder.HasIndex(u => new { u.TenantId, u.Code })
            .IsUnique()
            .HasDatabaseName("uq_units_2");

        builder.Property(u => u.TenantId).IsRequired();

        builder.Property(u => u.Code)
            .HasColumnName("code")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(u => u.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(u => u.Symbol)
            .HasColumnName("symbol")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(u => u.MeasureGroup)
            .HasColumnName("measure_group")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(u => u.DecimalPlaces)
            .HasColumnName("decimal_places")
            .HasDefaultValue((short)3)
            .IsRequired();

        builder.Property(u => u.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

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
    }
}
