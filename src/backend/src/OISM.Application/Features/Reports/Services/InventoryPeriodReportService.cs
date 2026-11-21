using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Helpers;
using OISM.Application.Common.Interfaces;
using OISM.Application.Common.Services;
using OISM.Application.Features.Reports.Dtos;
using OISM.Domain.Entities;

namespace OISM.Application.Features.Reports.Services;

/// <summary>
/// Dịch vụ tính toán Báo cáo tồn kho theo kỳ (NXT) và Thẻ kho (S12-DN / S10-DN) (Upgrade 13 / OISMS-66).
/// Chỉ đọc dữ liệu từ sổ cái bất biến (inventory_transactions) và chứng từ liên quan; không sửa schema.
/// </summary>
public class InventoryPeriodReportService : IInventoryPeriodReportService
{
    private static readonly TimeSpan SaigonOffset = TimeSpan.FromHours(7);
    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICostVisibilityService _costVisibilityService;

    public InventoryPeriodReportService(
        IAppDbContext context,
        ICurrentTenantService tenantService,
        ICostVisibilityService? costVisibilityService = null)
    {
        _context = context;
        _tenantService = tenantService;
        _costVisibilityService = costVisibilityService ?? AllowAllCostVisibilityService.Instance;
    }

    /// <inheritdoc />
    public async Task<InventoryPeriodReportDto> GetInventoryPeriodReportAsync(
        InventoryPeriodFilterDto filter,
        bool? isOwner = null,
        CancellationToken ct = default)
    {
        return await GetInventoryPeriodReportInternalAsync(filter, isOwner ?? _costVisibilityService.IsOwner, ct);
    }

    private async Task<InventoryPeriodReportDto> GetInventoryPeriodReportInternalAsync(
        InventoryPeriodFilterDto filter,
        bool isOwner,
        CancellationToken ct = default)
    {
        if (!_tenantService.TenantId.HasValue || _tenantService.TenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Không xác định được danh tính cửa hàng (Tenant).");
        }
        var tenantId = _tenantService.TenantId.Value;
        ValidateDateRange(filter.FromDate, filter.ToDate);

        var (startBoundaryUtc, endBoundaryUtc) = ResolveUtcBoundaries(filter.FromDate, filter.ToDate);
        var today = ReportTimeHelper.ToLocalDate(DateTimeOffset.UtcNow);
        var isPeriodUpToNow = filter.ToDate >= today;

        // 1. Lấy thông tin chi nhánh (nếu có lọc theo một chi nhánh)
        string branchName = "Tất cả chi nhánh";
        if (filter.BranchId.HasValue && filter.BranchId.Value != Guid.Empty)
        {
            var branch = await _context.Branches
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == filter.BranchId.Value, ct);
            if (branch == null)
            {
                throw new OismException("BRANCH_NOT_FOUND", $"Không tìm thấy chi nhánh với ID '{filter.BranchId.Value}'.", 404);
            }
            branchName = branch.Name;
        }

        // 2. Phân trang SKU TRƯỚC rồi mới tổng hợp sổ cái (AC-04 / Q12)
        var skuQuery = _context.Skus
            .AsNoTracking()
            .Include(s => s.Product)
                .ThenInclude(p => p!.Category)
            .Where(s => s.TenantId == tenantId);

        if (filter.CategoryId.HasValue && filter.CategoryId.Value != Guid.Empty)
        {
            skuQuery = skuQuery.Where(s => s.Product != null && s.Product.CategoryId == filter.CategoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.Trim().ToLower();
            skuQuery = skuQuery.Where(s =>
                s.SKU.ToLower().Contains(term) ||
                (s.Barcode != null && s.Barcode.ToLower().Contains(term)) ||
                (s.Product != null && s.Product.Name.ToLower().Contains(term)));
        }

        var totalCount = await skuQuery.CountAsync(ct);

        var pageIndex = Math.Max(1, filter.Page);
        var pageSize = Math.Max(1, Math.Min(100, filter.PageSize));

        var pagedSkus = await skuQuery
            .OrderBy(s => s.Product != null ? s.Product.Name : "")
            .ThenBy(s => s.SKU)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var pagedSkuIds = pagedSkus.Select(s => s.Id).ToList();

        // 3. Tổng hợp số liệu sổ cái cho các SKU trong trang
        var items = new List<InventoryPeriodItemDto>();
        if (pagedSkuIds.Count > 0)
        {
            items = await ComputePeriodItemsAsync(
                tenantId,
                filter.BranchId,
                pagedSkus,
                startBoundaryUtc,
                endBoundaryUtc,
                isOwner,
                ct);
        }

        // 4. Tính dòng tổng cộng cho trang báo cáo
        var summary = BuildSummary(items, isOwner);

        return new InventoryPeriodReportDto
        {
            FromDate = filter.FromDate,
            ToDate = filter.ToDate,
            BranchId = filter.BranchId,
            BranchName = branchName,
            IsOwner = isOwner,
            IsPeriodUpToNow = isPeriodUpToNow,
            Items = items,
            Summary = summary,
            TotalCount = totalCount,
            Page = pageIndex,
            PageSize = pageSize
        };
    }

