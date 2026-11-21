namespace OISM.Application.Features.Pricing.Dtos;

public readonly record struct SkuUnitPriceKey(Guid SkuId, Guid UnitId);

public sealed record ResolvedPrice(Guid SkuUnitPriceId, decimal Price, DateTimeOffset ValidFrom);

public sealed record PriceActor(Guid? UserId, bool IsOwner);

public sealed record InitialPriceLine(string PriceListCode, Guid SkuId, Guid UnitId, decimal Price);

public sealed class CreatePriceChangeRequest
{
    public string ReasonType { get; set; } = "OTHER";
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset? RequestedValidFrom { get; set; }
    public string? PoConflictResolution { get; set; } // KEEP_MANUAL or APPLY_PO_LATER
    public List<CreatePriceChangeRequestItemDto> Items { get; set; } = new();
}

public sealed class CreatePriceChangeRequestItemDto
{
    public string PriceListCode { get; set; } = string.Empty;
    public Guid SkuId { get; set; }
    public Guid UnitId { get; set; }
    public decimal NewPrice { get; set; }
}

public sealed class PriceChangeRequestResponse
{
    public Guid Id { get; set; }
    public Guid RequestKey { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ReasonType { get; set; } = "OTHER";
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset? RequestedValidFrom { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
    public bool IsReplay { get; set; }
    public List<PriceChangeRequestItemDto> Items { get; set; } = new();
}

public sealed class PriceChangeRequestItemDto
{
    public Guid Id { get; set; }
    public Guid PriceListId { get; set; }
    public string PriceListCode { get; set; } = string.Empty;
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public Guid UnitId { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal NewPrice { get; set; }
}

public sealed class PriceListDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class EffectivePriceDto
{
    public Guid SkuUnitPriceId { get; set; }
    public Guid PriceListId { get; set; }
    public string PriceListCode { get; set; } = string.Empty;
    public Guid SkuId { get; set; }
    public Guid UnitId { get; set; }
    public decimal Price { get; set; }
    public DateTimeOffset ValidFrom { get; set; }
}

public sealed class PriceHistoryDto
{
    public Guid Id { get; set; }
    public string PriceListCode { get; set; } = string.Empty;
    public Guid SkuId { get; set; }
    public Guid UnitId { get; set; }
    public decimal Price { get; set; }
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
}

public sealed class RejectPriceChangeRequestDto
{
    public string? Note { get; set; }
}

public sealed class CancelSkuUnitPriceRequestDto
{
    public string? Reason { get; set; }
}

public sealed class ApprovePriceChangeRequestDto
{
    public string? PoConflictResolution { get; set; } // KEEP_MANUAL or APPLY_PO_LATER
}

public sealed class PendingPurchaseOrderSalePriceDto
{
    public Guid PurchaseOrderId { get; set; }
    public string PurchaseOrderNumber { get; set; } = string.Empty;
    public DateOnly OrderDate { get; set; }
    public Guid PurchaseOrderItemId { get; set; }
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
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
    public decimal? ProjectedPrice { get; set; }
}
