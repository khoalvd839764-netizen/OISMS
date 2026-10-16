using FluentValidation;

namespace OISM.Application.Features.Auth.RegisterTenant;

/// <summary>
/// Bộ kiểm tra tính hợp lệ dữ liệu đăng ký cửa hàng theo chuẩn NFR-SEC và NFR-TENANT.
/// </summary>
public class RegisterTenantValidator : AbstractValidator<RegisterTenantRequest>
{
    public RegisterTenantValidator()
    {
        RuleFor(x => x.TenantName)
            .NotEmpty().WithMessage("Tên cửa hàng không được để trống.")
            .MaximumLength(150).WithMessage("Tên cửa hàng không được vượt quá 150 ký tự.");

        RuleFor(x => x.TenantCode)
            .NotEmpty().WithMessage("Mã cửa hàng không được để trống.")
            .MinimumLength(3).WithMessage("Mã cửa hàng phải có ít nhất 3 ký tự.")
            .MaximumLength(30).WithMessage("Mã cửa hàng không được vượt quá 30 ký tự.")
            .Matches("^[a-zA-Z0-9_-]+$").WithMessage("Mã cửa hàng chỉ được chứa chữ cái, số, gạch dưới hoặc gạch nối.");

        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Họ và tên chủ shop không được để trống.")
            .MaximumLength(100).WithMessage("Họ và tên không được vượt quá 100 ký tự.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email không được để trống.")
            .EmailAddress().WithMessage("Định dạng email không hợp lệ.")
            .MaximumLength(150).WithMessage("Email không được vượt quá 150 ký tự.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Mật khẩu không được để trống.")
            .MinimumLength(6).WithMessage("Mật khẩu phải có độ dài tối thiểu 6 ký tự.");

        RuleFor(x => x.PhoneNumber)
            .Matches(@"^[0-9+() -]*$").When(x => !string.IsNullOrEmpty(x.PhoneNumber))
            .WithMessage("Số điện thoại chứa ký tự không hợp lệ.");
    }
}
