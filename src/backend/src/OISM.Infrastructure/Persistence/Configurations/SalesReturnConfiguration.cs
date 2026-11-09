using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class SalesReturnConfiguration : IEntityTypeConfiguration<SalesReturn>
{
    public void Configure(EntityTypeBuilder<SalesReturn> builder)
    {
        builder.ToTable("sales_returns");

        builder.HasKey(r => new { r.TenantId, r.Id });

        builder.HasIndex(r => new { r.TenantId, r.Id })
            .IsUnique()
            .HasDatabaseName("uq_sales_returns_1");

        builder.HasIndex(r => new { r.TenantId, r.Number })
            .IsUnique()
            .HasDatabaseName("uq_sales_returns_2");

        builder.HasIndex(r => new { r.TenantId, r.Id, r.OrderId })
            .IsUnique()
            .HasDatabaseName("uq_sales_returns_3");

        builder.HasIndex(r => new { r.TenantId, r.RequestKey })
            .IsUnique()
            .HasDatabaseName("uq_sales_returns_4");

        builder.HasIndex(r => new { r.TenantId, r.Status, r.CreatedAt, r.Id })
            .HasDatabaseName("ix_sales_returns_status_time");

        builder.Property(r => r.TenantId).IsRequired();

        builder.Property(r => r.RequestKey)
            .HasColumnName("request_key")
            .IsRequired();

        builder.Property(r => r.Number)
            .HasColumnName("number")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.OrderId)
            .HasColumnName("order_id")
            .IsRequired();

        builder.Property(r => r.BranchId)
            .HasColumnName("branch_id")
            .IsRequired();

        builder.Property(r => r.WarehouseId)
            .HasColumnName("warehouse_id")
            .IsRequired();

        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasDefaultValue("Draft")
            .IsRequired();

        builder.Property(r => r.Reason)
            .HasColumnName("reason")
            .IsRequired();

        builder.Property(r => r.Note)
            .HasColumnName("note")
            .IsRequired(false);

        builder.Property(r => r.TotalRefundAmount)
            .HasColumnName("total_refund_amount")
            .HasPrecision(20, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(r => r.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired();

        builder.Property(r => r.ConfirmedBy)
            .HasColumnName("confirmed_by")
            .IsRequired(false);

        builder.Property(r => r.ConfirmedAt)
            .HasColumnName("confirmed_at")
            .IsRequired(false);

        builder.Property(r => r.RefundMethod)
            .HasColumnName("refund_method")
            .HasMaxLength(20)
            .IsRequired(false);

        builder.Property(r => r.RefundedAt)
            .HasColumnName("refunded_at")
            .IsRequired(false);

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);

        // Foreign keys
        builder.HasOne(r => r.Tenant)
            .WithMany()
            .HasForeignKey(r => r.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Order)
            .WithMany()
            .HasForeignKey(r => new { r.TenantId, r.OrderId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Branch)
            .WithMany()
            .HasForeignKey(r => new { r.TenantId, r.BranchId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Warehouse)
            .WithMany()
            .HasForeignKey(r => new { r.TenantId, r.WarehouseId, r.BranchId })
            .HasPrincipalKey(w => new { w.TenantId, w.Id, w.BranchId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Creator)
            .WithMany()
            .HasForeignKey(r => new { r.TenantId, r.CreatedBy })
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Confirmer)
            .WithMany()
            .HasForeignKey(r => new { r.TenantId, r.ConfirmedBy })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Items)
            .WithOne(i => i.SalesReturn)
            .HasForeignKey(i => new { i.TenantId, i.SalesReturnId, i.OrderId })
            .HasPrincipalKey(r => new { r.TenantId, r.Id, r.OrderId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
