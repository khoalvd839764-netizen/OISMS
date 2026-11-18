namespace OISM.Application.Features.Reports.Dtos;

public class RebuildDailySummaryRequestDto
{
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
}

public class RebuildDailySummaryResponseDto
{
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public int DaysProcessed { get; set; }
    public int RowsWritten { get; set; }
}

public class DailySalesSummaryMetricsDto
{
    public decimal Revenue { get; set; }
    public decimal ReturnAmount { get; set; }
    public decimal NetRevenue { get; set; }
    public decimal CostOfGoodsSold { get; set; }
    public decimal GrossProfit { get; set; }
}

public class DailySalesSummaryDayDto
{
    public DateOnly Date { get; set; }
    public DailySalesSummaryMetricsDto? Summary { get; set; }
    public DailySalesSummaryMetricsDto Live { get; set; } = new();
    public bool Matched { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class DailySalesSummaryComparisonDto
{
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public Guid? BranchId { get; set; }
    public List<DailySalesSummaryDayDto> Days { get; set; } = new();
}
