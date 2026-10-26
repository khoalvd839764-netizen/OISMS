using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class StockCountConfiguration : IEntityTypeConfiguration<StockCount>
{
    public void Configure(EntityTypeBuilder<StockCount> builder)
    {
        builder.ToTable("stock_counts");

        builder.HasKey(c => new { c.TenantId, c.Id });

        builder.HasIndex(c => new { c.TenantId, c.Id })
            .IsUnique()
            .HasDatabaseName("uq_stock_counts_1");

        builder.HasIndex(c => new { c.TenantId, c.Number })
            .IsUnique()
            .HasDatabaseName("uq_stock_counts_2");

        builder.HasIndex(c => new { c.TenantId, c.RequestKey })
            .IsUnique()
            .HasDatabaseName("uq_stock_counts_3");

        builder.HasIndex(c => new { c.TenantId, c.GeneratedAdjustmentId })
            .IsUnique()
            .HasDatabaseName("uq_stock_counts_4");

        builder.Property(c => c.TenantId).IsRequired();

        builder.Property(c => c.Number)
            .HasColumnName("number")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(c => c.RequestKey)
            .HasColumnName("request_key")
            .IsRequired(false);

        builder.Property(c => c.PayloadSignature)
            .HasColumnName("payload_signature")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(c => c.GeneratedAdjustmentId)
            .HasColumnName("generated_adjustment_id")
            .IsRequired(false);

        builder.Property(c => c.BranchId)
            .HasColumnName("branch_id")
            .IsRequired();

        builder.Property(c => c.WarehouseId)
            .HasColumnName("warehouse_id")
            .IsRequired();

        builder.Property(c => c.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasDefaultValue("Draft")
            .IsRequired();

        builder.Property(c => c.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired();

        builder.Property(c => c.ConfirmedBy)
            .HasColumnName("confirmed_by")
            .IsRequired(false);

        builder.Property(c => c.ConfirmedAt)
            .HasColumnName("confirmed_at")
            .IsRequired(false);

        builder.Property(c => c.Note)
            .HasColumnName("note")
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
            .HasConstraintName("fk_stock_counts_warehouse_branch")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.CreatedByUser)
            .WithMany()
            .HasForeignKey(c => new { c.TenantId, c.CreatedBy })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.ConfirmedByUser)
            .WithMany()
            .HasForeignKey(c => new { c.TenantId, c.ConfirmedBy })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.GeneratedAdjustment)
            .WithMany()
            .HasForeignKey(c => new { c.TenantId, c.GeneratedAdjustmentId, c.BranchId, c.WarehouseId })
            .HasPrincipalKey(a => new { a.TenantId, a.Id, a.BranchId, a.WarehouseId })
            .HasConstraintName("fk_stock_counts_generated_adjustment")
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Items)
            .WithOne(i => i.StockCount)
            .HasForeignKey(i => new { i.TenantId, i.StockCountId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
