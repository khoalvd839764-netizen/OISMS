namespace OISM.Application.Common.Interfaces;

/// <summary>
/// Giao diện trừu tượng hóa các thao tác khóa bi quan và khóa đồng thời ở mức cơ sở dữ liệu.
/// Hiện thực tại tầng Infrastructure để loại bỏ SQL đặc thù của database provider khỏi Application.
/// </summary>
public interface ISqlLockService
{
    /// <summary>
    /// Khóa toàn bộ cây danh mục của Tenant bằng advisory lock để chống race condition và vòng lặp phân cấp.
    /// </summary>
    Task LockCategoryTreeAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Khóa bi quan bản ghi Sản phẩm (FOR UPDATE) để chống xung đột đồng thời khi thêm/sửa biến thể.
    /// </summary>
    Task LockProductAsync(Guid productId, CancellationToken ct = default);

    /// <summary>
    /// Khóa bi quan bản ghi Refresh Token (FOR UPDATE) để đảm bảo token rotation nguyên tử.
    /// </summary>
    Task LockRefreshTokenAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>
    /// Khóa bi quan bản ghi Kho hàng (FOR UPDATE) để chống xung đột đồng thời khi đổi chi nhánh hoặc cập nhật kho.
    /// </summary>
    Task LockWarehouseAsync(Guid warehouseId, Guid tenantId, CancellationToken ct = default);
}
