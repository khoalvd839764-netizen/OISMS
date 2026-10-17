using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Npgsql;
using OISM.Application.Common.Interfaces;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using OISM.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Cấu hình Cơ sở dữ liệu (PostgreSQL qua EF Core)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Host=localhost;Port=5432;Database=oism_db;Username=postgres;Password=postgres";

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
        npgsqlOptions.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
    });
});

builder.Services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());

// 2. Đăng ký các dịch vụ hệ thống & HttpContextAccessor
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentTenantService, CurrentTenantService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddSingleton<IDatabaseReadinessService, NpgsqlDatabaseReadinessService>();

// 3. Cấu hình Xác thực JWT Bearer (FR-AUTH-02, NFR-SEC-01)
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "SuperSecretKeyForOISMOmnichannelRetailSystem2026!#";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "OISM_WebAPI";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "OISM_Client";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false; // Phục vụ môi trường Development / Docker Local
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();

// 4. Cấu hình CORS cho Frontend Admin (5173) và Frontend POS (5174)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontendApps", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:5174", "https://localhost:5173", "https://localhost:5174")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// 5. Cấu hình Swagger với nút "Authorize" nhập Bearer Token
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "OISMS Web API - Omnichannel Inventory & Sales Management",
        Version = "v1",
        Description = "Tài liệu API Backend hệ thống quản lý bán hàng và tồn kho đa kênh SaaS Multi-tenant."
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Nhập JWT token theo cú pháp: Bearer {chuỗi_token_của_bạn}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer", doc),
            new List<string>()
        }
    });
});

var app = builder.Build();

// 6. Tự động khởi tạo dữ liệu mẫu Roles nếu cơ sở dữ liệu đã sẵn sàng
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var db = services.GetRequiredService<AppDbContext>();
        if (db.Database.CanConnect())
        {
            if (!db.Roles.Any())
            {
                db.Roles.AddRange(
                    new Role { Name = "Chủ cửa hàng", Code = "Owner", Description = "Toàn quyền quản trị cửa hàng" },
                    new Role { Name = "Quản lý / Nhân viên kho", Code = "Staff", Description = "Quản lý đơn hàng và nghiệp vụ kho" },
                    new Role { Name = "Thu ngân POS", Code = "Cashier", Description = "Bán hàng và thu tiền tại quầy" }
                );
                db.SaveChanges();
            }
        }
    }
    catch
    {
        // Bỏ qua nếu DB chưa được migrate trong lần khởi chạy đầu
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "OISMS API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontendApps");

app.UseAuthentication();
app.UseAuthorization();

// Controllers API (Auth, Products, Categories, Brands, POS, Branches, Suppliers)
app.MapControllers();

// Root endpoint
app.MapGet("/", () => Results.Ok(new
{
    name = "OISMS Web API",
    service = "OISM Web API",
    status = "Running",
    timestamp = DateTime.UtcNow
}));

// Liveness probe: reports if the web application process is responsive
app.MapGet("/health/live", () => Results.Ok(new
{
    status = "Live",
    timestamp = DateTime.UtcNow
}));

// Readiness probe: verifies real database connection with runtime account and validates schema readiness
app.MapGet("/health/ready", async (IDatabaseReadinessService readinessService, CancellationToken ct) =>
{
    var result = await readinessService.CheckReadinessAsync(ct);
    return Results.Json(result, statusCode: result.StatusCode);
});

// General health probe: alias for readiness check to allow calling /health directly
app.MapGet("/health", async (IDatabaseReadinessService readinessService, CancellationToken ct) =>
{
    var result = await readinessService.CheckReadinessAsync(ct);
    return Results.Json(result, statusCode: result.StatusCode);
});

app.Run();

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

    public static readonly string[] RequiredTables = new[]
    {
        "tenants", "users", "refresh_tokens", "branches", "warehouses",
        "categories", "brands", "products", "skus", "suppliers",
        "goods_receipts", "goods_receipt_items", "inventory_balances",
        "warehouse_inventory_balances", "stock_transfers", "stock_transfer_items",
        "stock_counts", "stock_count_items", "inventory_adjustments",
        "inventory_adjustment_items", "orders", "order_items", "reservations",
        "payments", "sales_returns", "sales_return_items", "inventory_transactions",
        "daily_sales_summaries", "replenishment_suggestions"
    };

    public NpgsqlDatabaseReadinessService(IConfiguration configuration, ILogger<NpgsqlDatabaseReadinessService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public static NpgsqlConnectionStringBuilder BuildConnectionStringBuilder(IConfiguration configuration)
    {
        var builder = new NpgsqlConnectionStringBuilder();

        // Check dedicated Database configuration section or environment variables
        var dbSection = configuration.GetSection("Database");
        var host = dbSection["Host"] ?? configuration["DB_HOST"];
        var portStr = dbSection["Port"] ?? configuration["DB_PORT"];
        var database = dbSection["Database"] ?? dbSection["Name"] ?? configuration["DB_NAME"] ?? configuration["POSTGRES_DB"];
        var username = dbSection["Username"] ?? configuration["DB_USER"] ?? configuration["OISMS_APP_USER"];
        var password = dbSection["Password"] ?? configuration["DB_PASSWORD"] ?? configuration["OISMS_APP_PASSWORD"];

        if (!string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(username))
        {
            builder.Host = host;
            if (int.TryParse(portStr, out var port))
            {
                builder.Port = port;
            }
            else
            {
                builder.Port = 5432;
            }
            builder.Database = !string.IsNullOrWhiteSpace(database) ? database : "oisms";
            builder.Username = username;
            // Direct assignment avoids corrupting passwords containing semicolons, quotes, etc.
            builder.Password = password ?? string.Empty;
            builder.Timeout = 5;
            builder.CommandTimeout = 5;
            return builder;
        }

        // Fallback to raw connection string if separate properties are not provided
        var rawConnStr = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(rawConnStr))
        {
            var fallbackBuilder = new NpgsqlConnectionStringBuilder(rawConnStr);
            if (fallbackBuilder.Timeout == 15) fallbackBuilder.Timeout = 5;
            if (fallbackBuilder.CommandTimeout == 30) fallbackBuilder.CommandTimeout = 5;
            return fallbackBuilder;
        }

        throw new InvalidOperationException("Database connection settings ('Database' section or 'DefaultConnection') are missing or unconfigured.");
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

            var missingTables = RequiredTables.Where(t => !existingTables.Contains(t)).ToList();
            if (missingTables.Count > 0)
            {
                return new ReadinessCheckResult(
                    false, StatusCodes.Status503ServiceUnavailable, "Unhealthy",
                    $"Database schema is incomplete ({existingTables.Count} of {RequiredTables.Length} base tables found). Missing: {string.Join(", ", missingTables)}. Database migration required.",
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
                "Database connection, schema completeness (29 tables), trigger immutability, and runtime permissions verified.",
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

public partial class Program { }
