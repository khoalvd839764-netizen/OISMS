using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OISM.Application.Common.Interfaces;
using OISM.Infrastructure.Health;
using OISM.Infrastructure.Jobs;
using OISM.Infrastructure.Persistence;
using OISM.Infrastructure.Services;
using OISM.Infrastructure.Services.Ai;

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
        services.AddSingleton<IDatabaseReadinessService, NpgsqlDatabaseReadinessService>();
        services.AddScoped<ISqlLockService>(provider => provider.GetRequiredService<AppDbContext>().Locks);
        services.AddScoped<IWarehouseBranchReassignmentStore, WarehouseBranchReassignmentStore>();
        services.AddOismHangfire(configuration);
        services.AddOptions<AiOptions>()
            .Bind(configuration.GetSection(AiOptions.SectionName))
            .PostConfigure<ILogger<AiOptions>>((options, logger) =>
            {
                options.ApiKey ??= configuration["AI_API_KEY"];
                if (options.Enabled && LlmRouter.Providers(options).Count == 0)
                {
                    logger.LogWarning("Ai is enabled without any provider API key. The AI assistant is disabled.");
                }
            })
            .ValidateOnStart();
        services.Configure<OISM.Application.Features.Ai.Services.AiChatLimits>(configuration.GetSection(AiOptions.SectionName));
        services.AddSingleton<IUserGuideProvider, OISM.Infrastructure.Ai.UserGuideProvider>();
        services.AddSingleton<DisabledLlmClient>();
        services.AddHttpClient<OpenAiCompatibleLlmClient>((provider, client) =>
            client.Timeout = TimeSpan.FromSeconds(125))
            .RedactLoggedHeaders(new[] { "Authorization" });
        services.AddTransient<LlmRouter>();
        services.AddTransient<ILlmClient>(provider => provider.GetRequiredService<LlmRouter>());
        services.AddTransient<ILlmModelCatalog>(provider => provider.GetRequiredService<LlmRouter>());

        return services;
    }
}
