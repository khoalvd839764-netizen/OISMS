using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho Sản phẩm chung (Product cha).
/// Chứa thông tin chung, danh mục, thương hiệu, và danh sách các Biến thể (Variants/SKUs).
/// </summary>
public class Product : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? BrandId { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Category? Category { get; set; }
    public Brand? Brand { get; set; }
    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
}
