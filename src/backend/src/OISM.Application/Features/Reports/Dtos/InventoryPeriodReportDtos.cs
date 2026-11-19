using System.ComponentModel.DataAnnotations;

namespace OISM.Application.Features.Reports.Dtos;

/// <summary>
/// Bộ lọc báo cáo tồn kho theo kỳ (FR-REP / Upgrade 13).
/// </summary>
public class InventoryPeriodFilterDto
{
    [Required]
    public DateOnly FromDate { get; set; }

    [Required]
    public DateOnly ToDate { get; set; }

    /// <summary>
    /// Chi nhánh cần xem. Nếu null hoặc Guid.Empty: xem toàn bộ cửa hàng (chế độ Tất cả).
    /// </summary>
    public Guid? BranchId { get; set; }

    /// <summary>
    /// Tìm kiếm theo tên sản phẩm, mã SKU hoặc mã vạch barcode.
    /// </summary>
    public string? SearchTerm { get; set; }

    /// <summary>
    /// Lọc theo danh mục sản phẩm.
    /// </summary>
    public Guid? CategoryId { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

/// <summary>
/// Phân rã chi tiết các nguồn nhập kho trong kỳ.
/// </summary>
public class InventoryPeriodInBreakdownDto
{
    public decimal PurchaseQuantity { get; set; }
    public decimal? PurchaseValue { get; set; }

    public decimal ReturnQuantity { get; set; }
    public decimal? ReturnValue { get; set; }

    public decimal TransferInQuantity { get; set; }
    public decimal? TransferInValue { get; set; }

    public decimal AdjustmentInQuantity { get; set; }
    public decimal? AdjustmentInValue { get; set; }
}

/// <summary>
/// Phân rã chi tiết các kênh xuất kho trong kỳ.
/// </summary>
public class InventoryPeriodOutBreakdownDto
{
    public decimal PosQuantity { get; set; }
    public decimal? PosValue { get; set; }

    public decimal EcommerceQuantity { get; set; }
    public decimal? EcommerceValue { get; set; }

    public decimal AdminQuantity { get; set; }
    public decimal? AdminValue { get; set; }

    public decimal TransferOutQuantity { get; set; }
    public decimal? TransferOutValue { get; set; }

    public decimal AdjustmentOutQuantity { get; set; }
    public decimal? AdjustmentOutValue { get; set; }
}

/// <summary>
/// Dòng báo cáo tồn kho theo kỳ của một SKU.
/// </summary>
public class InventoryPeriodItemDto
{
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? VariantName { get; set; }
    public string? CategoryName { get; set; }
    public string UnitName { get; set; } = "Cái";

    public decimal OpeningQuantity { get; set; }
    public decimal? OpeningValue { get; set; }

    public decimal InQuantity { get; set; }
    public decimal? InValue { get; set; }

    public decimal OutQuantity { get; set; }
    public decimal? OutValue { get; set; }

    public decimal ClosingQuantity { get; set; }
    public decimal? ClosingValue { get; set; }

    /// <summary>
    /// Chế độ Tất cả: Hàng đang chuyển đầu kỳ (phiếu InTransit xuất trước mốc đầu kỳ mà chưa nhận trước đầu kỳ).
    /// </summary>
    public decimal OpeningInTransitQuantity { get; set; }

    /// <summary>
    /// Chế độ Tất cả: Hàng đang chuyển cuối kỳ.
    /// Chế độ Một chi nhánh: Hàng đang chuyển đến chi nhánh này tại thời điểm cuối kỳ.
    /// </summary>
    public decimal ClosingInTransitQuantity { get; set; }

    public InventoryPeriodInBreakdownDto InBreakdown { get; set; } = new();
    public InventoryPeriodOutBreakdownDto OutBreakdown { get; set; } = new();
}

/// <summary>
/// Dòng tổng cộng báo cáo tồn kho theo kỳ.
/// </summary>
public class InventoryPeriodSummaryDto
{
    public decimal TotalOpeningQuantity { get; set; }
    public decimal? TotalOpeningValue { get; set; }

    public decimal TotalInQuantity { get; set; }
    public decimal? TotalInValue { get; set; }

    public decimal TotalOutQuantity { get; set; }
    public decimal? TotalOutValue { get; set; }

    public decimal TotalClosingQuantity { get; set; }
    public decimal? TotalClosingValue { get; set; }

    public decimal TotalOpeningInTransitQuantity { get; set; }
    public decimal TotalClosingInTransitQuantity { get; set; }
}

/// <summary>
/// Báo cáo tồn kho theo kỳ (NXT).
/// </summary>
public class InventoryPeriodReportDto
{
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public Guid? BranchId { get; set; }
    public string BranchName { get; set; } = "Tất cả chi nhánh";
    public bool IsOwner { get; set; }
    public bool IsPeriodUpToNow { get; set; }

    public List<InventoryPeriodItemDto> Items { get; set; } = new();
    public InventoryPeriodSummaryDto Summary { get; set; } = new();

    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
}

/// <summary>
/// Bộ lọc thẻ kho cho một SKU cụ thể tại một chi nhánh.
/// </summary>
public class StockCardFilterDto
{
    [Required]
    public Guid SkuId { get; set; }

    [Required]
    public Guid BranchId { get; set; }

    [Required]
    public DateOnly FromDate { get; set; }

    [Required]
    public DateOnly ToDate { get; set; }
}

/// <summary>
/// Dòng phát sinh trong thẻ kho / sổ chi tiết hàng hóa.
/// </summary>
public class StockCardEntryDto
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Type { get; set; } = string.Empty; // "IN" hoặc "OUT"
    public string SourceType { get; set; } = string.Empty; // "Nhập mua", "Xuất bán POS", "Xuất bán Shopee", "Khách trả", "Chuyển kho đến", "Chuyển kho đi", "Kiểm kê"...
    public string DocumentNumber { get; set; } = string.Empty;
    public Guid? ReferenceId { get; set; }

    public decimal InQuantity { get; set; }
    public decimal? InUnitPrice { get; set; }
    public decimal? InValue { get; set; }

    public decimal OutQuantity { get; set; }
    public decimal? OutUnitPrice { get; set; }
    public decimal? OutValue { get; set; }

    public decimal BalanceAfterQuantity { get; set; }
    public decimal? BalanceAfterValue { get; set; }
}

/// <summary>
/// Thẻ kho S12-DN (Staff) hoặc Sổ chi tiết hàng hóa S10-DN (Owner).
/// </summary>
public class StockCardReportDto
{
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? VariantName { get; set; }
    public string UnitName { get; set; } = "Cái";

    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;

    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }

    public bool IsOwner { get; set; }
    public string ReportTitle { get; set; } = string.Empty; // "Thẻ kho (S12-DN)" hoặc "Sổ chi tiết hàng hóa (S10-DN)"

    public decimal OpeningQuantity { get; set; }
    public decimal? OpeningValue { get; set; }

    public List<StockCardEntryDto> Entries { get; set; } = new();

    public decimal TotalInQuantity { get; set; }
    public decimal? TotalInValue { get; set; }

    public decimal TotalOutQuantity { get; set; }
    public decimal? TotalOutValue { get; set; }

    public decimal ClosingQuantity { get; set; }
    public decimal? ClosingValue { get; set; }

    // Dành cho Owner khi kỳ tính đến hiện tại:
    public bool HasRoundingDifference { get; set; }
    public decimal? RoundingDifference { get; set; }
    public decimal? CurrentOnHand { get; set; }
    public decimal? CurrentWac { get; set; }
    public decimal? CurrentTheoreticalValue { get; set; }
}
