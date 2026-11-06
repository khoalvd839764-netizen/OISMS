using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using OISM.Infrastructure.Persistence;

namespace OISM.Infrastructure.Health;

public record ReadinessCheckResult(
    bool IsHealthy,
    int StatusCode,
    string Status,
    string Description,
    string? CurrentUser = null,
    int? TablesCount = null,
    IReadOnlyList<string>? MissingTables = null,
    bool? TriggerVerified = null,
    string? ErrorType = null,
    DateTime? Timestamp = null
);

public interface IDatabaseReadinessService
{
    Task<ReadinessCheckResult> CheckReadinessAsync(CancellationToken ct = default);
}

public class NpgsqlDatabaseReadinessService : IDatabaseReadinessService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<NpgsqlDatabaseReadinessService> _logger;

    public NpgsqlDatabaseReadinessService(IConfiguration configuration, ILogger<NpgsqlDatabaseReadinessService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public static NpgsqlConnectionStringBuilder BuildConnectionStringBuilder(IConfiguration configuration)
    {
        return DatabaseConnectionHelper.BuildConnectionStringBuilder(configuration);
    }

    public async Task<ReadinessCheckResult> CheckReadinessAsync(CancellationToken ct = default)
    {
        NpgsqlConnectionStringBuilder connBuilder;
        try
        {
            connBuilder = BuildConnectionStringBuilder(_configuration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to construct database connection builder.");
            return new ReadinessCheckResult(
                false, StatusCodes.Status503ServiceUnavailable, "Unhealthy",
                "Database connection configuration is invalid or missing.",
                ErrorType: ex.GetType().Name,
                Timestamp: DateTime.UtcNow);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            await using var connection = new NpgsqlConnection(connBuilder.ConnectionString);
            await connection.OpenAsync(cts.Token);

            // 1. Identify current connected user
            await using var userCmd = new NpgsqlCommand("SELECT current_user, session_user;", connection);
            await using var userReader = await userCmd.ExecuteReaderAsync(cts.Token);
            string currentUser = "unknown";
            if (await userReader.ReadAsync(cts.Token))
            {
                currentUser = userReader.GetString(0);
            }
            await userReader.CloseAsync();

            // 2. Security validation: Ensure runtime account has no direct or indirect admin rights or schema/table ownership
            await using var secCmd = new NpgsqlCommand(@"
                SELECT
                    (r.rolsuper OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication OR r.rolbypassrls) AS is_direct_admin,
                    EXISTS (
                        SELECT 1 FROM pg_roles ar
                        WHERE (ar.rolsuper OR ar.rolcreatedb OR ar.rolcreaterole OR ar.rolreplication OR ar.rolbypassrls OR ar.rolname IN (
                            'pg_read_all_data', 'pg_write_all_data', 'pg_database_owner',
                            'pg_execute_server_program', 'pg_read_server_files', 'pg_write_server_files'
                        )) AND pg_has_role(current_user, ar.oid, 'member')
                    ) AS inherits_admin,
                    EXISTS (
                        SELECT 1 FROM pg_namespace n
                        WHERE n.nspname = 'public'
                          AND (n.nspowner = (SELECT oid FROM pg_roles WHERE rolname = current_user)
                               OR pg_has_role(current_user, n.nspowner, 'member'))
                    ) AS owns_schema,
                    EXISTS (
                        SELECT 1 FROM pg_class c
                        JOIN pg_namespace n ON c.relnamespace = n.oid
                        WHERE n.nspname = 'public' AND c.relkind = 'r'
                          AND (c.relowner = (SELECT oid FROM pg_roles WHERE rolname = current_user)
                               OR pg_has_role(current_user, c.relowner, 'member'))
                    ) AS owns_table
                FROM pg_roles r
                WHERE r.rolname = current_user;
            ", connection);

            await using var secReader = await secCmd.ExecuteReaderAsync(cts.Token);
            bool isDirectAdmin = false, inheritsAdmin = false, ownsSchema = false, ownsTable = false;
            if (await secReader.ReadAsync(cts.Token))
            {
                isDirectAdmin = secReader.GetBoolean(0);
                inheritsAdmin = secReader.GetBoolean(1);
                ownsSchema = secReader.GetBoolean(2);
                ownsTable = secReader.GetBoolean(3);
            }
            await secReader.CloseAsync();

            if (isDirectAdmin || inheritsAdmin || ownsSchema || ownsTable)
            {
                _logger.LogWarning("Security violation: Runtime user '{User}' has administrative privileges (direct={Direct}, inherited={Inherited}) or owns schema/tables.",
                    currentUser, isDirectAdmin, inheritsAdmin);
                return new ReadinessCheckResult(
                    false, StatusCodes.Status503ServiceUnavailable, "Degraded",
                    "Security alert: Connected database user possesses direct or inherited administrative privileges or owns schema/tables. Least-privilege runtime account required.",
                    CurrentUser: currentUser,
                    Timestamp: DateTime.UtcNow);
            }

            // 3. Schema completeness check: verify presence of all 29 required tables in public schema
            await using var tableCmd = new NpgsqlCommand(
                "SELECT table_name FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE';",
                connection);
            await using var tableReader = await tableCmd.ExecuteReaderAsync(cts.Token);
            var existingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (await tableReader.ReadAsync(cts.Token))
            {
                existingTables.Add(tableReader.GetString(0));
            }
            await tableReader.CloseAsync();

            var missingTables = DatabaseMigrationService.RequiredTables.Where(t => !existingTables.Contains(t)).ToList();
            if (missingTables.Count > 0)
            {
                return new ReadinessCheckResult(
                    false, StatusCodes.Status503ServiceUnavailable, "Unhealthy",
                    $"Database schema is incomplete ({existingTables.Count} of {DatabaseMigrationService.RequiredTables.Length} base tables found). Missing: {string.Join(", ", missingTables)}. Database migration required.",
                    CurrentUser: currentUser,
                    TablesCount: existingTables.Count,
                    MissingTables: missingTables,
                    Timestamp: DateTime.UtcNow);
            }

            // 4. Critical trigger check: verify trigger 'ledger_append_only' on 'public.inventory_transactions' is enabled
            await using var triggerCmd = new NpgsqlCommand(@"
                SELECT count(*)::int
                FROM pg_trigger tg
                JOIN pg_class c ON tg.tgrelid = c.oid
                JOIN pg_namespace n ON c.relnamespace = n.oid
                WHERE n.nspname = 'public'
                  AND c.relname = 'inventory_transactions'
                  AND tg.tgname = 'ledger_append_only'
                  AND tg.tgenabled IN ('O', 'A')
                  AND NOT tg.tgisinternal;
            ", connection);

            var triggerCount = (int)(await triggerCmd.ExecuteScalarAsync(cts.Token) ?? 0);
            if (triggerCount == 0)
            {
                return new ReadinessCheckResult(
                    false, StatusCodes.Status503ServiceUnavailable, "Unhealthy",
                    "Critical trigger 'ledger_append_only' on 'public.inventory_transactions' is missing or disabled. Database migration required.",
                    CurrentUser: currentUser,
                    TablesCount: existingTables.Count,
                    TriggerVerified: false,
                    Timestamp: DateTime.UtcNow);
            }

            // 5. Query permission verification: verify runtime can query business tables
            await using var pingCmd = new NpgsqlCommand("SELECT count(*)::int FROM tenants;", connection);
            await pingCmd.ExecuteScalarAsync(cts.Token);

            return new ReadinessCheckResult(
                true, StatusCodes.Status200OK, "Healthy",
                $"Database connection, schema completeness ({DatabaseMigrationService.RequiredTables.Length} tables), trigger immutability, and runtime permissions verified.",
                CurrentUser: currentUser,
                TablesCount: existingTables.Count,
                TriggerVerified: true,
                Timestamp: DateTime.UtcNow);
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("Database readiness check timed out after 5 seconds.");
            return new ReadinessCheckResult(
                false, StatusCodes.Status503ServiceUnavailable, "Unhealthy",
                "Database readiness check timed out. PostgreSQL service is not responding.",
                ErrorType: nameof(OperationCanceledException),
                Timestamp: DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            // Log detailed exception internally without leaking connection string or secrets to client
            _logger.LogError(ex, "Database readiness check failed.");
            return new ReadinessCheckResult(
                false, StatusCodes.Status503ServiceUnavailable, "Unhealthy",
                "Database connection or validation failed. PostgreSQL service is not ready.",
                ErrorType: ex.GetType().Name,
                Timestamp: DateTime.UtcNow);
        }
    }
}
