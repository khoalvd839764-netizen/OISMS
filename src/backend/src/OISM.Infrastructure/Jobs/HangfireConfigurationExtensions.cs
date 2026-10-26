using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OISM.Application.Common.Interfaces;

namespace OISM.Infrastructure.Jobs;

/// <summary>
/// Cấu hình mở rộng tích hợp Hangfire với PostgreSQL storage schema 'hangfire'.
/// Tuân thủ nguyên tắc:
/// - Mặc định Hangfire:Enabled = false (opt-in).
/// - Khi enabled = false, không khởi chạy Hangfire Server, không kết nối storage và không đăng ký recurring job.
/// - Khi enabled = true, bắt buộc phải có connection string hợp lệ; cấm tuyệt đối fallback In-Memory âm thầm.
/// - Lưu trữ metadata Hangfire độc lập trong PostgreSQL schema 'hangfire', không xâm lấn 30 bảng nghiệp vụ.
/// </summary>
public static class HangfireConfigurationExtensions
{
    public const string RecurringJobId = "oisms-reservation-expiry";

    public static IServiceCollection AddOismHangfire(this IServiceCollection services, IConfiguration configuration)
    {
        // Luôn đăng ký IReservationExpiryJob trong DI container để có thể inject độc lập
        services.AddScoped<IReservationExpiryJob, ReservationExpiryJob>();

        var enabled = configuration.GetValue<bool>("Hangfire:Enabled", false);
        if (!enabled)
        {
            return services;
        }

        var connectionString = configuration.GetConnectionString("HangfireConnection")
            ?? configuration.GetConnectionString("DefaultConnection")
            ?? configuration["ConnectionStrings:DefaultConnection"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Cấu hình 'Hangfire:Enabled' = true nhưng không tìm thấy chuỗi kết nối hợp lệ ('HangfireConnection' hoặc 'DefaultConnection'). Không fallback sang In-Memory.");
        }

        services.AddHangfire((sp, config) =>
        {
            config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                  .UseSimpleAssemblyNameTypeSerializer()
                  .UseRecommendedSerializerSettings()
                  .UseFilter(new AutomaticRetryAttribute { Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail })
                  .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString), new PostgreSqlStorageOptions
                  {
                      SchemaName = "hangfire",
                      PrepareSchemaIfNecessary = true,
                      QueuePollInterval = TimeSpan.FromSeconds(15),
                      InvisibilityTimeout = TimeSpan.FromMinutes(5),
                      DistributedLockTimeout = TimeSpan.FromMinutes(1)
                  });
        });

        var workerCount = configuration.GetValue<int?>("Hangfire:WorkerCount") ?? Math.Min(4, Math.Max(2, Environment.ProcessorCount));
        if (workerCount < 1) workerCount = 1;

        services.AddHangfireServer(options =>
        {
            options.WorkerCount = workerCount;
            options.ServerName = $"oisms-worker-{Environment.MachineName}";
        });

        return services;
    }

    public static IApplicationBuilder UseOismHangfire(this IApplicationBuilder app, IConfiguration? configuration = null)
    {
        var recurringJobManager = app.ApplicationServices.GetService<IRecurringJobManager>();
        if (recurringJobManager == null)
        {
            return app;
        }

        recurringJobManager.AddOrUpdate<IReservationExpiryJob>(
            RecurringJobId,
            job => job.ExecuteAsync(null, CancellationToken.None),
            "*/1 * * * *",
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            });

        return app;
    }
}