    /// <inheritdoc />
    public async Task<InventoryPeriodReportDto> GetInventoryPeriodReportForExportAsync(
        InventoryPeriodFilterDto filter,
        bool? isOwner = null,
        CancellationToken ct = default)
    {
        return await GetInventoryPeriodReportForExportInternalAsync(filter, isOwner ?? _costVisibilityService.IsOwner, ct);
    }

    private async Task<InventoryPeriodReportDto> GetInventoryPeriodReportForExportInternalAsync(
        InventoryPeriodFilterDto filter,
        bool isOwner,
        CancellationToken ct = default)
    {
        // Xuất Excel lấy toàn bộ SKU khớp bộ lọc: đi lần lượt từng trang 100 SKU (giữ nguyên cách tính
        // phân trang SKU trước rồi mới tổng hợp sổ cái), dòng tổng cộng tính trên toàn bộ dòng đã xuất.
        const int batchSize = 100;
        var allItems = new List<InventoryPeriodItemDto>();
        InventoryPeriodReportDto? firstPage = null;
        var page = 1;

        while (true)
        {
            var pageReport = await GetInventoryPeriodReportAsync(new InventoryPeriodFilterDto
            {
                FromDate = filter.FromDate,
                ToDate = filter.ToDate,
                BranchId = filter.BranchId,
                SearchTerm = filter.SearchTerm,
                CategoryId = filter.CategoryId,
                Page = page,
                PageSize = batchSize
            }, isOwner, ct);

            firstPage ??= pageReport;
            allItems.AddRange(pageReport.Items);

            if (pageReport.Items.Count < batchSize || allItems.Count >= pageReport.TotalCount)
            {
                break;
            }
            page++;
        }

        firstPage.Items = allItems;
        firstPage.Summary = BuildSummary(allItems, isOwner);
        firstPage.Page = 1;
        firstPage.PageSize = Math.Max(1, allItems.Count);
        firstPage.TotalCount = allItems.Count;
        return firstPage;
    }

    /// <inheritdoc />
    public async Task<StockCardReportDto> GetStockCardReportAsync(
        StockCardFilterDto filter,
        bool? isOwner = null,
        CancellationToken ct = default)
    {
        return await GetStockCardReportInternalAsync(filter, isOwner ?? _costVisibilityService.IsOwner, ct);
    }

    private async Task<StockCardReportDto> GetStockCardReportInternalAsync(
        StockCardFilterDto filter,
        bool isOwner,
        CancellationToken ct = default)
    {
        if (!_tenantService.TenantId.HasValue || _tenantService.TenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Không xác định được danh tính cửa hàng (Tenant).");
        }
        var tenantId = _tenantService.TenantId.Value;
        ValidateDateRange(filter.FromDate, filter.ToDate);

        var (startBoundaryUtc, endBoundaryUtc) = ResolveUtcBoundaries(filter.FromDate, filter.ToDate);
        var today = ReportTimeHelper.ToLocalDate(DateTimeOffset.UtcNow);

        // 1. Kiểm tra SKU và Chi nhánh
        var sku = await _context.Skus
            .AsNoTracking()
            .Include(s => s.Product)
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == filter.SkuId, ct);
        if (sku == null)
        {
            throw new OismException("SKU_NOT_FOUND", $"Không tìm thấy SKU với ID '{filter.SkuId}'.", 404);
        }

