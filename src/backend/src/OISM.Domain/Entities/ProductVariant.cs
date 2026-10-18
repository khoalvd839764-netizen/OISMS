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

    private string _sku = string.Empty;
    /// <summary>
    /// Mã SKU (cột code varchar(80) trong bảng skus) - chuẩn hóa UPPER(BTRIM(code)).
    /// </summary>
    public string SKU
    {
        get => _sku;
        set => _sku = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Bí danh Code tương thích với schema v0.5.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string Code
    {
        get => SKU;
        set => SKU = value;
    }

    /// <summary>
    /// Đơn vị tính cơ bản (mặc định 'piece').
    /// </summary>
    public string Unit { get; set; } = "piece";

    /// <summary>
    /// Loại mã vạch: 'EAN13' hoặc 'Code128'. Phải đi cùng Barcode theo ck_skus_barcode_pair.
    /// </summary>
    public string? BarcodeType { get; set; }

    private string? _barcode;
    /// <summary>
    /// Mã vạch sản phẩm. Tự động chuẩn hóa và suy luận BarcodeType nếu chưa set.
    /// </summary>
    public string? Barcode
    {
        get => _barcode;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                _barcode = null;
                BarcodeType = null;
            }
            else
            {
                _barcode = value.Trim();
                if (string.IsNullOrEmpty(BarcodeType))
                {
                    BarcodeType = (_barcode.Length == 13 && _barcode.All(char.IsDigit)) ? "EAN13" : "Code128";
                }
            }
        }
    }

    /// <summary>
    /// Giá bán lẻ (Retail Price) niêm yết cho khách lẻ / POS.
    /// </summary>
    public decimal RetailPrice { get; set; } = 0;

    /// <summary>
    /// Giá bán buôn/sỉ (Wholesale Price).
    /// </summary>
    public decimal WholesalePrice { get; set; } = 0;

    /// <summary>
    /// Chuỗi JSON thuộc tính biến thể (lưu trong cột attributes jsonb của bảng skus).
    /// Mặc định là object rỗng "{}" để thỏa mãn ck_skus_attributes_object.
    /// </summary>
    public string AttributesJson { get; set; } = "{}";

    /// <summary>
    /// Giá vốn bình quân (WAC / COGS).
    /// Lưu ý: Schema v0.5 theo dõi WAC theo chi nhánh trong bảng inventory_balances.average_cost.
    /// Trường CostPrice ở đây không map vào bảng skus.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public decimal CostPrice { get; set; } = 0;

    // Helper getters/setters cho thuộc tính biến thể thông qua AttributesJson
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string VariantName
    {
        get => GetAttribute("variant_name") ?? string.Empty;
        set => SetAttribute("variant_name", value);
    }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string? Color
    {
        get => GetAttribute("color");
        set => SetAttribute("color", value);
    }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string? Size
    {
        get => GetAttribute("size");
        set => SetAttribute("size", value);
    }

    public bool IsActive { get; set; } = true;

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Product? Product { get; set; }

    public Dictionary<string, object> GetAttributes()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(AttributesJson) && AttributesJson.Trim().StartsWith("{"))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(AttributesJson);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        var val = ConvertElement(prop.Value);
                        if (val != null)
                        {
                            result[prop.Name] = val;
                        }
                    }
                    return result;
                }
            }
        }
        catch { }
        return new Dictionary<string, object>();
    }

    private static object? ConvertElement(System.Text.Json.JsonElement element)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Null:
            case System.Text.Json.JsonValueKind.Undefined:
                return null;
            case System.Text.Json.JsonValueKind.String:
                return element.GetString();
            case System.Text.Json.JsonValueKind.Number:
                return element.TryGetInt64(out var l) ? l : element.GetDecimal();
            case System.Text.Json.JsonValueKind.True:
                return true;
            case System.Text.Json.JsonValueKind.False:
                return false;
            case System.Text.Json.JsonValueKind.Object:
                var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in element.EnumerateObject())
                {
                    var v = ConvertElement(prop.Value);
                    dict[prop.Name] = v!;
                }
                return dict;
            case System.Text.Json.JsonValueKind.Array:
                var list = new List<object>();
                foreach (var item in element.EnumerateArray())
                {
                    var v = ConvertElement(item);
                    list.Add(v!);
                }
                return list;
            default:
                return element.Clone();
        }
    }

    private string? GetAttribute(string key)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(AttributesJson) ? "{}" : AttributesJson);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object && doc.RootElement.TryGetProperty(key, out var prop))
            {
                return prop.ValueKind == System.Text.Json.JsonValueKind.String ? prop.GetString() : prop.ToString();
            }
        }
        catch
        {
            // fallback
        }
        return null;
    }

    private void SetAttribute(string key, string? value)
    {
        var dict = new Dictionary<string, object>();
        try
        {
            if (!string.IsNullOrWhiteSpace(AttributesJson) && AttributesJson.Trim().StartsWith("{"))
            {
                var parsed = GetAttributes();
                if (parsed != null) dict = parsed;
            }
        }
        catch { }

        if (string.IsNullOrWhiteSpace(value))
        {
            dict.Remove(key);
        }
        else
        {
            dict[key] = value.Trim();
        }

        AttributesJson = System.Text.Json.JsonSerializer.Serialize(dict);
    }
}
