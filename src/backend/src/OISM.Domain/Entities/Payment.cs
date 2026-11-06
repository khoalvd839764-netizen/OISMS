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
    public string Status { get; set; } = PaymentStatus.Pending;

    public decimal Amount { get; set; }
    public decimal? CashReceived { get; set; }
    public DateTimeOffset? PaidAt { get; set; }

    /// <summary>
    /// Nội dung chuyển khoản hoặc mã tham chiếu giao dịch QR.
    /// </summary>
    public string? TransferReference { get; set; }

    /// <summary>
    /// Bản chụp mã BIN ngân hàng nhận tiền tại thời điểm giao dịch.
    /// </summary>
    public string? ReceiverBankBin { get; set; }

    /// <summary>
    /// Bản chụp số tài khoản nhận tiền tại thời điểm giao dịch.
    /// </summary>
    public string? ReceiverAccountNumber { get; set; }

    /// <summary>
    /// Bản chụp tên chủ tài khoản nhận tiền tại thời điểm giao dịch.
    /// </summary>
    public string? ReceiverAccountName { get; set; }

    /// <summary>
    /// ID tài khoản ngân hàng thụ hưởng (FK đến payment_accounts).
    /// </summary>
    public Guid? PaymentAccountId { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Order? Order { get; set; }
    public PaymentAccount? PaymentAccount { get; set; }

    public void Succeed(DateTimeOffset at)
    {
        StateTransition.Ensure(Status == PaymentStatus.Pending, nameof(Payment), Status, PaymentStatus.Succeeded);
        Status = PaymentStatus.Succeeded;
        PaidAt = at;
    }
}
