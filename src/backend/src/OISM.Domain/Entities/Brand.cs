using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho Thương hiệu sản phẩm.
/// </summary>
public class Brand : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
