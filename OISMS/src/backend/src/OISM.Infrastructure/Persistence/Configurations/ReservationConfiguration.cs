using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("reservations");

        builder.HasKey(r => new { r.TenantId, r.Id });

        builder.HasIndex(r => new { r.TenantId, r.Id })
            .IsUnique()
            .HasDatabaseName("uq_reservations_1");

        builder.HasIndex(r => new { r.TenantId, r.OrderItemId, r.WarehouseId })
            .IsUnique()
            .HasDatabaseName("uq_reservations_2");

        builder.Property(r => r.TenantId).IsRequired();

        builder.Property(r => r.OrderId)
            .HasColumnName("order_id")
            .IsRequired();

        builder.Property(r => r.OrderItemId)
            .HasColumnName("order_item_id")
            .IsRequired();

        builder.Property(r => r.BranchId)
            .HasColumnName("branch_id")
            .IsRequired();

        builder.Property(r => r.WarehouseId)
            .HasColumnName("warehouse_id")
            .IsRequired();

        builder.Property(r => r.SkuId)
            .HasColumnName("sku_id")
            .IsRequired();

        builder.Property(r => r.Quantity)
            .HasColumnName("quantity")
            .HasPrecision(18, 3)
            .IsRequired();

        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasDefaultValue("Active")
            .IsRequired();

        builder.Property(r => r.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired();

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
            .WithMany(o => o.Reservations)
            .HasForeignKey(r => new { r.TenantId, r.OrderId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.OrderItem)
            .WithMany()
            .HasForeignKey(r => new { r.TenantId, r.OrderItemId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Branch)
            .WithMany()
            .HasForeignKey(r => new { r.TenantId, r.BranchId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Warehouse)
            .WithMany()
            .HasForeignKey(r => new { r.TenantId, r.WarehouseId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Sku)
            .WithMany()
            .HasForeignKey(r => new { r.TenantId, r.SkuId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
