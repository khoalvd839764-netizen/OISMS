using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Quản lý chu kỳ sống của Refresh Token phục vụ cấp mới JWT Access Token an toàn.
/// </summary>
public class RefreshToken : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }

    public string Token { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public bool IsRevoked { get; set; } = false;
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByToken { get; set; }

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    public bool IsActive => !IsRevoked && !IsExpired;

    // Navigation properties
    public User? User { get; set; }
}
