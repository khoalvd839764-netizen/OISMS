using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.GoodsIssues.Dtos;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Domain.Entities;

namespace OISM.Application.Features.GoodsIssues.Services;

public class GoodsIssueService : IGoodsIssueService
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private const int MaxBulkOrderIds = 100;
    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GoodsIssueService(IAppDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    private Guid RequireTenantId()
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Không xác định được danh tính Tenant hợp lệ.");
        }
        return tenantId.Value;
    }

    public async Task<PagedResult<GoodsIssueDto>> GetGoodsIssuesAsync(
        GoodsIssueFilterDto filter,
        bool canViewCost,
        CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var query = _context.GoodsIssues
            .AsNoTracking()
            .Include(g => g.Items)
                .ThenInclude(i => i.Sku)
                    .ThenInclude(s => s!.BaseUnit)
            .Where(g => g.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(filter.Number))
        {
            var trimmed = filter.Number.Trim();
            query = query.Where(g => g.Number == trimmed);
        }

        if (filter.OrderId.HasValue)
        {
            query = query.Where(g => g.OrderId == filter.OrderId.Value);
        }

        if (filter.SalesInvoiceId.HasValue)
        {
            query = query.Where(g => g.SalesInvoiceId == filter.SalesInvoiceId.Value);
        }

        if (filter.BranchId.HasValue)
        {
            query = query.Where(g => g.BranchId == filter.BranchId.Value);
        }

        if (filter.WarehouseId.HasValue)
        {
            query = query.Where(g => g.WarehouseId == filter.WarehouseId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Channel))
        {
            var ch = filter.Channel.Trim();
            query = query.Where(g => g.Channel == ch);
        }

        if (filter.FromDate.HasValue)
        {
            var fromUtc = filter.FromDate.Value.ToUniversalTime();
            query = query.Where(g => g.IssuedAt >= fromUtc);
        }

        if (filter.ToDate.HasValue)
        {
            var toUtc = filter.ToDate.Value.ToUniversalTime();
            query = query.Where(g => g.IssuedAt < toUtc);
        }

        var totalCount = await query.CountAsync(ct);

        var pageIndex = Math.Max(1, filter.PageIndex);
        var pageSize = Math.Max(1, Math.Min(100, filter.PageSize));

        var entities = await query
            .OrderByDescending(g => g.IssuedAt)
            .ThenBy(g => g.WarehouseId)
            .ThenByDescending(g => g.Id)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = entities.Select(g => MapToDto(g, canViewCost)).ToList();

        return new PagedResult<GoodsIssueDto>
        {
            Items = items,
            PageIndex = pageIndex,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<GoodsIssueDto> GetByIdAsync(Guid id, bool canViewCost, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var entity = await _context.GoodsIssues
            .AsNoTracking()
            .Include(g => g.Items)
                .ThenInclude(i => i.Sku)
                    .ThenInclude(s => s!.BaseUnit)
            .FirstOrDefaultAsync(g => g.TenantId == tenantId && g.Id == id, ct);

        if (entity == null)
        {
            throw new OismException("GOODS_ISSUE_NOT_FOUND", $"Không tìm thấy phiếu xuất kho với mã '{id}'.", 404);
        }

        return MapToDto(entity, canViewCost);
    }

    public async Task<List<GoodsIssueDto>> GetByOrderIdAsync(Guid orderId, bool canViewCost, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var orderExists = await _context.Orders
            .AsNoTracking()
            .AnyAsync(o => o.TenantId == tenantId && o.Id == orderId, ct);

        if (!orderExists)
        {
            throw new OismException("ORDER_NOT_FOUND", $"Không tìm thấy đơn hàng với mã '{orderId}'.", 404);
        }

        var entities = await _context.GoodsIssues
            .AsNoTracking()
            .Include(g => g.Items)
                .ThenInclude(i => i.Sku)
                    .ThenInclude(s => s!.BaseUnit)
            .Where(g => g.TenantId == tenantId && g.OrderId == orderId)
            .OrderBy(g => g.WarehouseId)
            .ToListAsync(ct);

        return entities.Select(g => MapToDto(g, canViewCost)).ToList();
    }

    public async Task<List<GoodsIssueDto>> GetByOrderIdsAsync(
        IReadOnlyCollection<Guid> orderIds,
        bool canViewCost,
        CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        if (orderIds == null || orderIds.Count == 0)
        {
            throw new OismException("VALIDATION_ERROR", "Danh sách mã đơn hàng không được để trống.", 400);
        }

        var distinctIds = orderIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            throw new OismException("VALIDATION_ERROR", "Danh sách mã đơn hàng không hợp lệ.", 400);
        }

        if (distinctIds.Count > MaxBulkOrderIds)
        {
            throw new OismException("VALIDATION_ERROR", $"Mỗi lần in hàng loạt tối đa {MaxBulkOrderIds} đơn hàng.", 400);
        }

        var entities = await _context.GoodsIssues
            .AsNoTracking()
            .Include(g => g.Items)
                .ThenInclude(i => i.Sku)
                    .ThenInclude(s => s!.BaseUnit)
            .Where(g => g.TenantId == tenantId && distinctIds.Contains(g.OrderId))
            .OrderBy(g => g.IssuedAt)
            .ThenBy(g => g.OrderId)
            .ThenBy(g => g.WarehouseId)
            .ToListAsync(ct);

        return entities.Select(g => MapToDto(g, canViewCost)).ToList();
    }

    public async Task<PosDailyGoodsIssueSummaryDto> GetPosDailySummaryAsync(
        PosDailyGoodsIssueQueryDto query,
        bool canViewCost,
        CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var rawDate = !string.IsNullOrWhiteSpace(query.Date) ? query.Date : query.BusinessDate;
        DateOnly businessDate;
        if (string.IsNullOrWhiteSpace(rawDate))
        {
            var vnNow = DateTimeOffset.UtcNow.ToOffset(VietnamOffset);
            businessDate = DateOnly.FromDateTime(vnNow.DateTime);
        }
        else if (!DateOnly.TryParseExact(rawDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out businessDate) &&
                 !DateOnly.TryParse(rawDate.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out businessDate))
        {
            throw new OismException("VALIDATION_ERROR", $"Ngày bảng kê '{rawDate}' không hợp lệ. Vui lòng dùng định dạng YYYY-MM-DD.", 400);
        }

        string branchName = "Tất cả chi nhánh";
        if (query.BranchId.HasValue)
        {
            if (query.BranchId.Value == Guid.Empty)
            {
                throw new OismException("VALIDATION_ERROR", "Mã chi nhánh không hợp lệ.", 400);
            }

            var branch = await _context.Branches
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == query.BranchId.Value, ct);

            if (branch == null)
            {
                throw new OismException("BRANCH_NOT_FOUND", $"Không tìm thấy chi nhánh với mã '{query.BranchId.Value}'.", 404);
            }

            branchName = branch.Name;
        }

        // Khoảng nửa mở [00:00:00+07:00, 24:00:00+07:00) theo ngày nghiệp vụ UTC+07:00
        var startVn = new DateTimeOffset(businessDate.Year, businessDate.Month, businessDate.Day, 0, 0, 0, VietnamOffset);
        var endVn = startVn.AddDays(1);
        var fromUtc = startVn.ToUniversalTime();
        var toUtc = endVn.ToUniversalTime();

        var issuesQuery = _context.GoodsIssues
            .AsNoTracking()
            .Include(g => g.SalesInvoice)
            .Include(g => g.Items)
                .ThenInclude(i => i.Sku)
                    .ThenInclude(s => s!.BaseUnit)
            .Where(g => g.TenantId == tenantId &&
                        g.Channel == "POS" &&
                        g.IssuedAt >= fromUtc &&
                        g.IssuedAt < toUtc);

        if (query.BranchId.HasValue)
        {
            issuesQuery = issuesQuery.Where(g => g.BranchId == query.BranchId.Value);
        }

        var issues = await issuesQuery
            .OrderBy(g => g.IssuedAt)
            .ThenBy(g => g.WarehouseId)
            .ThenBy(g => g.Id)
            .ToListAsync(ct);

        var allItems = issues.SelectMany(g => g.Items).ToList();

        var skuRows = allItems
            .GroupBy(i => i.SkuId)
            .Select(grp =>
            {
                var first = grp.First();
                var baseUnitName = ResolveBaseUnitName(first);
                var totalQty = grp.Sum(x => x.Quantity);
                var totalBaseQty = grp.Sum(x => x.BaseQuantity);
                var rawCost = grp.Sum(x => x.BaseQuantity * x.UnitCost);

                return new PosDailyGoodsIssueSkuDto
                {
                    SkuId = grp.Key,
                    SkuCode = first.SkuCodeSnapshot,
                    SkuName = first.SkuNameSnapshot,
                    BaseUnitName = baseUnitName,
                    TotalQuantity = totalQty,
                    TotalBaseQuantity = totalBaseQty,
                    UnitCost = canViewCost
                        ? (totalBaseQty > 0m ? Math.Round(rawCost / totalBaseQty, 6, MidpointRounding.AwayFromZero) : 0m)
                        : null,
                    TotalCost = canViewCost
                        ? Math.Round(rawCost, 6, MidpointRounding.AwayFromZero)
                        : null
                };
            })
            .OrderBy(s => s.SkuCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Đơn xuất từ nhiều kho có nhiều phiếu cùng một hóa đơn: tiền hóa đơn chỉ gán cho phiếu đầu tiên
        // (theo số phiếu) để cộng cột TotalSalesAmount không bị đếm trùng.
        var invoiceAmountOwners = issues
            .GroupBy(g => g.SalesInvoiceId)
            .Select(grp => grp.OrderBy(g => g.Number, StringComparer.Ordinal).First().Id)
            .ToHashSet();

        var invoiceRows = issues
            .Select(g =>
            {
                var issueQty = g.Items.Sum(i => i.Quantity);
                var issueBaseQty = g.Items.Sum(i => i.BaseQuantity);
                var issueRawCost = g.Items.Sum(i => i.BaseQuantity * i.UnitCost);

                return new PosDailyGoodsIssueInvoiceDto
                {
                    GoodsIssueId = g.Id,
                    GoodsIssueNumber = g.Number,
                    SalesInvoiceId = g.SalesInvoiceId,
                    SalesInvoiceNumber = g.SalesInvoiceNumberSnapshot,
                    OrderId = g.OrderId,
                    OrderNumber = g.OrderNumberSnapshot,
                    BranchId = g.BranchId,
                    BranchName = g.BranchNameSnapshot,
                    WarehouseId = g.WarehouseId,
                    WarehouseName = g.WarehouseNameSnapshot,
                    CustomerName = g.CustomerNameSnapshot,
                    IssuedByName = g.IssuedByNameSnapshot,
                    IssuedAt = g.IssuedAt,
                    TotalQuantity = issueQty,
                    TotalBaseQuantity = issueBaseQty,
                    TotalSalesAmount = invoiceAmountOwners.Contains(g.Id) ? g.SalesInvoice?.TotalAmount ?? 0m : 0m,
                    TotalCost = canViewCost
                        ? Math.Round(issueRawCost, 6, MidpointRounding.AwayFromZero)
                        : null
                };
            })
            .ToList();

        var totalRawCost = allItems.Sum(i => i.BaseQuantity * i.UnitCost);

        return new PosDailyGoodsIssueSummaryDto
        {
            BusinessDate = businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            FromUtc = fromUtc,
            ToUtc = toUtc,
            BranchId = query.BranchId,
            BranchName = branchName,
            TotalOrders = issues.Select(g => g.OrderId).Distinct().Count(),
            TotalInvoices = issues.Select(g => g.SalesInvoiceId).Distinct().Count(),
            TotalGoodsIssues = issues.Count,
            TotalQuantity = allItems.Sum(i => i.Quantity),
            TotalBaseQuantity = allItems.Sum(i => i.BaseQuantity),
            TotalSalesAmount = invoiceRows.Sum(r => r.TotalSalesAmount),
            TotalCost = canViewCost
                ? Math.Round(totalRawCost, 6, MidpointRounding.AwayFromZero)
                : null,
            Items = skuRows,
            Invoices = invoiceRows
        };
    }

    private static GoodsIssueDto MapToDto(GoodsIssue entity, bool canViewCost)
    {
        var itemDtos = (entity.Items ?? new List<GoodsIssueItem>())
            .OrderBy(i => i.SkuCodeSnapshot, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Id)
            .Select(i =>
            {
                var lineCost = Math.Round(i.BaseQuantity * i.UnitCost, 6, MidpointRounding.AwayFromZero);
                return new GoodsIssueItemDto
                {
                    Id = i.Id,
                    GoodsIssueId = i.GoodsIssueId,
                    InventoryTransactionId = i.InventoryTransactionId,
                    OrderItemId = i.OrderItemId,
                    SkuId = i.SkuId,
                    SkuCode = i.SkuCodeSnapshot,
                    SkuName = i.SkuNameSnapshot,
                    UnitId = i.UnitId,
                    UnitName = i.UnitNameSnapshot,
                    BaseUnitName = ResolveBaseUnitName(i),
                    FactorSnapshot = i.FactorSnapshot,
                    Quantity = i.Quantity,
                    BaseQuantity = i.BaseQuantity,
                    UnitCost = canViewCost ? i.UnitCost : null,
                    TotalCost = canViewCost ? lineCost : null
                };
            })
            .ToList();

        var rawTotalCost = (entity.Items ?? new List<GoodsIssueItem>())
            .Sum(i => i.BaseQuantity * i.UnitCost);

        return new GoodsIssueDto
        {
            Id = entity.Id,
            Number = entity.Number,
            OrderId = entity.OrderId,
            OrderNumber = entity.OrderNumberSnapshot,
            SalesInvoiceId = entity.SalesInvoiceId,
            SalesInvoiceNumber = entity.SalesInvoiceNumberSnapshot,
            BranchId = entity.BranchId,
            BranchName = entity.BranchNameSnapshot,
            WarehouseId = entity.WarehouseId,
            WarehouseName = entity.WarehouseNameSnapshot,
            Channel = entity.Channel,
            Reason = BuildReason(entity.Channel, entity.OrderNumberSnapshot, entity.SalesInvoiceNumberSnapshot),
            CustomerName = entity.CustomerNameSnapshot,
            IssuedBy = entity.IssuedBy,
            IssuedByName = entity.IssuedByNameSnapshot,
            IssuedAt = entity.IssuedAt,
            TotalQuantity = itemDtos.Sum(i => i.Quantity),
            TotalBaseQuantity = itemDtos.Sum(i => i.BaseQuantity),
            TotalCost = canViewCost ? Math.Round(rawTotalCost, 6, MidpointRounding.AwayFromZero) : null,
            Items = itemDtos
        };
    }

    private static string ResolveBaseUnitName(GoodsIssueItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Sku?.BaseUnit?.Name))
        {
            return item.Sku!.BaseUnit!.Name;
        }
        if (item.FactorSnapshot == 1m && !string.IsNullOrWhiteSpace(item.UnitNameSnapshot))
        {
            return item.UnitNameSnapshot;
        }
        if (!string.IsNullOrWhiteSpace(item.Sku?.Unit))
        {
            return item.Sku!.Unit;
        }
        return item.UnitNameSnapshot;
    }

    private static string BuildReason(string channel, string orderNumber, string invoiceNumber)
    {
        var channelLabel = channel switch
        {
            "POS" => "Bán POS",
            "Admin" => "Bán đơn Admin",
            "Shopee" or "TikTok" or "Lazada" => $"Bán TMĐT ({channel})",
            _ => $"Bán hàng ({channel})"
        };
        return $"{channelLabel} — Đơn {orderNumber}, Hóa đơn {invoiceNumber}";
    }
}
