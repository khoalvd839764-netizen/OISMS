using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class UserBranchConfiguration : IEntityTypeConfiguration<UserBranch>
{
    public void Configure(EntityTypeBuilder<UserBranch> builder)
    {
        builder.ToTable("user_branches");

        builder.HasKey(ub => new { ub.TenantId, ub.UserId, ub.BranchId })
            .HasName("pk_user_branches");

        builder.HasIndex(ub => new { ub.TenantId, ub.BranchId })
            .HasDatabaseName("ix_user_branches_branch");

        builder.Property(ub => ub.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(ub => ub.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(ub => ub.BranchId)
            .HasColumnName("branch_id")
            .IsRequired();

        builder.Property(ub => ub.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasOne(ub => ub.Tenant)
            .WithMany()
            .HasForeignKey(ub => ub.TenantId)
            .HasConstraintName("fk_user_branches_tenant")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(ub => ub.User)
            .WithMany()
            .HasForeignKey(ub => new { ub.TenantId, ub.UserId })
            .HasPrincipalKey(u => new { u.TenantId, u.Id })
            .HasConstraintName("fk_user_branches_user")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ub => ub.Branch)
            .WithMany()
            .HasForeignKey(ub => new { ub.TenantId, ub.BranchId })
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .HasConstraintName("fk_user_branches_branch")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
