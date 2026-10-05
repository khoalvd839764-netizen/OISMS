using Microsoft.EntityFrameworkCore;
using OISM.Application.Common;
using OISM.Infrastructure.Persistence;
using OISM.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// =========================================================================
// 1. ĐĂNG KÝ HTTP CONTEXT ACCESSOR & CURRENT TENANT SERVICE
// =========================================================================
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentTenantService, CurrentTenantService>();

// =========================================================================
// 2. ĐĂNG KÝ APPDBCONTEXT KẾT NỐI POSTGRESQL
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
// 3. ENDPOINT TEST KIỂM THỬ KẾT NỐI DATABASE VÀ BẢNG TENANTS
// =========================================================================
app.MapGet("/api/test-db", async (AppDbContext context) =>
{
    try
    {
        // Chạy thử lệnh _context.Tenants.AnyAsync() theo yêu cầu để xác nhận kết nối
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
// 4. KIỂM TRA KẾT NỐI KHI KHỞI ĐỘNG ỨNG DỤNG
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
