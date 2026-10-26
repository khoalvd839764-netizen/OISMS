namespace OISM.Application.Features.Orders.Dtos;

/// <summary>
/// Ngữ cảnh phía Server tin cậy phục vụ tiếp nhận đơn hàng TMĐT và giữ chỗ tồn kho nguyên tử.
/// Không trực tiếp bind từ HTTP body để bảo vệ các cờ ủy quyền và giá bán sàn.
/// </summary>
public class InternalMarketplaceOrderContext
{
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string ExternalShopId { get; set; } = string.Empty;
    public string ExternalOrderId { get; set; } = string.Empty;
    public string WebhookPayloadSignature { get; set; } = string.Empty;
    public Guid RequestKey { get; set; } = Guid.NewGuid();
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string? ShippingAddress { get; set; }
    public decimal ExpectedTotalAmount { get; set; }
    public DateTimeOffset OrderTimestamp { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public List<InternalMarketplaceOrderItemContext> Items { get; set; } = new();
}

public class InternalMarketplaceOrderItemContext
{
    public string SkuCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
