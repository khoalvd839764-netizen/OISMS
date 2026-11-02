using OISM.Application.Features.Ai.Dtos;

namespace OISM.Application.Features.Ai.Services;

/// <summary>
/// Giao diện dịch vụ phân tích dữ liệu lịch sử xuất kho và dự báo nhu cầu nhập hàng thông minh (FR-AI-01).
/// </summary>
public interface IAiForecastingService
{
    /// <summary>
    /// Lấy chuỗi dữ liệu lịch sử xuất kho theo ngày và SKU phục vụ thuật toán AI dự báo.
    /// </summary>
    Task<ExportHistoryResponseDto> GetExportHistoryAsync(ExportHistoryQueryDto query, CancellationToken ct = default);

    /// <summary>
    /// Tính toán và đề xuất kế hoạch nhập lại hàng hóa dựa trên vận tốc bán, tồn khả dụng và thời gian chờ giao hàng.
    /// </summary>
    Task<ReorderRecommendationSummaryDto> GetReorderRecommendationsAsync(ReorderRecommendationQueryDto query, CancellationToken ct = default);
}
