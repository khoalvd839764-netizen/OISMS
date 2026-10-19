using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> builder)
    {
        builder.ToTable("goods_receipts");

        builder.HasKey(r => new { r.TenantId, r.Id });

        builder.HasIndex(r => new { r.TenantId, r.Id })
            .IsUnique()
            .HasDatabaseName("uq_goods_receipts_1");

        builder.HasIndex(r => new { r.TenantId, r.Number })
            .IsUnique()
            .HasDatabaseName("uq_goods_receipts_2");

        builder.Property(r => r.TenantId).IsRequired();

        builder.Property(r => r.Number)
            .HasColumnName("number")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.BranchId)
            .HasColumnName("branch_id")
            .IsRequired();

        builder.Property(r => r.WarehouseId)
            .HasColumnName("warehouse_id")
            .IsRequired();

        builder.Property(r => r.SupplierId)
            .HasColumnName("supplier_id")
            .IsRequired();

        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasDefaultValue("Draft")
            .IsRequired();

        builder.Property(r => r.Note)
            .HasColumnName("note")
            .IsRequired(false);

        builder.Property(r => r.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired();

        builder.Property(r => r.ConfirmedAt)
            .HasColumnName("confirmed_at")
            .IsRequired(false);

        builder.Property(r => r.ConfirmedBy)
            .HasColumnName("confirmed_by")
            .IsRequired(false);

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);

        // Foreign keys with composite keys
        builder.HasOne(r => r.Tenant)
            .WithMany()
            .HasForeignKey(r => r.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Branch)
            .WithMany()
            .HasForeignKey(r => new { r.TenantId, r.BranchId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Warehouse)
            .WithMany()
            .HasForeignKey(r => new { r.TenantId, r.WarehouseId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Supplier)
            .WithMany()
            .HasForeignKey(r => new { r.TenantId, r.SupplierId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.CreatedByUser)
            .WithMany()
            .HasForeignKey(r => new { r.TenantId, r.CreatedBy })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ConfirmedByUser)
            .WithMany()
            .HasForeignKey(r => new { r.TenantId, r.ConfirmedBy })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Items)
            .WithOne(i => i.Receipt)
            .HasForeignKey(i => new { i.TenantId, i.ReceiptId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
