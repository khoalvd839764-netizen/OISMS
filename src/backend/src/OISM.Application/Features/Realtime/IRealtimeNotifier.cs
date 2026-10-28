using OISM.Application.Features.Realtime.Dtos;

namespace OISM.Application.Features.Realtime;

/// <summary>
/// Giao diện phát thông báo thời gian thực qua SignalR Hub (FR-SIM-02).
/// Phục vụ Web Admin Dashboard và Web POS PWA cập nhật tức thời không cần polling F5.
/// Bảo đảm phát sau khi DB transaction commit thành công và không rò rỉ dữ liệu nhạy cảm (CostPrice/COGS).
/// </summary>
public interface IRealtimeNotifier
{
    /// <summary>
    /// Phát sự kiện Đơn hàng mới (kèm âm thanh chuông và popup thông báo).
    /// Sử dụng OrderSummaryRealtimeDto tối thiểu, tuyệt đối không gửi giá vốn.
    /// </summary>
    Task NotifyNewOrderAsync(Guid tenantId, OrderSummaryRealtimeDto order, CancellationToken ct = default);

    /// <summary>
    /// Phát sự kiện Biến động tồn kho khả dụng tức thời tại chi nhánh.
    /// </summary>
    Task NotifyStockChangedAsync(Guid tenantId, Guid branchId, Guid skuId, decimal newAvailableStock, CancellationToken ct = default);

    /// <summary>
    /// Phát cảnh báo Mặt hàng dưới ngưỡng an toàn (Low-stock Alert).
    /// </summary>
    Task NotifyLowStockAlertAsync(Guid tenantId, Guid branchId, Guid skuId, string skuCode, decimal currentAvailable, decimal threshold, CancellationToken ct = default);
}
