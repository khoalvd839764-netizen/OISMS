using System.ComponentModel.DataAnnotations;
using OISM.Application.Features.Inventory.Dtos;

namespace OISM.Application.Features.PurchaseOrders.Dtos;

public class CreatePurchaseOrderDto
{
    public Guid SupplierId { get; set; }
    public Guid BranchId { get; set; }

    [MaxLength(50)]
    public string? Number { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }

    public List<CreatePurchaseOrderItemDto> Items { get; set; } = new();
}

public class UpdatePurchaseOrderDraftDto
{
    public Guid SupplierId { get; set; }
    public Guid BranchId { get; set; }

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
    public decimal UnitPrice { get; set; }
}

public class ApprovePurchaseOrderRequestDto
{
    public bool ConfirmZeroPrice { get; set; }
}

public class ClosePurchaseOrderRequestDto
{
    public string? Reason { get; set; }
}

public class PurchaseOrderFilterDto
{
    public string? Status { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? BranchId { get; set; }
    public int Page { get; set; } = 1;
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class PurchaseOrderResponseDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public Guid RequestKey { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }
    public decimal TotalAmount { get; set; }
    public Guid CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public Guid? ApprovedBy { get; set; }
    public string? ApprovedByName { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
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

    public List<PurchaseOrderItemResponseDto> Items { get; set; } = new();
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
    public decimal UnitPrice { get; set; }
    public decimal ReceivedBaseQuantity { get; set; }
    public decimal RemainingBaseQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    public decimal LineTotal => Quantity * UnitPrice;
}
