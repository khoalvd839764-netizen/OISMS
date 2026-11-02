using System.ComponentModel.DataAnnotations;

namespace OISM.Application.Features.Ai.Dtos;

/// <summary>
/// DTO truy vấn lịch sử xuất kho phục vụ phân tích xu hướng và huấn luyện mô hình dự báo AI (FR-AI-01).
/// </summary>
public class ExportHistoryQueryDto
{
    public Guid? BranchId { get; set; }
    public Guid? SkuId { get; set; }

    [Range(1, 365, ErrorMessage = "Số ngày thống kê phải từ 1 đến 365 ngày.")]
    public int Days { get; set; } = 30;

    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

/// <summary>
/// Chi tiết số lượng xuất kho theo ngày và SKU (chuẩn hóa theo đơn vị tồn gốc BaseUnit).
/// </summary>
public class ExportHistoryDailyItemDto
{
    public DateOnly Date { get; set; }
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public string SkuName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string BaseUnit { get; set; } = "piece";
    public int TransactionCount { get; set; }
}

/// <summary>
/// Dữ liệu tổng hợp lịch sử xuất kho chuỗi thời gian.
/// </summary>
public class ExportHistoryResponseDto
{
    public Guid TenantId { get; set; }
    public Guid? BranchId { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public decimal TotalExportQuantity { get; set; }
    public int TotalDays { get; set; }
    public decimal AverageDailyExportQuantity { get; set; }
    public List<ExportHistoryDailyItemDto> DailyRecords { get; set; } = new();
}

/// <summary>
/// DTO tham số truy vấn gợi ý kế hoạch nhập lại hàng hóa (Reorder Recommendations).
/// </summary>
public class ReorderRecommendationQueryDto
{
    public Guid? BranchId { get; set; }

    /// <summary>
    /// Thời gian nhà cung cấp giao hàng tính bằng ngày (Lead time). Mặc định 7 ngày.
    /// </summary>
    [Range(1, 180, ErrorMessage = "Lead time phải từ 1 đến 180 ngày.")]
    public int LeadTimeDays { get; set; } = 7;

    /// <summary>
    /// Số ngày dự trữ an toàn (Safety stock days). Mặc định 3 ngày.
    /// </summary>
    [Range(0, 90, ErrorMessage = "Số ngày dự trữ an toàn phải từ 0 đến 90 ngày.")]
    public int SafetyStockDays { get; set; } = 3;

    /// <summary>
    /// Chỉ lọc các SKU thực sự cần nhập hàng (NeedsReorder = true).
    /// </summary>
    public bool OnlyNeedsReorder { get; set; } = false;
}

/// <summary>
/// Bản ghi gợi ý kế hoạch nhập hàng theo từng SKU (FR-AI-01).
/// </summary>
public class ReorderRecommendationDto
{
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public string SkuName { get; set; } = string.Empty;
    public string BaseUnit { get; set; } = "piece";

    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;

    public decimal OnHandQuantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal AvailableQuantity { get; set; }
    public decimal ReorderPoint { get; set; }

    /// <summary>
    /// Vận tốc tiêu thụ trung bình ngày (Daily Sales Velocity).
    /// </summary>
    public decimal DailySalesVelocity { get; set; }

    /// <summary>
    /// Số ngày dự kiến tồn kho hiện tại sẽ cạn kiệt (Days until stockout).
    /// </summary>
    public int DaysUntilStockout { get; set; }

    public int LeadTimeDays { get; set; }
    public int SafetyStockDays { get; set; }

    /// <summary>
    /// Đánh giá có cần đặt hàng nhập thêm hay không.
    /// </summary>
    public bool NeedsReorder { get; set; }

    /// <summary>
    /// Số lượng đề xuất nhập thêm (tính theo BaseUnit).
    /// </summary>
    public decimal SuggestedQuantity { get; set; }

    /// <summary>
    /// Mức độ tin cậy của thuật toán dự báo: 'High', 'Medium', 'Low'.
    /// </summary>
    public string ConfidenceLevel { get; set; } = "Medium";

    /// <summary>
    /// Lý do và giải thích logic gợi ý (Explainable AI reasoning).
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Tổng kết danh sách gợi ý nhập hàng toàn hệ thống hoặc chi nhánh.
/// </summary>
public class ReorderRecommendationSummaryDto
{
    public Guid TenantId { get; set; }
    public Guid? BranchId { get; set; }
    public DateTime EvaluatedAt { get; set; }
    public int TotalSkusAnalyzed { get; set; }
    public int SkusNeedingReorder { get; set; }
    public decimal TotalSuggestedQuantity { get; set; }
    public List<ReorderRecommendationDto> Recommendations { get; set; } = new();
}
