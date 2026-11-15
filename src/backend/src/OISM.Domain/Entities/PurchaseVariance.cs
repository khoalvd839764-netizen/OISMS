using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>Chứng từ chênh lệch trước thuế và quyết định của Owner.</summary>
public sealed class PurchaseVariance : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid GoodsReceiptId { get; set; }
    public string VarianceType { get; set; } = string.Empty;
    public decimal TotalAmountDiff { get; set; }
    public string Status { get; set; } = PurchaseVarianceStatus.Pending;
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
}
