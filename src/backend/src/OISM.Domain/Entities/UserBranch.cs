using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Phân công chi nhánh được phép truy cập cho nhân viên thuộc một Tenant (CS-02).
/// Không có bản ghi nào đồng nghĩa với nhân viên được phép truy cập mọi chi nhánh.
/// </summary>
public class UserBranch : IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid BranchId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public User? User { get; set; }
    public Branch? Branch { get; set; }
}
