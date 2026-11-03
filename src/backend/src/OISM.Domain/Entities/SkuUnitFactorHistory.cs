using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Lịch sử thay đổi hệ số quy đổi đơn vị theo SKU (bảng append-only).
/// Map vào bảng sku_unit_factor_history trong PostgreSQL schema.
/// </summary>
public class SkuUnitFactorHistory : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public Guid SkuUnitId { get; set; }

    public decimal? OldFactor { get; set; }
    public decimal NewFactor { get; set; }

    public int? OldVersion { get; set; }
    public int NewVersion { get; set; }

    public Guid? ChangedBy { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public SkuUnit? SkuUnit { get; set; }
    public User? ChangedByUser { get; set; }
}
