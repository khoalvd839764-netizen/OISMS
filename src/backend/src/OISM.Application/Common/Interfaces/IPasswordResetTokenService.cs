namespace OISM.Application.Common.Interfaces;

/// <summary>
/// Nội dung đọc được từ token đặt lại mật khẩu sau khi đã kiểm tra chữ ký.
/// </summary>
public sealed record PasswordResetTokenPayload(Guid UserId, DateTimeOffset ExpiresAt, string Fingerprint);

/// <summary>
/// Token đặt lại mật khẩu tự ký, không lưu trong database (Upgrade 10, cách B).
/// Token gắn dấu vân tay của password_hash hiện tại: đổi mật khẩu xong thì mọi token cũ hết hiệu lực.
/// </summary>
public interface IPasswordResetTokenService
{
    /// <summary>Có khóa ký hợp lệ để phát hành token hay không.</summary>
    bool IsConfigured { get; }

    TimeSpan Lifetime { get; }

    string CreateToken(Guid userId, string passwordHash, DateTimeOffset expiresAt);

    /// <summary>
    /// Kiểm tra định dạng và chữ ký. Trả null nếu token sai hoặc bị sửa; không kiểm tra hạn dùng.
    /// </summary>
    PasswordResetTokenPayload? ReadToken(string token);

    /// <summary>So dấu vân tay trong token với password_hash hiện tại, so sánh thời gian hằng.</summary>
    bool MatchesPasswordHash(PasswordResetTokenPayload payload, string passwordHash);
}
