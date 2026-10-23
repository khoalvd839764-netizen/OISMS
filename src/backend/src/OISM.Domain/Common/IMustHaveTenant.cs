namespace OISM.Domain.Common;

/// <summary>
/// Đánh dấu thực thể thuộc về một Cửa hàng (Tenant) cụ thể.
/// Bắt buộc có TenantId để phục vụ bộ lọc cô lập dữ liệu tự động (Global Query Filter).
/// </summary>
public interface IMustHaveTenant
{
    Guid TenantId { get; set; }
}
