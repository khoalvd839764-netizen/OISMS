using System.Text.RegularExpressions;
using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho Cấu hình tài khoản ngân hàng thụ hưởng nhận tiền theo Tenant (Upgrade CS-05).
/// Ánh xạ vào bảng payment_accounts trong PostgreSQL schema.
/// </summary>
public class PaymentAccount : BaseEntity, IMustHaveTenant
{
    private static readonly Regex BinRegex = new(@"^[0-9]{6}$", RegexOptions.Compiled);
    private static readonly Regex AccountNumberRegex = new(@"^[A-Za-z0-9]{6,19}$", RegexOptions.Compiled);

    public Guid TenantId { get; set; }

    /// <summary>
    /// Mã định danh ngân hàng (BIN) - 6 chữ số theo chuẩn Napas/NHNN.
    /// </summary>
    public string BankBin { get; set; } = string.Empty;

    /// <summary>
    /// Tên ngân hàng đầy đủ (ví dụ: Ngân hàng TMCP Quân Đội).
    /// </summary>
    public string BankName { get; set; } = string.Empty;

    /// <summary>
    /// Tên viết tắt ngân hàng (ví dụ: MB, VCB, TCB).
    /// </summary>
    public string BankShortName { get; set; } = string.Empty;

    /// <summary>
    /// Số tài khoản thụ hưởng (6-19 ký tự chữ và số).
    /// </summary>
    public string AccountNumber { get; set; } = string.Empty;

    /// <summary>
    /// Tên chủ tài khoản thụ hưởng (viết hoa không dấu hoặc có dấu theo ngân hàng).
    /// </summary>
    public string AccountName { get; set; } = string.Empty;

    /// <summary>
    /// Tài khoản mặc định của shop khi tạo giao dịch QR thanh toán.
    /// Mỗi Tenant chỉ có tối đa một tài khoản mặc định đang hoạt động (IsDefault && IsActive).
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Trạng thái hoạt động của tài khoản.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Người cập nhật cuối cùng.
    /// </summary>
    public Guid? UpdatedByUserId { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public User? UpdatedByUser { get; set; }
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public static void ValidateFields(string bankBin, string bankName, string bankShortName, string accountNumber, string accountName)
    {
        if (string.IsNullOrWhiteSpace(bankBin) || !BinRegex.IsMatch(bankBin.Trim()))
        {
            throw new ArgumentException("Mã định danh ngân hàng (BIN) phải gồm đúng 6 chữ số.", nameof(bankBin));
        }

        if (string.IsNullOrWhiteSpace(bankName))
        {
            throw new ArgumentException("Tên ngân hàng không được để trống.", nameof(bankName));
        }

        if (string.IsNullOrWhiteSpace(bankShortName))
        {
            throw new ArgumentException("Tên viết tắt ngân hàng không được để trống.", nameof(bankShortName));
        }

        if (string.IsNullOrWhiteSpace(accountNumber) || !AccountNumberRegex.IsMatch(accountNumber.Trim()))
        {
            throw new ArgumentException("Số tài khoản phải gồm từ 6 đến 19 ký tự chữ hoặc số.", nameof(accountNumber));
        }

        if (string.IsNullOrWhiteSpace(accountName))
        {
            throw new ArgumentException("Tên chủ tài khoản không được để trống.", nameof(accountName));
        }
    }

    public void Update(string bankBin, string bankName, string bankShortName, string accountNumber, string accountName, Guid? updatedByUserId)
    {
        ValidateFields(bankBin, bankName, bankShortName, accountNumber, accountName);

        BankBin = bankBin.Trim();
        BankName = bankName.Trim();
        BankShortName = bankShortName.Trim().ToUpperInvariant();
        AccountNumber = accountNumber.Trim();
        AccountName = accountName.Trim().ToUpperInvariant();
        UpdatedByUserId = updatedByUserId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetDefault(Guid? updatedByUserId)
    {
        IsDefault = true;
        UpdatedByUserId = updatedByUserId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UnsetDefault(Guid? updatedByUserId)
    {
        IsDefault = false;
        UpdatedByUserId = updatedByUserId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Deactivate(Guid? updatedByUserId)
    {
        IsActive = false;
        IsDefault = false;
        UpdatedByUserId = updatedByUserId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
