using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");

        builder.HasKey(o => new { o.TenantId, o.Id });

        builder.HasIndex(o => new { o.TenantId, o.Id })
            .IsUnique()
            .HasDatabaseName("uq_orders_1");

        builder.HasIndex(o => new { o.TenantId, o.Number })
            .IsUnique()
            .HasDatabaseName("uq_orders_2");

        builder.HasIndex(o => new { o.TenantId, o.RequestKey })
            .IsUnique()
            .HasDatabaseName("uq_orders_3");

        builder.Property(o => o.TenantId).IsRequired();

        builder.Property(o => o.Number)
            .HasColumnName("number")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(o => o.BranchId)
            .HasColumnName("branch_id")
            .IsRequired();

        builder.Property(o => o.Channel)
            .HasColumnName("channel")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(o => o.ExternalShopId)
            .HasColumnName("external_shop_id")
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(o => o.ExternalOrderId)
            .HasColumnName("external_order_id")
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(o => o.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasDefaultValue("Draft")
            .IsRequired();

        builder.Property(o => o.CustomerName)
            .HasColumnName("customer_name")
            .HasMaxLength(200)
            .IsRequired(false);

        builder.Property(o => o.CustomerPhone)
            .HasColumnName("customer_phone")
            .HasMaxLength(32)
            .IsRequired(false);

        builder.Property(o => o.ShippingAddress)
            .HasColumnName("shipping_address")
            .IsRequired(false);

        builder.Property(o => o.TotalAmount)
            .HasColumnName("total_amount")
            .HasPrecision(20, 2)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(o => o.RequestKey)
            .HasColumnName("request_key")
            .IsRequired();

        builder.Property(o => o.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired(false);

        builder.Property(o => o.ConfirmedAt)
            .HasColumnName("confirmed_at")
            .IsRequired(false);

        builder.Property(o => o.CompletedAt)
            .HasColumnName("completed_at")
            .IsRequired(false);

        builder.Property(o => o.CancelledAt)
            .HasColumnName("cancelled_at")
            .IsRequired(false);

        builder.Property(o => o.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(o => o.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);

        // Foreign keys
        builder.HasOne(o => o.Tenant)
            .WithMany()
            .HasForeignKey(o => o.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Branch)
            .WithMany()
            .HasForeignKey(o => new { o.TenantId, o.BranchId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.CreatedByUser)
            .WithMany()
            .HasForeignKey(o => new { o.TenantId, o.CreatedBy })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(o => o.Items)
            .WithOne(i => i.Order)
            .HasForeignKey(i => new { i.TenantId, i.OrderId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(o => o.Reservations)
            .WithOne(r => r.Order)
            .HasForeignKey(r => new { r.TenantId, r.OrderId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
