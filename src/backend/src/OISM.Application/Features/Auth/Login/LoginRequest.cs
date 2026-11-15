namespace OISM.Application.Features.Auth.Login;

/// <summary>
/// Yêu cầu đăng nhập tài khoản.
/// </summary>
public class LoginRequest
{
    /// <summary>
    /// Email hoặc số điện thoại đăng nhập (Upgrade 10).
    /// </summary>
    public string? Identifier { get; set; }

    /// <summary>
    /// Trường cũ, giữ để client và test cũ không vỡ. Chỉ dùng khi <see cref="Identifier"/> rỗng.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Mã cửa hàng (nếu có để chỉ định rõ đăng nhập vào tenant nào).
    /// </summary>
    public string? TenantCode { get; set; }

    public string RawIdentifier => string.IsNullOrWhiteSpace(Identifier) ? Email : Identifier;
}
