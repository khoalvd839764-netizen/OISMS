using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OISM.Application.Common.Interfaces;
using OISM.Infrastructure.Persistence;

namespace OISM.IntegrationTests;

/// <summary>
/// Quản lý môi trường cơ sở dữ liệu PostgreSQL cho Integration Tests.
/// Các đặc tính an toàn:
/// 1. Cách ly hoàn toàn khỏi PostgreSQL làm việc: Sử dụng database riêng (mặc định oisms_test) và role riêng (mặc định oisms_test_app).
/// 2. Cơ chế bảo vệ mục tiêu (Target Verification Guard): Nghiêm cấm chạy test hoặc truncate trên database làm việc (oisms) hoặc hệ thống (postgres).
/// 3. Marker xác thực: Chỉ dọn dẹp database có chứa marker xác thực do fixture tạo ra.
/// 4. Bảo toàn role có sẵn: Nếu role đã tồn tại trong cluster, tuyệt đối không tự ý đổi mật khẩu hay thu hồi quyền.
/// 5. Xử lý an toàn chuỗi kết nối và ký tự đặc biệt: Sử dụng NpgsqlConnectionStringBuilder và format('%I', ...)/format('%L', ...).
/// </summary>
public static class PostgresTestEnvironment
{
    public static readonly string RunId = Guid.NewGuid().ToString("N");
    private static readonly string UniqueRunDatabaseName = $"oisms_test_{RunId[..12]}";

    public static string Host => Environment.GetEnvironmentVariable("OISM_TEST_HOST") ?? "localhost";
    public static int Port => int.TryParse(Environment.GetEnvironmentVariable("OISM_TEST_PORT"), out var p) ? p : 5432;
    public static string DatabaseName => Environment.GetEnvironmentVariable("OISM_TEST_DATABASE") ?? UniqueRunDatabaseName;
    public static string AdminUser => Environment.GetEnvironmentVariable("OISM_TEST_ADMIN_USER") ?? "postgres";
    public static string AdminPassword => Environment.GetEnvironmentVariable("OISM_TEST_ADMIN_PASSWORD") ?? "admin123";
    public static string AppUser => Environment.GetEnvironmentVariable("OISM_TEST_APP_USER") ?? "oisms_test_app";
    public static string AppPassword => Environment.GetEnvironmentVariable("OISM_TEST_APP_PASSWORD") ?? "TestPassword_2026!#$";

    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> CreatedDatabases = new(StringComparer.OrdinalIgnoreCase);
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> CreatedRoles = new(StringComparer.OrdinalIgnoreCase);

