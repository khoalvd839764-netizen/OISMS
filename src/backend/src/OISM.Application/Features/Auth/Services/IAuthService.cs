using OISM.Application.Features.Auth.CurrentUser;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.RefreshToken;
using OISM.Application.Features.Auth.RegisterTenant;

namespace OISM.Application.Features.Auth.Services;

/// <summary>
/// Giao diện xử lý nghiệp vụ Xác thực và Quản trị Người dùng/Tenant (FR-AUTH-01, FR-AUTH-02, NFR-SEC-01).
/// </summary>
public interface IAuthService
{
    Task<RegisterTenantResponse> RegisterTenantAsync(RegisterTenantRequest request, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken ct = default);
    Task RevokeTokenAsync(RefreshTokenRequest request, CancellationToken ct = default);
    Task<CurrentUserResponse?> GetCurrentUserAsync(Guid userId, CancellationToken ct = default);
}
