namespace OISM.Application.Features.Auth.RegisterTenant;

/// <summary>
/// Yêu cầu đăng ký Cửa hàng (Tenant) mới kèm tài khoản Chủ shop (Owner).
/// </summary>
public class RegisterTenantRequest
{
    public string TenantName { get; set; } = string.Empty;
    public string TenantCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string Password { get; set; } = string.Empty;
}
