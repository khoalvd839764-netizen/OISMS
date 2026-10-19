namespace OISM.Application.Features.Inventory.Dtos;

public class InventoryLedgerDto
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// Loại biến động: 'IN' hoặc 'OUT'.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal BalanceAfter { get; set; }
    public long SequenceNo { get; set; }

    public Guid? ReceiptItemId { get; set; }
    public string? DocumentNumber { get; set; }
    public Guid ReferenceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class InventoryLedgerFilterDto
{
    public Guid? BranchId { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? SkuId { get; set; }
    public string? Type { get; set; }
    public DateTimeOffset? FromDate { get; set; }
    public DateTimeOffset? ToDate { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
