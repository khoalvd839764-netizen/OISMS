using OISM.Domain.Common;

namespace OISM.Domain.Entities;

/// <summary>
/// Bản ghi tổng hợp doanh thu, hàng trả và giá vốn theo ngày kinh doanh (UTC+07), chi nhánh, kênh bán và SKU.
/// Map vào bảng daily_sales_summaries trong PostgreSQL schema v0.5.
/// Dùng để đối soát và tổng hợp sẵn báo cáo ngày; CostOfGoodsSold là giá vốn thuần (COGS bán - COGS trả).
/// </summary>
public class DailySalesSummary : BaseEntity, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public Guid BranchId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public Guid SkuId { get; set; }

    public decimal SoldQuantity { get; set; }
    public decimal ReturnedQuantity { get; set; }
    public decimal Revenue { get; set; }
    public decimal ReturnAmount { get; set; }
    public decimal CostOfGoodsSold { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public Branch? Branch { get; set; }
    public ProductVariant? Sku { get; set; }
}
