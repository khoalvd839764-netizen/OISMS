using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OISM.Application.Common.Interfaces;
using OISM.Infrastructure.Persistence;
using OISM.Infrastructure.Services;
using OISM.WebApi;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers(); // Đăng ký Controllers (Bắt buộc để AuthController hoạt động)
builder.Services.AddEndpointsApiExplorer();

// =========================================================================
// 1. ĐĂNG KÝ HTTP CONTEXT ACCESSOR & SERVICES
// =========================================================================
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentTenantService, CurrentTenantService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>(); // Đăng ký JwtTokenService

// =========================================================================
// 2. CẤU HÌNH JWT AUTHENTICATION
// =========================================================================
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"]!;

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
    };
});

// =========================================================================
// 3. CẤU HÌNH SWAGGER VỚI NÚT AUTHORIZE (Ổ KHÓA)
// =========================================================================
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "OISM API", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Nhập theo định dạng: Bearer {chuoi_jwt_token_cua_ban}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// =========================================================================
// 4. ĐĂNG KÝ APPDBCONTEXT KẾT NỐI POSTGRESQL
// =========================================================================
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString)
           .UseSnakeCaseNamingConvention();
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// =========================================================================
// 5. THÊM MIDDLEWARE XÁC THỰC & PHÂN QUYỀN (BẮT BUỘC ĐÚNG THỨ TỰ)
// =========================================================================
app.UseAuthentication(); // Bắt buộc đứng trước UseAuthorization
app.UseAuthorization();

app.MapControllers(); // Map các Controller API

// =========================================================================
// 6. ENDPOINT TEST KIỂM THỬ KẾT NỐI DATABASE VÀ BẢNG TENANTS
// =========================================================================
app.MapGet("/api/test-db", async (AppDbContext context) =>
{
    try
    {
        var hasTenants = await context.Tenants.AnyAsync();
        var totalTenants = await context.Tenants.CountAsync();

        return Results.Ok(new
        {
            success = true,
            message = "Kết nối Database PostgreSQL thành công!",
            hasTenants = hasTenants,
            totalTenants = totalTenants
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Lỗi kết nối cơ sở dữ liệu PostgreSQL",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
})
.WithName("TestDatabaseConnection")
.WithOpenApi();

// Endpoint kiểm tra Tenant Context
app.MapGet("/api/test-tenant", (ICurrentTenantService tenantService) =>
{
    return Results.Ok(new
    {
        tenantId = tenantService.TenantId,
        message = tenantService.TenantId.HasValue
            ? $"Current Tenant: {tenantService.TenantId.Value}"
            : "No Tenant context found (Pass header 'X-Tenant-Id: <guid>' to test)"
    });
})
.WithName("GetTestTenant")
.WithOpenApi();

// =========================================================================
// 7. KIỂM TRA KẾT NỐI KHI KHỞI ĐỘNG ỨNG DỤNG
// =========================================================================
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (context.Database.CanConnect())
        {
            var hasTenants = await context.Tenants.AnyAsync();
            logger.LogInformation(">>> [DATABASE CHECK] Kết nối PostgreSQL thành công! Bảng Tenants có dữ liệu: {HasTenants}", hasTenants);
        }
        else
        {
            logger.LogWarning(">>> [DATABASE CHECK] Chưa thể kết nối PostgreSQL. Vui lòng kiểm tra lại chuỗi kết nối trong appsettings.json.");
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(">>> [DATABASE CHECK] Lỗi kết nối CSDL: {Message}", ex.Message);
    }
}

app.Run();