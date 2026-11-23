using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class GoodsIssueItemConfiguration : IEntityTypeConfiguration<GoodsIssueItem>
{
    public void Configure(EntityTypeBuilder<GoodsIssueItem> builder)
    {
        builder.ToTable("goods_issue_items");

        builder.HasKey(i => new { i.TenantId, i.Id });

        builder.HasIndex(i => new { i.TenantId, i.Id })
            .IsUnique()
            .HasDatabaseName("uq_goods_issue_items_1");

        builder.HasIndex(i => new { i.TenantId, i.InventoryTransactionId })
            .IsUnique()
            .HasDatabaseName("uq_goods_issue_items_2");

        builder.HasIndex(i => new { i.TenantId, i.GoodsIssueId })
            .HasDatabaseName("ix_goods_issue_items_goods_issue");

        builder.HasIndex(i => new { i.TenantId, i.OrderItemId })
            .HasDatabaseName("ix_goods_issue_items_order_item");

        builder.Property(i => i.TenantId).IsRequired();

        builder.Property(i => i.GoodsIssueId)
            .HasColumnName("goods_issue_id")
            .IsRequired();

        builder.Property(i => i.InventoryTransactionId)
            .HasColumnName("inventory_transaction_id")
            .IsRequired();

        builder.Property(i => i.OrderItemId)
            .HasColumnName("order_item_id")
            .IsRequired();

        builder.Property(i => i.SkuId)
            .HasColumnName("sku_id")
            .IsRequired();

        builder.Property(i => i.SkuCodeSnapshot)
            .HasColumnName("sku_code_snapshot")
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(i => i.SkuNameSnapshot)
            .HasColumnName("sku_name_snapshot")
            .HasMaxLength(250)
            .IsRequired();

        builder.Property(i => i.UnitId)
            .HasColumnName("unit_id")
            .IsRequired(false);

        builder.Property(i => i.UnitNameSnapshot)
            .HasColumnName("unit_name_snapshot")
            .HasMaxLength(100)
            .HasDefaultValue(string.Empty)
            .IsRequired();

        builder.Property(i => i.FactorSnapshot)
            .HasColumnName("factor_snapshot")
            .HasPrecision(18, 6)
            .HasDefaultValue(1m)
            .IsRequired();

        builder.Property(i => i.Quantity)
            .HasColumnName("quantity")
            .HasPrecision(18, 3)
            .IsRequired();

        builder.Property(i => i.BaseQuantity)
            .HasColumnName("base_quantity")
            .HasPrecision(18, 3)
            .IsRequired();

        builder.Property(i => i.UnitCost)
            .HasColumnName("unit_cost")
            .HasPrecision(20, 6)
            .IsRequired();

        builder.Property(i => i.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(i => i.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);

        // Foreign keys
        builder.HasOne(i => i.Tenant)
            .WithMany()
            .HasForeignKey(i => i.TenantId)
            .HasConstraintName("fk_goods_issue_items_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.GoodsIssue)
            .WithMany(g => g.Items)
            .HasForeignKey(i => new { i.TenantId, i.GoodsIssueId })
            .HasPrincipalKey(g => new { g.TenantId, g.Id })
            .HasConstraintName("fk_goods_issue_items_goods_issue_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.InventoryTransaction)
            .WithMany()
            .HasForeignKey(i => new { i.TenantId, i.InventoryTransactionId })
            .HasPrincipalKey(t => new { t.TenantId, t.Id })
            .HasConstraintName("fk_goods_issue_items_inventory_tx")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.OrderItem)
            .WithMany()
            .HasForeignKey(i => new { i.TenantId, i.OrderItemId })
            .HasPrincipalKey(oi => new { oi.TenantId, oi.Id })
            .HasConstraintName("fk_goods_issue_items_order_item")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Sku)
            .WithMany()
            .HasForeignKey(i => new { i.TenantId, i.SkuId })
            .HasPrincipalKey(s => new { s.TenantId, s.Id })
            .HasConstraintName("fk_goods_issue_items_sku_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Unit)
            .WithMany()
            .HasForeignKey(i => new { i.TenantId, i.UnitId })
            .HasPrincipalKey(u => new { u.TenantId, u.Id })
            .HasConstraintName("fk_goods_issue_items_unit_id")
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
