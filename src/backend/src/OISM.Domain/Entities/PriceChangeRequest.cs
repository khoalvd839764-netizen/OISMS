using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho một Phiếu đề xuất / thay đổi giá bán.
/// Map vào bảng price_change_requests trong PostgreSQL schema.
/// </summary>
public class PriceChangeRequest : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid RequestKey { get; set; }
    public string Kind { get; set; } = string.Empty; // 'Opening', 'Initial', 'Change'
    public string Status { get; set; } = string.Empty; // 'Pending', 'Approved', 'Rejected'
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset? RequestedValidFrom { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public User? CreatedByUser { get; set; }
    public User? ReviewedByUser { get; set; }
    public ICollection<PriceChangeRequestItem> Items { get; set; } = new List<PriceChangeRequestItem>();

    public bool IsPending => Status == PriceChangeRequestStatus.Pending;

    public void Approve(Guid? reviewerId, DateTimeOffset at)
    {
        StateTransition.Ensure(IsPending, nameof(PriceChangeRequest), Status, PriceChangeRequestStatus.Approved);
        Status = PriceChangeRequestStatus.Approved;
        ReviewedBy = reviewerId;
        ReviewedAt = at;
    }

    public void Reject(Guid? reviewerId, string note, DateTimeOffset at)
    {
        StateTransition.Ensure(IsPending, nameof(PriceChangeRequest), Status, PriceChangeRequestStatus.Rejected);
        Status = PriceChangeRequestStatus.Rejected;
        ReviewedBy = reviewerId;
        ReviewedAt = at;
        ReviewNote = note;
    }
}
