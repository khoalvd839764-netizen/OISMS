using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho Thanh toán đơn hàng (Cash hoặc BankTransfer).
/// Map vào bảng payments trong PostgreSQL schema v0.5.
/// </summary>
public class Payment : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid OrderId { get; set; }

    /// <summary>
    /// Phương thức thanh toán: 'Cash', 'BankTransfer'.
    /// </summary>
    public string Method { get; set; } = "Cash";

    /// <summary>
    /// Trạng thái thanh toán: 'Pending', 'Succeeded', 'Failed'.
    /// </summary>
    public string Status { get; set; } = "Pending";

    public decimal Amount { get; set; }
    public decimal? CashReceived { get; set; }
    public DateTimeOffset? PaidAt { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Order? Order { get; set; }
}
