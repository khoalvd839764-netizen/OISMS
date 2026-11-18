using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.PasswordReset;

namespace OISM.Infrastructure.Services;

/// <summary>
/// Token đặt lại mật khẩu ký HMAC-SHA256, không lưu database (Upgrade 10).
/// Cấu trúc (base64url): userId(16) | hạn dùng unix giây(8) | dấu vân tay password_hash(16) | chữ ký(32).
/// </summary>
public sealed class PasswordResetTokenService : IPasswordResetTokenService
{
    private const int MinimumKeyLength = 32;
    private const int PayloadLength = 16 + 8 + FingerprintLength;
    private const int FingerprintLength = 16;
    private const int SignatureLength = 32;
    private const byte SignaturePurpose = 1;
    private const byte FingerprintPurpose = 2;

    private readonly byte[]? _key;

    public PasswordResetTokenService(
        IOptions<PasswordResetOptions> options,
        IHostEnvironment environment,
        ILogger<PasswordResetTokenService> logger)
    {
        var configured = options.Value.SigningKey;
        Lifetime = TimeSpan.FromMinutes(Math.Clamp(options.Value.TokenLifetimeMinutes, 5, 24 * 60));

        if (!string.IsNullOrEmpty(configured) && configured.Length >= MinimumKeyLength)
        {
            _key = Encoding.UTF8.GetBytes(configured);
        }
        else if (environment.IsDevelopment())
        {
            // Khóa tạm cho máy dev: khởi động lại backend thì link cũ hết hiệu lực, chấp nhận được.
            _key = RandomNumberGenerator.GetBytes(32);
        }
        else
        {
            logger.LogWarning("PasswordReset:SigningKey chưa đặt hoặc ngắn hơn {Length} ký tự. Tính năng quên mật khẩu bị tắt.", MinimumKeyLength);
        }
    }

    public bool IsConfigured => _key != null;

    public TimeSpan Lifetime { get; }

    public string CreateToken(Guid userId, string passwordHash, DateTimeOffset expiresAt)
    {
        var key = _key ?? throw new InvalidOperationException("Password reset signing key is not configured.");
        var buffer = new byte[PayloadLength + SignatureLength];
        userId.TryWriteBytes(buffer.AsSpan(0, 16));
        BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(16, 8), expiresAt.ToUnixTimeSeconds());
        Fingerprint(key, passwordHash).CopyTo(buffer.AsSpan(24, FingerprintLength));
        Sign(key, buffer.AsSpan(0, PayloadLength)).CopyTo(buffer.AsSpan(PayloadLength));
        return Base64UrlEncode(buffer);
    }

    public PasswordResetTokenPayload? ReadToken(string token)
    {
        if (_key == null || string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var buffer = Base64UrlDecode(token.Trim());
        if (buffer == null || buffer.Length != PayloadLength + SignatureLength)
        {
            return null;
        }

        var payload = buffer.AsSpan(0, PayloadLength);
        if (!CryptographicOperations.FixedTimeEquals(Sign(_key, payload), buffer.AsSpan(PayloadLength)))
        {
            return null;
        }

        var userId = new Guid(payload[..16]);
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt64BigEndian(payload.Slice(16, 8)));
        var fingerprint = Convert.ToHexString(payload.Slice(24, FingerprintLength));
        return new PasswordResetTokenPayload(userId, expiresAt, fingerprint);
    }

    public bool MatchesPasswordHash(PasswordResetTokenPayload payload, string passwordHash)
    {
        if (_key == null)
        {
            return false;
        }

        var expected = Convert.FromHexString(payload.Fingerprint);
        return CryptographicOperations.FixedTimeEquals(expected, Fingerprint(_key, passwordHash));
    }

    private static byte[] Sign(byte[] key, ReadOnlySpan<byte> payload)
    {
        var input = new byte[payload.Length + 1];
        input[0] = SignaturePurpose;
        payload.CopyTo(input.AsSpan(1));
        return HMACSHA256.HashData(key, input);
    }

    private static byte[] Fingerprint(byte[] key, string passwordHash)
    {
        var hashBytes = Encoding.UTF8.GetBytes(passwordHash);
        var input = new byte[hashBytes.Length + 1];
        input[0] = FingerprintPurpose;
        hashBytes.CopyTo(input, 1);
        return HMACSHA256.HashData(key, input)[..FingerprintLength];
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[]? Base64UrlDecode(string value)
    {
        if (value.Length > 256)
        {
            return null;
        }

        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
