using System.Text.RegularExpressions;

namespace OISM.Application.Common.Helpers;

/// <summary>
/// Tiện ích hỗ trợ kiểm tra và sinh mã vạch chuẩn quốc tế EAN-13 và Code128 theo chuẩn FR-PROD-03.
/// </summary>
public static class BarcodeHelper
{
    /// <summary>
    /// Kiểm tra xem chuỗi barcode có phải là chuẩn EAN-13 hợp lệ không (13 chữ số và số kiểm tra chuẩn xác).
    /// </summary>
    public static bool IsValidEan13(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode) || barcode.Length != 13 || !Regex.IsMatch(barcode, @"^\d{13}$"))
        {
            return false;
        }

        var checkDigit = CalculateEan13CheckDigit(barcode.Substring(0, 12));
        return (barcode[12] - '0') == checkDigit;
    }

    /// <summary>
    /// Tính số kiểm tra (Check digit) cho 12 chữ số đầu của mã EAN-13 theo công thức chuẩn GS1:
    /// Nhân các số ở vị trí lẻ với 1, vị trí chẵn với 3, cộng lại rồi lấy phần bù 10.
    /// </summary>
    public static int CalculateEan13CheckDigit(string twelveDigits)
    {
        if (twelveDigits.Length != 12 || !Regex.IsMatch(twelveDigits, @"^\d{12}$"))
        {
            throw new ArgumentException("Đầu vào phải đúng 12 chữ số.", nameof(twelveDigits));
        }

        int sum = 0;
        for (int i = 0; i < 12; i++)
        {
            int digit = twelveDigits[i] - '0';
            sum += (i % 2 == 0) ? digit : digit * 3;
        }

        int remainder = sum % 10;
        return (10 - remainder) % 10;
    }

    /// <summary>
    /// Tự động sinh mã vạch chuẩn EAN-13 ngẫu nhiên hợp lệ với tiền tố Việt Nam (893).
    /// </summary>
    public static string GenerateEan13()
    {
        var random = new Random();
        // Tiền tố Việt Nam 893 + 9 số ngẫu nhiên = 12 số
        string prefix = "893";
        string randomPart = random.Next(100000000, 999999999).ToString();
        string twelveDigits = prefix + randomPart;

        int checkDigit = CalculateEan13CheckDigit(twelveDigits);
        return twelveDigits + checkDigit;
    }
}
