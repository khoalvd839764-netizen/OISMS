using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho Giữ chỗ tồn kho (Stock Reservation) chống bán âm / Overselling.
/// Map vào bảng reservations trong PostgreSQL schema v0.5.
/// </summary>
public class Reservation : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid OrderId { get; set; }
    public Guid OrderItemId { get; set; }
    public Guid BranchId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid SkuId { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>
    /// Trạng thái giữ chỗ: 'Active', 'Consumed', 'Released', 'Expired'.
    /// </summary>
    public string Status { get; set; } = "Active";

    public DateTimeOffset ExpiresAt { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Order? Order { get; set; }
    public OrderItem? OrderItem { get; set; }
    public Branch? Branch { get; set; }
    public Warehouse? Warehouse { get; set; }
    public ProductVariant? Sku { get; set; }
}
