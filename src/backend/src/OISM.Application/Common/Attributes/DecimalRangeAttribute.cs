using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace OISM.Application.Common.Attributes;

/// <summary>
/// Kiểm tra khoảng giá trị decimal, an toàn khi nhiều request dùng chung một instance attribute.
/// Thay cho [Range(typeof(decimal), ...)]: RangeAttribute chuyển đổi giới hạn lười ở lần dùng đầu và không
/// an toàn luồng, nên các request đồng thời ngay sau khi khởi động có thể gặp InvalidCastException và trả 500.
/// Vẫn kế thừa RangeAttribute để tài liệu API tiếp tục đọc được Minimum và Maximum.
/// </summary>
public sealed class DecimalRangeAttribute : RangeAttribute
{
    private readonly decimal _minimum;
    private readonly decimal _maximum;

    public DecimalRangeAttribute(string minimum, string maximum)
        : base(typeof(decimal), minimum, maximum)
    {
        _minimum = decimal.Parse(minimum, NumberStyles.Number, CultureInfo.InvariantCulture);
        _maximum = decimal.Parse(maximum, NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    public override bool IsValid(object? value)
    {
        if (value is null or "")
        {
            return true;
        }

        try
        {
            var number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            return number >= _minimum && number <= _maximum;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return false;
        }
    }

    public override string FormatErrorMessage(string name) =>
        string.Format(CultureInfo.CurrentCulture, ErrorMessageString, name, _minimum, _maximum);
}
