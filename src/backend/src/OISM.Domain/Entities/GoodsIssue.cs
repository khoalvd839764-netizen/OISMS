using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho Phiếu xuất kho bán hàng mẫu 02-VT (Goods Issue) lưu kèm dòng bút toán kho OUT (Upgrade 13 - P1, P5).
/// Mỗi kho xuất hàng trong đơn tạo đúng một phiếu xuất kho trong cùng transaction với dòng sổ OUT.
/// Bảng chỉ ghi (append-only), map vào bảng goods_issues trong PostgreSQL schema v0.5.
/// </summary>
public class GoodsIssue : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public string Number { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public Guid SalesInvoiceId { get; set; }
    public Guid BranchId { get; set; }
    public Guid WarehouseId { get; set; }
    public string Channel { get; set; } = "POS";

    public string OrderNumberSnapshot { get; set; } = string.Empty;
    public string SalesInvoiceNumberSnapshot { get; set; } = string.Empty;
    public string BranchNameSnapshot { get; set; } = string.Empty;
    public string WarehouseNameSnapshot { get; set; } = string.Empty;
    public string? CustomerNameSnapshot { get; set; }

    public Guid? IssuedBy { get; set; }
    public string? IssuedByNameSnapshot { get; set; }
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Order? Order { get; set; }
    public SalesInvoice? SalesInvoice { get; set; }
    public Branch? Branch { get; set; }
    public Warehouse? Warehouse { get; set; }
    public User? IssuedByUser { get; set; }
    public ICollection<GoodsIssueItem> Items { get; set; } = new List<GoodsIssueItem>();
}
