namespace OISM.Domain.Entities;

/// <summary>
/// Bảng trung gian liên kết phân quyền N-N giữa User và Role.
/// Sử dụng Khóa chính kép (Composite Key) gồm (UserId, RoleId).
/// </summary>
public class UserRole
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public User? User { get; set; }
    public Role? Role { get; set; }
}
