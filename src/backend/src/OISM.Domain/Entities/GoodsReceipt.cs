using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho Phiếu nhập kho từ Nhà cung cấp vào một Kho cụ thể của Chi nhánh.
/// Map vào bảng goods_receipts trong PostgreSQL schema v0.5.
/// </summary>
public class GoodsReceipt : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }

    /// <summary>
    /// Mã số phiếu nhập (ví dụ: GR-20261018-0001). Độc nhất theo Tenant.
    /// </summary>
    public string Number { get; set; } = string.Empty;

    public Guid? PurchaseOrderId { get; set; }
    public Guid BranchId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid SupplierId { get; set; }

    /// <summary>
    /// Trạng thái chứng từ: 'Draft', 'Confirmed', 'Cancelled'.
    /// </summary>
    public string Status { get; set; } = GoodsReceiptStatus.Draft;

    public string? Note { get; set; }

    public Guid CreatedBy { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public Guid? ConfirmedBy { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    public Branch? Branch { get; set; }
    public Warehouse? Warehouse { get; set; }
    public Supplier? Supplier { get; set; }
    public User? CreatedByUser { get; set; }
    public User? ConfirmedByUser { get; set; }

    public ICollection<GoodsReceiptItem> Items { get; set; } = new List<GoodsReceiptItem>();

    public bool IsDraft => Status == GoodsReceiptStatus.Draft;

    public void Confirm(Guid userId, DateTimeOffset at)
    {
        StateTransition.Ensure(IsDraft, nameof(GoodsReceipt), Status, GoodsReceiptStatus.Confirmed);
        Status = GoodsReceiptStatus.Confirmed;
        ConfirmedBy = userId;
        ConfirmedAt = at;
        UpdatedAt = at;
    }

    public void Cancel(DateTimeOffset at)
    {
        StateTransition.Ensure(IsDraft, nameof(GoodsReceipt), Status, GoodsReceiptStatus.Cancelled);
        Status = GoodsReceiptStatus.Cancelled;
        UpdatedAt = at;
    }
}
