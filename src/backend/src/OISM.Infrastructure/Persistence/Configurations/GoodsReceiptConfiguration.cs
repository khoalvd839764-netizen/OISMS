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

        builder.Property(r => r.BranchId).IsRequired();
        builder.Property(r => r.WarehouseId).IsRequired();
        builder.Property(r => r.SupplierId).IsRequired();

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
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .IsRequired(false);

        builder.HasOne(r => r.Tenant)
            .WithMany()
            .HasForeignKey(r => r.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Branch)
            .WithMany()
            .HasForeignKey(r => new { r.TenantId, r.BranchId })
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Warehouse)
            .WithMany()
            .HasForeignKey(r => new { r.TenantId, r.WarehouseId })
            .HasPrincipalKey(w => new { w.TenantId, w.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Supplier)
            .WithMany()
            .HasForeignKey(r => new { r.TenantId, r.SupplierId })
            .HasPrincipalKey(s => new { s.TenantId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Items)
            .WithOne(i => i.Receipt)
            .HasForeignKey(i => new { i.TenantId, i.ReceiptId })
            .HasPrincipalKey(r => new { r.TenantId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
