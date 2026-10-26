using Microsoft.AspNetCore.SignalR;
using OISM.Application.Features.Realtime;
using OISM.Application.Features.Realtime.Dtos;
using OISM.WebApi.Hubs;

namespace OISM.WebApi.Services;

/// <summary>
/// Triển khai IRealtimeNotifier phát sự kiện qua SignalR NotificationHub.
/// 
/// Nguyên tắc quan trọng:
/// 1. Chỉ gọi sau khi Database Transaction đã commit thành công.
/// 2. Nuốt và ghi log toàn bộ Exception để không làm hỏng giao dịch nghiệp vụ đã hoàn tất.
/// 3. Tuyệt đối không chứa CostPrice/COGS/TotalCost hoặc thực thể Entity trong payload.
/// </summary>
public class RealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<RealtimeNotifier> _logger;

    public RealtimeNotifier(
        IHubContext<NotificationHub> hubContext,
        ILogger<RealtimeNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyNewOrderAsync(Guid tenantId, OrderSummaryRealtimeDto order, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || order == null) return;

        try
        {
            var groupName = $"tenant_{tenantId}";
            await _hubContext.Clients.Group(groupName).SendAsync("OrderCreated", order, ct);
            _logger.LogInformation("Đã phát sự kiện OrderCreated cho Tenant {TenantId}, Đơn {OrderNumber}.",
                tenantId, order.OrderNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi phát sự kiện OrderCreated cho Tenant {TenantId}, Đơn {OrderNumber}.",
                tenantId, order.OrderNumber);
        }
    }

    public async Task NotifyStockChangedAsync(Guid tenantId, Guid branchId, Guid skuId, decimal newAvailableStock, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || branchId == Guid.Empty || skuId == Guid.Empty) return;

        try
        {
            var groupName = $"tenant_{tenantId}_branch_{branchId}";
            var payload = new StockChangedRealtimeDto
            {
                BranchId = branchId,
                SkuId = skuId,
                NewAvailableStock = newAvailableStock,
                Timestamp = DateTimeOffset.UtcNow
            };

            await _hubContext.Clients.Group(groupName).SendAsync("StockLevelChanged", payload, ct);
            _logger.LogInformation("Đã phát sự kiện StockLevelChanged tới nhóm {Group} cho SKU {SkuId}, Tồn mới: {Stock}.",
                groupName, skuId, newAvailableStock);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi phát sự kiện StockLevelChanged cho Tenant {TenantId}, Chi nhánh {BranchId}, SKU {SkuId}.",
                tenantId, branchId, skuId);
        }
    }

    public async Task NotifyLowStockAlertAsync(Guid tenantId, Guid branchId, Guid skuId, string skuCode, decimal currentAvailable, decimal threshold, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || branchId == Guid.Empty || skuId == Guid.Empty) return;

        try
        {
            var groupName = $"tenant_{tenantId}";
            var payload = new LowStockAlertRealtimeDto
            {
                BranchId = branchId,
                SkuId = skuId,
                SkuCode = skuCode,
                CurrentAvailable = currentAvailable,
                Threshold = threshold,
                AlertedAt = DateTimeOffset.UtcNow
            };

            await _hubContext.Clients.Group(groupName).SendAsync("LowStockWarning", payload, ct);
            _logger.LogInformation("Đã phát cảnh báo LowStockWarning cho Tenant {TenantId}, SKU {SkuCode}, Tồn: {Available}/{Threshold}.",
                tenantId, skuCode, currentAvailable, threshold);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi phát cảnh báo LowStockWarning cho Tenant {TenantId}, SKU {SkuCode}.",
                tenantId, skuCode);
        }
    }
}
