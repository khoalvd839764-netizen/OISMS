using FluentValidation;

namespace OISM.Application.Features.Auth.Login;

public class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.RawIdentifier)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Email hoặc số điện thoại đăng nhập không được để trống.")
            .Must(raw => LoginIdentifier.Resolve(raw).IsValid)
            .WithMessage("Email hoặc số điện thoại không hợp lệ.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Mật khẩu không được để trống.");
    }
}
