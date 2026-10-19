using Microsoft.EntityFrameworkCore;
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

        builder.HasIndex(t => new { t.TenantId, t.ReceiptItemId })
            .IsUnique()
            .HasDatabaseName("uq_inventory_transactions_3");

        builder.Property(t => t.TenantId).IsRequired();

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

        builder.Property(t => t.ReceiptItemId)
            .HasColumnName("receipt_item_id")
            .IsRequired(false);

        builder.Property(t => t.OrderItemId)
            .HasColumnName("order_item_id")
            .IsRequired(false);

        builder.Property(t => t.TransferItemId)
            .HasColumnName("transfer_item_id")
            .IsRequired(false);

        builder.Property(t => t.StockCountItemId)
            .HasColumnName("stock_count_item_id")
            .IsRequired(false);

        builder.Property(t => t.SalesReturnItemId)
            .HasColumnName("sales_return_item_id")
            .IsRequired(false);

        builder.Property(t => t.AdjustmentItemId)
            .HasColumnName("adjustment_item_id")
            .IsRequired(false);

        builder.Property(t => t.ReferenceId)
            .HasColumnName("reference_id")
            .HasComputedColumnSql("coalesce(receipt_item_id, order_item_id, transfer_item_id, stock_count_item_id, sales_return_item_id, adjustment_item_id)", stored: true)
            .ValueGeneratedOnAddOrUpdate();

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        // Không có cột updated_at trong bảng inventory_transactions
        builder.Ignore(t => t.UpdatedAt);

        // Foreign keys
        builder.HasOne(t => t.Tenant)
            .WithMany()
            .HasForeignKey(t => t.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Branch)
            .WithMany()
            .HasForeignKey(t => new { t.TenantId, t.BranchId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Warehouse)
            .WithMany()
            .HasForeignKey(t => new { t.TenantId, t.WarehouseId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Sku)
            .WithMany()
            .HasForeignKey(t => new { t.TenantId, t.SkuId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.ReceiptItem)
            .WithMany()
            .HasForeignKey(t => new { t.TenantId, t.ReceiptItemId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
