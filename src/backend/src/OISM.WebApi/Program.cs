using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
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

// 6. Tự động khởi tạo dữ liệu mẫu Roles (Owner, Staff, Cashier) nếu cơ sở dữ liệu đã sẵn sàng
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var db = services.GetRequiredService<AppDbContext>();
        // Chỉ seed khi DB đã kết nối được
        if (db.Database.CanConnect())
        {
            db.Database.EnsureCreated();
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

app.MapControllers();

app.MapGet("/", () => Results.Ok(new
{
    name = "OISMS Web API",
    status = "Running",
    timestamp = DateTime.UtcNow
}));

app.Run();

public partial class Program { }
