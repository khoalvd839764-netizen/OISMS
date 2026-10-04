using OISM.Domain.Common;

namespace OISM.Domain.Entities;

public class Tenant : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    // Fix: Bổ sung Navigation Properties để từ Tenant có thể gọi ra danh sách User và Branch của nó
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Branch> Branches { get; set; } = new List<Branch>();
}