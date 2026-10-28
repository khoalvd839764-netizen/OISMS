using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Orders.Dtos;

namespace OISM.Application.Features.Orders.Services;

/// <summary>
/// Hợp đồng dịch vụ xử lý Đơn hàng đa kênh, Máy trạng thái và Thanh toán giỏ hàng nguyên tử (Order Core Engine).
/// </summary>
public interface IOrderService
{
    /// <summary>
    /// Thanh toán nhanh tại quầy POS: Chuỗi xử lý Reserve -> Confirm -> Deduct -> Ghi Ledger OUT -> Snapshot WAC
    /// trong MỘT Transaction CSDL nguyên tử duy nhất, bảo vệ bằng Row-level Advisory Lock và Idempotency Key.
    /// Trả về DTO PosCheckoutResponseDto/PosReceiptDto không chứa thông tin nhạy cảm về giá vốn/COGS.
    /// </summary>
    Task<PosCheckoutResponseDto> CheckoutPosAsync(PosCheckoutRequestDto dto, Guid? cashierUserId, CancellationToken ct = default);

    /// <summary>
    /// Tạo đơn hàng thủ công hoặc tiếp nhận đơn: Khởi tạo Order 'Draft'/'Reserved' và giữ chỗ tồn kho.
    /// Bắt buộc RequestKey ổn định từ client. Kênh 'Admin' bắt buộc khớp giá bán lẻ catalog.
    /// </summary>
    Task<OrderResponseDto> CreateOrderAsync(CreateOrderRequestDto dto, Guid? createdByUserId, CancellationToken ct = default);

    /// <summary>
    /// Tiếp nhận và tạo đơn hàng từ Webhook sàn TMĐT (Shopee, TikTok, Lazada) ở trạng thái 'Reserved'
    /// với cùng lõi phân bổ kho và giữ chỗ tồn kho nguyên tử như đơn hàng Admin, sử dụng giá sàn TMĐT tin cậy từ server.
    /// </summary>
    Task<OrderResponseDto> CreateMarketplaceReservedOrderAsync(InternalMarketplaceOrderContext context, CancellationToken ct = default);

    /// <summary>
    /// Duyệt đơn hàng ('Reserved' -> 'Confirmed'): Trừ tồn kho on_hand, giảm reserved, ghi Sổ cái OUT và chốt giá vốn WAC snapshot.
    /// </summary>
    Task<OrderResponseDto> ConfirmOrderAsync(Guid orderId, ConfirmOrderRequestDto dto, Guid? confirmedByUserId, CancellationToken ct = default);

    /// <summary>
    /// Hoàn tất đơn hàng online ('Confirmed' -> 'Completed'): Ghi nhận khách nhận hàng thành công, kết thúc vòng đời.
    /// Tuyệt đối KHÔNG trừ tồn kho hay ghi ledger OUT lần thứ hai (vì đã ghi khi Confirm).
    /// </summary>
    Task<OrderResponseDto> CompleteOrderAsync(Guid orderId, CompleteOrderRequestDto dto, Guid? completedByUserId, CancellationToken ct = default);

    /// <summary>
    /// Hủy đơn hàng ('Reserved' -> 'Cancelled'): Giải phóng tồn giữ chỗ (Reserved) về lại quỹ tồn khả dụng (Available).
    /// </summary>
    Task<OrderResponseDto> CancelOrderAsync(Guid orderId, CancelOrderRequestDto dto, Guid? cancelledByUserId, CancellationToken ct = default);

    /// <summary>
    /// Tra cứu chi tiết một đơn hàng theo ID kèm thông tin mặt hàng, giá vốn snapshot và thanh toán (Staff/Owner).
    /// </summary>
    Task<OrderResponseDto?> GetOrderByIdAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>
    /// Tìm kiếm và lọc danh sách đơn hàng có phân trang.
    /// </summary>
    Task<PagedResultDto<OrderResponseDto>> GetOrdersAsync(OrderFilterRequestDto filter, CancellationToken ct = default);

    /// <summary>
    /// Tác vụ nền (Hangfire Worker): Tự động quét và giải phóng các phiếu giữ chỗ quá hạn TTL (15 phút).
    /// </summary>
    Task<int> ExpireOverdueReservationsAsync(DateTimeOffset? cutoffUtc = null, CancellationToken ct = default);

    /// <summary>
    /// Thống kê chi tiết kết quả xử lý batch của lượt quét hết hạn gần nhất trên instance hiện tại.
    /// </summary>
    BatchExecutionStats LastExecutionStats { get; }

    // ======================================================================================
    // Phương thức tương thích cho BE2 Query và Simulator (sẽ hoàn thiện tại T02)
    // ======================================================================================
    Task<PagedResult<OrderSummaryDto>> GetOrdersAsync(OrderFilterDto filter, CancellationToken ct = default);
    Task<OrderDetailDto?> GetOrderDetailByIdAsync(Guid id, CancellationToken ct = default);
    Task<OrderDetailDto> CreateOrderAsync(CreateOrderDto dto, Guid? userId, CancellationToken ct = default);
    Task<OrderDetailDto> ConfirmOrderAsync(Guid id, Guid? userId, CancellationToken ct = default);
    Task<OrderDetailDto> ProcessOrderAsync(Guid id, Guid? userId, CancellationToken ct = default);
    Task<OrderDetailDto> CompleteOrderAsync(Guid id, Guid? userId, CancellationToken ct = default);
    Task<OrderDetailDto> CancelOrderAsync(Guid id, Guid? userId, CancellationToken ct = default);
}
