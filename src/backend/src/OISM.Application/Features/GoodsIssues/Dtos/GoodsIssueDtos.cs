namespace OISM.Application.Features.GoodsIssues.Dtos;

public class GoodsIssueDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid SalesInvoiceId { get; set; }
    public string SalesInvoiceNumber { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public Guid? IssuedBy { get; set; }
    public string? IssuedByName { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal TotalBaseQuantity { get; set; }
    public decimal? TotalCost { get; set; }
    public List<GoodsIssueItemDto> Items { get; set; } = new();
}

public class GoodsIssueItemDto
{
    public Guid Id { get; set; }
    public Guid GoodsIssueId { get; set; }
    public Guid InventoryTransactionId { get; set; }
    public Guid OrderItemId { get; set; }
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public string SkuName { get; set; } = string.Empty;
    public Guid? UnitId { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public string BaseUnitName { get; set; } = string.Empty;
    public decimal FactorSnapshot { get; set; }
    public decimal Quantity { get; set; }
    public decimal BaseQuantity { get; set; }
    public decimal? UnitCost { get; set; }
    public decimal? TotalCost { get; set; }
}

public class GoodsIssueFilterDto
{
    public string? Number { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? SalesInvoiceId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? Channel { get; set; }
    public DateTimeOffset? FromDate { get; set; }
    public DateTimeOffset? ToDate { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class BulkGoodsIssueByOrdersRequestDto
{
    public List<Guid> OrderIds { get; set; } = new();
}

public class PosDailyGoodsIssueQueryDto
{
    /// <summary>
    /// Ngày nghiệp vụ theo múi giờ UTC+07:00 (định dạng YYYY-MM-DD). Nếu để trống, mặc định là ngày hôm nay theo UTC+07:00.
    /// </summary>
    public string? Date { get; set; }

    /// <summary>
    /// Alias cho Date (YYYY-MM-DD).
    /// </summary>
    public string? BusinessDate { get; set; }

    /// <summary>
    /// Lọc theo chi nhánh (tùy chọn). Nếu null thì tổng hợp toàn bộ chi nhánh của tenant.
    /// </summary>
    public Guid? BranchId { get; set; }
}

public class PosDailyGoodsIssueSummaryDto
{
    public string BusinessDate { get; set; } = string.Empty;
    public DateTimeOffset FromUtc { get; set; }
    public DateTimeOffset ToUtc { get; set; }
    public Guid? BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public int TotalOrders { get; set; }
    public int TotalInvoices { get; set; }
    public int TotalGoodsIssues { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal TotalBaseQuantity { get; set; }
    public decimal? TotalCost { get; set; }
    public List<PosDailyGoodsIssueSkuDto> Items { get; set; } = new();
    public List<PosDailyGoodsIssueInvoiceDto> Invoices { get; set; } = new();
}

public class PosDailyGoodsIssueSkuDto
{
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public string SkuName { get; set; } = string.Empty;
    public string BaseUnitName { get; set; } = string.Empty;
    public decimal TotalQuantity { get; set; }
    public decimal TotalBaseQuantity { get; set; }
    public decimal? UnitCost { get; set; }
    public decimal? TotalCost { get; set; }
}

public class PosDailyGoodsIssueInvoiceDto
{
    public Guid GoodsIssueId { get; set; }
    public string GoodsIssueNumber { get; set; } = string.Empty;
    public Guid SalesInvoiceId { get; set; }
    public string SalesInvoiceNumber { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string? IssuedByName { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal TotalBaseQuantity { get; set; }
    public decimal TotalSalesAmount { get; set; }
    public decimal? TotalCost { get; set; }
}
