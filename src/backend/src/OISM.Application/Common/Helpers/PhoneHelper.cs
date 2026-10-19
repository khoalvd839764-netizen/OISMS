using System.Text.RegularExpressions;

namespace OISM.Application.Common.Helpers;

/// <summary>
/// Tiện ích chuẩn hóa và xác thực số điện thoại theo chuẩn quốc tế E.164
/// và ràng buộc cơ sở dữ liệu ck_users_phone_normalized: phone ~ '^\+[1-9][0-9]{7,14}$'.
/// </summary>
public static class PhoneHelper
{
    private static readonly Regex E164Regex = new(@"^\+[1-9][0-9]{7,14}$", RegexOptions.Compiled);
    private static readonly Regex AllowedCharsRegex = new(@"^[0-9+() \-\.]*$", RegexOptions.Compiled);

    /// <summary>
    /// Kiểm tra chuỗi số điện thoại thô có thể chuẩn hóa thành định dạng E.164 hợp lệ hay không.
    /// Cho phép số điện thoại nội địa Việt Nam (ví dụ: "090 123 4567", "0901234567", "090-123-4567")
    /// hoặc số điện thoại quốc tế E.164 (ví dụ: "+84 90 123 4567", "+12025550123").
    /// </summary>
    public static bool IsValidPhoneNumber(string? rawPhone)
    {
        if (string.IsNullOrWhiteSpace(rawPhone))
        {
            return false;
        }

        return TryNormalizePhoneNumber(rawPhone, out _);
    }

    /// <summary>
    /// Chuẩn hóa số điện thoại thô sang định dạng E.164 chuẩn (bắt đầu bằng '+', từ 8 đến 15 chữ số).
    /// Loại bỏ khoảng trắng, dấu gạch ngang, dấu chấm, dấu ngoặc.
    /// Chuyển đổi tiền tố '0' của Việt Nam thành '+84'.
    /// </summary>
    public static bool TryNormalizePhoneNumber(string? rawPhone, out string? normalizedPhone)
    {
        normalizedPhone = null;

        if (string.IsNullOrWhiteSpace(rawPhone))
        {
            return false;
        }

        var trimmed = rawPhone.Trim();

        // Kiểm tra xem chuỗi có chứa ký tự không hợp lệ không (chữ cái, ký tự lạ)
        if (!AllowedCharsRegex.IsMatch(trimmed))
        {
            return false;
        }

        // Loại bỏ các ký tự phân tách cho phép: dấu cách, gạch nối, chấm, ngoặc đơn
        var cleaned = trimmed
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace(".", string.Empty)
            .Replace("(", string.Empty)
            .Replace(")", string.Empty);

        if (string.IsNullOrEmpty(cleaned))
        {
            return false;
        }

        // Kiểm tra dấu '+'
        int plusCount = cleaned.Count(c => c == '+');
        if (plusCount > 1 || (plusCount == 1 && !cleaned.StartsWith("+")))
        {
            return false;
        }

        string candidate;

        if (cleaned.StartsWith("+"))
        {
            candidate = cleaned;
        }
        else if (cleaned.StartsWith("0"))
        {
            // Số nội địa Việt Nam: 0901234567 -> +84901234567
            candidate = "+84" + cleaned.Substring(1);
        }
        else if (cleaned.StartsWith("84") && cleaned.Length >= 10)
        {
            // Đã có mã quốc gia 84 nhưng thiếu dấu '+'
            candidate = "+" + cleaned;
        }
        else
        {
            // Trường hợp số quốc tế nhưng người dùng không gõ dấu '+'
            candidate = "+" + cleaned;
        }

        // Kiểm tra candidate có khớp chuẩn ck_users_phone_normalized: ^\+[1-9][0-9]{7,14}$
        if (E164Regex.IsMatch(candidate))
        {
            normalizedPhone = candidate;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Chuẩn hóa số điện thoại, nếu không hợp lệ hoặc rỗng trả về null.
    /// </summary>
    public static string? NormalizePhoneNumber(string? rawPhone)
    {
        if (TryNormalizePhoneNumber(rawPhone, out var normalized))
        {
            return normalized;
        }
        return null;
    }
}
