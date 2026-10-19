using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace OISM.Application.Common.Helpers;

/// <summary>
/// Kết quả hợp nhất và chuẩn hóa thuộc tính của SKU (ProductVariant).
/// Đảm bảo tính nhất quán 100% giữa dữ liệu kiểm tra trùng lặp và dữ liệu lưu trữ xuống PostgreSQL.
/// </summary>
public readonly record struct SkuAttributeResolutionResult(
    Dictionary<string, object> MergedAttributes,
    string CombinationSignature,
    string SerializedJson,
    string? EffectiveColor,
    string? EffectiveSize,
    string? EffectiveVariantName
);

/// <summary>
/// Helper chuẩn hóa và xử lý thuộc tính động của SKU (bảng skus.attributes jsonb).
/// Đảm bảo:
/// 1. Bảo toàn 100% kiểu dữ liệu JSON: Object, Array, String, Number, Boolean từ request -> database jsonb -> response.
/// 2. Hợp nhất thống nhất giữa Explicit Fields (Color, Size, VariantName) và Custom Attributes (JSON object).
/// 3. Tính toán Chữ ký tổ hợp thuộc tính (Combination Signature) canonical: phân biệt kiểu dữ liệu (phân biệt 10 với "10", true với "true"),
///    chuẩn hóa thứ tự key của object một cách nhất quán (kể cả object lồng nhau), giữ nguyên thứ tự array, tránh va chạm ký tự phân cách.
/// 4. Dùng chính bộ thuộc tính cuối cùng sau khi hợp nhất cho cả kiểm tra trùng và lưu database.
/// 5. An toàn vòng đời dữ liệu (không lưu JsonElement phụ thuộc JsonDocument đã bị dispose).
/// </summary>
public static class SkuAttributeHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    /// <summary>
    /// Hợp nhất thuộc tính từ các nguồn (dữ liệu cũ trong DB, custom attributes JSON, và các trường riêng Color/Size/VariantName),
    /// chuẩn hóa key/value, tính chữ ký tổ hợp canonical và sinh chuỗi JSON lưu trữ.
    /// </summary>
    public static SkuAttributeResolutionResult ResolveAttributes(
        string? explicitColor,
        string? explicitSize,
        string? explicitVariantName,
        IDictionary<string, object>? customAttributes = null,
        string? existingAttributesJson = null)
    {
        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        // 1. Nạp từ existingAttributesJson nếu có (chuyển thành managed CLR objects độc lập)
        if (!string.IsNullOrWhiteSpace(existingAttributesJson) && existingAttributesJson.Trim().StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(existingAttributesJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        var key = prop.Name.Trim().ToLowerInvariant();
                        if (string.IsNullOrEmpty(key)) continue;

                        var val = ConvertJsonElementToClr(prop.Value, trimEmptyStringToNull: false);
                        if (val != null)
                        {
                            dict[key] = val;
                        }
                    }
                }
            }
            catch
            {
                // Bỏ qua lỗi parse chuỗi cũ
            }
        }

        // 2. Nạp và ghi đè từ customAttributes (nếu có)
        if (customAttributes != null)
        {
            foreach (var kvp in customAttributes)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key)) continue;
                var key = kvp.Key.Trim().ToLowerInvariant();

                var normalizedVal = NormalizeRawValue(kvp.Value, isRootLevel: true);
                if (normalizedVal == null)
                {
                    dict.Remove(key);
                }
                else
                {
                    dict[key] = normalizedVal;
                }
            }
        }

        // 3. Áp dụng Explicit Fields (Color, Size, VariantName) với độ ưu tiên cao nhất
        ApplyExplicitField(dict, "color", explicitColor);
        ApplyExplicitField(dict, "size", explicitSize);
        ApplyExplicitField(dict, "variant_name", explicitVariantName);

        // 4. Tính toán Chữ ký Tổ hợp Canonical (Combination Signature)
        var canonicalDict = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in dict)
        {
            var key = kvp.Key.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(key)) continue;

            var valSig = BuildCanonicalValueSignature(kvp.Value);
            if (valSig != "null")
            {
                canonicalDict[key] = valSig;
            }
        }

        string signature;
        var specificAttributeKeys = canonicalDict.Keys
            .Where(k => !string.Equals(k, "variant_name", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(k, "variantname", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (specificAttributeKeys.Count > 0)
        {
            // Có thuộc tính xác định (như color, size, dimensions, material,...) -> loại bỏ variant_name khỏi chữ ký
            canonicalDict.Remove("variant_name");
            canonicalDict.Remove("variantname");
            signature = string.Join("|", canonicalDict.Select(kvp => $"{EscapeCanonicalString(kvp.Key)}={kvp.Value}"));
        }
        else
        {
            // Không có thuộc tính nào khác -> kiểm tra xem có variant_name không
            string? fallbackName = null;
            if (canonicalDict.TryGetValue("variant_name", out var vName) && !string.IsNullOrWhiteSpace(vName))
            {
                fallbackName = vName;
            }
            else if (canonicalDict.TryGetValue("variantname", out var vName2) && !string.IsNullOrWhiteSpace(vName2))
            {
                fallbackName = vName2;
            }

            if (!string.IsNullOrWhiteSpace(fallbackName))
            {
                signature = $"variant_name={fallbackName}";
            }
            else
            {
                signature = "default";
            }
        }

        // 5. Chuỗi JSON lưu trữ (luôn là object hợp lệ)
        string serializedJson = dict.Count == 0
            ? "{}"
            : JsonSerializer.Serialize(dict, JsonOptions);

        // 6. Trích xuất Effective Values cho các trường riêng
        string? effectiveColor = dict.TryGetValue("color", out var cVal) ? cVal?.ToString() : null;
        string? effectiveSize = dict.TryGetValue("size", out var sVal) ? sVal?.ToString() : null;
        string? effectiveVariantName = dict.TryGetValue("variant_name", out var vnVal) ? vnVal?.ToString() : null;

        return new SkuAttributeResolutionResult(
            dict,
            signature,
            serializedJson,
            effectiveColor,
            effectiveSize,
            effectiveVariantName
        );
    }

    private static void ApplyExplicitField(Dictionary<string, object> dict, string fieldKey, string? explicitValue)
    {
        if (explicitValue != null)
        {
            var trimmed = explicitValue.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                // Chuỗi rỗng -> chủ động XÓA thuộc tính này
                dict.Remove(fieldKey);
            }
            else
            {
                // Có giá trị -> Ghi đè tuyệt đối (ưu tiên cao nhất)
                dict[fieldKey] = trimmed;
            }
        }
        // Nếu explicitValue == null: giữ nguyên giá trị đã quyết định từ Custom Attributes / dữ liệu cũ
    }

    /// <summary>
    /// Chuẩn hóa giá trị từ các nguồn đa dạng (JsonElement hoặc CLR object) thành các kiểu .NET độc lập.
    /// Bảo toàn kiểu dữ liệu: Object -> Dictionary, Array -> List, Number -> long/decimal, Boolean -> bool, String -> string.
    /// </summary>
    public static object? NormalizeRawValue(object? val, bool isRootLevel = true)
    {
        if (val == null) return null;

        if (val is JsonElement elem)
        {
            return ConvertJsonElementToClr(elem, trimEmptyStringToNull: isRootLevel);
        }

        if (val is string s)
        {
            if (isRootLevel && string.IsNullOrWhiteSpace(s))
            {
                return null;
            }
            return s.Trim();
        }

        if (val is bool b) return b;

        if (val is sbyte or byte or short or ushort or int or uint or long or ulong)
        {
            return Convert.ToInt64(val);
        }

        if (val is float or double or decimal)
        {
            return Convert.ToDecimal(val);
        }

        if (val is IDictionary<string, object> dict)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in dict)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key)) continue;
                // isRootLevel: false cho thuộc tính lồng nhau -> giữ nguyên null
                var childVal = NormalizeRawValue(kvp.Value, isRootLevel: false);
                result[kvp.Key] = childVal!;
            }
            return result;
        }

        if (val is IDictionary nonGenericDict)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in nonGenericDict)
            {
                var k = entry.Key?.ToString();
                if (string.IsNullOrWhiteSpace(k)) continue;
                var childVal = NormalizeRawValue(entry.Value, isRootLevel: false);
                result[k] = childVal!;
            }
            return result;
        }

        if (val is IEnumerable enumerable)
        {
            var list = new List<object>();
            foreach (var item in enumerable)
            {
                // isRootLevel: false cho phần tử mảng -> giữ nguyên null và thứ tự/vị trí
                var itemVal = NormalizeRawValue(item, isRootLevel: false);
                list.Add(itemVal!);
            }
            return list;
        }

        return val;
    }

    /// <summary>
    /// Chuyển đổi một JsonElement thành cấu trúc đối tượng .NET thuần (CLR) an toàn,
    /// không phụ thuộc vào vòng đời của JsonDocument gốc.
    /// Giữ nguyên null trong object và array lồng nhau.
    /// </summary>
    public static object? ConvertJsonElementToClr(JsonElement element, bool trimEmptyStringToNull = true)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;

            case JsonValueKind.String:
                var str = element.GetString();
                if (trimEmptyStringToNull && string.IsNullOrWhiteSpace(str))
                {
                    return null;
                }
                return str?.Trim();

            case JsonValueKind.Number:
                if (element.TryGetInt64(out var l))
                {
                    return l;
                }
                return element.GetDecimal();

            case JsonValueKind.True:
                return true;

            case JsonValueKind.False:
                return false;

            case JsonValueKind.Object:
                var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in element.EnumerateObject())
                {
                    if (string.IsNullOrWhiteSpace(prop.Name)) continue;
                    var childVal = ConvertJsonElementToClr(prop.Value, trimEmptyStringToNull: false);
                    dict[prop.Name] = childVal!;
                }
                return dict;

            case JsonValueKind.Array:
                var list = new List<object>();
                foreach (var item in element.EnumerateArray())
                {
                    var itemVal = ConvertJsonElementToClr(item, trimEmptyStringToNull: false);
                    list.Add(itemVal!);
                }
                return list;

            default:
                return element.Clone();
        }
    }

    /// <summary>
    /// Sinh biểu diễn canonical cho một giá trị bất kỳ để làm chữ ký tổ hợp thuộc tính.
    /// Phân biệt rõ ràng kiểu dữ liệu:
    /// - number: n:10 (khác chuỗi s:"10")
    /// - string: s:"..."
    /// - boolean: b:true / b:false (khác chuỗi s:"true")
    /// - array: a:[elem1,elem2,...] (giữ nguyên thứ tự và giá trị null)
    /// - object: o:{k1:v1,k2:v2,...} (chuẩn hóa sắp xếp key theo thứ tự bảng chữ cái không phân biệt hoa thường, giữ key có giá trị null)
    /// </summary>
    public static string BuildCanonicalValueSignature(object? val)
    {
        if (val == null)
        {
            return "null";
        }

        if (val is JsonElement elem)
        {
            return BuildCanonicalJsonElementSignature(elem);
        }

        if (val is bool b)
        {
            return b ? "b:true" : "b:false";
        }

        if (val is sbyte or byte or short or ushort or int or uint or long or ulong)
        {
            return $"n:{Convert.ToInt64(val)}";
        }

        if (val is float or double or decimal)
        {
            return $"n:{Convert.ToDecimal(val).ToString(CultureInfo.InvariantCulture)}";
        }

        if (val is string s)
        {
            var trimmed = s.Trim().ToLowerInvariant();
            return $"s:{EscapeCanonicalString(trimmed)}";
        }

        if (val is IDictionary<string, object> dict)
        {
            var sorted = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in dict)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key)) continue;
                var normKey = kvp.Key.Trim().ToLowerInvariant();
                var childSig = BuildCanonicalValueSignature(kvp.Value);
                sorted[normKey] = childSig;
            }
            return $"o:{{{string.Join(",", sorted.Select(k => $"{EscapeCanonicalString(k.Key)}:{k.Value}"))}}}";
        }

        if (val is IDictionary nonGenericDict)
        {
            var sorted = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in nonGenericDict)
            {
                var k = entry.Key?.ToString();
                if (string.IsNullOrWhiteSpace(k)) continue;
                var normKey = k.Trim().ToLowerInvariant();
                var childSig = BuildCanonicalValueSignature(entry.Value);
                sorted[normKey] = childSig;
            }
            return $"o:{{{string.Join(",", sorted.Select(k => $"{EscapeCanonicalString(k.Key)}:{k.Value}"))}}}";
        }

        if (val is IEnumerable enumerable)
        {
            var items = new List<string>();
            foreach (var item in enumerable)
            {
                items.Add(BuildCanonicalValueSignature(item));
            }
            return $"a:[{string.Join(",", items)}]";
        }

        var str = val.ToString()?.Trim().ToLowerInvariant() ?? string.Empty;
        return $"s:{EscapeCanonicalString(str)}";
    }

    private static string BuildCanonicalJsonElementSignature(JsonElement elem)
    {
        switch (elem.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return "null";

            case JsonValueKind.True:
                return "b:true";

            case JsonValueKind.False:
                return "b:false";

            case JsonValueKind.Number:
                if (elem.TryGetInt64(out var l))
                {
                    return $"n:{l}";
                }
                return $"n:{elem.GetDecimal().ToString(CultureInfo.InvariantCulture)}";

            case JsonValueKind.String:
                var s = elem.GetString()?.Trim().ToLowerInvariant() ?? string.Empty;
                return $"s:{EscapeCanonicalString(s)}";

            case JsonValueKind.Object:
                var sorted = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in elem.EnumerateObject())
                {
                    var normKey = prop.Name.Trim().ToLowerInvariant();
                    if (string.IsNullOrEmpty(normKey)) continue;
                    var childSig = BuildCanonicalJsonElementSignature(prop.Value);
                    sorted[normKey] = childSig;
                }
                return $"o:{{{string.Join(",", sorted.Select(k => $"{EscapeCanonicalString(k.Key)}:{k.Value}"))}}}";

            case JsonValueKind.Array:
                var items = new List<string>();
                foreach (var item in elem.EnumerateArray())
                {
                    items.Add(BuildCanonicalJsonElementSignature(item));
                }
                return $"a:[{string.Join(",", items)}]";

            default:
                var fallback = elem.ToString().Trim().ToLowerInvariant();
                return $"s:{EscapeCanonicalString(fallback)}";
        }
    }

    private static string EscapeCanonicalString(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return input
            .Replace("\\", "\\\\")
            .Replace("|", "\\|")
            .Replace("=", "\\=")
            .Replace(":", "\\:")
            .Replace(",", "\\,")
            .Replace("{", "\\{")
            .Replace("}", "\\}")
            .Replace("[", "\\[")
            .Replace("]", "\\]");
    }

    /// <summary>
    /// Chuẩn hóa và tính toán chữ ký tổ hợp thuộc tính canonical (Tương thích ngược).
    /// </summary>
    public static string ComputeCombinationSignature(
        string? color,
        string? size,
        string? variantName,
        IDictionary<string, object>? customAttributes = null,
        string? attributesJson = null)
    {
        return ResolveAttributes(color, size, variantName, customAttributes, attributesJson).CombinationSignature;
    }

    /// <summary>
    /// Đóng gói các thuộc tính thành chuỗi JSON object hợp lệ để lưu vào cột attributes jsonb (Tương thích ngược).
    /// </summary>
    public static string SerializeAttributes(
        string? color,
        string? size,
        string? variantName,
        IDictionary<string, object>? customAttributes = null,
        string? existingAttributesJson = null)
    {
        return ResolveAttributes(color, size, variantName, customAttributes, existingAttributesJson).SerializedJson;
    }

    /// <summary>
    /// Giải mã chuỗi JSON attributes thành Dictionary phục vụ trả API, bảo toàn 100% kiểu dữ liệu Object, Array, Number, Bool, String.
    /// </summary>
    public static Dictionary<string, object> ExtractAttributes(string? attributesJson)
    {
        if (string.IsNullOrWhiteSpace(attributesJson) || !attributesJson.Trim().StartsWith("{"))
        {
            return new Dictionary<string, object>();
        }

        try
        {
            using var doc = JsonDocument.Parse(attributesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<string, object>();
            }

            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var val = ConvertJsonElementToClr(prop.Value, trimEmptyStringToNull: false);
                if (val != null)
                {
                    result[prop.Name] = val;
                }
            }
            return result;
        }
        catch
        {
            return new Dictionary<string, object>();
        }
    }
}
