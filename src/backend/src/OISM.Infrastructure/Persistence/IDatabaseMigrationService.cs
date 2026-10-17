namespace OISM.Infrastructure.Persistence;

public interface IDatabaseMigrationService
{
    /// <summary>
    /// Tiếp nhận database hiện có (adopt baseline) hoặc cài đặt schema v0.5 mới hoàn chỉnh.
    /// Kiểm tra tính toàn vẹn 29 bảng, trigger, và quyền runtime trước khi ghi nhận baseline.
    /// </summary>
    Task<MigrationExecutionResult> InitializeOrBaselineDatabaseAsync(
        string adminConnectionString,
        CancellationToken cancellationToken = default);
}

public record MigrationExecutionResult(
    bool Success,
    bool IsFreshInstall,
    bool IsBaselineAdopted,
    int TableCount,
    string Message,
    IReadOnlyList<string>? Errors = null);
