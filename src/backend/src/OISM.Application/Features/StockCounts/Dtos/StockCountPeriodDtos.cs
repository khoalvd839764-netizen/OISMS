namespace OISM.Application.Features.StockCounts.Dtos;

public class StockCountPeriodFilterRequestDto
{
    public Guid? BranchId { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? Status { get; set; }
    public bool? Overdue { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class StockCountPeriodFilterDto : StockCountPeriodFilterRequestDto
{
}

public class StockCountPeriodResponseDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ScheduleId { get; set; }
    public Guid BranchId { get; set; }
    public string? BranchName { get; set; }
    public Guid WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public string PeriodType { get; set; } = string.Empty;
    public string PeriodKey { get; set; } = string.Empty;
    public DateTimeOffset ScheduledAt { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public Guid AssigneeUserId { get; set; }
    public string? AssigneeName { get; set; }
    public string Status { get; set; } = "Planned";
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public bool IsOverdue { get; set; }
    public Guid? StockCountId { get; set; }
    public string? StockCountNumber { get; set; }
    public string? StockCountStatus { get; set; }
    public List<Guid> CountedByUserIds { get; set; } = new();
    public Guid? ConfirmedBy { get; set; }
    public int DifferenceSkuCount { get; set; }
}
