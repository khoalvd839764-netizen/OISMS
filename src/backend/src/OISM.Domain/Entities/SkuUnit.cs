using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Cấu hình quy đổi đơn vị tính theo từng SKU.
/// Map vào bảng sku_units trong PostgreSQL schema.
/// </summary>
public class SkuUnit : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid SkuId { get; set; }
    public Guid UnitId { get; set; }

    /// <summary>
    /// Hệ số quy đổi về đơn vị gốc (numeric(18,6) > 0).
    /// Ví dụ: 1 bao 25 kg -> FactorToBase = 25.
    /// </summary>
    public decimal FactorToBase { get; set; } = 1m;

    public bool CanPurchase { get; set; } = true;
    public bool CanSell { get; set; } = true;
    public bool IsDefaultPurchase { get; set; } = false;
    public bool IsDefaultSale { get; set; } = false;

    public string? Barcode { get; set; }

    /// <summary>
    /// Số chữ số thập phân cho phép của số lượng giao dịch theo đơn vị này (0..3).
    /// </summary>
    public short QuantityDecimalPlaces { get; set; } = 3;

    /// <summary>
    /// Giá bán riêng cho đơn vị quy cách (numeric(20,2)).
    /// Đơn vị gốc luôn dùng skus.retail_price (SalePrice = null).
    /// </summary>
    public decimal? SalePrice { get; set; }

    /// <summary>
    /// Phiên bản cấu hình hệ số, tăng 1 mỗi khi đổi hệ số quy đổi.
    /// </summary>
    public int Version { get; set; } = 1;

    public bool IsActive { get; set; } = true;
    public Guid? UpdatedBy { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public ProductVariant? Sku { get; set; }
    public Unit? Unit { get; set; }
    public User? UpdatedByUser { get; set; }
    public ICollection<SkuUnitFactorHistory> FactorHistories { get; set; } = new List<SkuUnitFactorHistory>();
}
