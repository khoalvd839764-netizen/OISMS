using System.ComponentModel.DataAnnotations;

namespace OISM.Application.Features.Inventory.Dtos;

public class ReserveStockRequestDto
{
    [Required(ErrorMessage = "Chi nhánh là bắt buộc.")]
    public Guid BranchId { get; set; }

    [Required(ErrorMessage = "Kho giữ chỗ là bắt buộc.")]
    public Guid WarehouseId { get; set; }

    [Required(ErrorMessage = "Mặt hàng SKU là bắt buộc.")]
    public Guid SkuId { get; set; }

    [Range(0.001, 1000000, ErrorMessage = "Số lượng giữ chỗ phải lớn hơn 0.")]
    public decimal Quantity { get; set; }

    /// <summary>
    /// Mã đơn hàng liên kết (nếu có). Nếu null, harness hoặc service sẽ tự sinh đơn nháp tối thiểu.
    /// </summary>
    public Guid? OrderId { get; set; }

    /// <summary>
    /// Mã dòng đơn hàng liên kết (nếu có).
    /// </summary>
    public Guid? OrderItemId { get; set; }

    /// <summary>
    /// Khóa định danh thao tác (Idempotency Key). Nếu client gửi lại cùng RequestKey cho cùng một yêu cầu giữ chỗ,
    /// hệ thống sẽ trả lại kết quả thao tác cũ mà không tạo thêm Order, OrderItem, Reservation hoặc tăng Reserved.
    /// </summary>
    public Guid? RequestKey { get; set; }

    /// <summary>
    /// Thời gian giữ chỗ (phút), mặc định 15 phút.
    /// </summary>
    [Range(1, 1440, ErrorMessage = "Thời hạn giữ chỗ từ 1 đến 1440 phút.")]
    public int DurationMinutes { get; set; } = 15;
}

public class ReserveStockResponseDto
{
    public Guid ReservationId { get; set; }
    public Guid OrderId { get; set; }
    public Guid OrderItemId { get; set; }
    public Guid BranchId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid SkuId { get; set; }
    public decimal Quantity { get; set; }
    public string Status { get; set; } = "Active";
    public DateTimeOffset ExpiresAt { get; set; }
    public decimal RemainingAvailableStock { get; set; }
}
