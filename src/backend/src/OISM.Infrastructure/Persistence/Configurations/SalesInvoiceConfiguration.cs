using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class SalesInvoiceConfiguration : IEntityTypeConfiguration<SalesInvoice>
{
    public void Configure(EntityTypeBuilder<SalesInvoice> builder)
    {
        builder.ToTable("sales_invoices");

        builder.HasKey(s => new { s.TenantId, s.Id });

        builder.HasIndex(s => new { s.TenantId, s.Id })
            .IsUnique()
            .HasDatabaseName("uq_sales_invoices_1");

        builder.HasIndex(s => new { s.TenantId, s.Number })
            .IsUnique()
            .HasDatabaseName("uq_sales_invoices_2");

        builder.HasIndex(s => new { s.TenantId, s.OrderId })
            .IsUnique()
            .HasDatabaseName("uq_sales_invoices_3");

        builder.HasIndex(s => new { s.TenantId, s.IssuedAt, s.Id })
            .HasDatabaseName("ix_sales_invoices_time");

        builder.Property(s => s.TenantId).IsRequired();

        builder.Property(s => s.Number)
            .HasColumnName("number")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(s => s.OrderId)
            .HasColumnName("order_id")
            .IsRequired();

        builder.Property(s => s.BranchId)
            .HasColumnName("branch_id")
            .IsRequired();

        builder.Property(s => s.Channel)
            .HasColumnName("channel")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(s => s.BranchNameSnapshot)
            .HasColumnName("branch_name_snapshot")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(s => s.BranchAddressSnapshot)
            .HasColumnName("branch_address_snapshot")
            .IsRequired(false);

        builder.Property(s => s.CustomerNameSnapshot)
            .HasColumnName("customer_name_snapshot")
            .HasMaxLength(200)
            .IsRequired(false);

        builder.Property(s => s.CustomerPhoneSnapshot)
            .HasColumnName("customer_phone_snapshot")
            .HasMaxLength(32)
            .IsRequired(false);

        builder.Property(s => s.IssuedBy)
            .HasColumnName("issued_by")
            .IsRequired(false);

        builder.Property(s => s.IssuedByNameSnapshot)
            .HasColumnName("issued_by_name_snapshot")
            .HasMaxLength(150)
            .IsRequired(false);

        builder.Property(s => s.TotalAmount)
            .HasColumnName("total_amount")
            .HasPrecision(20, 2)
            .IsRequired();

        builder.Property(s => s.PaymentMethodSnapshot)
            .HasColumnName("payment_method_snapshot")
            .HasMaxLength(20)
            .IsRequired(false);

        builder.Property(s => s.IssuedAt)
            .HasColumnName("issued_at")
            .IsRequired();

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(s => s.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);

        // Foreign keys
        builder.HasOne(s => s.Tenant)
            .WithMany()
            .HasForeignKey(s => s.TenantId)
            .HasConstraintName("fk_sales_invoices_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Order)
            .WithOne(o => o.SalesInvoice)
            .HasForeignKey<SalesInvoice>(s => new { s.TenantId, s.OrderId, s.BranchId })
            .HasPrincipalKey<Order>(o => new { o.TenantId, o.Id, o.BranchId })
            .HasConstraintName("fk_sales_invoices_order_branch")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Branch)
            .WithMany()
            .HasForeignKey(s => new { s.TenantId, s.BranchId })
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .HasConstraintName("fk_sales_invoices_branch_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.IssuedByUser)
            .WithMany()
            .HasForeignKey(s => new { s.TenantId, s.IssuedBy })
            .HasPrincipalKey(u => new { u.TenantId, u.Id })
            .HasConstraintName("fk_sales_invoices_issued_by")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
