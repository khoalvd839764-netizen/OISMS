using System.Security.Cryptography;
using System.Text;

namespace OISM.Application.Features.Webhooks.Services;

/// <summary>
/// Helper băm và xác thực Webhook Secret của kênh tích hợp gian hàng sàn TMĐT.
/// Đảm bảo không lưu plaintext secret trong CSDL và so sánh bằng thuật toán thời gian hằng số (constant-time) chống timing attacks.
/// </summary>
public static class WebhookSecretHasher
{
    private const string SaltPrefix = "OISMS_WEBHOOK_SECRET_SALT_V1:";

    public static string HashSecret(string rawSecret)
    {
        if (string.IsNullOrWhiteSpace(rawSecret))
        {
            throw new ArgumentException("Webhook Secret không được để trống.", nameof(rawSecret));
        }

        var bytes = Encoding.UTF8.GetBytes(SaltPrefix + rawSecret.Trim());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool VerifySecret(string rawSecret, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(rawSecret) || string.IsNullOrWhiteSpace(expectedHash))
        {
            return false;
        }

        var computedHash = HashSecret(rawSecret);
        var computedBytes = Encoding.UTF8.GetBytes(computedHash);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedHash.Trim().ToLowerInvariant());

        return CryptographicOperations.FixedTimeEquals(computedBytes, expectedBytes);
    }
}
