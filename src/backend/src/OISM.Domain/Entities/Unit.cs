using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Đại diện cho một Đơn vị tính (UoM) trong danh mục đơn vị của Tenant.
/// Map vào bảng units trong PostgreSQL schema.
/// </summary>
public class Unit : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }

    private string _code = string.Empty;
    /// <summary>
    /// Mã chuẩn hóa của đơn vị (code = lower(btrim(code)) AND length(code) > 0).
    /// </summary>
    public string Code
    {
        get => _code;
        set => _code = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }

    public string Name { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Nhóm đo lường: 'Mass', 'Volume', 'Length', 'Count', 'Packaging'.
    /// </summary>
    public string MeasureGroup { get; set; } = MeasureGroups.Count;

    /// <summary>
    /// Số chữ số thập phân cho phép (0..3).
    /// </summary>
    public short DecimalPlaces { get; set; } = 3;

    public bool IsActive { get; set; } = true;

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public ICollection<SkuUnit> SkuUnits { get; set; } = new List<SkuUnit>();
}

public static class MeasureGroups
{
    public const string Mass = "Mass";
    public const string Volume = "Volume";
    public const string Length = "Length";
    public const string Count = "Count";
    public const string Packaging = "Packaging";

    public static readonly string[] All = [Mass, Volume, Length, Count, Packaging];

    public static bool IsValid(string group) => All.Contains(group, StringComparer.OrdinalIgnoreCase);
}
