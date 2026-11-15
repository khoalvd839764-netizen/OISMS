using FluentValidation;
using OISM.Application.Features.Auth.Login;

namespace OISM.Application.Features.Auth.PasswordReset;

/// <summary>
/// Cấu hình đặt lại mật khẩu (section "PasswordReset"). URL frontend lấy từ cấu hình,
/// không bao giờ dựng từ header Host hay dữ liệu client gửi lên.
/// </summary>
public sealed class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    /// <summary>Khóa ký HMAC, tối thiểu 32 ký tự. Development để trống thì tự sinh khóa tạm.</summary>
    public string? SigningKey { get; set; }

    public int TokenLifetimeMinutes { get; set; } = 30;

    public string? AdminBaseUrl { get; set; }

    public string? PosBaseUrl { get; set; }
}

public static class PasswordResetApps
{
    public const string Admin = "admin";
    public const string Pos = "pos";
}

public class ForgotPasswordRequest
{
    /// <summary>Email hoặc số điện thoại của tài khoản.</summary>
    public string Identifier { get; set; } = string.Empty;

    /// <summary>App gửi yêu cầu, quyết định link trong mail trỏ về đâu: "admin" (mặc định) hoặc "pos".</summary>
    public string? App { get; set; }
}

public class ResetPasswordRequest
{
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class AdminResetPasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;
}

public class PasswordResetStatusResponse
{
    public bool Enabled { get; set; }
}

internal static class PasswordRules
{
    public static IRuleBuilderOptions<T, string> NewPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Mật khẩu mới không được để trống.")
            .MinimumLength(6).WithMessage("Mật khẩu mới phải chứa ít nhất 6 ký tự.")
            .MaximumLength(100).WithMessage("Mật khẩu mới không được vượt quá 100 ký tự.");
}

public class ForgotPasswordValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordValidator()
    {
        RuleFor(x => x.Identifier)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Vui lòng nhập email hoặc số điện thoại.")
            .Must(raw => LoginIdentifier.Resolve(raw).IsValid).WithMessage("Email hoặc số điện thoại không hợp lệ.");

        RuleFor(x => x.App)
            .Must(app => string.IsNullOrWhiteSpace(app) || app is PasswordResetApps.Admin or PasswordResetApps.Pos)
            .WithMessage("Ứng dụng gửi yêu cầu chỉ được là 'admin' hoặc 'pos'.");
    }
}

public class ResetPasswordValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Thiếu mã đặt lại mật khẩu.")
            .MaximumLength(512).WithMessage("Mã đặt lại mật khẩu không hợp lệ.");
        RuleFor(x => x.NewPassword).NewPassword();
    }
}

public class AdminResetPasswordValidator : AbstractValidator<AdminResetPasswordRequest>
{
    public AdminResetPasswordValidator()
    {
        RuleFor(x => x.NewPassword).NewPassword();
    }
}
