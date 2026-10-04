using OISM.Domain.Common;

namespace OISM.Domain.Entities;

public class Branch : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    // Fix: Khớp với DB, Phone có thể null
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;

    // Fix: Bổ sung Navigation Properties để Entity Framework biết Branch này thuộc Tenant nào
    public Tenant? Tenant { get; set; }
}