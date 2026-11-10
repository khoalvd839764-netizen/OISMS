using OISM.Application;
using OISM.Infrastructure;
using OISM.Infrastructure.Health;
using OISM.WebApi.Http;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using OISM.Application.Common.Interfaces;
using OISM.Domain.Entities;
using OISM.Infrastructure.Jobs;
using OISM.Infrastructure.Persistence;
using OISM.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Cấu hình Cơ sở dữ liệu và các dịch vụ tầng Infrastructure
builder.Services.AddInfrastructureServices(builder.Configuration);

// 2. Đăng ký các use-case và nghiệp vụ tầng Application
builder.Services.AddApplicationServices();

// 3. Đăng ký các dịch vụ WebApi, SignalR & HttpContextAccessor
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<OISM.Application.Features.Realtime.IRealtimeNotifier, OISM.WebApi.Services.RealtimeNotifier>();
builder.Services.AddSignalR();

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
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/notifications"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("AiChat", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.GetUserId().GetValueOrDefault(),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    options.OnRejected = async (rejection, ct) =>
    {
        const string code = "AI_RATE_LIMITED";
        var context = rejection.HttpContext;
        var problem = ApiErrorResponse.CreateProblemDetails(context, code, ApiErrorTitles.Get(ApiErrorProfile.Ai, code),
            StatusCodes.Status429TooManyRequests, "Bạn hỏi hơi nhanh, thử lại sau ít phút.");
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken: ct);
    };
});

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

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            if (context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<ApiErrorContractAttribute>()?.Profile == ApiErrorProfile.Ai)
            {
                const string code = "AI_INVALID_REQUEST";
                return ApiErrorResponse.Problem(context.HttpContext, ApiErrorProfile.Ai, code,
                    ApiErrorTitles.Get(ApiErrorProfile.Ai, code), StatusCodes.Status400BadRequest,
                    "Dữ liệu yêu cầu trợ lý AI không hợp lệ.");
            }

            var errors = context.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                );

            var firstError = context.ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => !string.IsNullOrWhiteSpace(e.ErrorMessage) ? e.ErrorMessage : e.Exception?.Message)
                .FirstOrDefault(msg => !string.IsNullOrWhiteSpace(msg)) ?? "Dữ liệu yêu cầu không hợp lệ.";

            var problemDetails = ApiErrorResponse.CreateProblemDetails(
                context.HttpContext, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, firstError);
            problemDetails.Extensions["errors"] = errors;

            return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(problemDetails)
            {
                ContentTypes = { "application/problem+json" }
            };
        };
    });
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

// 6. Tự động khởi tạo dữ liệu mẫu theo chính sách bảo mật môi trường DemoSeedPolicy:
// - Chỉ Development và Test được phép seed demo.
// - Production, Staging và các môi trường khác TUYỆT ĐỐI không seed dù cấu hình bị bật nhầm.
// - Mặc định không seed; phải bật chủ động 'Database:SeedDemoData' = true.
// - Khi bật cấu hình nhưng thiếu 'Database:DemoSeedPassword', báo lỗi cấu hình và dừng seed; không fallback hardcode.
var seedEnabledInConfig = builder.Configuration.GetValue<bool>("Database:SeedDemoData", false);
var demoPassword = builder.Configuration["Database:DemoSeedPassword"];
var seedResult = DemoSeedPolicy.Evaluate(app.Environment.EnvironmentName, seedEnabledInConfig, demoPassword);

if (seedResult.ShouldSeed)
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = services.GetRequiredService<AppDbContext>();
        if (db.Database.CanConnect())
        {
            logger.LogInformation("Khởi tạo dữ liệu mẫu cho môi trường {Environment}...", app.Environment.EnvironmentName);
            await OismsDatabaseSeeder.SeedAsync(db, demoPassword!);
            logger.LogInformation("Đã khởi tạo thành công dữ liệu mẫu cho môi trường {Environment}.", app.Environment.EnvironmentName);
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Bỏ qua seed dữ liệu khi khởi chạy.");
    }
}
else if (seedResult.IsConfigurationError)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogError("Lỗi cấu hình khởi tạo dữ liệu mẫu: {Reason}", seedResult.Reason);
}
else if (seedEnabledInConfig && !DemoSeedPolicy.IsEnvironmentAllowed(app.Environment.EnvironmentName))
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogWarning("Phát hiện cấu hình 'Database:SeedDemoData' = true trong môi trường {Environment}. {Reason}", 
        app.Environment.EnvironmentName, seedResult.Reason);
}

// Đọc IP và scheme gốc khi chạy sau reverse proxy (Caddy trong mạng Docker).
// Mặc định ASP.NET chỉ tin proxy loopback, nên phải khai báo dải mạng riêng mà Docker dùng.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
                       Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
foreach (var (prefix, length) in new[] { ("10.0.0.0", 8), ("172.16.0.0", 12), ("192.168.0.0", 16), ("127.0.0.0", 8) })
{
    forwardedHeadersOptions.KnownNetworks.Add(
        new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Parse(prefix), length));
}
forwardedHeadersOptions.KnownProxies.Add(System.Net.IPAddress.IPv6Loopback);
app.UseForwardedHeaders(forwardedHeadersOptions);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "OISMS API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseCors("AllowFrontendApps");
app.UseMiddleware<OISM.WebApi.Middlewares.ExceptionHandlingMiddleware>();

if (!app.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("UseHttpsRedirection", false))
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseOismHangfire(builder.Configuration);

// Controllers API (Auth, Products, Categories, Brands, POS, Branches, Suppliers)
app.MapControllers();
app.MapHub<OISM.WebApi.Hubs.NotificationHub>("/hubs/notifications");

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

public partial class Program { }