    public static string BuildConnectionString(string host, int port, string database, string username, string password, int timeout = 15, int commandTimeout = 30)
    {
        var b = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = database,
            Username = username,
            Password = password,
            Timeout = timeout,
            CommandTimeout = commandTimeout
        };
        return b.ConnectionString;
    }

    public static string AdminServerConnectionString =>
        Environment.GetEnvironmentVariable("OISM_TEST_ADMIN_CONNECTION")
        ?? BuildConnectionString(Host, Port, "postgres", AdminUser, AdminPassword);

    public static string AdminDatabaseConnectionString =>
        BuildConnectionString(Host, Port, DatabaseName, AdminUser, AdminPassword);

    public static string RuntimeConnectionString =>
        Environment.GetEnvironmentVariable("OISM_TEST_APP_CONNECTION")
        ?? BuildConnectionString(Host, Port, DatabaseName, AppUser, AppPassword);

    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static bool _isInitialized;

    public static string ExtractDatabaseName(string databaseNameOrConnectionString)
    {
        if (string.IsNullOrWhiteSpace(databaseNameOrConnectionString))
        {
            throw new InvalidOperationException("Tên cơ sở dữ liệu kiểm thử hoặc chuỗi kết nối không được để trống.");
        }

        if (databaseNameOrConnectionString.Contains('='))
        {
            try
            {
                var csb = new NpgsqlConnectionStringBuilder(databaseNameOrConnectionString);
                if (!string.IsNullOrWhiteSpace(csb.Database))
                {
                    return csb.Database.Trim();
                }
            }
            catch
            {
                // Fallback nếu không parse được chuỗi kết nối
            }
        }

        return databaseNameOrConnectionString.Trim();
    }

    public static void AssertSafeTestTarget(string databaseNameOrConnectionString)
    {
        var databaseName = ExtractDatabaseName(databaseNameOrConnectionString);

        string[] forbiddenDatabases = ["oisms", "postgres", "template0", "template1", "master", "model", "msdb"];
        if (forbiddenDatabases.Contains(databaseName, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cảnh báo an toàn dữ liệu: Nghiêm cấm thực hiện thao tác kiểm thử/xóa dữ liệu trên cơ sở dữ liệu làm việc hoặc hệ thống '{databaseName}'.");
        }

        if (!databaseName.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cơ sở dữ liệu '{databaseName}' không hợp lệ cho kiểm thử. Tên database kiểm thử bắt buộc phải chứa từ khóa 'test'.");
        }
    }

    /// <summary>
    /// Xác minh database mục tiêu thực sự thuộc quyền quản lý của lượt kiểm thử hiện tại thông qua RunId.
    /// Từ chối trước mọi thao tác phá hủy nếu database không chứa marker hoặc marker thuộc lượt chạy khác.
    /// </summary>
    public static async Task AssertDatabaseOwnedByRunAsync(string adminConnectionString, string expectedRunId, CancellationToken cancellationToken = default)
    {
        var targetDb = ExtractDatabaseName(adminConnectionString);
        AssertSafeTestTarget(targetDb);

        await using var conn = new NpgsqlConnection(adminConnectionString);
        await conn.OpenAsync(cancellationToken);

        const string checkMarkerTableSql = @"
SELECT 1 FROM information_schema.tables 
WHERE table_schema = 'public' AND table_name = '_oisms_test_fixture_marker';";
        await using (var checkCmd = new NpgsqlCommand(checkMarkerTableSql, conn))
        {
            var hasMarkerTable = (await checkCmd.ExecuteScalarAsync(cancellationToken)) != null;
            if (!hasMarkerTable)
            {
                throw new InvalidOperationException(
                    $"Từ chối thao tác trên cơ sở dữ liệu '{targetDb}': Không tìm thấy bảng marker xác thực test fixture. Database này không thuộc quyền quản lý của fixture.");
            }
        }

        const string checkMarkerRunSql = @"
SELECT 1 FROM public._oisms_test_fixture_marker 
WHERE run_id = @runId;";
        await using (var checkRunCmd = new NpgsqlCommand(checkMarkerRunSql, conn))
        {
            checkRunCmd.Parameters.AddWithValue("runId", expectedRunId);
            var hasValidRun = (await checkRunCmd.ExecuteScalarAsync(cancellationToken)) != null;
            if (!hasValidRun)
            {
                throw new InvalidOperationException(
                    $"Từ chối thao tác trên cơ sở dữ liệu '{targetDb}': Marker không khớp với lượt kiểm thử hiện tại (RunId: '{expectedRunId}').");
            }
        }
    }

    public static async Task EnsureInitializedAsync(string? targetAdminConnectionString = null, CancellationToken cancellationToken = default)
    {
        var isDefault = string.IsNullOrWhiteSpace(targetAdminConnectionString);
        var targetConnStr = isDefault ? AdminDatabaseConnectionString : targetAdminConnectionString!;
        var targetDb = ExtractDatabaseName(targetConnStr);

        if (isDefault && _isInitialized) return;

        await Lock.WaitAsync(cancellationToken);
        try
        {
            if (isDefault && _isInitialized) return;

            AssertSafeTestTarget(targetConnStr);
            if (isDefault)
            {
                AssertSafeTestTarget(RuntimeConnectionString);
            }

            // 1. Kết nối Admin vào postgres server database để kiểm tra/tạo database test
            await using (var serverConn = new NpgsqlConnection(AdminServerConnectionString))
            {
                await serverConn.OpenAsync(cancellationToken);

                var dbExists = false;
                await using (var cmd = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @db", serverConn))
                {
                    cmd.Parameters.AddWithValue("db", targetDb);
                    dbExists = (await cmd.ExecuteScalarAsync(cancellationToken)) != null;
                }

                if (dbExists)
                {
                    // Nếu database đã tồn tại trước khi fixture này tạo trong lượt chạy
                    if (!CreatedDatabases.ContainsKey(targetDb))
                    {
                        var isOwned = false;
                        try
                        {
                            await using var testCheckConn = new NpgsqlConnection(targetConnStr);
                            await testCheckConn.OpenAsync(cancellationToken);
                            const string verifySql = @"
SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = '_oisms_test_fixture_marker'
AND EXISTS (SELECT 1 FROM public._oisms_test_fixture_marker WHERE run_id = @runId);";
                            await using var verifyCmd = new NpgsqlCommand(verifySql, testCheckConn);
                            verifyCmd.Parameters.AddWithValue("runId", RunId);
                            isOwned = (await verifyCmd.ExecuteScalarAsync(cancellationToken)) != null;
                        }
                        catch
                        {
                            isOwned = false;
                        }

                        if (!isOwned)
                        {
                            throw new InvalidOperationException(
                                $"Từ chối nhận diện cơ sở dữ liệu có sẵn '{targetDb}' làm tài nguyên fixture. Database này không được tạo bởi lượt kiểm thử hiện tại (RunId: '{RunId}') và chưa có marker xác thực hợp lệ.");
                        }
                    }
                }
                else
                {
                    // Database chưa tồn tại -> Fixture tự tạo mới và ghi nhận quyền sở hữu
                    var safeDbIdentifier = EscapeSqlIdentifier(targetDb);
                    await using var createCmd = new NpgsqlCommand($"CREATE DATABASE {safeDbIdentifier} ENCODING 'UTF8';", serverConn);
                    await createCmd.ExecuteNonQueryAsync(cancellationToken);
                    CreatedDatabases[targetDb] = true;
                }

                // 2. Tạo role test chuyên dụng nếu chưa có. NẾU ĐÃ CÓ: GIỮ NGUYÊN, KHÔNG ĐỔI MẬT KHẨU!
                var roleExists = false;
                await using (var checkRoleCmd = new NpgsqlCommand("SELECT 1 FROM pg_roles WHERE rolname = @user", serverConn))
                {
                    checkRoleCmd.Parameters.AddWithValue("user", AppUser);
                    roleExists = (await checkRoleCmd.ExecuteScalarAsync(cancellationToken)) != null;
                }

                if (!roleExists)
                {
                    var safeRole = EscapeSqlIdentifier(AppUser);
                    var safePass = EscapeSqlLiteral(AppPassword);
                    var createRoleSql = $"CREATE ROLE {safeRole} WITH LOGIN PASSWORD {safePass};";
                    await using var createRoleCmd = new NpgsqlCommand(createRoleSql, serverConn);
                    await createRoleCmd.ExecuteNonQueryAsync(cancellationToken);
                    CreatedRoles[AppUser] = true;
                }
            }

            // 3. Khởi tạo schema v0.5 và baseline bằng DatabaseMigrationService
            var migrationService = new DatabaseMigrationService();
            var migrationResult = await migrationService.InitializeOrBaselineDatabaseAsync(targetConnStr, cancellationToken);
            if (!migrationResult.Success)
            {
                throw new InvalidOperationException($"Không thể khởi tạo schema test database: {migrationResult.Message}. Chi tiết: {string.Join("; ", migrationResult.Errors ?? Array.Empty<string>())}");
            }

            // 4. Ghi marker xác thực test fixture gắn với RunId của lượt kiểm thử hiện tại
            await using (var dbConn = new NpgsqlConnection(targetConnStr))
            {
                await dbConn.OpenAsync(cancellationToken);

                const string markerSql = @"
CREATE TABLE IF NOT EXISTS public._oisms_test_fixture_marker (
    run_id text PRIMARY KEY,
    created_at timestamptz DEFAULT now()
);
INSERT INTO public._oisms_test_fixture_marker (run_id)
VALUES (@runId)
ON CONFLICT (run_id) DO NOTHING;";
                await using (var markerCmd = new NpgsqlCommand(markerSql, dbConn))
                {
                    markerCmd.Parameters.AddWithValue("runId", RunId);
                    await markerCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                // 5. Cấp quyền: CHỈ cấp cho role được tạo bởi fixture trong lượt chạy này!
                // Tuyệt đối không cấp thêm quyền cho role có sẵn ngoài phạm vi fixture!
                if (CreatedRoles.ContainsKey(AppUser))
                {
                    var grantSql = $@"
DO $$
BEGIN
    IF EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'oisms_runtime') THEN
        EXECUTE 'GRANT oisms_runtime TO ' || quote_ident({EscapeSqlLiteral(AppUser)});
    END IF;
    EXECUTE 'GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO ' || quote_ident({EscapeSqlLiteral(AppUser)});
END $$;";
                    await using var grantCmd = new NpgsqlCommand(grantSql, dbConn);
                    await grantCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            if (isDefault)
            {
                _isInitialized = true;
            }
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>
    /// Xóa sạch dữ liệu trong các bảng nghiệp vụ (TRUNCATE CASCADE) để mỗi ca kiểm thử hoàn toàn độc lập.
    /// Yêu cầu xác minh mục tiêu nghiêm ngặt và kiểm tra marker test fixture chứa đúng RunId trước khi thực hiện.
    /// Không xóa bảng __EFMigrationsHistory hoặc _oisms_test_fixture_marker.
    /// </summary>
    public static async Task ResetTestDataAsync(string? targetAdminConnectionString = null, CancellationToken cancellationToken = default)
    {
        var targetConnStr = string.IsNullOrWhiteSpace(targetAdminConnectionString)
            ? AdminDatabaseConnectionString
            : targetAdminConnectionString;

        AssertSafeTestTarget(targetConnStr);
        await EnsureInitializedAsync(targetConnStr, cancellationToken);

        // Xác minh quyền sở hữu: database phải thuộc RunId này trước khi reset
        await AssertDatabaseOwnedByRunAsync(targetConnStr, RunId, cancellationToken);

        await using var conn = new NpgsqlConnection(targetConnStr);
        await conn.OpenAsync(cancellationToken);

        const string truncateSql = @"
SET session_replication_role = 'replica';
DO $$
DECLARE
    r RECORD;
BEGIN
    FOR r IN (
        SELECT tablename 
        FROM pg_tables 
        WHERE schemaname = 'public' 
          AND tablename NOT IN ('__EFMigrationsHistory', '_oisms_test_fixture_marker')
    ) LOOP
        EXECUTE 'TRUNCATE TABLE public.' || quote_ident(r.tablename) || ' CASCADE;';
    END LOOP;
END $$;
SET session_replication_role = 'origin';";

        await using var cmd = new NpgsqlCommand(truncateSql, conn);
        cmd.CommandTimeout = 60;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Dọn dẹp tài nguyên do chính lượt chạy hiện tại tạo ra (Database và Role được xác minh thuộc RunId).
    /// Tuyệt đối không xóa bất kỳ database hay role nào tồn tại từ trước hoặc không thuộc lượt chạy.
    /// </summary>
    public static async Task CleanupCurrentRunAsync(CancellationToken cancellationToken = default)
    {
        await using var serverConn = new NpgsqlConnection(AdminServerConnectionString);
        await serverConn.OpenAsync(cancellationToken);

        foreach (var dbName in CreatedDatabases.Keys)
        {
            try
            {
                AssertSafeTestTarget(dbName);
                var dbConnStr = BuildConnectionString(Host, Port, dbName, AdminUser, AdminPassword);
                await AssertDatabaseOwnedByRunAsync(dbConnStr, RunId, cancellationToken);

                var termSql = $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = {EscapeSqlLiteral(dbName)} AND pid <> pg_backend_pid();";
                await using (var termCmd = new NpgsqlCommand(termSql, serverConn))
                {
                    await termCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                var dropSql = $"DROP DATABASE IF EXISTS {EscapeSqlIdentifier(dbName)};";
                await using (var dropCmd = new NpgsqlCommand(dropSql, serverConn))
                {
                    await dropCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            catch
            {
                // Teardown best-effort
            }
        }

        foreach (var roleName in CreatedRoles.Keys)
        {
            try
            {
                var dropRoleSql = $"DROP ROLE IF EXISTS {EscapeSqlIdentifier(roleName)};";
                await using var dropRoleCmd = new NpgsqlCommand(dropRoleSql, serverConn);
                await dropRoleCmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch
            {
                // Teardown best-effort
            }
        }
    }

    public static string EscapeSqlIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            throw new ArgumentException("SQL identifier cannot be null or empty.", nameof(identifier));
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }

    public static string EscapeSqlLiteral(string? literal)
    {
        return $"'{((literal ?? string.Empty).Replace("'", "''"))}'";
    }

    public static AppDbContext CreateRuntimeDbContext(ICurrentTenantService tenantService, params IInterceptor[] interceptors)
    {
        var services = new ServiceCollection();
        services.AddSingleton(tenantService);
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(RuntimeConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
            })
            .UseSnakeCaseNamingConvention();

            if (interceptors != null && interceptors.Length > 0)
            {
                options.AddInterceptors(interceptors);
            }
        });

        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<AppDbContext>();
    }
}

