namespace OISM.Application.Features.Reports.Dtos;

public class ReportFilterDto
{
    public Guid? BranchId { get; set; }
    public string? Channel { get; set; }
    public DateTimeOffset? StartDate { get; set; }
    public DateTimeOffset? EndDate { get; set; }
    public int TopN { get; set; } = 10;
}

public class ChannelRevenueBreakdownDto
{
    public string Channel { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public decimal CostOfGoodsSold { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal GrossMarginPercentage { get; set; }
    public int OrderCount { get; set; }
}

public class BranchRevenueBreakdownDto
{
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public decimal CostOfGoodsSold { get; set; }
    public decimal GrossProfit { get; set; }
    public int OrderCount { get; set; }
}

public class DailyRevenueBreakdownDto
{
    public string Date { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public decimal CostOfGoodsSold { get; set; }
    public decimal GrossProfit { get; set; }
    public int OrderCount { get; set; }
}

public class RevenueProfitReportDto
{
    public decimal TotalNetRevenue { get; set; }
    public decimal TotalCostOfGoodsSold { get; set; }
    public decimal TotalGrossProfit { get; set; }
    public decimal GrossMarginPercentage { get; set; }
    public int TotalOrders { get; set; }
    public decimal TotalItemsSold { get; set; }

    public List<ChannelRevenueBreakdownDto> ByChannel { get; set; } = new();
    public List<BranchRevenueBreakdownDto> ByBranch { get; set; } = new();
    public List<DailyRevenueBreakdownDto> DailyTrend { get; set; } = new();
}

public class SkuVelocityItemDto
{
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public string SkuName { get; set; } = string.Empty;
    public decimal QuantitySold { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal CurrentOnHand { get; set; }
    public decimal DaysWithoutSale { get; set; }
    /// <summary>Đơn vị gốc của SKU; null khi SKU chưa khai báo đơn vị gốc.</summary>
    public string? BaseUnitName { get; set; }
}

/// <summary>Số lượng tồn của các SKU có cùng đơn vị gốc.</summary>
public class UnitQuantityDto
{
    public string UnitName { get; set; } = string.Empty;
    public decimal OnHand { get; set; }
    public decimal Reserved { get; set; }
    public decimal Available { get; set; }
}

public class InventoryVelocityReportDto
{
    public decimal TotalInventoryValue { get; set; }
    public decimal TotalOnHandQuantity { get; set; }
    public decimal TotalReservedQuantity { get; set; }
    public decimal TotalAvailableQuantity { get; set; }
    /// <summary>Tồn tách theo đơn vị gốc, vì cộng gộp khác đơn vị (Cái, Kg) không có ý nghĩa.</summary>
    public List<UnitQuantityDto> QuantityByUnit { get; set; } = new();

    public List<SkuVelocityItemDto> BestSellers { get; set; } = new();
    public List<SkuVelocityItemDto> SlowMovingItems { get; set; } = new();
}

public class StockAlertDto
{
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public string SkuName { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public decimal OnHand { get; set; }
    public decimal Reserved { get; set; }
    public decimal Available { get; set; }
    public decimal ReorderPoint { get; set; }

    /// <summary>
    /// Mức độ cảnh báo: 'OutOfStock' (hết sạch), 'Critical' (dưới 50% ngưỡng), 'Warning' (chạm ngưỡng).
    /// </summary>
    public string AlertLevel { get; set; } = "Warning";
}

public class RecentOrderDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class DashboardSummaryDto
{
    public decimal TotalRevenue { get; set; }
    public int TotalOrders { get; set; }
    public decimal? TotalGrossProfit { get; set; }
    public decimal GrossMarginPercentage { get; set; }
    public int SkuCount { get; set; }
    public decimal? TotalInventoryValue { get; set; }
    public int LowStockCount { get; set; }
    public List<RecentOrderDto> RecentOrders { get; set; } = new();
    public List<DailyRevenueBreakdownDto> RecentRevenueTrend { get; set; } = new();
}
