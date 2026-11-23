using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class GoodsIssueConfiguration : IEntityTypeConfiguration<GoodsIssue>
{
    public void Configure(EntityTypeBuilder<GoodsIssue> builder)
    {
        builder.ToTable("goods_issues");

        builder.HasKey(g => new { g.TenantId, g.Id });

        builder.HasIndex(g => new { g.TenantId, g.Id })
            .IsUnique()
            .HasDatabaseName("uq_goods_issues_1");

        builder.HasIndex(g => new { g.TenantId, g.Number })
            .IsUnique()
            .HasDatabaseName("uq_goods_issues_2");

        builder.HasIndex(g => new { g.TenantId, g.OrderId, g.WarehouseId })
            .IsUnique()
            .HasDatabaseName("uq_goods_issues_3");

        builder.HasIndex(g => new { g.TenantId, g.BranchId, g.IssuedAt, g.Id })
            .HasDatabaseName("ix_goods_issues_branch_time");

        builder.Property(g => g.TenantId).IsRequired();

        builder.Property(g => g.Number)
            .HasColumnName("number")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(g => g.OrderId)
            .HasColumnName("order_id")
            .IsRequired();

        builder.Property(g => g.SalesInvoiceId)
            .HasColumnName("sales_invoice_id")
            .IsRequired();

        builder.Property(g => g.BranchId)
            .HasColumnName("branch_id")
            .IsRequired();

        builder.Property(g => g.WarehouseId)
            .HasColumnName("warehouse_id")
            .IsRequired();

        builder.Property(g => g.Channel)
            .HasColumnName("channel")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(g => g.OrderNumberSnapshot)
            .HasColumnName("order_number_snapshot")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(g => g.SalesInvoiceNumberSnapshot)
            .HasColumnName("sales_invoice_number_snapshot")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(g => g.BranchNameSnapshot)
            .HasColumnName("branch_name_snapshot")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(g => g.WarehouseNameSnapshot)
            .HasColumnName("warehouse_name_snapshot")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(g => g.CustomerNameSnapshot)
            .HasColumnName("customer_name_snapshot")
            .HasMaxLength(200)
            .IsRequired(false);

        builder.Property(g => g.IssuedBy)
            .HasColumnName("issued_by")
            .IsRequired(false);

        builder.Property(g => g.IssuedByNameSnapshot)
            .HasColumnName("issued_by_name_snapshot")
            .HasMaxLength(150)
            .IsRequired(false);

        builder.Property(g => g.IssuedAt)
            .HasColumnName("issued_at")
            .IsRequired();

        builder.Property(g => g.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(g => g.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);

        // Foreign keys
        builder.HasOne(g => g.Tenant)
            .WithMany()
            .HasForeignKey(g => g.TenantId)
            .HasConstraintName("fk_goods_issues_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(g => g.Order)
            .WithMany()
            .HasForeignKey(g => new { g.TenantId, g.OrderId })
            .HasPrincipalKey(o => new { o.TenantId, o.Id })
            .HasConstraintName("fk_goods_issues_order_branch")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(g => g.SalesInvoice)
            .WithMany()
            .HasForeignKey(g => new { g.TenantId, g.SalesInvoiceId })
            .HasPrincipalKey(s => new { s.TenantId, s.Id })
            .HasConstraintName("fk_goods_issues_sales_invoice")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(g => g.Branch)
            .WithMany()
            .HasForeignKey(g => new { g.TenantId, g.BranchId })
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .HasConstraintName("fk_goods_issues_branch_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(g => g.Warehouse)
            .WithMany()
            .HasForeignKey(g => new { g.TenantId, g.WarehouseId })
            .HasPrincipalKey(w => new { w.TenantId, w.Id })
            .HasConstraintName("fk_goods_issues_warehouse_branch")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(g => g.IssuedByUser)
            .WithMany()
            .HasForeignKey(g => new { g.TenantId, g.IssuedBy })
            .HasPrincipalKey(u => new { u.TenantId, u.Id })
            .HasConstraintName("fk_goods_issues_issued_by")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
