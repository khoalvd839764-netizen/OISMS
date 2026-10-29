using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OISM.Application.Common.Interfaces;
using OISM.Infrastructure.Jobs;
using OISM.Infrastructure.Persistence;
using OISM.Infrastructure.Services;

namespace OISM.Infrastructure;

/// <summary>
/// Đăng ký các dịch vụ thuộc tầng Infrastructure tuân thủ Clean Architecture.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionStringBuilder = DatabaseConnectionHelper.BuildConnectionStringBuilder(configuration);

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(connectionStringBuilder.ConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                npgsqlOptions.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
            })
            .UseSnakeCaseNamingConvention();
        });

        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddScoped<ICurrentTenantService, CurrentTenantService>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IDatabaseMigrationService, DatabaseMigrationService>();
        services.AddScoped<ISqlLockService, SqlLockService>();
        services.AddScoped<IWarehouseBranchReassignmentStore, WarehouseBranchReassignmentStore>();
        services.AddOismHangfire(configuration);

        return services;
    }
}
