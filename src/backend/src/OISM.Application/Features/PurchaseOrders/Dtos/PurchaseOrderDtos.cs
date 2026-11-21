using System.ComponentModel.DataAnnotations;
using OISM.Application.Features.Inventory.Dtos;

namespace OISM.Application.Features.PurchaseOrders.Dtos;

public class CreatePurchaseOrderDto
{
    public Guid? SupplierId { get; set; }
    public Guid BranchId { get; set; }
    public DateOnly? OrderDate { get; set; }
    public decimal? ShippingFee { get; set; }
    public decimal? DiscountAmount { get; set; }
    public decimal? VatAmount { get; set; }

    [MaxLength(50)]
    public string? Number { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }

    public List<CreatePurchaseOrderItemDto> Items { get; set; } = new();
}

public class UpdatePurchaseOrderDraftDto
{
    public Guid? SupplierId { get; set; }
    public Guid BranchId { get; set; }
    public DateOnly? OrderDate { get; set; }
    public decimal? ShippingFee { get; set; }
    public decimal? DiscountAmount { get; set; }
    public decimal? VatAmount { get; set; }
    public int? Version { get; set; }

    [MaxLength(50)]
    public string? Number { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }

    public List<CreatePurchaseOrderItemDto> Items { get; set; } = new();
}

public class CreatePurchaseOrderItemDto
{
    public Guid SkuId { get; set; }
    public Guid? UnitId { get; set; }
    public decimal Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public List<PurchaseOrderSalePriceConfigInputDto>? SalePriceConfigs { get; set; }
}

public class ApprovePurchaseOrderRequestDto
{
    public bool ConfirmZeroPrice { get; set; }
    public bool ConfirmLossPrice { get; set; }
    public int? Version { get; set; }
}

public class ReturnPurchaseOrderRequestDto
{
    public string? Reason { get; set; }
}

public class ClosePurchaseOrderRequestDto
{
    public string? Reason { get; set; }
}

public class PurchaseOrderPriceSuggestionQueryDto
{
    public Guid? SupplierId { get; set; }
    public Guid BranchId { get; set; }
    public Guid SkuId { get; set; }
    public Guid? UnitId { get; set; }
}

public class PurchaseOrderPriceSuggestionDto
{
    public Guid? SupplierId { get; set; }
    public Guid BranchId { get; set; }
    public Guid SkuId { get; set; }
    public Guid? UnitId { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal FactorToBase { get; set; } = 1m;
    public decimal? SuggestedUnitPrice { get; set; }
    public decimal? LastAllocatedUnitCost { get; set; }
    public Guid? LastPurchaseOrderId { get; set; }
    public string? LastPurchaseOrderNumber { get; set; }
    public DateOnly? LastOrderDate { get; set; }
    public DateTimeOffset? LastApprovedAt { get; set; }
    public decimal CurrentBaseWac { get; set; }
    public decimal CurrentUnitWac { get; set; }
}

public class PurchaseOrderFilterDto
{
    public string? Status { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? BranchId { get; set; }
    public DateTimeOffset? FromDate { get; set; }
    public DateTimeOffset? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class PurchaseOrderResponseDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public Guid RequestKey { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public DateOnly OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }
    public decimal? Subtotal { get; set; }
    public decimal? ShippingFee { get; set; }
    public decimal? DiscountAmount { get; set; }
    public decimal? VatAmount { get; set; }
    public decimal? TotalAllocatedCost { get; set; }
    public decimal? TotalAmount { get; set; }
    public int Version { get; set; }
    public Guid CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public Guid? ApprovedBy { get; set; }
    public string? ApprovedByName { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid? ReturnedBy { get; set; }
    public string? ReturnedByName { get; set; }
    public DateTimeOffset? ReturnedAt { get; set; }
    public string? ReturnReason { get; set; }
    public Guid? ClosedBy { get; set; }
    public string? ClosedByName { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string? CloseReason { get; set; }
    public Guid? CancelledBy { get; set; }
    public string? CancelledByName { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool IsReplay { get; set; }
    public PurchaseOrderApprovalReadinessDto? ApprovalReadiness { get; set; }

    public List<PurchaseOrderItemResponseDto> Items { get; set; } = new();
}

public class PurchaseOrderApprovalReadinessDto
{
    public bool IsReady { get; set; }
    public List<string> Missing { get; set; } = new();
    public bool HasZeroPrice { get; set; }
    public bool HasLossPrice { get; set; }
}

public class PurchaseOrderItemResponseDto
{
    public Guid Id { get; set; }
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public Guid? UnitId { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal FactorSnapshot { get; set; }
    public short QuantityDecimalPlaces { get; set; }
    public decimal Quantity { get; set; }
    public decimal OrderedBaseQuantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? AllocatedUnitCost { get; set; }
    public decimal ReceivedBaseQuantity { get; set; }
    public decimal RemainingBaseQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    public decimal? LineTotal => UnitPrice.HasValue ? Quantity * UnitPrice.Value : null;
    public decimal? AllocatedLineTotal => AllocatedUnitCost.HasValue ? Quantity * AllocatedUnitCost.Value : null;
    public List<PurchaseOrderSalePriceConfigDto>? SalePriceConfigs { get; set; }
}

public class PurchaseOrderSalePriceConfigInputDto
{
    public Guid UnitId { get; set; }
    public Guid PriceListId { get; set; }
    public string Mode { get; set; } = "KEEP"; // 'KEEP', 'WAC_MARGIN', 'FIXED'
    public decimal? MarginPercent { get; set; }
    public decimal? FixedPrice { get; set; }
    public string? PriceSource { get; set; } // 'SUPPLIER', 'OWNER'
    public string ApplyMode { get; set; } = "ON_RECEIPT"; // 'ON_RECEIPT', 'FROM_DATE'
    public DateTimeOffset? ApplyFrom { get; set; }
}

public class PurchaseOrderSalePriceConfigDto
{
    public Guid Id { get; set; }
    public Guid PurchaseOrderItemId { get; set; }
    public Guid UnitId { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public Guid PriceListId { get; set; }
    public string PriceListCode { get; set; } = string.Empty;
    public string PriceListName { get; set; } = string.Empty;
    public string Mode { get; set; } = "KEEP";
    public decimal? MarginPercent { get; set; }
    public decimal? FixedPrice { get; set; }
    public string? PriceSource { get; set; }
    public string ApplyMode { get; set; } = "ON_RECEIPT";
    public DateTimeOffset? ApplyFrom { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public Guid? PriceChangeRequestId { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal CurrentUnitWac { get; set; }
    public decimal CurrentMarginPercent { get; set; }
    public decimal ProjectedUnitWac { get; set; }
    public decimal ProjectedPrice { get; set; }
    public bool IsBelowCost { get; set; }
}

public class UpdatePurchaseOrderSalePriceConfigsDto
{
    public List<PurchaseOrderItemSalePriceConfigUpdateDto> Items { get; set; } = new();
}

public class PurchaseOrderItemSalePriceConfigUpdateDto
{
    public Guid PurchaseOrderItemId { get; set; }
    public List<PurchaseOrderSalePriceConfigInputDto> Configs { get; set; } = new();
}
