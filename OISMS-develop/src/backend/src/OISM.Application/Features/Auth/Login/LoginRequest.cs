namespace OISM.Application.Features.Auth.Login;

/// <summary>
/// Yêu cầu đăng nhập tài khoản.
/// </summary>
public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Mã cửa hàng (nếu có để chỉ định rõ đăng nhập vào tenant nào).
    /// </summary>
    public string? TenantCode { get; set; }
}
