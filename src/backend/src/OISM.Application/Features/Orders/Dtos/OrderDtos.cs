using System.ComponentModel.DataAnnotations;

namespace OISM.Application.Features.Orders.Dtos;

public class OrderItemDto
{
    public Guid Id { get; set; }
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public string SkuName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal? CostPrice { get; set; }
    public decimal TotalLineAmount => Quantity * UnitPrice;
}

public class OrderSummaryDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public string? BranchName { get; set; }
    public string Channel { get; set; } = "POS";
    public string? ExternalShopId { get; set; }
    public string? ExternalOrderId { get; set; }
    public string Status { get; set; } = "Draft";
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public decimal TotalAmount { get; set; }
    public int ItemCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
}

public class OrderDetailDto : OrderSummaryDto
{
    public string? ShippingAddress { get; set; }
    public Guid RequestKey { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}

public class CreateOrderItemRequestDto
{
    [Required(ErrorMessage = "Mã SKU (SkuId) là bắt buộc.")]
    public Guid SkuId { get; set; }

    [Range(0.001, 1000000, ErrorMessage = "Số lượng đặt hàng phải lớn hơn 0.")]
    public decimal Quantity { get; set; }

    [Range(0, 10000000000, ErrorMessage = "Đơn giá không được âm.")]
    public decimal UnitPrice { get; set; }
}

public class CreateOrderDto
{
    public string? Number { get; set; }

    [Required(ErrorMessage = "Chi nhánh là bắt buộc.")]
    public Guid BranchId { get; set; }

    /// <summary>
    /// Kênh bán: 'POS', 'Admin', 'Shopee', 'TikTok', 'Lazada'. Mặc định 'Admin'.
    /// </summary>
    public string Channel { get; set; } = "Admin";

    public string? ExternalShopId { get; set; }
    public string? ExternalOrderId { get; set; }

    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string? ShippingAddress { get; set; }

    /// <summary>
    /// Trạng thái ban đầu: 'Draft' hoặc 'Reserved'. Mặc định 'Draft'.
    /// </summary>
    public string InitialStatus { get; set; } = "Draft";

    public Guid? RequestKey { get; set; }

    [Required(ErrorMessage = "Đơn hàng phải có ít nhất một mặt hàng.")]
    [MinLength(1, ErrorMessage = "Đơn hàng phải có ít nhất một mặt hàng.")]
    public List<CreateOrderItemRequestDto> Items { get; set; } = new();
}

public class OrderFilterDto
{
    public string? Status { get; set; }
    public string? Channel { get; set; }
    public Guid? BranchId { get; set; }
    public DateTimeOffset? FromDate { get; set; }
    public DateTimeOffset? ToDate { get; set; }
    public string? Search { get; set; }

    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
