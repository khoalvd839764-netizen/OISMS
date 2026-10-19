using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(r => new { r.TenantId, r.Id });

        builder.HasIndex(r => new { r.TenantId, r.Id })
            .IsUnique()
            .HasDatabaseName("uq_refresh_tokens_1");

        builder.HasIndex(r => r.TokenHash)
            .IsUnique()
            .HasDatabaseName("uq_refresh_tokens_2");

        builder.HasIndex(r => new { r.TenantId, r.UserId })
            .HasDatabaseName("ix_refresh_tokens_user");

        builder.Property(r => r.TenantId)
            .IsRequired();

        builder.Property(r => r.UserId)
            .IsRequired();

        builder.Property(r => r.TokenHash)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(r => r.ExpiresAt)
            .IsRequired();

        builder.Property(r => r.RevokedAt)
            .IsRequired(false);

        builder.Property(r => r.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        // Foreign keys: (tenant_id, user_id) -> users(tenant_id, id)
        builder.HasOne(r => r.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(r => new { r.TenantId, r.UserId })
            .HasPrincipalKey(u => new { u.TenantId, u.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Ignore unmapped fields
        builder.Ignore(r => r.Token);
        builder.Ignore(r => r.ReplacedByToken);
        builder.Ignore(r => r.IsRevoked);
        builder.Ignore(r => r.UpdatedAt);
    }
}
