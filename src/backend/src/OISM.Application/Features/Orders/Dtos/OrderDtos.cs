using System.ComponentModel.DataAnnotations;
using OISM.Application.Common.Attributes;

namespace OISM.Application.Features.Orders.Dtos;

/// <summary>
/// DTO dòng sản phẩm khi tạo hoặc checkout đơn hàng.
/// </summary>
public class OrderItemRequestDto
{
    [Required(ErrorMessage = "Mã biến thể SKU là bắt buộc.")]
    [NotEmptyGuid(ErrorMessage = "Mã biến thể SKU không được rỗng (Guid.Empty).")]
    public Guid SkuId { get; set; }

    [Required(ErrorMessage = "Mã SKU là bắt buộc.")]
    public string SkuCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.001", "999999999999999.999", ErrorMessage = "Số lượng mua phải lớn hơn 0 và không vượt quá giới hạn tối đa.")]
    public decimal Quantity { get; set; }

    [Range(typeof(decimal), "0", "999999999999999999.99", ErrorMessage = "Đơn giá không được âm và không vượt quá giới hạn tối đa.")]
    public decimal UnitPrice { get; set; }
}

/// <summary>
/// Hợp đồng DTO thanh toán nhanh tại quầy POS (Hỗ trợ cả POST /api/pos/checkout và alias POST /api/orders/checkout).
/// Tương thích 100% với Frontend Web POS PWA (src/frontend/pos-app/src/types/pos.ts) và Admin Dashboard.
/// </summary>
public class PosCheckoutRequestDto
{
    [Required(ErrorMessage = "Chi nhánh là bắt buộc.")]
    [NotEmptyGuid(ErrorMessage = "Chi nhánh không được rỗng (Guid.Empty).")]
    public Guid BranchId { get; set; }

    public string Channel { get; set; } = "POS";

    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }

    /// <summary>
    /// Phương thức thanh toán: 'Cash', 'BankTransfer' (Chấp nhận cả 'CASH', 'BANK_TRANSFER_QR' từ POS).
    /// </summary>
    [Required(ErrorMessage = "Phương thức thanh toán là bắt buộc.")]
    public string PaymentMethod { get; set; } = "Cash";

    [Range(typeof(decimal), "0", "999999999999999999.99", ErrorMessage = "Số tiền khách đưa không được âm và không vượt quá giới hạn tối đa.")]
    public decimal? AmountGiven { get; set; }

    [Range(typeof(decimal), "0", "999999999999999999.99", ErrorMessage = "Tiền thừa không được âm và không vượt quá giới hạn tối đa.")]
    public decimal? ChangeDue { get; set; }

    /// <summary>
    /// Khóa Idempotency chống trừ kho kép và chống thanh toán trùng lặp khi mạng chập chờn hoặc nhân viên nhấn đúp F4.
    /// Bắt buộc client phải cung cấp ổn định, từ chối Guid.Empty.
    /// </summary>
    [Required(ErrorMessage = "RequestKey (Idempotency Key) là bắt buộc.")]
    [NotEmptyGuid(ErrorMessage = "RequestKey không được rỗng (Guid.Empty).")]
    public Guid RequestKey { get; set; }

    [Required(ErrorMessage = "Danh sách sản phẩm giỏ hàng không được rỗng.")]
    [MinLength(1, ErrorMessage = "Giỏ hàng phải có ít nhất 1 sản phẩm.")]
    public List<OrderItemRequestDto> Items { get; set; } = new();

    [Range(typeof(decimal), "0", "999999999999999999.99", ErrorMessage = "Tổng tiền đơn hàng không được âm và không vượt quá giới hạn tối đa.")]
    public decimal TotalAmount { get; set; }
}

/// <summary>
/// DTO tạo đơn hàng thủ công trên Admin Portal hoặc từ kênh bán nội bộ.
/// </summary>
public class CreateOrderRequestDto
{
    [Required(ErrorMessage = "Chi nhánh là bắt buộc.")]
    [NotEmptyGuid(ErrorMessage = "Chi nhánh không được rỗng (Guid.Empty).")]
    public Guid BranchId { get; set; }

    [Required(ErrorMessage = "Kênh bán là bắt buộc.")]
    public string Channel { get; set; } = "Admin"; // 'Admin', 'POS', 'Shopee', 'TikTok', 'Lazada'

    public string? ExternalShopId { get; set; }
    public string? ExternalOrderId { get; set; }

    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string? ShippingAddress { get; set; }

    /// <summary>
    /// Khóa Idempotency bắt buộc do Client/Frontend sinh ra và duy trì ổn định khi retry.
    /// Không cấp phát mặc định Guid.NewGuid() để tránh vi phạm nguyên tắc Client-Driven Idempotency.
    /// </summary>
    [Required(ErrorMessage = "RequestKey là bắt buộc.")]
    [NotEmptyGuid(ErrorMessage = "RequestKey không được rỗng (Guid.Empty).")]
    public Guid RequestKey { get; set; }

    [Required(ErrorMessage = "Danh sách sản phẩm không được rỗng.")]
    [MinLength(1, ErrorMessage = "Đơn hàng phải có ít nhất 1 sản phẩm.")]
    public List<OrderItemRequestDto> Items { get; set; } = new();
}

