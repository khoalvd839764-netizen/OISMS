using System.ComponentModel.DataAnnotations;

namespace OISM.Application.Features.PaymentAccounts.Dtos;

public class PaymentAccountDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string BankBin { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string BankShortName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class ActivePaymentAccountDto
{
    public Guid Id { get; set; }
    public string BankBin { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string BankShortName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

public class CreatePaymentAccountRequest
{
    [Required(ErrorMessage = "Mã BIN ngân hàng là bắt buộc.")]
    [RegularExpression(@"^[0-9]{6}$", ErrorMessage = "Mã BIN ngân hàng phải gồm đúng 6 chữ số.")]
    public string BankBin { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên ngân hàng là bắt buộc.")]
    [StringLength(255, ErrorMessage = "Tên ngân hàng không được vượt quá 255 ký tự.")]
    public string BankName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên viết tắt ngân hàng là bắt buộc.")]
    [StringLength(50, ErrorMessage = "Tên viết tắt ngân hàng không được vượt quá 50 ký tự.")]
    public string BankShortName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số tài khoản thụ hưởng là bắt buộc.")]
    [RegularExpression(@"^[A-Za-z0-9]{6,19}$", ErrorMessage = "Số tài khoản phải gồm từ 6 đến 19 ký tự chữ hoặc số.")]
    public string AccountNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên chủ tài khoản là bắt buộc.")]
    [StringLength(255, ErrorMessage = "Tên chủ tài khoản không được vượt quá 255 ký tự.")]
    public string AccountName { get; set; } = string.Empty;

    public bool IsDefault { get; set; }
}

public class UpdatePaymentAccountRequest
{
    [Required(ErrorMessage = "Mã BIN ngân hàng là bắt buộc.")]
    [RegularExpression(@"^[0-9]{6}$", ErrorMessage = "Mã BIN ngân hàng phải gồm đúng 6 chữ số.")]
    public string BankBin { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên ngân hàng là bắt buộc.")]
    [StringLength(255, ErrorMessage = "Tên ngân hàng không được vượt quá 255 ký tự.")]
    public string BankName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên viết tắt ngân hàng là bắt buộc.")]
    [StringLength(50, ErrorMessage = "Tên viết tắt ngân hàng không được vượt quá 50 ký tự.")]
    public string BankShortName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số tài khoản thụ hưởng là bắt buộc.")]
    [RegularExpression(@"^[A-Za-z0-9]{6,19}$", ErrorMessage = "Số tài khoản phải gồm từ 6 đến 19 ký tự chữ hoặc số.")]
    public string AccountNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên chủ tài khoản là bắt buộc.")]
    [StringLength(255, ErrorMessage = "Tên chủ tài khoản không được vượt quá 255 ký tự.")]
    public string AccountName { get; set; } = string.Empty;
}
