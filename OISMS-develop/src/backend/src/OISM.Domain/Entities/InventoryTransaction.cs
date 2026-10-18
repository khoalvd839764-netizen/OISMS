using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Bút toán Sổ kho bất biến (Append-Only Inventory Ledger).
/// Map vào bảng inventory_transactions trong PostgreSQL schema v0.5.
/// Bảng này KHÔNG có cột updated_at và được bảo vệ bởi trigger ledger_append_only.
/// </summary>
public class InventoryTransaction : IMustHaveTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid SkuId { get; set; }

    /// <summary>
    /// Chiều biến động: "IN" hoặc "OUT".
    /// </summary>
    public string Type { get; set; } = "IN";

    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal BalanceAfter { get; set; }
    public long SequenceNo { get; set; }

    public Guid? ReceiptItemId { get; set; }
    public Guid? OrderItemId { get; set; }
    public Guid? TransferItemId { get; set; }
    public Guid? StockCountItemId { get; set; }
    public Guid? SalesReturnItemId { get; set; }
    public Guid? AdjustmentItemId { get; set; }

    /// <summary>
    /// Cột dẫn xuất GENERATED ALWAYS AS (coalesce(...)) STORED trong PostgreSQL.
    /// </summary>
    public Guid ReferenceId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Branch? Branch { get; set; }
    public Warehouse? Warehouse { get; set; }
    public ProductVariant? Sku { get; set; }
    public GoodsReceiptItem? ReceiptItem { get; set; }
}
