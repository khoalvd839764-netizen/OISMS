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

    /// <summary>
    /// Nâng cấp schema không phá hủy (non-destructive upgrade) cho cơ sở dữ liệu v0.5 hiện hữu:
    /// Bổ sung cột request_key, payload_signature và unique constraint uq_stock_transfers_3 cho bảng stock_transfers nếu chưa có.
    /// </summary>
    Task<MigrationExecutionResult> UpgradeDatabaseSchemaAsync(
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
