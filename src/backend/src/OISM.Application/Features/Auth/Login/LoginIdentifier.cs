using System.Net.Mail;
using OISM.Application.Common.Helpers;

namespace OISM.Application.Features.Auth.Login;

public enum LoginIdentifierKind
{
    Invalid,
    Email,
    Phone
}

/// <summary>
/// Định danh đăng nhập đã chuẩn hóa (Upgrade 10). Chuỗi có "@" được coi là email,
/// còn lại được chuẩn hóa thành số điện thoại E.164 bằng <see cref="PhoneHelper"/>.
/// Dùng chung cho đăng nhập và quên mật khẩu.
/// </summary>
public readonly record struct LoginIdentifier(LoginIdentifierKind Kind, string Normalized)
{
    public bool IsValid => Kind != LoginIdentifierKind.Invalid;

    public static LoginIdentifier Resolve(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new LoginIdentifier(LoginIdentifierKind.Invalid, string.Empty);
        }

        var trimmed = raw.Trim();
        if (trimmed.Contains('@'))
        {
            var email = trimmed.ToLowerInvariant();
            return email.Length <= 254 && MailAddress.TryCreate(email, out var parsed) && parsed.Address == email
                ? new LoginIdentifier(LoginIdentifierKind.Email, email)
                : new LoginIdentifier(LoginIdentifierKind.Invalid, string.Empty);
        }

        return PhoneHelper.TryNormalizePhoneNumber(trimmed, out var phone) && phone != null
            ? new LoginIdentifier(LoginIdentifierKind.Phone, phone)
            : new LoginIdentifier(LoginIdentifierKind.Invalid, string.Empty);
    }
}
