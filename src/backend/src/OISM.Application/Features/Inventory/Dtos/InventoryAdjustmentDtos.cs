namespace OISM.Application.Features.Inventory.Dtos;

/// <summary>
/// DTO chi tiết Phiếu điều chỉnh cân bằng kho phục vụ xem và in chứng từ (Upgrade 14 - Q9).
/// Giá vốn (UnitCost, TotalCost) được che (null) cho Staff qua ICostVisibilityService.
/// </summary>
public class InventoryAdjustmentDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public Guid? StockCountId { get; set; }
    public string? StockCountNumber { get; set; }
    public Guid CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public Guid? ConfirmedBy { get; set; }
    public string? ConfirmedByName { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public decimal TotalQuantityDelta { get; set; }
    public decimal? TotalCost { get; set; }
    public List<InventoryAdjustmentItemDto> Items { get; set; } = new();
}

public class InventoryAdjustmentItemDto
{
    public Guid Id { get; set; }
    public Guid AdjustmentId { get; set; }
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public string SkuName { get; set; } = string.Empty;
    public string BaseUnitName { get; set; } = string.Empty;
    public decimal QuantityDelta { get; set; }
    public string Direction { get; set; } = string.Empty;
    public decimal? UnitCost { get; set; }
    public decimal? TotalCost { get; set; }
    public Guid? OriginalTransactionId { get; set; }
}
