using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Quản lý chu kỳ sống của Refresh Token phục vụ cấp mới JWT Access Token an toàn.
/// </summary>
public class RefreshToken : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }

    /// <summary>
    /// Chuỗi băm SHA-256 của Refresh Token (lưu trong cột token_hash varchar(128)).
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Raw token chỉ lưu tạm thời trong bộ nhớ để trả về client, không lưu plain text vào database.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string Token { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string? ReplacedByToken { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool IsRevoked
    {
        get => RevokedAt != null;
        set
        {
            if (value && RevokedAt == null) RevokedAt = DateTimeOffset.UtcNow;
            else if (!value) RevokedAt = null;
        }
    }

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    public bool IsActive => !IsRevoked && !IsExpired;

    public static string HashToken(string token)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // Navigation properties
    public User? User { get; set; }
}
