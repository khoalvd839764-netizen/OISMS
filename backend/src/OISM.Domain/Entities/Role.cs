using OISM.Domain.Common;

namespace OISM.Domain.Entities;

public class Role : BaseEntity
{
    public string Code { get; set; } = string.Empty;        // "Owner", "Staff", "Cashier"
    public string Description { get; set; } = string.Empty;

    // Fix: Bổ sung Navigation Properties để liên kết N-N qua bảng trung gian UserRole
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}