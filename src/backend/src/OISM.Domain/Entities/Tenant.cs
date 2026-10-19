using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho một Cửa hàng / Doanh nghiệp thuê dịch vụ SaaS OISMS.
/// </summary>
public class Tenant : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Mã định danh cửa hàng (viết liền không dấu, in hoa).
    /// Lưu ý: Schema v0.5 lưu id (UUID) và name; Code được giữ ở DTO/logic nghiệp vụ.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string Code { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    // Navigation properties
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Branch> Branches { get; set; } = new List<Branch>();
}
