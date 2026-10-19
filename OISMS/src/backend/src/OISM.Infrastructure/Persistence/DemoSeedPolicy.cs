namespace OISM.Infrastructure.Persistence;

/// <summary>
/// Kết quả đánh giá tính hợp lệ của việc khởi tạo dữ liệu mẫu (Seed Demo Data).
/// </summary>
public record SeedEligibilityResult(
    bool ShouldSeed,
    string Reason,
    bool IsConfigurationError = false);

/// <summary>
/// Chính sách kiểm soát khởi tạo dữ liệu mẫu theo môi trường và cấu hình.
/// Quy định:
/// 1. Chỉ Development và Test được phép seed demo.
/// 2. Production, Staging và bất kỳ môi trường nào khác tuyệt đối KHÔNG được seed, kể cả khi bật nhầm cấu hình.
/// 3. Mặc định KHÔNG seed; phải được bật chủ động qua cấu hình 'Database:SeedDemoData' = true.
/// 4. Khi được phép seed nhưng thiếu cấu hình mật khẩu demo ('Database:DemoSeedPassword'),
///    báo lỗi cấu hình rõ ràng trước khi ghi dữ liệu; tuyệt đối KHÔNG fallback sang mật khẩu hardcode.
/// </summary>
public static class DemoSeedPolicy
{
    private static readonly string[] AllowedEnvironments = ["Development", "Test", "Testing"];

    public static bool IsEnvironmentAllowed(string? environmentName)
    {
        if (string.IsNullOrWhiteSpace(environmentName)) return false;
        return AllowedEnvironments.Any(env => string.Equals(env, environmentName, StringComparison.OrdinalIgnoreCase));
    }

    public static SeedEligibilityResult Evaluate(
        string? environmentName,
        bool seedConfigEnabled,
        string? configuredPassword)
    {
        if (!IsEnvironmentAllowed(environmentName))
        {
            return new SeedEligibilityResult(
                ShouldSeed: false,
                Reason: $"Môi trường '{environmentName ?? "Unknown"}' không được phép chạy seed demo (chỉ cho phép Development và Test).");
        }

        if (!seedConfigEnabled)
        {
            return new SeedEligibilityResult(
                ShouldSeed: false,
                Reason: "Cấu hình 'Database:SeedDemoData' chưa được bật (mặc định false).");
        }

        if (string.IsNullOrWhiteSpace(configuredPassword))
        {
            return new SeedEligibilityResult(
                ShouldSeed: false,
                Reason: "Được phép seed nhưng thiếu cấu hình 'Database:DemoSeedPassword'. Hệ thống từ chối fallback sang mật khẩu mặc định.",
                IsConfigurationError: true);
        }

        return new SeedEligibilityResult(
            ShouldSeed: true,
            Reason: "Đủ điều kiện khởi tạo dữ liệu mẫu.");
    }
}
