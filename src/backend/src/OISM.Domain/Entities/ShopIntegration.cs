using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho cấu hình tích hợp gian hàng sàn TMĐT (Shopee, TikTok Shop, Lazada).
/// Ánh xạ mỗi gian hàng trên sàn của từng Tenant về Chi nhánh fulfillment mặc định.
/// </summary>
public class ShopIntegration : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }

    /// <summary>
    /// Kênh bán: 'Shopee', 'TikTok', 'Lazada'.
    /// </summary>
    public string Channel { get; set; } = "Shopee";

    /// <summary>
    /// Mã định danh gian hàng trên sàn TMĐT (duy nhất trong mỗi Tenant + Channel).
    /// </summary>
    public string ExternalShopId { get; set; } = string.Empty;

    /// <summary>
    /// Chi nhánh thực hiện xử lý và giữ chỗ đơn hàng cho gian hàng này.
    /// </summary>
    public Guid BranchId { get; set; }

    /// <summary>
    /// Trạng thái hoạt động của kênh tích hợp.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Băm mật mã xác thực Webhook Secret (tuyệt đối không lưu plaintext).
    /// </summary>
    public string SecretHash { get; set; } = string.Empty;

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Branch? Branch { get; set; }
}
