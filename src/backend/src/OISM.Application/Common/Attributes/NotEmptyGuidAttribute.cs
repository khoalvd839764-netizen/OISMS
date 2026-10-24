using System.ComponentModel.DataAnnotations;

namespace OISM.Application.Common.Attributes;

/// <summary>
/// Ràng buộc kiểm tra thuộc tính kiểu Guid hoặc chuỗi đại diện Guid không được rỗng (Guid.Empty / 00000000-0000-0000-0000-000000000000).
/// Khắc phục lỗ hổng [Required] của .NET khi đặt trên struct Guid (mặc định Guid.Empty vẫn pass [Required]).
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public class NotEmptyGuidAttribute : ValidationAttribute
{
    private const string DefaultErrorMessage = "Giá trị định danh '{0}' không được rỗng (Guid.Empty).";

    public NotEmptyGuidAttribute() : base(DefaultErrorMessage)
    {
    }

    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            return false;
        }

        if (value is Guid guid)
        {
            return guid != Guid.Empty;
        }

        if (value is string str)
        {
            return Guid.TryParse(str, out var parsedGuid) && parsedGuid != Guid.Empty;
        }

        return false;
    }

    public override string FormatErrorMessage(string name)
    {
        return string.Format(ErrorMessageString, name);
    }
}
