namespace OISM.Application.Features.Pos;

public class PosProductSearchResponse
{
    public Guid VariantId { get; set; }
    public Guid ProductId { get; set; }
    public string SKU { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public decimal RetailPrice { get; set; }
    public string? Color { get; set; }
    public string? Size { get; set; }
}
