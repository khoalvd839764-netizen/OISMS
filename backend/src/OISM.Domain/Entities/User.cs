using OISM.Domain.Common;

namespace OISM.Domain.Entities;

public class User : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    
    // Fix: Bổ sung PhoneNumber cho khớp với bảng users trong Database (có thể null)
    public string? PhoneNumber { get; set; }
    
    public bool IsActive { get; set; } = true;

    // Fix: Bổ sung Navigation Properties để Entity Framework query dễ dàng hơn (vd: .Include(u => u.Tenant))
    public Tenant? Tenant { get; set; }
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}