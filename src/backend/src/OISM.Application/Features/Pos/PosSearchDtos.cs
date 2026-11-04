namespace OISM.Application.Features.Pos;

public class PosProductSearchResponse
{
    public Guid VariantId { get; set; }
    public Guid ProductId { get; set; }
    public string SKU { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public decimal? RetailPrice { get; set; }
    public string? Color { get; set; }
    public string? Size { get; set; }
    public Dictionary<string, object>? Attributes { get; set; }

    /// <summary>
    /// Đơn vị được phép bán. Tồn kho (OnHand, Reserved, Available) luôn theo đơn vị gốc.
    /// </summary>
    public List<PosSkuUnitResponse> Units { get; set; } = new();

    /// <summary>
    /// Đơn vị khớp mã vạch vừa quét; NULL nghĩa là khớp mã vạch SKU, dùng đơn vị mặc định.
    /// </summary>
    public Guid? MatchedUnitId { get; set; }

    /// <summary>
    /// Số lượng tồn kho thực tế tại chi nhánh (nếu có lọc theo branchId).
    /// </summary>
    public decimal? OnHand { get; set; }

    /// <summary>
    /// Số lượng tồn kho đang được giữ chỗ tại chi nhánh.
    /// </summary>
    public decimal? Reserved { get; set; }

    /// <summary>
    /// Số lượng tồn kho khả dụng để bán tại chi nhánh: Available = Math.Max(0, OnHand - Reserved).
    /// </summary>
    public decimal? Available { get; set; }
}

public class PosSkuUnitResponse
{
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string UnitName { get; set; } = string.Empty;
    public decimal Factor { get; set; }
    public int DecimalPlaces { get; set; }
    public decimal? SalePrice { get; set; }
    public string? Barcode { get; set; }
    public bool IsBase { get; set; }
    public bool IsDefaultSale { get; set; }
}
