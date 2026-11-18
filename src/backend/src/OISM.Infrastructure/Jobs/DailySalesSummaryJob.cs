using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OISM.Application.Common.Helpers;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Reports.Services;
using OISM.Infrastructure.Persistence;

namespace OISM.Infrastructure.Jobs;

/// <summary>
/// Tác vụ nền Hangfire: Quét các tenant đang hoạt động và tổng hợp lại bảng <c>daily_sales_summaries</c>
/// cho cửa sổ N ngày gần nhất (mặc định 7 ngày từ hôm qua lùi về theo giờ Việt Nam UTC+07).
/// </summary>
[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
public class DailySalesSummaryJob : IDailySalesSummaryJob
{
    public const int DefaultSummaryRebuildDays = 7;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DailySalesSummaryJob> _logger;

    public DailySalesSummaryJob(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<DailySalesSummaryJob> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<int> ExecuteAsync(DateTimeOffset? nowUtc = null, CancellationToken ct = default)
    {
        var effectiveNow = nowUtc ?? DateTimeOffset.UtcNow;
        var yesterday = ReportTimeHelper.ToLocalDate(effectiveNow).AddDays(-1);
        var configuredDays = _configuration.GetValue<int>("Reports:SummaryRebuildDays", DefaultSummaryRebuildDays);
        var rebuildDays = configuredDays <= 0 ? DefaultSummaryRebuildDays : Math.Min(configuredDays, 365);
        var windowStart = yesterday.AddDays(-(rebuildDays - 1));

        _logger.LogInformation(
            "Bắt đầu tác vụ nền tổng hợp báo cáo ngày (Now: {NowUtc:O}, Window: {WindowStart:yyyy-MM-dd}..{WindowEnd:yyyy-MM-dd}, Days: {Days}).",
            effectiveNow,
            windowStart,
            yesterday,
            rebuildDays);

        // Bước 1: Khám phá danh sách tenant đang hoạt động trong Privileged Scope (Read-only, IgnoreQueryFilters + AsNoTracking)
        List<Guid> candidateTenantIds;
        using (var discoveryScope = _scopeFactory.CreateScope())
        {
            var db = discoveryScope.ServiceProvider.GetRequiredService<AppDbContext>();
            using (db.OpenPrivilegedScope())
            {
                candidateTenantIds = await db.Tenants
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(t => t.IsActive)
                    .Select(t => t.Id)
                    .OrderBy(t => t)
                    .ToListAsync(ct);
            }
        }

        if (candidateTenantIds.Count == 0)
        {
            _logger.LogInformation("Không phát hiện tenant nào đang hoạt động để tổng hợp báo cáo ngày.");
            return 0;
        }

        int totalRowsWritten = 0;
        int totalFailed = 0;
        List<Exception> batchErrors = new();

        // Bước 2: Tổng hợp cô lập theo từng tenant trong scope riêng biệt
        foreach (var tenantId in candidateTenantIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var tenantScope = _scopeFactory.CreateScope();
                var tenantService = tenantScope.ServiceProvider.GetRequiredService<ICurrentTenantService>();
                tenantService.SetTenant(tenantId);

                var summaryService = tenantScope.ServiceProvider.GetRequiredService<IDailySalesSummaryService>();
                int tenantRows = 0;
                for (int offset = rebuildDays - 1; offset >= 0; offset--)
                {
                    ct.ThrowIfCancellationRequested();
                    var targetDate = yesterday.AddDays(-offset);
                    tenantRows += await summaryService.RebuildAsync(tenantId, targetDate, ct);
                }

                totalRowsWritten += tenantRows;
                _logger.LogInformation(
                    "Tenant {TenantId}: Đã tổng hợp xong {Days} ngày ({WindowStart:yyyy-MM-dd}..{WindowEnd:yyyy-MM-dd}), ghi {Rows} dòng.",
                    tenantId,
                    rebuildDays,
                    windowStart,
                    yesterday,
                    tenantRows);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogWarning("Tác vụ tổng hợp báo cáo ngày bị hủy bỏ theo yêu cầu shutdown.");
                throw;
            }
            catch (Exception ex)
            {
                totalFailed++;
                batchErrors.Add(ex);
                _logger.LogError(
                    ex,
                    "Lỗi khi tổng hợp báo cáo ngày cho Tenant {TenantId}. Bỏ qua tenant này và tiếp tục tenant tiếp theo.",
                    tenantId);
            }
        }

        if (batchErrors.Count > 0)
        {
            throw new AggregateException(
                $"Tác vụ tổng hợp báo cáo ngày thất bại ({totalFailed} lỗi, {totalRowsWritten} dòng đã ghi thành công).",
                batchErrors);
        }

        _logger.LogInformation("Hoàn tất tác vụ nền tổng hợp báo cáo ngày. Tổng số dòng đã ghi: {Total}.", totalRowsWritten);
        return totalRowsWritten;
    }
}
