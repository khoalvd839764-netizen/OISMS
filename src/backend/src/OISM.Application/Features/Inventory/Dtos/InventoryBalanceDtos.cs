using FluentValidation;

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

    /// <summary>
    /// Ngưỡng cảnh báo hết hàng / đặt hàng lại (ReorderPoint - CS-03).
    /// </summary>
    public decimal ReorderPoint { get; set; }
}

public class UpdateReorderPointDto
{
    public Guid BranchId { get; set; }
    public Guid SkuId { get; set; }
    public decimal ReorderPoint { get; set; }
}

public class BulkUpdateReorderPointDto
{
    public Guid BranchId { get; set; }
    public List<SkuReorderPointItemDto> Items { get; set; } = new();
}

public class SkuReorderPointItemDto
{
    public Guid SkuId { get; set; }
    public decimal ReorderPoint { get; set; }
}

public class UpdateReorderPointValidator : FluentValidation.AbstractValidator<UpdateReorderPointDto>
{
    public UpdateReorderPointValidator()
    {
        RuleFor(x => x.BranchId).NotEmpty().WithMessage("Chi nhánh không được để trống.");
        RuleFor(x => x.SkuId).NotEmpty().WithMessage("SKU không được để trống.");
        RuleFor(x => x.ReorderPoint).GreaterThanOrEqualTo(0).WithMessage("Ngưỡng cảnh báo tồn kho không được âm.");
    }
}

public class BulkUpdateReorderPointValidator : FluentValidation.AbstractValidator<BulkUpdateReorderPointDto>
{
    public BulkUpdateReorderPointValidator()
    {
        RuleFor(x => x.BranchId).NotEmpty().WithMessage("Chi nhánh không được để trống.");
        RuleFor(x => x.Items).NotEmpty().WithMessage("Danh sách sản phẩm cấu hình không được để trống.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.SkuId).NotEmpty().WithMessage("Mã SKU không được để trống.");
            item.RuleFor(i => i.ReorderPoint).GreaterThanOrEqualTo(0).WithMessage("Ngưỡng cảnh báo tồn kho không được âm.");
        });
    }
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
