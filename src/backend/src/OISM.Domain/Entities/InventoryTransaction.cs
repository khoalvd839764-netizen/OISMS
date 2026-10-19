using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho một bản ghi Sổ cái kho bất biến (Append-Only Inventory Ledger).
/// Ghi nhận mọi biến động tăng/giảm tồn kho kèm thứ tự sequence và số dư tức thời.
/// Map vào bảng inventory_transactions trong PostgreSQL schema v0.5.
/// </summary>
public class InventoryTransaction : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid SkuId { get; set; }

    /// <summary>
    /// Loại biến động: 'IN' (nhập, trả hàng) hoặc 'OUT' (xuất bán, chuyển đi).
    /// </summary>
    public string Type { get; set; } = "IN";

    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal BalanceAfter { get; set; }
    public long SequenceNo { get; set; }

    // Khóa ngoại chứng từ nguồn (num_nonnulls = 1)
    public Guid? ReceiptItemId { get; set; }
    public Guid? OrderItemId { get; set; }
    public Guid? TransferItemId { get; set; }
    public Guid? StockCountItemId { get; set; }
    public Guid? SalesReturnItemId { get; set; }
    public Guid? AdjustmentItemId { get; set; }

    /// <summary>
    /// Cột sinh tự động trong PostgreSQL: coalesce(receipt_item_id, order_item_id, ...).
    /// </summary>
    public Guid ReferenceId { get; private set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Branch? Branch { get; set; }
    public Warehouse? Warehouse { get; set; }
    public ProductVariant? Sku { get; set; }
    public GoodsReceiptItem? ReceiptItem { get; set; }
}
