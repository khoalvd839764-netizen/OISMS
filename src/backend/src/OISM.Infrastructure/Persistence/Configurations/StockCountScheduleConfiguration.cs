using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class StockCountScheduleConfiguration : IEntityTypeConfiguration<StockCountSchedule>
{
    public void Configure(EntityTypeBuilder<StockCountSchedule> builder)
    {
        builder.ToTable("stock_count_schedules");

        builder.HasKey(c => new { c.TenantId, c.Id });

        builder.HasIndex(c => new { c.TenantId, c.Id })
            .IsUnique()
            .HasDatabaseName("uq_stock_count_schedules_1");

        builder.HasIndex(c => new { c.TenantId, c.WarehouseId, c.PeriodType })
            .IsUnique()
            .HasDatabaseName("uq_stock_count_schedules_2");

        builder.Property(c => c.TenantId).IsRequired();

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

        builder.Property(c => c.DayOfMonth)
            .HasColumnName("day_of_month")
            .IsRequired(false);

        builder.Property(c => c.LocalTime)
            .HasColumnName("local_time")
            .HasColumnType("time")
            .IsRequired();

        builder.Property(c => c.DueDays)
            .HasColumnName("due_days")
            .IsRequired();

        builder.Property(c => c.AssigneeUserId)
            .HasColumnName("assignee_user_id")
            .IsRequired();

        builder.Property(c => c.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(c => c.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired();

        builder.Property(c => c.UpdatedBy)
            .HasColumnName("updated_by")
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

        builder.HasOne(c => c.Branch)
            .WithMany()
            .HasForeignKey(c => new { c.TenantId, c.BranchId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Warehouse)
            .WithMany()
            .HasForeignKey(c => new { c.TenantId, c.WarehouseId, c.BranchId })
            .HasPrincipalKey(w => new { w.TenantId, w.Id, w.BranchId })
            .HasConstraintName("fk_stock_count_schedules_warehouse_branch")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.AssigneeUser)
            .WithMany()
            .HasForeignKey(c => new { c.TenantId, c.AssigneeUserId })
            .HasConstraintName("fk_stock_count_schedules_assignee")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.CreatedByUser)
            .WithMany()
            .HasForeignKey(c => new { c.TenantId, c.CreatedBy })
            .HasConstraintName("fk_stock_count_schedules_created_by")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.UpdatedByUser)
            .WithMany()
            .HasForeignKey(c => new { c.TenantId, c.UpdatedBy })
            .HasConstraintName("fk_stock_count_schedules_updated_by")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Periods)
            .WithOne(p => p.Schedule)
            .HasForeignKey(p => new { p.TenantId, p.ScheduleId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
