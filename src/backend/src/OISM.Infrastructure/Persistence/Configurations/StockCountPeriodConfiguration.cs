using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class StockCountPeriodConfiguration : IEntityTypeConfiguration<StockCountPeriod>
{
    public void Configure(EntityTypeBuilder<StockCountPeriod> builder)
    {
        builder.ToTable("stock_count_periods");

        builder.HasKey(c => new { c.TenantId, c.Id });

        builder.HasIndex(c => new { c.TenantId, c.Id })
            .IsUnique()
            .HasDatabaseName("uq_stock_count_periods_1");

        builder.HasIndex(c => new { c.TenantId, c.WarehouseId, c.PeriodKey })
            .IsUnique()
            .HasDatabaseName("uq_stock_count_periods_2");

        builder.HasIndex(c => new { c.TenantId, c.Status, c.DueAt, c.Id })
            .HasDatabaseName("ix_stock_count_periods_status_due");

        builder.Property(c => c.TenantId).IsRequired();

        builder.Property(c => c.ScheduleId)
            .HasColumnName("schedule_id")
            .IsRequired();

        builder.Property(c => c.BranchId)
            .HasColumnName("branch_id")
            .IsRequired();

        builder.Property(c => c.WarehouseId)
            .HasColumnName("warehouse_id")
            .IsRequired();

        builder.Property(c => c.PeriodType)
            .HasColumnName("period_type")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(c => c.PeriodKey)
            .HasColumnName("period_key")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(c => c.ScheduledAt)
            .HasColumnName("scheduled_at")
            .IsRequired();

        builder.Property(c => c.DueAt)
            .HasColumnName("due_at")
            .IsRequired();

        builder.Property(c => c.AssigneeUserId)
            .HasColumnName("assignee_user_id")
            .IsRequired();

        builder.Property(c => c.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasDefaultValue("Planned")
            .IsRequired();

        builder.Property(c => c.CompletedAt)
            .HasColumnName("completed_at")
            .IsRequired(false);

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

        builder.HasOne(c => c.Schedule)
            .WithMany(s => s.Periods)
            .HasForeignKey(c => new { c.TenantId, c.ScheduleId })
            .HasConstraintName("fk_stock_count_periods_schedule")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Branch)
            .WithMany()
            .HasForeignKey(c => new { c.TenantId, c.BranchId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Warehouse)
            .WithMany()
            .HasForeignKey(c => new { c.TenantId, c.WarehouseId, c.BranchId })
            .HasPrincipalKey(w => new { w.TenantId, w.Id, w.BranchId })
            .HasConstraintName("fk_stock_count_periods_warehouse_branch")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.AssigneeUser)
            .WithMany()
            .HasForeignKey(c => new { c.TenantId, c.AssigneeUserId })
            .HasConstraintName("fk_stock_count_periods_assignee")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.StockCounts)
            .WithOne(s => s.Period)
            .HasForeignKey(s => new { s.TenantId, s.PeriodId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
