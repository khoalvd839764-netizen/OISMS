namespace OISM.Application.Common.Interfaces;

/// <summary>
/// Hợp đồng tác vụ nền quét và tự động hủy các đơn hàng giữ chỗ quá hạn TTL (15 phút).
/// </summary>
public interface IReservationExpiryJob
{
    /// <summary>
    /// Thực thi quét đa tenant và giải phóng phiếu giữ chỗ quá hạn.
    /// </summary>
    /// <param name="cutoffUtc">Mốc thời gian so sánh (mặc định DateTimeOffset.UtcNow nếu null).</param>
    /// <param name="ct">CancellationToken kiểm soát dừng tác vụ sạch.</param>
    /// <returns>Tổng số đơn hàng đã được tự động hủy thành công trên tất cả tenant.</returns>
    Task<int> ExecuteAsync(DateTimeOffset? cutoffUtc = null, CancellationToken ct = default);
}
