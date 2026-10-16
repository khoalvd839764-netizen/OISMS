using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using OISM.Application.Common.Interfaces;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Services;

/// <summary>
/// Dịch vụ cấp phát và kiểm tra JWT Access Token và Refresh Token bảo mật.
/// Đảm bảo token chứa đầy đủ TenantId, UserId, Email, Role theo chuẩn FR-AUTH-02.
/// </summary>
public class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string GenerateAccessToken(User user, IEnumerable<string> roles)
    {
        var secret = _configuration["Jwt:Secret"] ?? "SuperSecretKeyForOISMOmnichannelRetailSystem2026!#";
        var issuer = _configuration["Jwt:Issuer"] ?? "OISM_WebAPI";
        var audience = _configuration["Jwt:Audience"] ?? "OISM_Client";
        var expiryMinutes = int.TryParse(_configuration["Jwt:ExpiryMinutes"], out var mins) ? mins : 60;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("userId", user.Id.ToString()),
            new("tenantId", user.TenantId.ToString()),
            new("name", user.FullName)
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
            claims.Add(new Claim("role", role));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public RefreshToken GenerateRefreshToken(Guid tenantId, Guid userId)
    {
        var randomNumber = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);

        return new RefreshToken
        {
            TenantId = tenantId,
            UserId = userId,
            Token = Convert.ToBase64String(randomNumber),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7), // Refresh token có hiệu lực 7 ngày
            IsRevoked = false
        };
    }

    public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
    {
        var secret = _configuration["Jwt:Secret"] ?? "SuperSecretKeyForOISMOmnichannelRetailSystem2026!#";
        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = false,
            ValidateIssuer = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidateLifetime = false // Không kiểm tra thời gian hết hạn vì đang lấy từ expired token
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out var securityToken);

        if (securityToken is not JwtSecurityToken jwtSecurityToken ||
            !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
        {
            throw new SecurityTokenException("Token không đúng định dạng bảo mật.");
        }

        return principal;
    }
}
