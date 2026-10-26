namespace OISM.Application.Features.Realtime.Dtos;

/// <summary>
/// DTO sự kiện đơn hàng mới tối thiểu phát qua SignalR Realtime Hub.
/// Tuyệt đối KHÔNG chứa CostPrice, TotalCost, TotalCogs (bảo vệ bí mật kinh doanh, cách ly theo quyền RBAC).
/// </summary>
public class OrderSummaryRealtimeDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public string? BranchName { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int TotalItems { get; set; }
    public string? CustomerName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// DTO sự kiện biến động tồn kho khả dụng tức thời tại chi nhánh.
/// </summary>
public class StockChangedRealtimeDto
{
    public Guid BranchId { get; set; }
    public Guid SkuId { get; set; }
    public decimal NewAvailableStock { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// DTO cảnh báo mặt hàng dưới ngưỡng an toàn (Low-stock Alert).
/// </summary>
public class LowStockAlertRealtimeDto
{
    public Guid BranchId { get; set; }
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public decimal CurrentAvailable { get; set; }
    public decimal Threshold { get; set; }
    public DateTimeOffset AlertedAt { get; set; } = DateTimeOffset.UtcNow;
}
