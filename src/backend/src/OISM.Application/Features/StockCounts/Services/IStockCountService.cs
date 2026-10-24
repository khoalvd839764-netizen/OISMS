using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.StockCounts.Dtos;

namespace OISM.Application.Features.StockCounts.Services;

/// <summary>
/// Hợp đồng dịch vụ Kiểm kê kho và Bút toán cân bằng sai lệch tồn (FR-INV-04).
/// </summary>
public interface IStockCountService
{
    /// <summary>
    /// Khởi tạo đợt kiểm kê: Chụp snapshot số lượng tồn hệ thống (expected_quantity) và sequence number tại kho.
    /// </summary>
    Task<StockCountResponseDto> CreateStockCountAsync(CreateStockCountRequestDto dto, Guid createdByUserId, CancellationToken ct = default);

    /// <summary>
    /// Cập nhật số lượng kiểm đếm thực tế của nhân viên kho ('Draft' -> 'Counting').
    /// </summary>
    Task<StockCountResponseDto> RecordCountsAsync(Guid stockCountId, RecordStockCountBatchRequestDto dto, Guid recordedByUserId, CancellationToken ct = default);

    /// <summary>
    /// Chốt đợt kiểm kê ('Counting' -> 'Confirmed'):
    /// Tự động sinh phiếu điều chỉnh tồn kho (InventoryAdjustment) và ghi bút toán cân bằng Sổ cái (Ledger) cho mọi SKU có sai lệch.
    /// </summary>
    Task<StockCountResponseDto> ConfirmStockCountAsync(Guid stockCountId, ConfirmStockCountRequestDto dto, Guid confirmedByUserId, CancellationToken ct = default);

    /// <summary>
    /// Hủy đợt kiểm kê khi chưa chốt ('Draft'/'Counting' -> 'Cancelled').
    /// </summary>
    Task<StockCountResponseDto> CancelStockCountAsync(Guid stockCountId, string? reason, Guid cancelledByUserId, CancellationToken ct = default);

    /// <summary>
    /// Lấy chi tiết đợt kiểm kê theo ID kèm so sánh chênh lệch giữa số đếm và sổ sách.
    /// </summary>
    Task<StockCountResponseDto?> GetStockCountByIdAsync(Guid stockCountId, CancellationToken ct = default);

    /// <summary>
    /// Danh sách đợt kiểm kê có phân trang và bộ lọc.
    /// </summary>
    Task<PagedResultDto<StockCountResponseDto>> GetStockCountsAsync(StockCountFilterRequestDto filter, CancellationToken ct = default);
}
