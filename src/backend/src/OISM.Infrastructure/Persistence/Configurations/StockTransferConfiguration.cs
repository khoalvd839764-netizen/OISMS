using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class StockTransferConfiguration : IEntityTypeConfiguration<StockTransfer>
{
    public void Configure(EntityTypeBuilder<StockTransfer> builder)
    {
        builder.ToTable("stock_transfers");

        builder.HasKey(t => new { t.TenantId, t.Id });

        builder.HasIndex(t => new { t.TenantId, t.Id })
            .IsUnique()
            .HasDatabaseName("uq_stock_transfers_1");

        builder.HasIndex(t => new { t.TenantId, t.Number })
            .IsUnique()
            .HasDatabaseName("uq_stock_transfers_2");

        builder.HasIndex(t => new { t.TenantId, t.RequestKey })
            .IsUnique()
            .HasDatabaseName("uq_stock_transfers_3");

        builder.Property(t => t.TenantId).IsRequired();

        builder.Property(t => t.Number)
            .HasColumnName("number")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(t => t.RequestKey)
            .HasColumnName("request_key")
            .IsRequired(false);

        builder.Property(t => t.PayloadSignature)
            .HasColumnName("payload_signature")
            .HasMaxLength(128)
            .IsRequired(false);

        builder.Property(t => t.FromBranchId)
            .HasColumnName("from_branch_id")
            .IsRequired();

        builder.Property(t => t.ToBranchId)
            .HasColumnName("to_branch_id")
            .IsRequired();

        builder.Property(t => t.FromWarehouseId)
            .HasColumnName("from_warehouse_id")
            .IsRequired();

        builder.Property(t => t.ToWarehouseId)
            .HasColumnName("to_warehouse_id")
            .IsRequired();

        builder.Property(t => t.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasDefaultValue("Draft")
            .IsRequired();

        builder.Property(t => t.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired();

        builder.Property(t => t.DispatchedAt)
            .HasColumnName("dispatched_at")
            .IsRequired(false);

        builder.Property(t => t.ReceivedAt)
            .HasColumnName("received_at")
            .IsRequired(false);

        builder.Property(t => t.ReceivedBy)
            .HasColumnName("received_by")
            .IsRequired(false);

        builder.Property(t => t.Note)
            .HasColumnName("note")
            .IsRequired(false);

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(t => t.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);

        // Foreign keys
        builder.HasOne(t => t.Tenant)
            .WithMany()
            .HasForeignKey(t => t.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.FromBranch)
            .WithMany()
            .HasForeignKey(t => new { t.TenantId, t.FromBranchId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.ToBranch)
            .WithMany()
            .HasForeignKey(t => new { t.TenantId, t.ToBranchId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.FromWarehouse)
            .WithMany()
            .HasForeignKey(t => new { t.TenantId, t.FromWarehouseId, t.FromBranchId })
            .HasPrincipalKey(w => new { w.TenantId, w.Id, w.BranchId })
            .HasConstraintName("fk_stock_transfers_source_warehouse")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.ToWarehouse)
            .WithMany()
            .HasForeignKey(t => new { t.TenantId, t.ToWarehouseId, t.ToBranchId })
            .HasPrincipalKey(w => new { w.TenantId, w.Id, w.BranchId })
            .HasConstraintName("fk_stock_transfers_destination_warehouse")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.CreatedByUser)
            .WithMany()
            .HasForeignKey(t => new { t.TenantId, t.CreatedBy })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.ReceivedByUser)
            .WithMany()
            .HasForeignKey(t => new { t.TenantId, t.ReceivedBy })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.Items)
            .WithOne(i => i.Transfer)
            .HasForeignKey(i => new { i.TenantId, i.TransferId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
