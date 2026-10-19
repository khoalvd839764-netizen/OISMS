using System.Security.Claims;
using OISM.Domain.Entities;

namespace OISM.Application.Common.Interfaces;

/// <summary>
/// Giao diện xử lý sinh và xác thực mã JWT Access Token và Refresh Token.
/// </summary>
public interface IJwtTokenService
{
    string GenerateAccessToken(User user, IEnumerable<string> roles);
    RefreshToken GenerateRefreshToken(Guid tenantId, Guid userId);
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
}
