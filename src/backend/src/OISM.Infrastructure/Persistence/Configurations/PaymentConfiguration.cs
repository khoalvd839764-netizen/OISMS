using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");

        builder.HasKey(p => new { p.TenantId, p.Id });

        builder.HasIndex(p => new { p.TenantId, p.Id })
            .IsUnique()
            .HasDatabaseName("uq_payments_1");

        builder.HasIndex(p => new { p.TenantId, p.OrderId })
            .IsUnique()
            .HasDatabaseName("uq_payments_2");

        builder.Property(p => p.TenantId).IsRequired();

        builder.Property(p => p.OrderId)
            .HasColumnName("order_id")
            .IsRequired();

        builder.Property(p => p.Method)
            .HasColumnName("method")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasDefaultValue("Pending")
            .IsRequired();

        builder.Property(p => p.Amount)
            .HasColumnName("amount")
            .HasPrecision(20, 2)
            .IsRequired();

        builder.Property(p => p.CashReceived)
            .HasColumnName("cash_received")
            .HasPrecision(20, 2)
            .IsRequired(false);

        builder.Property(p => p.PaidAt)
            .HasColumnName("paid_at")
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

        builder.HasOne(p => p.Order)
            .WithOne(o => o.Payment)
            .HasForeignKey<Payment>(p => new { p.TenantId, p.OrderId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
