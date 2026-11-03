using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.StockCounts.Services;
using OISM.Infrastructure.Persistence;

namespace OISM.Infrastructure.Jobs;

/// <summary>
/// Tác vụ nền Hangfire: Khám phá các tenant có lịch kiểm kê đang kích hoạt và tự động sinh kỳ kiểm kê cô lập theo tenant.
/// </summary>
[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
public class StockCountPeriodJob : IStockCountPeriodJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StockCountPeriodJob> _logger;

    public StockCountPeriodJob(IServiceScopeFactory scopeFactory, ILogger<StockCountPeriodJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<int> ExecuteAsync(DateTimeOffset? nowUtc = null, CancellationToken ct = default)
    {
        var effectiveNow = nowUtc ?? DateTimeOffset.UtcNow;
        _logger.LogInformation("Bắt đầu tác vụ nền sinh kỳ kiểm kê định kỳ (Now: {NowUtc:O}).", effectiveNow);

        // Bước 1: Khám phá cross-tenant trong Privileged Scope (Read-only, IgnoreQueryFilters + AsNoTracking)
        List<Guid> candidateTenantIds;
        using (var discoveryScope = _scopeFactory.CreateScope())
        {
            var db = discoveryScope.ServiceProvider.GetRequiredService<AppDbContext>();
            candidateTenantIds = await db.StockCountSchedules
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(s => s.IsActive)
                .Select(s => s.TenantId)
                .Distinct()
                .OrderBy(t => t)
                .ToListAsync(ct);
        }

        if (candidateTenantIds.Count == 0)
        {
            _logger.LogInformation("Không phát hiện tenant nào có lịch kiểm kê đang kích hoạt.");
            return 0;
        }

        int totalCreated = 0;
        int totalFailed = 0;
        List<Exception> batchErrors = new();

        // Bước 2: Thực thi nghiệp vụ cô lập từng tenant trong scope riêng biệt
        foreach (var tenantId in candidateTenantIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var tenantScope = _scopeFactory.CreateScope();
                var tenantService = tenantScope.ServiceProvider.GetRequiredService<ICurrentTenantService>();
                tenantService.SetTenant(tenantId);

                var scheduleService = tenantScope.ServiceProvider.GetRequiredService<IStockCountScheduleService>();
                var createdCount = await scheduleService.GeneratePeriodsAsync(effectiveNow, ct);
                totalCreated += createdCount;

                _logger.LogInformation("Tenant {TenantId}: Đã tự động sinh {Count} kỳ kiểm kê mới.", tenantId, createdCount);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Tác vụ sinh kỳ kiểm kê bị hủy bỏ theo yêu cầu shutdown.");
                throw;
            }
            catch (Exception ex)
            {
                totalFailed++;
                batchErrors.Add(ex);
                _logger.LogError(ex, "Lỗi khi xử lý sinh kỳ kiểm kê cho Tenant {TenantId}. Bỏ qua tenant này và tiếp tục tenant tiếp theo.", tenantId);
            }
        }

        if (batchErrors.Count > 0)
        {
            throw new AggregateException(
                $"Tác vụ tự động sinh kỳ kiểm kê thất bại ({totalFailed} lỗi, {totalCreated} kỳ đã tạo thành công).",
                batchErrors);
        }

        _logger.LogInformation("Hoàn tất tác vụ nền sinh kỳ kiểm kê định kỳ. Tổng số kỳ đã tạo: {Total}.", totalCreated);
        return totalCreated;
    }
}
