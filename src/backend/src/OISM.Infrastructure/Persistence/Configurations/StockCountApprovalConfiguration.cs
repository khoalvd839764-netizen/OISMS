using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class StockCountApprovalConfiguration : IEntityTypeConfiguration<StockCountApproval>
{
    public void Configure(EntityTypeBuilder<StockCountApproval> builder)
    {
        builder.ToTable("stock_count_approvals");

        builder.HasKey(c => new { c.TenantId, c.Id });

        builder.HasIndex(c => new { c.TenantId, c.Id })
            .IsUnique()
            .HasDatabaseName("uq_stock_count_approvals_1");

        builder.HasIndex(c => new { c.TenantId, c.StockCountId, c.CreatedAt })
            .HasDatabaseName("ix_stock_count_approvals_count");

        builder.Property(c => c.TenantId).IsRequired();

        builder.Property(c => c.StockCountId)
            .HasColumnName("stock_count_id")
            .IsRequired();

        builder.Property(c => c.Action)
            .HasColumnName("action")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(c => c.ActorUserId)
            .HasColumnName("actor_user_id")
            .IsRequired();

        builder.Property(c => c.Reason)
            .HasColumnName("reason")
            .IsRequired(false);

        builder.Property(c => c.Differences)
            .HasColumnName("differences")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);

        // Foreign keys
        builder.HasOne(c => c.Tenant)
            .WithMany()
            .HasForeignKey(c => c.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.StockCount)
            .WithMany(s => s.Approvals)
            .HasForeignKey(c => new { c.TenantId, c.StockCountId })
            .HasConstraintName("fk_stock_count_approvals_stock_count")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.ActorUser)
            .WithMany()
            .HasForeignKey(c => new { c.TenantId, c.ActorUserId })
            .HasConstraintName("fk_stock_count_approvals_actor")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
