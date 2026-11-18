using System.Security.Cryptography;
using System.Text;

namespace OISM.Infrastructure.Services;

/// <summary>
/// Khóa ký JWT đã qua kiểm tra. <see cref="Warning"/> có giá trị khi Development hoặc Test đang dùng khóa tạm.
/// </summary>
public sealed record JwtSigningKey(byte[] Bytes, string? Warning = null);

/// <summary>
/// Chính sách chọn khóa ký JWT theo môi trường:
/// 1. Khóa hợp lệ phải dài tối thiểu 32 byte và không phải khóa mẫu đã lộ trong lịch sử git.
/// 2. Development và Test thiếu khóa hợp lệ thì dùng khóa ngẫu nhiên theo tiến trình.
/// 3. Mọi môi trường khác dừng khởi động, tuyệt đối không fallback sang khóa viết cứng.
/// </summary>
public static class JwtSigningKeyPolicy
{
    public const int MinimumKeyBytes = 32;

    private static readonly string[] EphemeralKeyEnvironments = ["Development", "Test", "Testing"];

    // Lưu dấu vân tay thay vì giá trị để khóa đã lộ không xuất hiện lại trong source.
    private const string LeakedSampleKeySha256 = "4ab71af269b5e56ab5c34f39df54ed76d66fc2057d69c341d62f5026b885024c";

    // Dùng chung trong tiến trình để nhiều host cùng tiến trình (integration test) xác thực được token của nhau.
    private static readonly Lazy<byte[]> EphemeralKey = new(() => RandomNumberGenerator.GetBytes(MinimumKeyBytes));

    public static JwtSigningKey Resolve(string? configuredKey, string? environmentName)
    {
        var problem = FindProblem(configuredKey);
        if (problem == null)
        {
            return new JwtSigningKey(Encoding.UTF8.GetBytes(configuredKey!));
        }

        if (AllowsEphemeralKey(environmentName))
        {
            return new JwtSigningKey(
                EphemeralKey.Value,
                $"Jwt:Secret {problem}. Môi trường {environmentName} dùng khóa tạm, khởi động lại backend thì phải đăng nhập lại.");
        }

        throw new InvalidOperationException(
            $"Jwt:Secret {problem}. Hãy đặt biến JWT_SECRET bằng khóa ngẫu nhiên, ví dụ tạo bằng 'openssl rand -hex 32'.");
    }

    private static string? FindProblem(string? configuredKey)
    {
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            return "chưa được cấu hình";
        }

        var keyBytes = Encoding.UTF8.GetBytes(configuredKey);
        if (keyBytes.Length < MinimumKeyBytes)
        {
            return $"ngắn hơn {MinimumKeyBytes} byte";
        }

        var fingerprint = Convert.ToHexString(SHA256.HashData(keyBytes));
        return string.Equals(fingerprint, LeakedSampleKeySha256, StringComparison.OrdinalIgnoreCase)
            ? "đang dùng khóa mẫu đã công khai trong repo"
            : null;
    }

    private static bool AllowsEphemeralKey(string? environmentName) =>
        EphemeralKeyEnvironments.Any(env => string.Equals(env, environmentName, StringComparison.OrdinalIgnoreCase));
}
