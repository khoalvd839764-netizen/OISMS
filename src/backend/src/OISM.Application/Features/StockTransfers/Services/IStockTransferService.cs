using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.StockTransfers.Dtos;

namespace OISM.Application.Features.StockTransfers.Services;

/// <summary>
/// Hợp đồng dịch vụ Chuyển kho nội bộ 2 bước (FR-INV-03).
/// </summary>
public interface IStockTransferService
{
    /// <summary>
    /// Tạo phiếu chuyển kho ở trạng thái nháp ('Draft').
    /// </summary>
    Task<StockTransferResponseDto> CreateTransferAsync(CreateStockTransferRequestDto dto, Guid createdByUserId, CancellationToken ct = default);

    /// <summary>
    /// Bước 1 - Xuất gửi hàng ('Draft' -> 'InTransit'):
    /// Trừ on_hand kho xuất, ghi nhận hàng đang luân chuyển, ghi Sổ cái InventoryTransaction OUT với giá vốn WAC tại kho xuất.
    /// </summary>
    Task<StockTransferResponseDto> DispatchTransferAsync(Guid transferId, DispatchStockTransferRequestDto dto, Guid dispatchedByUserId, CancellationToken ct = default);

    /// <summary>
    /// Bước 2 - Tiếp nhận nhập kho đích ('InTransit' -> 'Received'):
    /// Cộng on_hand kho đích, ghi Sổ cái InventoryTransaction IN, cập nhật lại WAC tại chi nhánh/kho đích theo công thức bình quân gia quyền.
    /// </summary>
    Task<StockTransferResponseDto> ReceiveTransferAsync(Guid transferId, ReceiveStockTransferRequestDto dto, Guid receivedByUserId, CancellationToken ct = default);

    /// <summary>
    /// Hủy phiếu chuyển kho khi còn ở trạng thái 'Draft' ('Draft' -> 'Cancelled').
    /// </summary>
    Task<StockTransferResponseDto> CancelTransferAsync(Guid transferId, CancelStockTransferRequestDto? dto, Guid cancelledByUserId, CancellationToken ct = default);

    /// <summary>
    /// Hủy phiếu chuyển kho khi còn ở trạng thái 'Draft' ('Draft' -> 'Cancelled') kèm lý do string.
    /// </summary>
    Task<StockTransferResponseDto> CancelTransferAsync(Guid transferId, string? reason, Guid cancelledByUserId, CancellationToken ct = default);

    /// <summary>
    /// Lấy chi tiết phiếu chuyển kho theo ID.
    /// </summary>
    Task<StockTransferResponseDto?> GetTransferByIdAsync(Guid transferId, CancellationToken ct = default);

    /// <summary>
    /// Danh sách phiếu chuyển kho có phân trang và lọc theo trạng thái, kho bãi.
    /// </summary>
    Task<PagedResultDto<StockTransferResponseDto>> GetTransfersAsync(StockTransferFilterRequestDto filter, CancellationToken ct = default);
}
