namespace OISM.Application.Features.Auth.Login;

/// <summary>
/// Kết quả trả về sau khi xác thực đăng nhập hoặc làm mới token thành công.
/// </summary>
public class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; } = 3600; // 60 phút tính theo giây
    public UserProfileDto User { get; set; } = new();
}

public class UserProfileDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string TenantCode { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public List<string> Roles { get; set; } = new();
}
