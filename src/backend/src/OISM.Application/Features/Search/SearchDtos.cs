namespace OISM.Application.Features.Search;

public class SearchResultItemDto
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty; // "Product", "Order", "GoodsReceipt"
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? Status { get; set; }
    public string Url { get; set; } = string.Empty;
}

public class GlobalSearchResponse
{
    public string Query { get; set; } = string.Empty;
    public List<SearchResultItemDto> Products { get; set; } = new();
    public List<SearchResultItemDto> Orders { get; set; } = new();
    public List<SearchResultItemDto> GoodsReceipts { get; set; } = new();
    public int TotalCount => Products.Count + Orders.Count + GoodsReceipts.Count;
}
