using OISM.Domain.Common;

namespace OISM.Domain.Entities;

// Fix: KHÔNG kế thừa BaseEntity. Bảng trong DB dùng khóa chính kép (user_id, role_id) và không có cột id riêng.
public class UserRole
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    
    // Fix: Dùng AssignedAt thay vì CreatedAt (của BaseEntity) cho khớp cột assigned_at trong DB
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;

    // Fix: Bổ sung Navigation Properties để Entity Framework query dễ dàng hơn
    public User? User { get; set; }
    public Role? Role { get; set; }
}