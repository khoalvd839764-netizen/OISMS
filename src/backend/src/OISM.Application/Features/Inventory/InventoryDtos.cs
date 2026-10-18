using FluentValidation;

namespace OISM.Application.Features.Inventory;

public class InventoryBalanceResponse
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public decimal OnHand { get; set; }
    public decimal Reserved { get; set; }
    public decimal Available => OnHand - Reserved;
    public decimal AverageCost { get; set; }
    public decimal TotalValue => OnHand * AverageCost;
    public decimal ReorderPoint { get; set; }
}

public class UpdateReorderPointRequest
{
    public Guid? WarehouseId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid SkuId { get; set; }
    public decimal ReorderPoint { get; set; }
}

public class CreateGoodsReceiptLineRequest
{
    public Guid SkuId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
}

public class CreateGoodsReceiptRequest
{
    public Guid WarehouseId { get; set; }
    public Guid SupplierId { get; set; }
    public string? Note { get; set; }
    public List<CreateGoodsReceiptLineRequest>? Lines { get; set; }
    public List<CreateGoodsReceiptLineRequest>? Items { get; set; }
}

public class CreateGoodsReceiptValidator : AbstractValidator<CreateGoodsReceiptRequest>
{
    public CreateGoodsReceiptValidator()
    {
        RuleFor(x => x.WarehouseId)
            .NotEmpty().WithMessage("Vui lòng chọn kho nhận hàng.");

        RuleFor(x => x.SupplierId)
            .NotEmpty().WithMessage("Vui lòng chọn nhà cung cấp.");

        RuleFor(x => x)
            .Must(x => (x.Lines != null && x.Lines.Count > 0) || (x.Items != null && x.Items.Count > 0))
            .WithMessage("Phiếu nhập kho phải có ít nhất 1 dòng hàng SKU.");
    }
}

public class GoodsReceiptLineResponse
{
    public Guid Id { get; set; }
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineTotal => Quantity * UnitCost;
}

public class GoodsReceiptResponse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public string? Note { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public List<GoodsReceiptLineResponse> Lines { get; set; } = new();
}

public class InventoryLedgerEntryResponse
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string Direction { get; set; } = "IN";
    public string OperationType { get; set; } = "GoodsReceipt";
    public decimal QuantityDelta { get; set; }
    public decimal BalanceAfter { get; set; }
    public string ReferenceCode { get; set; } = string.Empty;
}

public class ConfirmGoodsReceiptResponse
{
    public string Message { get; set; } = string.Empty;
    public GoodsReceiptResponse Receipt { get; set; } = new();
}