/// <summary>
/// DTO duyệt đơn hàng trực tuyến hoặc đơn sàn (Reserved -> Confirmed).
/// Chốt giá vốn WAC Cost Snapshot và ghi Sổ cái Inventory Transaction OUT.
/// </summary>
public class ConfirmOrderRequestDto
{
    public Guid? ConfirmedBy { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// DTO hoàn tất đơn hàng trực tuyến hoặc đơn sàn (Confirmed -> Completed).
/// Ghi nhận khách đã nhận hàng thành công.
/// Tuyệt đối KHÔNG trừ tồn hay ghi ledger OUT lần thứ hai (vì đã ghi khi Confirm).
/// </summary>
public class CompleteOrderRequestDto
{
    public string? Note { get; set; }
}

/// <summary>
/// DTO hủy đơn hàng (Reserved -> Cancelled).
/// Tự động rollback giải phóng tồn giữ chỗ (Reserved) về lại tồn khả dụng (Available).
/// </summary>
public class CancelOrderRequestDto
{
    [Required(ErrorMessage = "Lý do hủy đơn là bắt buộc.")]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// DTO lọc và tìm kiếm danh sách đơn hàng cho Admin.
/// </summary>
public class OrderFilterRequestDto
{
    public Guid? BranchId { get; set; }
    public string? Status { get; set; } // 'Draft', 'Reserved', 'Confirmed', 'Completed', 'Cancelled'
    public string? Channel { get; set; } // 'POS', 'Admin', 'Shopee', 'TikTok', 'Lazada'
    public DateTimeOffset? FromDate { get; set; }
    public DateTimeOffset? ToDate { get; set; }
    public string? SearchKeyword { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// DTO chi tiết một dòng sản phẩm trong đơn hàng đã tạo/chốt (Dành cho Admin Portal).
/// </summary>
public class OrderItemResponseDto
{
    public Guid Id { get; set; }
    public Guid SkuId { get; set; }
    public string SkuCodeSnapshot { get; set; } = string.Empty;
    public string SkuNameSnapshot { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal? CostPrice { get; set; } // WAC snapshot tại thời điểm Confirm/Checkout
    public decimal Subtotal => Quantity * UnitPrice;
    public decimal? TotalCost => CostPrice.HasValue ? Quantity * CostPrice.Value : null;
}

/// <summary>
/// DTO chi tiết thông tin thanh toán của đơn hàng.
/// </summary>
public class PaymentResponseDto
{
    public Guid Id { get; set; }
    public string Method { get; set; } = "Cash";
    public string Status { get; set; } = "Pending";
    public decimal Amount { get; set; }
    public decimal? CashReceived { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
}

/// <summary>
/// DTO thông tin đầy đủ của một Đơn hàng đa kênh (Dành riêng cho Owner/Staff trên Admin Portal).
/// Có chứa thông tin giá vốn WAC và COGS.
/// </summary>
public class OrderResponseDto
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
    public string? ShippingAddress { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal? TotalCogs { get; set; }
    public Guid RequestKey { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<OrderItemResponseDto> Items { get; set; } = new();
    public PaymentResponseDto? Payment { get; set; }

    /// <summary>
    /// Cờ nội bộ phục vụ Controller phân biệt 201 Created (lần đầu) và 200 OK (idempotent replay).
    /// Bỏ qua khi serialize JSON ra ngoài để giữ trọn vẹn hợp đồng API.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsReplay { get; set; }
}

/// <summary>
/// DTO dòng hóa đơn bán lẻ tại quầy POS.
/// Tuyệt đối KHÔNG chứa CostPrice và TotalCost (Bảo vệ bí mật biên lợi nhuận trước thu ngân).
/// </summary>
public class PosReceiptItemDto
{
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? VariantName { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal => Quantity * UnitPrice;
}

/// <summary>
/// DTO hóa đơn bán lẻ dành riêng cho thu ngân POS và khách hàng (Khớp với ReceiptData của POS App).
/// Tuyệt đối KHÔNG chứa CostPrice, TotalCost, TotalCogs.
/// </summary>
public class PosReceiptDto
{
    public Guid OrderId { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public string? BranchName { get; set; }
    public string? BranchAddress { get; set; }
    public string? BranchPhone { get; set; }
    public string? CashierName { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public decimal TotalAmount { get; set; }
    public string PaymentMethod { get; set; } = "Cash";
    public decimal? AmountGiven { get; set; }
    public decimal? ChangeDue { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<PosReceiptItemDto> Items { get; set; } = new();
}

/// <summary>
/// DTO phản hồi kết quả thanh toán tại quầy POS kèm dữ liệu in hóa đơn chuẩn.
/// An toàn dữ liệu theo quyền RBAC (Role Cashier): Không để lộ giá vốn.
/// </summary>
public class PosCheckoutResponseDto
{
    public bool Success { get; set; } = true;
    public string Message { get; set; } = "Thanh toán thành công.";
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal? AmountGiven { get; set; }
    public decimal? ChangeDue { get; set; }
    public string PaymentMethod { get; set; } = "Cash";
    public DateTimeOffset CheckoutAt { get; set; }
    public PosReceiptDto Receipt { get; set; } = null!;
}

/// <summary>
/// DTO phân trang danh sách đơn hàng.
/// </summary>
public class PagedResultDto<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
