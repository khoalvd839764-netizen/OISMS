using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho Phiếu chuyển kho nội bộ 2 bước (Inter-Warehouse Stock Transfer).
/// Map vào bảng stock_transfers trong PostgreSQL schema v0.5.
/// </summary>
public class StockTransfer : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public string Number { get; set; } = string.Empty;
    public Guid? RequestKey { get; set; }
    public string? PayloadSignature { get; set; }
    public Guid FromBranchId { get; set; }
    public Guid ToBranchId { get; set; }
    public Guid FromWarehouseId { get; set; }
    public Guid ToWarehouseId { get; set; }

    /// <summary>
    /// Trạng thái chuyển kho: 'Draft', 'InTransit', 'Received', 'Cancelled'.
    /// </summary>
    public string Status { get; set; } = "Draft";

    public Guid CreatedBy { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public Guid? ReceivedBy { get; set; }
    public string? Note { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Branch? FromBranch { get; set; }
    public Branch? ToBranch { get; set; }
    public Warehouse? FromWarehouse { get; set; }
    public Warehouse? ToWarehouse { get; set; }
    public User? CreatedByUser { get; set; }
    public User? ReceivedByUser { get; set; }

    public ICollection<StockTransferItem> Items { get; set; } = new List<StockTransferItem>();
}
