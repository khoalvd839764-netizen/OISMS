namespace OISM.Application.Features.Inventory.Dtos;

public class InventoryBalanceDto
{
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public Guid? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }

    /// <summary>
    /// Tồn thực tế trong kho.
    /// </summary>
    public decimal OnHand { get; set; }

    /// <summary>
    /// Số lượng đang giữ chỗ cho đơn hàng.
    /// </summary>
    public decimal Reserved { get; set; }

    /// <summary>
    /// Số lượng tồn khả dụng để bán (Available = OnHand - Reserved).
    /// </summary>
    public decimal Available => Math.Max(0, OnHand - Reserved);

    /// <summary>
    /// Giá vốn bình quân gia quyền (WAC).
    /// </summary>
    public decimal AverageCost { get; set; }
}

public class InventoryBalanceFilterDto
{
    public Guid? BranchId { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? SkuId { get; set; }
    public string? SearchTerm { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
