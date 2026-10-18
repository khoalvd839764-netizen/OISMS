using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
<<<<<<< HEAD
/// Bút toán Sổ kho bất biến (Append-Only Inventory Ledger).
/// Map vào bảng inventory_transactions trong PostgreSQL schema v0.5.
/// Bảng này KHÔNG có cột updated_at và được bảo vệ bởi trigger ledger_append_only.
/// </summary>
public class InventoryTransaction : IMustHaveTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
=======
/// Đại diện cho một bản ghi Sổ cái kho bất biến (Append-Only Inventory Ledger).
/// Ghi nhận mọi biến động tăng/giảm tồn kho kèm thứ tự sequence và số dư tức thời.
/// Map vào bảng inventory_transactions trong PostgreSQL schema v0.5.
/// </summary>
public class InventoryTransaction : BaseEntity, IMustHaveTenant
{
>>>>>>> 2cc35c255b5ed4a5ecafb66a14f858702cf6e168
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid SkuId { get; set; }

    /// <summary>
<<<<<<< HEAD
    /// Chiều biến động: "IN" hoặc "OUT".
=======
    /// Loại biến động: 'IN' (nhập, trả hàng) hoặc 'OUT' (xuất bán, chuyển đi).
>>>>>>> 2cc35c255b5ed4a5ecafb66a14f858702cf6e168
    /// </summary>
    public string Type { get; set; } = "IN";

    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal BalanceAfter { get; set; }
    public long SequenceNo { get; set; }

<<<<<<< HEAD
=======
    // Khóa ngoại chứng từ nguồn (num_nonnulls = 1)
>>>>>>> 2cc35c255b5ed4a5ecafb66a14f858702cf6e168
    public Guid? ReceiptItemId { get; set; }
    public Guid? OrderItemId { get; set; }
    public Guid? TransferItemId { get; set; }
    public Guid? StockCountItemId { get; set; }
    public Guid? SalesReturnItemId { get; set; }
    public Guid? AdjustmentItemId { get; set; }

    /// <summary>
<<<<<<< HEAD
    /// Cột dẫn xuất GENERATED ALWAYS AS (coalesce(...)) STORED trong PostgreSQL.
    /// </summary>
    public Guid ReferenceId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
=======
    /// Cột sinh tự động trong PostgreSQL: coalesce(receipt_item_id, order_item_id, ...).
    /// </summary>
    public Guid ReferenceId { get; private set; }
>>>>>>> 2cc35c255b5ed4a5ecafb66a14f858702cf6e168

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Branch? Branch { get; set; }
    public Warehouse? Warehouse { get; set; }
    public ProductVariant? Sku { get; set; }
    public GoodsReceiptItem? ReceiptItem { get; set; }
}
