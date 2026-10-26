using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Orders.Services;
using OISM.Infrastructure.Persistence;

namespace OISM.Infrastructure.Jobs;

/// <summary>
/// Tác vụ nền Hangfire: Khám phá các tenant có phiếu giữ chỗ quá hạn và thực thi hủy đơn hàng cô lập theo tenant.
/// </summary>
[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
public class ReservationExpiryJob : IReservationExpiryJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReservationExpiryJob> _logger;
    private static Guid? _lastTenantId = null;
    private static readonly object _tenantLock = new();

    public ReservationExpiryJob(IServiceScopeFactory scopeFactory, ILogger<ReservationExpiryJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public static void ResetTenantCursor()
    {
        lock (_tenantLock)
        {
            _lastTenantId = null;
        }
    }

    public async Task<int> ExecuteAsync(DateTimeOffset? cutoffUtc = null, CancellationToken ct = default)
    {
        var effectiveCutoff = cutoffUtc ?? DateTimeOffset.UtcNow;
        _logger.LogInformation("Bắt đầu tác vụ nền quét đơn giữ chỗ quá hạn (Cutoff: {CutoffUtc:O}).", effectiveCutoff);

        // Bước 1: Khám phá cross-tenant trong Privileged Scope (Read-only, IgnoreQueryFilters + AsNoTracking)
        List<Guid> candidateTenantIds;
        using (var discoveryScope = _scopeFactory.CreateScope())
        {
            var db = discoveryScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var baseQuery = db.Reservations
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(r => r.Status == "Active" && r.ExpiresAt <= effectiveCutoff)
                .Select(r => r.TenantId)
                .Distinct();

            Guid? cursor;
            lock (_tenantLock)
            {
                cursor = _lastTenantId;
            }

            if (cursor.HasValue)
            {
                candidateTenantIds = await baseQuery
                    .Where(t => t > cursor.Value)
                    .OrderBy(t => t)
                    .Take(100)
                    .ToListAsync(ct);

                if (candidateTenantIds.Count == 0)
                {
                    // Hết trang theo cursor: quay vòng lại đầu (wrap-around)
                    candidateTenantIds = await baseQuery
                        .OrderBy(t => t)
                        .Take(100)
                        .ToListAsync(ct);
                }
            }
            else
            {
                candidateTenantIds = await baseQuery
                    .OrderBy(t => t)
                    .Take(100)
                    .ToListAsync(ct);
            }

            lock (_tenantLock)
            {
                if (candidateTenantIds.Count > 0)
                {
                    _lastTenantId = candidateTenantIds.Last();
                }
                else
                {
                    _lastTenantId = null;
                }
            }
        } // Đóng discoveryScope ngay lập tức trước khi thực hiện nghiệp vụ mutate

        if (candidateTenantIds.Count == 0)
        {
            _logger.LogInformation("Không phát hiện tenant nào có phiếu giữ chỗ quá hạn.");
            return 0;
        }

        int totalCancelled = 0;
        int totalScanned = 0;
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

                var orderService = tenantScope.ServiceProvider.GetRequiredService<IOrderService>();
                var cancelledCount = await orderService.ExpireOverdueReservationsAsync(effectiveCutoff, ct);
                totalCancelled += cancelledCount;

                var stats = orderService.LastExecutionStats;
                totalScanned += stats.Scanned;
                totalFailed += stats.Failed;
                if (stats.Errors != null && stats.Errors.Count > 0)
                {
                    batchErrors.AddRange(stats.Errors);
                }

                _logger.LogInformation("Tenant {TenantId}: Đã tự động hủy {Count} đơn hàng quá hạn (Scanned: {Scanned}, Failed: {Failed}).", tenantId, cancelledCount, stats.Scanned, stats.Failed);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Tác vụ quét đơn giữ chỗ quá hạn bị hủy bỏ theo yêu cầu shutdown.");
                throw;
            }
            catch (Exception ex)
            {
                totalFailed++;
                batchErrors.Add(ex);
                _logger.LogError(ex, "Lỗi khi xử lý hủy đơn giữ chỗ quá hạn cho Tenant {TenantId}. Bỏ qua tenant này và tiếp tục tenant tiếp theo.", tenantId);
            }
        }

        // T6-F02: Nếu có thất bại mà không có đơn nào thành công (0 đơn thành công, ít nhất 1 lỗi),
        // ném exception để Hangfire server ghi nhận trạng thái 'Failed' và kích hoạt policy retry / quan sát thất bại,
        // tuyệt đối không che giấu lỗi thành một lượt no-op thành công.
        // Hỗ trợ đầy đủ: toàn bộ candidate lỗi, lỗi candidate scan query trước khi tính scanned, hoặc lỗi discovery tenant.
        if (totalFailed > 0 && totalCancelled == 0)
        {
            throw new AggregateException(
                $"Tác vụ tự động hủy đơn giữ chỗ thất bại ({totalFailed} lỗi, {totalCancelled} đơn thành công, {totalScanned} đơn đã quét).",
                batchErrors);
        }

        _logger.LogInformation("Hoàn tất tác vụ nền quét đơn giữ chỗ quá hạn. Tổng số đơn đã hủy: {Total}.", totalCancelled);
        return totalCancelled;
    }
}
