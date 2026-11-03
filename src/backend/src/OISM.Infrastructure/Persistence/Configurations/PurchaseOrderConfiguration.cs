using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("purchase_orders");

        builder.HasKey(p => new { p.TenantId, p.Id });

        builder.HasIndex(p => new { p.TenantId, p.Id })
            .IsUnique()
            .HasDatabaseName("uq_purchase_orders_1");

        builder.HasIndex(p => new { p.TenantId, p.Number })
            .IsUnique()
            .HasDatabaseName("uq_purchase_orders_2");

        builder.HasIndex(p => new { p.TenantId, p.RequestKey })
            .IsUnique()
            .HasDatabaseName("uq_purchase_orders_3");

        builder.HasIndex(p => new { p.TenantId, p.Status, p.CreatedAt, p.Id })
            .HasDatabaseName("ix_purchase_orders_status_time");

        builder.Property(p => p.TenantId).IsRequired();

        builder.Property(p => p.Number)
            .HasColumnName("number")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.RequestKey)
            .HasColumnName("request_key")
            .IsRequired();

        builder.Property(p => p.SupplierId)
            .HasColumnName("supplier_id")
            .IsRequired();

        builder.Property(p => p.BranchId)
            .HasColumnName("branch_id")
            .IsRequired();

        builder.Property(p => p.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.Note)
            .HasColumnName("note")
            .HasMaxLength(1000)
            .IsRequired(false);

        builder.Property(p => p.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired();

        builder.Property(p => p.ApprovedBy)
            .HasColumnName("approved_by")
            .IsRequired(false);

        builder.Property(p => p.ApprovedAt)
            .HasColumnName("approved_at")
            .IsRequired(false);

        builder.Property(p => p.ClosedBy)
            .HasColumnName("closed_by")
            .IsRequired(false);

        builder.Property(p => p.ClosedAt)
            .HasColumnName("closed_at")
            .IsRequired(false);

        builder.Property(p => p.CloseReason)
            .HasColumnName("close_reason")
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(p => p.CancelledBy)
            .HasColumnName("cancelled_by")
            .IsRequired(false);

        builder.Property(p => p.CancelledAt)
            .HasColumnName("cancelled_at")
            .IsRequired(false);

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);

        // Foreign keys
        builder.HasOne(p => p.Tenant)
            .WithMany()
            .HasForeignKey(p => p.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Supplier)
            .WithMany()
            .HasForeignKey(p => new { p.TenantId, p.SupplierId })
            .HasPrincipalKey(s => new { s.TenantId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Branch)
            .WithMany()
            .HasForeignKey(p => new { p.TenantId, p.BranchId })
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.CreatedByUser)
            .WithMany()
            .HasForeignKey(p => new { p.TenantId, p.CreatedBy })
            .HasPrincipalKey(u => new { u.TenantId, u.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.ApprovedByUser)
            .WithMany()
            .HasForeignKey(p => new { p.TenantId, p.ApprovedBy })
            .HasPrincipalKey(u => new { u.TenantId, u.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.ClosedByUser)
            .WithMany()
            .HasForeignKey(p => new { p.TenantId, p.ClosedBy })
            .HasPrincipalKey(u => new { u.TenantId, u.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.CancelledByUser)
            .WithMany()
            .HasForeignKey(p => new { p.TenantId, p.CancelledBy })
            .HasPrincipalKey(u => new { u.TenantId, u.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Items)
            .WithOne(i => i.PurchaseOrder)
            .HasForeignKey(i => new { i.TenantId, i.PurchaseOrderId })
            .HasPrincipalKey(p => new { p.TenantId, p.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
