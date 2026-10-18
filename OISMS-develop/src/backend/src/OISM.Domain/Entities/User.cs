using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho tài khoản người dùng thuộc về một Tenant trong hệ thống.
/// </summary>
public class User : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }

    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public string FullName
    {
        get => DisplayName;
        set => DisplayName = value;
    }

    public string? Phone { get; set; }
    public string? PhoneNumber
    {
        get => Phone;
        set => Phone = value;
    }

    /// <summary>
    /// Vai trò người dùng theo schema v0.5: 'Owner', 'Staff', 'Cashier'.
    /// </summary>
    public string Role { get; set; } = "Staff";

    /// <summary>
    /// Chuỗi băm mật khẩu bảo mật BCrypt theo chuẩn NFR-SEC-01.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
