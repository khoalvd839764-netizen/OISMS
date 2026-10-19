using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => new { u.TenantId, u.Id });

        // Unique constraints per schema v0.5
        builder.HasIndex(u => new { u.TenantId, u.Id })
            .IsUnique()
            .HasDatabaseName("uq_users_1");

        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasDatabaseName("uq_users_2");

        builder.HasIndex(u => u.Phone)
            .IsUnique()
            .HasDatabaseName("uq_users_3");

        builder.Property(u => u.TenantId)
            .IsRequired();

        builder.Property(u => u.Email)
            .HasMaxLength(254)
            .IsRequired(false);

        builder.Property(u => u.Phone)
            .HasMaxLength(16)
            .IsRequired(false);

        builder.Property(u => u.DisplayName)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(u => u.PasswordHash)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(u => u.Role)
            .HasMaxLength(20)
            .HasDefaultValue("Staff")
            .IsRequired();

        builder.Property(u => u.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(u => u.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(u => u.UpdatedAt)
            .IsRequired(false);

        // Foreign keys
        builder.HasOne(u => u.Tenant)
            .WithMany(t => t.Users)
            .HasForeignKey(u => u.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Ignore unmapped/virtual properties
        builder.Ignore(u => u.FullName);
        builder.Ignore(u => u.PhoneNumber);
        builder.Ignore(u => u.UserRoles);
    }
}
