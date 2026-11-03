using FluentValidation;
using OISM.Application.Common.Helpers;

namespace OISM.Application.Features.Users;

public class UserResponse
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class CreateUserRequest
{
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = "Staff";
}

public class UpdateUserRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Role { get; set; } = "Staff";
    public bool IsActive { get; set; } = true;
}

public class UpdateUserStatusRequest
{
    public bool IsActive { get; set; }
}

public class CreateUserValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Họ và tên nhân viên không được để trống.")
            .MaximumLength(150).WithMessage("Họ và tên không được vượt quá 150 ký tự.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email không được để trống.")
            .EmailAddress().WithMessage("Định dạng email không hợp lệ.")
            .MaximumLength(254).WithMessage("Email không được vượt quá 254 ký tự.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Mật khẩu không được để trống.")
            .MinimumLength(6).WithMessage("Mật khẩu phải chứa ít nhất 6 ký tự.")
            .MaximumLength(100).WithMessage("Mật khẩu không được vượt quá 100 ký tự.");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Vai trò không được để trống.")
            .Must(r => r == "Staff" || r == "Cashier")
            .WithMessage("Vai trò nhân viên chỉ được phép là 'Staff' hoặc 'Cashier'.");

        When(x => !string.IsNullOrWhiteSpace(x.Phone), () =>
        {
            RuleFor(x => x.Phone)
                .Must(PhoneHelper.IsValidPhoneNumber).WithMessage("Định dạng số điện thoại không hợp lệ.");
        });
    }
}

public class UpdateUserValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserValidator()
    {
        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Họ và tên nhân viên không được để trống.")
            .MaximumLength(150).WithMessage("Họ và tên không được vượt quá 150 ký tự.");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Vai trò không được để trống.")
            .Must(r => r == "Owner" || r == "Staff" || r == "Cashier")
            .WithMessage("Vai trò chỉ được phép là 'Owner', 'Staff' hoặc 'Cashier'.");

        When(x => !string.IsNullOrWhiteSpace(x.Phone), () =>
        {
            RuleFor(x => x.Phone)
                .Must(PhoneHelper.IsValidPhoneNumber).WithMessage("Định dạng số điện thoại không hợp lệ.");
        });
    }
}
