using OISM.Domain.Common;

namespace OISM.Domain.Entities;

public class Branch : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}