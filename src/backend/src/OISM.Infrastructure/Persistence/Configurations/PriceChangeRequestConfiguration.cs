using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class PriceChangeRequestConfiguration : IEntityTypeConfiguration<PriceChangeRequest>
{
    public void Configure(EntityTypeBuilder<PriceChangeRequest> builder)
    {
        builder.ToTable("price_change_requests");

        builder.HasKey(r => new { r.TenantId, r.Id });

        builder.HasIndex(r => new { r.TenantId, r.Id })
            .IsUnique()
            .HasDatabaseName("uq_price_change_requests_1");

        builder.HasIndex(r => new { r.TenantId, r.RequestKey })
            .IsUnique()
            .HasDatabaseName("uq_price_change_requests_2");

        builder.Property(r => r.TenantId).IsRequired();

        builder.Property(r => r.RequestKey)
            .HasColumnName("request_key")
            .IsRequired();

        builder.Property(r => r.Kind)
            .HasColumnName("kind")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.Reason)
            .HasColumnName("reason")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(r => r.RequestedValidFrom)
            .HasColumnName("requested_valid_from")
            .IsRequired(false);

        builder.Property(r => r.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired(false);

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(r => r.ReviewedBy)
            .HasColumnName("reviewed_by")
            .IsRequired(false);

        builder.Property(r => r.ReviewedAt)
            .HasColumnName("reviewed_at")
            .IsRequired(false);

        builder.Property(r => r.ReviewNote)
            .HasColumnName("review_note")
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Ignore(r => r.UpdatedAt);

        // Foreign keys
        builder.HasOne(r => r.Tenant)
            .WithMany()
            .HasForeignKey(r => r.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.CreatedByUser)
            .WithMany()
            .HasForeignKey(r => new { r.TenantId, r.CreatedBy })
            .HasPrincipalKey(u => new { u.TenantId, u.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ReviewedByUser)
            .WithMany()
            .HasForeignKey(r => new { r.TenantId, r.ReviewedBy })
            .HasPrincipalKey(u => new { u.TenantId, u.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
