using System.ComponentModel.DataAnnotations;
using OISM.Application.Features.Orders.Dtos;

namespace OISM.Application.Features.Simulator.Dtos;

public class WebhookOrderItemPayloadDto
{
    [Required(ErrorMessage = "Mã SKU sàn (SkuCode) là bắt buộc.")]
    public string SkuCode { get; set; } = string.Empty;

    [Range(0.001, 1000000, ErrorMessage = "Số lượng đặt hàng phải lớn hơn 0.")]
    public decimal Quantity { get; set; } = 1;

    [Range(0, 10000000000, ErrorMessage = "Đơn giá không được âm.")]
    public decimal UnitPrice { get; set; }
}

public class WebhookOrderPayloadDto
{
    [Required(ErrorMessage = "Mã đơn hàng sàn (ExternalOrderId) là bắt buộc.")]
    public string ExternalOrderId { get; set; } = string.Empty;

    public string? ExternalShopId { get; set; }

    [Required(ErrorMessage = "Chi nhánh tiếp nhận đơn (BranchId) là bắt buộc.")]
    public Guid BranchId { get; set; }

    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string? ShippingAddress { get; set; }

    [Required(ErrorMessage = "Danh sách mặt hàng không được để trống.")]
    [MinLength(1, ErrorMessage = "Đơn hàng sàn phải có ít nhất 1 mặt hàng.")]
    public List<WebhookOrderItemPayloadDto> Items { get; set; } = new();
}

public class SimulationBatchRequestDto
{
    [Required(ErrorMessage = "Kênh sàn là bắt buộc (Shopee, TikTok, Lazada).")]
    public string Channel { get; set; } = "Shopee";

    [Required(ErrorMessage = "Chi nhánh là bắt buộc.")]
    public Guid BranchId { get; set; }

    [Required(ErrorMessage = "Mã SKU kiểm thử là bắt buộc.")]
    public string SkuCode { get; set; } = string.Empty;

    [Range(1, 100, ErrorMessage = "Số lượng request mô phỏng đồng thời từ 1 đến 100.")]
    public int ConcurrencyCount { get; set; } = 50;

    public decimal QuantityPerOrder { get; set; } = 1;
    public decimal UnitPrice { get; set; } = 100000;
}

public class SimulationBatchResultDto
{
    public string Channel { get; set; } = string.Empty;
    public string SkuCode { get; set; } = string.Empty;
    public int TotalRequests { get; set; }
    public int SuccessCount { get; set; }
    public int FailedConflictCount { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public List<string> CreatedOrderNumbers { get; set; } = new();
    public List<string> ErrorMessages { get; set; } = new();
}
