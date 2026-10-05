namespace OISM.Application.Common;

/// <summary>
/// Service cung cấp định danh TenantId của cửa hàng hiện tại đang gửi request.
/// Được dùng cho cơ chế SaaS Multi-tenancy (Global Query Filter trong DbContext).
/// </summary>
public interface ICurrentTenantService
{
    /// <summary>
    /// ID của cửa hàng (Tenant) hiện tại.
    /// Có thể là null đối với các tác vụ không yêu cầu Tenant (ví dụ: Đăng ký Tenant mới, SuperAdmin, Background worker).
    /// </summary>
    Guid? TenantId { get; }
}
