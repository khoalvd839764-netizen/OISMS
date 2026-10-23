using Microsoft.Extensions.Configuration;
using Npgsql;

namespace OISM.Infrastructure.Persistence;

/// <summary>
/// Trợ giúp xây dựng Connection String đồng nhất giữa AppDbContext và Database Readiness Service.
/// Hỗ trợ cả hai cơ chế cấu hình:
/// 1. Các biến cấu hình rời rạc (Database__Host, Database__Port, Database__Database, Database__Username, Database__Password)
/// 2. ConnectionStrings:DefaultConnection
/// Đảm bảo runtime luôn sử dụng tài khoản ứng dụng (oisms_app), tuyệt đối không fallback sang tài khoản quản trị (postgres).
/// </summary>
public static class DatabaseConnectionHelper
{
    public const string DefaultRuntimeUser = "oisms_app";
    public const string DefaultDatabase = "oisms";

    public static NpgsqlConnectionStringBuilder BuildConnectionStringBuilder(IConfiguration configuration)
    {
        var builder = new NpgsqlConnectionStringBuilder();

        // 1. Kiểm tra cấu hình Database__* (hoặc env DB_*)
        var dbSection = configuration.GetSection("Database");
        var host = dbSection["Host"] ?? configuration["DB_HOST"];
        var portStr = dbSection["Port"] ?? configuration["DB_PORT"];
        var database = dbSection["Database"] ?? dbSection["Name"] ?? configuration["DB_NAME"] ?? configuration["POSTGRES_DB"];
        var username = dbSection["Username"] ?? configuration["DB_USER"] ?? configuration["OISMS_APP_USER"];
        var password = dbSection["Password"] ?? configuration["DB_PASSWORD"] ?? configuration["OISMS_APP_PASSWORD"];

        if (!string.IsNullOrWhiteSpace(host))
        {
            builder.Host = host;
            builder.Port = int.TryParse(portStr, out var port) ? port : 5432;
            builder.Database = !string.IsNullOrWhiteSpace(database) ? database : DefaultDatabase;
            builder.Username = !string.IsNullOrWhiteSpace(username) ? username : DefaultRuntimeUser;
            // Gán trực tiếp tránh lỗi parse ký tự đặc biệt hoặc dấu chấm phẩy
            builder.Password = password ?? string.Empty;
            builder.Timeout = 10;
            builder.CommandTimeout = 30;

            ValidateRuntimeUser(builder.Username);
            return builder;
        }

        // 2. Fallback sang ConnectionStrings:DefaultConnection
        var rawConnStr = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(rawConnStr))
        {
            var fallbackBuilder = new NpgsqlConnectionStringBuilder(rawConnStr);
            if (string.IsNullOrWhiteSpace(fallbackBuilder.Username))
            {
                fallbackBuilder.Username = DefaultRuntimeUser;
            }
            if (fallbackBuilder.Timeout == 15) fallbackBuilder.Timeout = 10;
            if (fallbackBuilder.CommandTimeout == 30) fallbackBuilder.CommandTimeout = 30;

            ValidateRuntimeUser(fallbackBuilder.Username);
            return fallbackBuilder;
        }

        // 3. Ném ngoại lệ nếu cả hai cơ chế cấu hình đều không có
        throw new InvalidOperationException("Database connection settings ('Database' section or 'DefaultConnection') are missing or unconfigured.");
    }

    private static void ValidateRuntimeUser(string username)
    {
        if (string.Equals(username, "postgres", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(username, "oisms_runtime", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Security violation: Application runtime must use application login account (e.g., '{DefaultRuntimeUser}'), not administrative or group role ('{username}').");
        }
    }
}
