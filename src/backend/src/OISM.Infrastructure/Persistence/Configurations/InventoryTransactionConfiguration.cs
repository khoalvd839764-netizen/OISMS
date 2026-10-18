using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> builder)
    {
        builder.ToTable("inventory_transactions");

        builder.HasKey(t => new { t.TenantId, t.Id });

        builder.HasIndex(t => new { t.TenantId, t.Id })
            .IsUnique()
            .HasDatabaseName("uq_inventory_transactions_1");

        builder.HasIndex(t => new { t.TenantId, t.BranchId, t.SkuId, t.SequenceNo })
            .IsUnique()
            .HasDatabaseName("uq_inventory_transactions_2");

        builder.Property(t => t.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(t => t.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(t => t.BranchId)
            .HasColumnName("branch_id")
            .IsRequired();

        builder.Property(t => t.WarehouseId)
            .HasColumnName("warehouse_id")
            .IsRequired();

        builder.Property(t => t.SkuId)
            .HasColumnName("sku_id")
            .IsRequired();

        builder.Property(t => t.Type)
            .HasColumnName("type")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(t => t.Quantity)
            .HasColumnName("quantity")
            .HasPrecision(18, 3)
            .IsRequired();

        builder.Property(t => t.UnitCost)
            .HasColumnName("unit_cost")
            .HasPrecision(20, 6)
            .IsRequired();

        builder.Property(t => t.BalanceAfter)
            .HasColumnName("balance_after")
            .HasPrecision(18, 3)
            .IsRequired();

        builder.Property(t => t.SequenceNo)
            .HasColumnName("sequence_no")
            .IsRequired();

        builder.Property(t => t.ReceiptItemId).HasColumnName("receipt_item_id");
        builder.Property(t => t.OrderItemId).HasColumnName("order_item_id");
        builder.Property(t => t.TransferItemId).HasColumnName("transfer_item_id");
        builder.Property(t => t.StockCountItemId).HasColumnName("stock_count_item_id");
        builder.Property(t => t.SalesReturnItemId).HasColumnName("sales_return_item_id");
        builder.Property(t => t.AdjustmentItemId).HasColumnName("adjustment_item_id");

        // reference_id là GENERATED ALWAYS AS (...) STORED trong PostgreSQL
        builder.Property(t => t.ReferenceId)
            .HasColumnName("reference_id")
            .ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasOne(t => t.Tenant)
            .WithMany()
            .HasForeignKey(t => t.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Branch)
            .WithMany()
            .HasForeignKey(t => new { t.TenantId, t.BranchId })
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Warehouse)
            .WithMany()
            .HasForeignKey(t => new { t.TenantId, t.WarehouseId })
            .HasPrincipalKey(w => new { w.TenantId, w.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Sku)
            .WithMany()
            .HasForeignKey(t => new { t.TenantId, t.SkuId })
            .HasPrincipalKey(v => new { v.TenantId, v.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.ReceiptItem)
            .WithMany()
            .HasForeignKey(t => new { t.TenantId, t.ReceiptItemId })
            .HasPrincipalKey(i => new { i.TenantId, i.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
