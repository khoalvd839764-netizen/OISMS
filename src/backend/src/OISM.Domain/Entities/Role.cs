using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho các vai trò cố định trong hệ thống OISMS (Owner, Staff, Cashier).
/// </summary>
public class Role : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Mã vai trò chuẩn ("Owner", "Staff", "Cashier").
    /// </summary>
    public string Code { get; set; } = string.Empty;

    public string? Description { get; set; }

    // Navigation properties
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