        var branch = await _context.Branches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == filter.BranchId, ct);
        if (branch == null)
        {
            throw new OismException("BRANCH_NOT_FOUND", $"Không tìm thấy chi nhánh với ID '{filter.BranchId}'.", 404);
        }

        // 2. Lấy số dư đầu kỳ (dòng sổ cuối cùng trước mốc startBoundaryUtc)
        var lastOpeningTx = await _context.InventoryTransactions
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId &&
                        t.BranchId == filter.BranchId &&
                        t.SkuId == filter.SkuId &&
                        t.CreatedAt < startBoundaryUtc)
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.SequenceNo)
            .ThenByDescending(t => t.Id)
            .FirstOrDefaultAsync(ct);

        decimal openingQuantity = lastOpeningTx?.BalanceAfter ?? 0m;

        // Giá trị đầu kỳ: tích lũy Σ(IN * unit_cost) - Σ(OUT * unit_cost) trước mốc startBoundaryUtc
        decimal openingValue = 0m;
        if (isOwner)
        {
            openingValue = await _context.InventoryTransactions
                .AsNoTracking()
                .Where(t => t.TenantId == tenantId &&
                            t.BranchId == filter.BranchId &&
                            t.SkuId == filter.SkuId &&
                            t.CreatedAt < startBoundaryUtc)
                .SumAsync(t => t.Type == "IN" ? t.Quantity * t.UnitCost : -(t.Quantity * t.UnitCost), ct);
        }

        // 3. Lấy tất cả dòng sổ trong kỳ [startBoundaryUtc, endBoundaryUtc)
        var periodTxs = await _context.InventoryTransactions
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId &&
                        t.BranchId == filter.BranchId &&
                        t.SkuId == filter.SkuId &&
                        t.CreatedAt >= startBoundaryUtc &&
                        t.CreatedAt < endBoundaryUtc)
            .OrderBy(t => t.CreatedAt)
            .ThenBy(t => t.SequenceNo)
            .ThenBy(t => t.Id)
            .ToListAsync(ct);

        // Nạp thông tin chứng từ cho các dòng trong kỳ
        var documentMap = await ResolveDocumentNumbersAsync(tenantId, periodTxs, ct);

        // Số phiếu xuất kho 02-VT của các dòng OUT bán hàng (Upgrade 13 - P1, đối chiếu trên thẻ kho)
        var salesOutTxIds = periodTxs.Where(t => t.OrderItemId.HasValue).Select(t => t.Id).ToList();
        var goodsIssuesByTxId = new Dictionary<Guid, (Guid Id, string Number)>();
        if (salesOutTxIds.Count > 0)
        {
            var issueLinks = await _context.GoodsIssueItems
                .AsNoTracking()
                .Where(gii => gii.TenantId == tenantId && salesOutTxIds.Contains(gii.InventoryTransactionId))
                .Select(gii => new { gii.InventoryTransactionId, gii.GoodsIssueId, Number = gii.GoodsIssue!.Number })
                .ToListAsync(ct);
            foreach (var link in issueLinks)
            {
                goodsIssuesByTxId[link.InventoryTransactionId] = (link.GoodsIssueId, link.Number);
            }
        }

        // 4. Duyệt từng dòng phát sinh
        var entries = new List<StockCardEntryDto>();
        decimal runningValue = openingValue;

        foreach (var tx in periodTxs)
        {
            var isReceipt = tx.ReceiptItemId.HasValue;
            var isReturn = tx.SalesReturnItemId.HasValue;
            var isTransfer = tx.TransferItemId.HasValue;
            var isOrder = tx.OrderItemId.HasValue;
            var isStockCount = tx.StockCountItemId.HasValue;
            var isAdjustment = tx.AdjustmentItemId.HasValue;

            string sourceType;
            string docNumber = documentMap.TryGetValue(tx.Id, out var mapped) ? mapped.Number : string.Empty;
            string? orderChannel = mapped.Channel;

            if (isReceipt)
            {
                sourceType = "Nhập mua NCC";
            }
            else if (isReturn)
            {
                sourceType = "Khách trả hàng";
            }
            else if (isTransfer)
            {
                sourceType = tx.Type == "IN" ? "Nhập chuyển kho" : "Xuất chuyển kho";
            }
            else if (isOrder)
            {
                var ch = (orderChannel ?? "POS").ToUpperInvariant();
                if (ch.Contains("SHOPEE") || ch.Contains("TIKTOK") || ch.Contains("LAZADA"))
                {
                    sourceType = $"Xuất bán sàn ({orderChannel})";
                }
                else if (ch == "ADMIN")
                {
                    sourceType = "Xuất bán Admin";
                }
                else
                {
                    sourceType = "Xuất bán POS";
                }
            }
            else if (isStockCount)
            {
                sourceType = tx.Type == "IN" ? "Kiểm kê tăng" : "Kiểm kê giảm";
            }
            else if (isAdjustment)
            {
                sourceType = tx.Type == "IN" ? "Điều chỉnh tăng" : "Điều chỉnh giảm";
            }
            else
            {
                sourceType = tx.Type == "IN" ? "Nhập khác" : "Xuất khác";
            }

            var inQty = tx.Type == "IN" ? tx.Quantity : 0m;
            var outQty = tx.Type == "OUT" ? tx.Quantity : 0m;

            decimal? inUnitCost = isOwner && tx.Type == "IN" ? tx.UnitCost : null;
            decimal? inVal = isOwner && tx.Type == "IN" ? tx.Quantity * tx.UnitCost : null;

            decimal? outUnitCost = isOwner && tx.Type == "OUT" ? tx.UnitCost : null;
            decimal? outVal = isOwner && tx.Type == "OUT" ? tx.Quantity * tx.UnitCost : null;

            if (isOwner)
            {
                runningValue += (inVal ?? 0m) - (outVal ?? 0m);
            }

            entries.Add(new StockCardEntryDto
            {
                Id = tx.Id,
                CreatedAt = tx.CreatedAt,
                Type = tx.Type,
                SourceType = sourceType,
                DocumentNumber = docNumber,
                GoodsIssueId = goodsIssuesByTxId.TryGetValue(tx.Id, out var gi) ? gi.Id : null,
                GoodsIssueNumber = goodsIssuesByTxId.TryGetValue(tx.Id, out var giNum) ? giNum.Number : null,
                ReferenceId = tx.ReferenceId,
                InQuantity = inQty,
                InUnitPrice = inUnitCost,
                InValue = inVal,
                OutQuantity = outQty,
                OutUnitPrice = outUnitCost,
                OutValue = outVal,
                BalanceAfterQuantity = tx.BalanceAfter,
                BalanceAfterValue = isOwner ? runningValue : null
            });
        }

        var totalInQty = entries.Sum(e => e.InQuantity);
        var totalInVal = isOwner ? entries.Sum(e => e.InValue ?? 0m) : (decimal?)null;
        var totalOutQty = entries.Sum(e => e.OutQuantity);
        var totalOutVal = isOwner ? entries.Sum(e => e.OutValue ?? 0m) : (decimal?)null;

        var closingQuantity = openingQuantity + totalInQty - totalOutQty;
        var closingValue = isOwner ? (openingValue + (totalInVal ?? 0m) - (totalOutVal ?? 0m)) : (decimal?)null;

        // 5. Kiểm tra chênh lệch làm tròn khi kỳ tính đến hiện tại (Q14)
        bool hasRoundingDiff = false;
        decimal? roundingDiff = null;
        decimal? currentOnHand = null;
        decimal? currentWac = null;
        decimal? theoreticalValue = null;

        if (isOwner && filter.ToDate >= today)
        {
            var invBalance = await _context.InventoryBalances
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.TenantId == tenantId &&
                                          b.BranchId == filter.BranchId &&
                                          b.SkuId == filter.SkuId, ct);
            if (invBalance != null)
            {
                currentOnHand = invBalance.OnHand;
                currentWac = invBalance.AverageCost;
                theoreticalValue = Math.Round(currentOnHand.Value * currentWac.Value, 0);
                var diff = theoreticalValue.Value - (closingValue ?? 0m);
                if (Math.Abs(diff) > 0.001m)
                {
                    hasRoundingDiff = true;
                    roundingDiff = diff;
                }
            }
        }

        return new StockCardReportDto
        {
            SkuId = sku.Id,
            SkuCode = sku.SKU,
            Barcode = sku.Barcode,
            ProductName = sku.Product?.Name ?? string.Empty,
            VariantName = sku.VariantName,
            UnitName = !string.IsNullOrWhiteSpace(sku.Unit) ? sku.Unit : "Cái",
            BranchId = branch.Id,
            BranchName = branch.Name,
            FromDate = filter.FromDate,
            ToDate = filter.ToDate,
            IsOwner = isOwner,
            ReportTitle = isOwner ? "Sổ chi tiết hàng hóa (S10-DN)" : "Thẻ kho (S12-DN)",
            OpeningQuantity = openingQuantity,
            OpeningValue = isOwner ? openingValue : null,
            Entries = entries,
            TotalInQuantity = totalInQty,
            TotalInValue = totalInVal,
            TotalOutQuantity = totalOutQty,
            TotalOutValue = totalOutVal,
            ClosingQuantity = closingQuantity,
            ClosingValue = closingValue,
            HasRoundingDifference = hasRoundingDiff,
            RoundingDifference = roundingDiff,
            CurrentOnHand = currentOnHand,
            CurrentWac = currentWac,
            CurrentTheoreticalValue = theoreticalValue
        };
    }

    /// <summary>
    /// Tính toán các chỉ số tồn kho cho danh sách SKU trong trang hiện tại.
    /// Mọi tổng hợp sổ cái chạy ở PostgreSQL (GROUP BY), không nạp từng dòng sổ lên bộ nhớ (Q12).
    /// Đầu kỳ và cuối kỳ lấy trực tiếp từ sổ cái tại mốc (balance_after dòng cuối trước mốc, giá trị Σ IN − Σ OUT trước mốc).
    /// Chế độ Tất cả: bỏ chuyển nội bộ khỏi nhập/xuất, nên đối chiếu là
    /// (đầu kỳ + đang chuyển đầu kỳ) + nhập − xuất = (cuối kỳ + đang chuyển cuối kỳ) (Q9).
    /// </summary>
    private async Task<List<InventoryPeriodItemDto>> ComputePeriodItemsAsync(
        Guid tenantId,
        Guid? branchId,
        List<ProductVariant> skus,
        DateTimeOffset startBoundaryUtc,
        DateTimeOffset endBoundaryUtc,
        bool isOwner,
        CancellationToken ct)
    {
        var skuIds = skus.Select(s => s.Id).ToList();
        var isAllBranches = !branchId.HasValue || branchId.Value == Guid.Empty;
        Guid? scopedBranchId = isAllBranches ? null : branchId!.Value;

        // A. Đầu kỳ và cuối kỳ theo sổ cái tại mốc
        var opening = await LoadLedgerBalancesBeforeAsync(tenantId, skuIds, scopedBranchId, startBoundaryUtc, ct);
        var closing = await LoadLedgerBalancesBeforeAsync(tenantId, skuIds, scopedBranchId, endBoundaryUtc, ct);

        // B. Phát sinh trong kỳ [startBoundaryUtc, endBoundaryUtc), gom theo SKU × loại × nguồn × kênh
        var periodTxQuery = _context.InventoryTransactions
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId &&
                        skuIds.Contains(t.SkuId) &&
                        t.CreatedAt >= startBoundaryUtc &&
                        t.CreatedAt < endBoundaryUtc);

        if (scopedBranchId.HasValue)
        {
            periodTxQuery = periodTxQuery.Where(t => t.BranchId == scopedBranchId.Value);
        }

        var periodRows = await periodTxQuery
            .Select(t => new
            {
                t.SkuId,
                t.Type,
                Kind = t.ReceiptItemId != null ? 1
                    : t.SalesReturnItemId != null ? 2
                    : t.TransferItemId != null ? 3
                    : t.OrderItemId != null ? 4
                    : 0,
                Channel = t.OrderItemId != null ? t.OrderItem!.Order!.Channel : null,
                t.Quantity,
                Value = t.Quantity * t.UnitCost
            })
            .GroupBy(x => new { x.SkuId, x.Type, x.Kind, x.Channel })
            .Select(g => new
            {
                g.Key.SkuId,
                g.Key.Type,
                g.Key.Kind,
                g.Key.Channel,
                Quantity = g.Sum(x => x.Quantity),
                Value = g.Sum(x => x.Value)
            })
            .ToListAsync(ct);

        // C. Hàng đang chuyển (Q9)
        Dictionary<Guid, (decimal Quantity, decimal Value)> openingInTransit;
        Dictionary<Guid, (decimal Quantity, decimal Value)> closingInTransit;
        if (isAllBranches)
        {
            // Phiếu đã gửi trước mốc mà chưa nhận trước mốc
            openingInTransit = await LoadInTransitAsync(tenantId, skuIds, null, startBoundaryUtc, ct);
            closingInTransit = await LoadInTransitAsync(tenantId, skuIds, null, endBoundaryUtc, ct);
        }
        else
        {
            // Một chi nhánh: chỉ cột "Đang chuyển đến" tại cuối kỳ để tham khảo
            openingInTransit = new Dictionary<Guid, (decimal Quantity, decimal Value)>();
            closingInTransit = await LoadInTransitAsync(tenantId, skuIds, scopedBranchId, endBoundaryUtc, ct);
        }

        // D. Tổng hợp dữ liệu cho từng SKU
        var result = new List<InventoryPeriodItemDto>();
        var rowsBySku = periodRows.GroupBy(r => r.SkuId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var sku in skus)
        {
            var skuId = sku.Id;
            var open = opening.GetValueOrDefault(skuId);
            var close = closing.GetValueOrDefault(skuId);

            var inBreakdown = new InventoryPeriodInBreakdownDto();
            var outBreakdown = new InventoryPeriodOutBreakdownDto();

            decimal totalInQty = 0m;
            decimal totalInVal = 0m;
            decimal totalOutQty = 0m;
            decimal totalOutVal = 0m;

            if (rowsBySku.TryGetValue(skuId, out var skuRows))
            {
                foreach (var row in skuRows)
                {
                    // Ở chế độ Tất cả: bỏ qua chuyển nội bộ khỏi Nhập và Xuất (AC-03 / Q9)
                    if (isAllBranches && row.Kind == 3)
                    {
                        continue;
                    }

                    var qty = row.Quantity;
                    var val = row.Value;

                    if (row.Type == "IN")
                    {
                        totalInQty += qty;
                        totalInVal += val;

                        switch (row.Kind)
                        {
                            case 1:
                                inBreakdown.PurchaseQuantity += qty;
                                if (isOwner) inBreakdown.PurchaseValue = (inBreakdown.PurchaseValue ?? 0m) + val;
                                break;
                            case 2:
                                inBreakdown.ReturnQuantity += qty;
                                if (isOwner) inBreakdown.ReturnValue = (inBreakdown.ReturnValue ?? 0m) + val;
                                break;
                            case 3:
                                inBreakdown.TransferInQuantity += qty;
                                if (isOwner) inBreakdown.TransferInValue = (inBreakdown.TransferInValue ?? 0m) + val;
                                break;
                            default: // Kiểm kê hoặc điều chỉnh
                                inBreakdown.AdjustmentInQuantity += qty;
                                if (isOwner) inBreakdown.AdjustmentInValue = (inBreakdown.AdjustmentInValue ?? 0m) + val;
                                break;
                        }
                    }
                    else if (row.Type == "OUT")
                    {
                        totalOutQty += qty;
                        totalOutVal += val;

                        if (row.Kind == 4)
                        {
                            var ch = (row.Channel ?? "POS").ToUpperInvariant();
                            if (ch.Contains("SHOPEE") || ch.Contains("TIKTOK") || ch.Contains("LAZADA"))
                            {
                                outBreakdown.EcommerceQuantity += qty;
                                if (isOwner) outBreakdown.EcommerceValue = (outBreakdown.EcommerceValue ?? 0m) + val;
                            }
                            else if (ch == "ADMIN")
                            {
                                outBreakdown.AdminQuantity += qty;
                                if (isOwner) outBreakdown.AdminValue = (outBreakdown.AdminValue ?? 0m) + val;
                            }
                            else
                            {
                                outBreakdown.PosQuantity += qty;
                                if (isOwner) outBreakdown.PosValue = (outBreakdown.PosValue ?? 0m) + val;
                            }
                        }
                        else if (row.Kind == 3)
                        {
                            outBreakdown.TransferOutQuantity += qty;
                            if (isOwner) outBreakdown.TransferOutValue = (outBreakdown.TransferOutValue ?? 0m) + val;
                        }
                        else // Kiểm kê hoặc điều chỉnh
                        {
                            outBreakdown.AdjustmentOutQuantity += qty;
                            if (isOwner) outBreakdown.AdjustmentOutValue = (outBreakdown.AdjustmentOutValue ?? 0m) + val;
                        }
                    }
                }
            }

            var openTransit = openingInTransit.GetValueOrDefault(skuId);
            var closeTransit = closingInTransit.GetValueOrDefault(skuId);

            result.Add(new InventoryPeriodItemDto
            {
                SkuId = sku.Id,
                SkuCode = sku.SKU,
                Barcode = sku.Barcode,
                ProductName = sku.Product?.Name ?? string.Empty,
                VariantName = sku.VariantName,
                CategoryName = sku.Product?.Category?.Name,
                UnitName = !string.IsNullOrWhiteSpace(sku.Unit) ? sku.Unit : "Cái",
                OpeningQuantity = open.Quantity,
                OpeningValue = isOwner ? open.Value : null,
                InQuantity = totalInQty,
                InValue = isOwner ? totalInVal : null,
                OutQuantity = totalOutQty,
                OutValue = isOwner ? totalOutVal : null,
                ClosingQuantity = close.Quantity,
                ClosingValue = isOwner ? close.Value : null,
                OpeningInTransitQuantity = openTransit.Quantity,
                OpeningInTransitValue = isOwner ? openTransit.Value : null,
                ClosingInTransitQuantity = closeTransit.Quantity,
                ClosingInTransitValue = isOwner ? closeTransit.Value : null,
                InBreakdown = inBreakdown,
                OutBreakdown = outBreakdown
            });
        }

        return result;
    }

    /// <summary>
    /// Số lượng và giá trị tồn theo sổ cái ngay trước mốc, gom theo SKU (cộng các chi nhánh khi không lọc chi nhánh).
    /// Số lượng = balance_after của dòng cuối trước mốc tại từng chi nhánh; giá trị = Σ IN − Σ OUT (quantity × unit_cost).
    /// </summary>
    private async Task<Dictionary<Guid, (decimal Quantity, decimal Value)>> LoadLedgerBalancesBeforeAsync(
        Guid tenantId,
        List<Guid> skuIds,
        Guid? branchId,
        DateTimeOffset boundaryUtc,
        CancellationToken ct)
    {
        var query = _context.InventoryTransactions
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId && skuIds.Contains(t.SkuId) && t.CreatedAt < boundaryUtc);

        if (branchId.HasValue)
        {
            query = query.Where(t => t.BranchId == branchId.Value);
        }

        var rows = await query
            .GroupBy(t => new { t.SkuId, t.BranchId })
            .Select(g => new
            {
                g.Key.SkuId,
                Quantity = g
                    .OrderByDescending(t => t.CreatedAt)
                    .ThenByDescending(t => t.SequenceNo)
                    .ThenByDescending(t => t.Id)
                    .Select(t => t.BalanceAfter)
                    .FirstOrDefault(),
                Value = g.Sum(t => t.Type == "IN" ? t.Quantity * t.UnitCost : -(t.Quantity * t.UnitCost))
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.SkuId)
            .ToDictionary(g => g.Key, g => (g.Sum(x => x.Quantity), g.Sum(x => x.Value)));
    }

    /// <summary>
    /// Hàng đang chuyển tại mốc: phiếu đã gửi (ghi OUT) trước mốc nhưng chưa nhận (chưa ghi IN) trước mốc.
    /// Giá trị theo giá vốn chụp lúc gửi (stock_transfer_items.unit_cost), cùng giá với dòng sổ OUT/IN của phiếu.
    /// </summary>
    private async Task<Dictionary<Guid, (decimal Quantity, decimal Value)>> LoadInTransitAsync(
        Guid tenantId,
        List<Guid> skuIds,
        Guid? toBranchId,
        DateTimeOffset boundaryUtc,
        CancellationToken ct)
    {
        var query = _context.StockTransferItems
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId &&
                        skuIds.Contains(i.SkuId) &&
                        i.Transfer!.DispatchedAt != null &&
                        i.Transfer.DispatchedAt < boundaryUtc &&
                        (i.Transfer.ReceivedAt == null || i.Transfer.ReceivedAt >= boundaryUtc));

        if (toBranchId.HasValue)
        {
            query = query.Where(i => i.Transfer!.ToBranchId == toBranchId.Value);
        }

        var rows = await query
            .GroupBy(i => i.SkuId)
            .Select(g => new
            {
                SkuId = g.Key,
                Quantity = g.Sum(i => i.Quantity),
                Value = g.Sum(i => i.Quantity * (i.UnitCost ?? 0m))
            })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.SkuId, r => (r.Quantity, r.Value));
    }

    private static InventoryPeriodSummaryDto BuildSummary(IReadOnlyCollection<InventoryPeriodItemDto> items, bool isOwner) => new()
    {
        TotalOpeningQuantity = items.Sum(i => i.OpeningQuantity),
        TotalOpeningValue = isOwner ? items.Sum(i => i.OpeningValue ?? 0m) : null,
        TotalInQuantity = items.Sum(i => i.InQuantity),
        TotalInValue = isOwner ? items.Sum(i => i.InValue ?? 0m) : null,
        TotalOutQuantity = items.Sum(i => i.OutQuantity),
        TotalOutValue = isOwner ? items.Sum(i => i.OutValue ?? 0m) : null,
        TotalClosingQuantity = items.Sum(i => i.ClosingQuantity),
        TotalClosingValue = isOwner ? items.Sum(i => i.ClosingValue ?? 0m) : null,
        TotalOpeningInTransitQuantity = items.Sum(i => i.OpeningInTransitQuantity),
        TotalOpeningInTransitValue = isOwner ? items.Sum(i => i.OpeningInTransitValue ?? 0m) : null,
        TotalClosingInTransitQuantity = items.Sum(i => i.ClosingInTransitQuantity),
        TotalClosingInTransitValue = isOwner ? items.Sum(i => i.ClosingInTransitValue ?? 0m) : null
    };

    /// <summary>
    /// Nạp số hiệu chứng từ tương ứng cho các dòng sổ cái.
    /// </summary>
    private async Task<Dictionary<Guid, (string Number, string? Channel)>> ResolveDocumentNumbersAsync(
        Guid tenantId,
        List<InventoryTransaction> transactions,
        CancellationToken ct)
    {
        var map = new Dictionary<Guid, (string Number, string? Channel)>();

        // 1. PO Receipt Items
        var receiptItemIds = transactions.Where(t => t.ReceiptItemId.HasValue).Select(t => t.ReceiptItemId!.Value).Distinct().ToList();
        if (receiptItemIds.Count > 0)
        {
            var receiptDocs = await _context.GoodsReceiptItems
                .AsNoTracking()
                .Where(ri => receiptItemIds.Contains(ri.Id))
                .Select(ri => new { ri.Id, Number = ri.Receipt != null ? ri.Receipt.Number : "" })
                .ToDictionaryAsync(x => x.Id, x => x.Number, ct);

            foreach (var tx in transactions.Where(t => t.ReceiptItemId.HasValue))
            {
                if (receiptDocs.TryGetValue(tx.ReceiptItemId!.Value, out var num))
                {
                    map[tx.Id] = (num, null);
                }
            }
        }

        // 2. Sales Orders
        var orderItemIds = transactions.Where(t => t.OrderItemId.HasValue).Select(t => t.OrderItemId!.Value).Distinct().ToList();
        if (orderItemIds.Count > 0)
        {
            var orderDocs = await _context.OrderItems
                .AsNoTracking()
                .Where(oi => orderItemIds.Contains(oi.Id))
                .Select(oi => new { oi.Id, Number = oi.Order != null ? oi.Order.Number : "", Channel = oi.Order != null ? oi.Order.Channel : (string?)null })
                .ToDictionaryAsync(x => x.Id, x => new { x.Number, x.Channel }, ct);

            foreach (var tx in transactions.Where(t => t.OrderItemId.HasValue))
            {
                if (orderDocs.TryGetValue(tx.OrderItemId!.Value, out var ord))
                {
                    map[tx.Id] = (ord.Number, ord.Channel);
                }
            }
        }

        // 3. Stock Transfers
        var transferItemIds = transactions.Where(t => t.TransferItemId.HasValue).Select(t => t.TransferItemId!.Value).Distinct().ToList();
        if (transferItemIds.Count > 0)
        {
            var transferDocs = await _context.StockTransferItems
                .AsNoTracking()
                .Where(ti => transferItemIds.Contains(ti.Id))
                .Select(ti => new { ti.Id, Number = ti.Transfer != null ? ti.Transfer.Number : "" })
                .ToDictionaryAsync(x => x.Id, x => x.Number, ct);

            foreach (var tx in transactions.Where(t => t.TransferItemId.HasValue))
            {
                if (transferDocs.TryGetValue(tx.TransferItemId!.Value, out var num))
                {
                    map[tx.Id] = (num, null);
                }
            }
        }

        // 4. Sales Returns
        var returnItemIds = transactions.Where(t => t.SalesReturnItemId.HasValue).Select(t => t.SalesReturnItemId!.Value).Distinct().ToList();
        if (returnItemIds.Count > 0)
        {
            var returnDocs = await _context.SalesReturnItems
                .AsNoTracking()
                .Where(ri => returnItemIds.Contains(ri.Id))
                .Select(ri => new { ri.Id, Number = ri.SalesReturn != null ? ri.SalesReturn.Number : "" })
                .ToDictionaryAsync(x => x.Id, x => x.Number, ct);

            foreach (var tx in transactions.Where(t => t.SalesReturnItemId.HasValue))
            {
                if (returnDocs.TryGetValue(tx.SalesReturnItemId!.Value, out var num))
                {
                    map[tx.Id] = (num, null);
                }
            }
        }

        // 5. Stock Count Items
        var countItemIds = transactions.Where(t => t.StockCountItemId.HasValue).Select(t => t.StockCountItemId!.Value).Distinct().ToList();
        if (countItemIds.Count > 0)
        {
            var countDocs = await _context.StockCountItems
                .AsNoTracking()
                .Where(ci => countItemIds.Contains(ci.Id))
                .Select(ci => new { ci.Id, Number = ci.StockCount != null ? ci.StockCount.Number : "" })
                .ToDictionaryAsync(x => x.Id, x => x.Number, ct);

            foreach (var tx in transactions.Where(t => t.StockCountItemId.HasValue))
            {
                if (countDocs.TryGetValue(tx.StockCountItemId!.Value, out var num))
                {
                    map[tx.Id] = (num, null);
                }
            }
        }

        // 6. Adjustments
        var adjustmentItemIds = transactions.Where(t => t.AdjustmentItemId.HasValue).Select(t => t.AdjustmentItemId!.Value).Distinct().ToList();
        if (adjustmentItemIds.Count > 0)
        {
            var adjDocs = await _context.InventoryAdjustmentItems
                .AsNoTracking()
                .Where(ai => adjustmentItemIds.Contains(ai.Id))
                .Select(ai => new { ai.Id, ai.Adjustment!.Number })
                .ToDictionaryAsync(x => x.Id, x => x.Number, ct);

            foreach (var tx in transactions.Where(t => t.AdjustmentItemId.HasValue))
            {
                if (adjDocs.TryGetValue(tx.AdjustmentItemId!.Value, out var num))
                {
                    map[tx.Id] = (num, null);
                }
            }
        }

        return map;
    }

    /// <summary>
    /// Kiểm tra khoảng ngày báo cáo (AC-01 / Q2).
    /// </summary>
    private static void ValidateDateRange(DateOnly fromDate, DateOnly toDate)
    {
        if (fromDate > toDate)
        {
            throw new OismException("VALIDATION_ERROR", "Ngày bắt đầu không được lớn hơn ngày kết thúc.", 400);
        }

        var today = ReportTimeHelper.ToLocalDate(DateTimeOffset.UtcNow);
        if (fromDate > today || toDate > today)
        {
            throw new OismException("VALIDATION_ERROR", "Không được chọn ngày trong tương lai.", 400);
        }

        var days = toDate.DayNumber - fromDate.DayNumber + 1;
        if (days > 366)
        {
            throw new OismException("VALIDATION_ERROR", "Khoảng thời gian báo cáo không được vượt quá 366 ngày.", 400);
        }
    }

    /// <summary>
    /// Quy đổi khoảng ngày nửa mở [FromDate, ToDate + 1) theo múi giờ Asia/Saigon sang UTC (AC-01).
    /// </summary>
    private static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) ResolveUtcBoundaries(DateOnly fromDate, DateOnly toDate)
    {
        var startLocal = fromDate.ToDateTime(TimeOnly.MinValue);
        var startUtc = new DateTimeOffset(startLocal, SaigonOffset).ToUniversalTime();

        var endLocal = toDate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var endUtc = new DateTimeOffset(endLocal, SaigonOffset).ToUniversalTime();

        return (startUtc, endUtc);
    }
}
