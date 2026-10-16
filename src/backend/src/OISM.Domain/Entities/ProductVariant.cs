using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho Biến thể sản phẩm (SKU) có thể bán và quản lý kho.
/// Quản lý mã SKU độc nhất theo Tenant, Barcode, Giá bán lẻ, Giá bán sỉ và Thuộc tính (Color, Size).
/// </summary>
public class ProductVariant : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }

    /// <summary>
    /// Mã SKU - duy nhất trong phạm vi cùng một TenantId.
    /// </summary>
    public string SKU { get; set; } = string.Empty;

    public string VariantName { get; set; } = string.Empty;

    /// <summary>
    /// Mã vạch chuẩn EAN-13 hoặc Code128 dùng cho máy quét POS.
    /// </summary>
    public string? Barcode { get; set; }

    /// <summary>
    /// Giá bán lẻ (Retail Price) niêm yết cho khách lẻ / POS.
    /// </summary>
    public decimal RetailPrice { get; set; } = 0;

    /// <summary>
    /// Giá bán buôn/sỉ (Wholesale Price).
    /// </summary>
    public decimal WholesalePrice { get; set; } = 0;

    /// <summary>
    /// Giá vốn bình quân (WAC / COGS) được tính tự động từ Sổ kho khi nhập hàng.
    /// </summary>
    public decimal CostPrice { get; set; } = 0;

    // Thuộc tính tùy biến: Màu sắc, Kích cỡ
    public string? Color { get; set; }
    public string? Size { get; set; }

    public bool IsActive { get; set; } = true;

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Product? Product { get; set; }
}
