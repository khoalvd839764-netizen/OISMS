using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho một Bảng giá của Tenant (retail, wholesale).
/// Map vào bảng price_lists trong PostgreSQL schema.
/// </summary>
public class PriceList : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public ICollection<PriceChangeRequestItem> Items { get; set; } = new List<PriceChangeRequestItem>();
    public ICollection<SkuUnitPrice> Prices { get; set; } = new List<SkuUnitPrice>();
}
