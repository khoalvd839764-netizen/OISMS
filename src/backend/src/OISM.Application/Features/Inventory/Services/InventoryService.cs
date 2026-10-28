using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Inventory.Dtos;

namespace OISM.Application.Features.Inventory.Services;

public interface IInventoryService
{
    Task<PagedResult<InventoryBalanceDto>> GetBalancesAsync(InventoryBalanceFilterDto filter, CancellationToken ct = default);
    Task<PagedResult<InventoryLedgerDto>> GetLedgerAsync(InventoryLedgerFilterDto filter, CancellationToken ct = default);
    Task<InventoryBalanceDto?> GetSkuBalanceAtBranchAsync(Guid branchId, Guid skuId, CancellationToken ct = default);
}

public class InventoryService : IInventoryService
{
    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _currentTenantService;

    public InventoryService(IAppDbContext context, ICurrentTenantService currentTenantService)
    {
        _context = context;
        _currentTenantService = currentTenantService;
    }

    private Guid RequireTenantId()
    {
        var tenantId = _currentTenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Yêu cầu xác thực gian hàng (TenantId hợp lệ).");
        }
        return tenantId.Value;
    }

    public async Task<PagedResult<InventoryBalanceDto>> GetBalancesAsync(InventoryBalanceFilterDto filter, CancellationToken ct = default)
    {
        RequireTenantId();

        var pageIndex = Math.Max(1, filter.PageIndex);
        var pageSize = Math.Max(1, Math.Min(100, filter.PageSize));

        // Nếu người dùng lọc theo Kho cụ thể: Dùng bảng warehouse_inventory_balances
        if (filter.WarehouseId.HasValue)
        {
            var query = _context.WarehouseInventoryBalances
                .Include(wb => wb.Branch)
                .Include(wb => wb.Warehouse)
                .Include(wb => wb.Sku)
                    .ThenInclude(s => s!.Product)
                .Where(wb => wb.WarehouseId == filter.WarehouseId.Value)
                .AsNoTracking();

            if (filter.BranchId.HasValue)
                query = query.Where(wb => wb.BranchId == filter.BranchId.Value);

            if (filter.SkuId.HasValue)
                query = query.Where(wb => wb.SkuId == filter.SkuId.Value);

            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var search = filter.SearchTerm.Trim().ToLower();
                query = query.Where(wb =>
                    (wb.Sku != null && wb.Sku.SKU.ToLower().Contains(search)) ||
                    (wb.Sku != null && wb.Sku.Barcode != null && wb.Sku.Barcode.ToLower().Contains(search)) ||
                    (wb.Sku != null && wb.Sku.Product != null && wb.Sku.Product.Name.ToLower().Contains(search)));
            }

            var totalCount = await query.CountAsync(ct);

            var items = await query
                .OrderBy(wb => wb.Sku!.SKU)
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .Select(wb => new InventoryBalanceDto
                {
                    SkuId = wb.SkuId,
                    SkuCode = wb.Sku != null ? wb.Sku.SKU : string.Empty,
                    ProductName = wb.Sku != null && wb.Sku.Product != null ? wb.Sku.Product.Name : string.Empty,
                    Barcode = wb.Sku != null ? wb.Sku.Barcode : null,
                    BranchId = wb.BranchId,
                    BranchName = wb.Branch != null ? wb.Branch.Name : string.Empty,
                    WarehouseId = wb.WarehouseId,
                    WarehouseName = wb.Warehouse != null ? wb.Warehouse.Name : string.Empty,
                    OnHand = wb.OnHand,
                    Reserved = wb.Reserved,
                    AverageCost = 0 // WAC được theo dõi ở cấp Chi nhánh
                })
                .ToListAsync(ct);

            // Bổ sung WAC từ branch balances cho các SKU này (nếu có kết quả)
            if (items.Count > 0)
            {
                var skuIds = items.Select(i => i.SkuId).Distinct().ToList();
                var branchIds = items.Select(i => i.BranchId).Distinct().ToList();

                var branchWacs = await _context.InventoryBalances
                    .Where(b => branchIds.Contains(b.BranchId) && skuIds.Contains(b.SkuId))
                    .Select(b => new { b.BranchId, b.SkuId, b.AverageCost })
                    .ToListAsync(ct);

                var wacLookup = branchWacs.ToDictionary(b => (b.BranchId, b.SkuId), b => b.AverageCost);

                foreach (var item in items)
                {
                    if (wacLookup.TryGetValue((item.BranchId, item.SkuId), out var wac))
                    {
                        item.AverageCost = wac;
                    }
                }
            }

            return new PagedResult<InventoryBalanceDto>
            {
                Items = items,
                PageIndex = pageIndex,
                PageSize = pageSize,
                TotalCount = totalCount
            };
        }
        else
        {
            // Xem tổng tồn theo Chi nhánh (mặc định)
            var query = _context.InventoryBalances
                .Include(b => b.Branch)
                .Include(b => b.Sku)
                    .ThenInclude(s => s!.Product)
                .AsNoTracking();

            if (filter.BranchId.HasValue)
                query = query.Where(b => b.BranchId == filter.BranchId.Value);

            if (filter.SkuId.HasValue)
                query = query.Where(b => b.SkuId == filter.SkuId.Value);

            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                var search = filter.SearchTerm.Trim().ToLower();
                query = query.Where(b =>
                    (b.Sku != null && b.Sku.SKU.ToLower().Contains(search)) ||
                    (b.Sku != null && b.Sku.Barcode != null && b.Sku.Barcode.ToLower().Contains(search)) ||
                    (b.Sku != null && b.Sku.Product != null && b.Sku.Product.Name.ToLower().Contains(search)));
            }

            var totalCount = await query.CountAsync(ct);

            var items = await query
                .OrderBy(b => b.Sku!.SKU)
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .Select(b => new InventoryBalanceDto
                {
                    SkuId = b.SkuId,
                    SkuCode = b.Sku != null ? b.Sku.SKU : string.Empty,
                    ProductName = b.Sku != null && b.Sku.Product != null ? b.Sku.Product.Name : string.Empty,
                    Barcode = b.Sku != null ? b.Sku.Barcode : null,
                    BranchId = b.BranchId,
                    BranchName = b.Branch != null ? b.Branch.Name : string.Empty,
                    WarehouseId = null,
                    WarehouseName = "Tất cả các kho",
                    OnHand = b.OnHand,
                    Reserved = b.Reserved,
                    AverageCost = b.AverageCost
                })
                .ToListAsync(ct);

            return new PagedResult<InventoryBalanceDto>
            {
                Items = items,
                PageIndex = pageIndex,
                PageSize = pageSize,
                TotalCount = totalCount
            };
        }
    }

    public async Task<InventoryBalanceDto?> GetSkuBalanceAtBranchAsync(Guid branchId, Guid skuId, CancellationToken ct = default)
    {
        RequireTenantId();

        var balance = await _context.InventoryBalances
            .Include(b => b.Branch)
            .Include(b => b.Sku)
                .ThenInclude(s => s!.Product)
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.BranchId == branchId && b.SkuId == skuId, ct);

        if (balance == null) return null;

        return new InventoryBalanceDto
        {
            SkuId = balance.SkuId,
            SkuCode = balance.Sku?.SKU ?? string.Empty,
            ProductName = balance.Sku?.Product?.Name ?? string.Empty,
            Barcode = balance.Sku?.Barcode,
            BranchId = balance.BranchId,
            BranchName = balance.Branch?.Name ?? string.Empty,
            WarehouseId = null,
            WarehouseName = "Tất cả các kho",
            OnHand = balance.OnHand,
            Reserved = balance.Reserved,
            AverageCost = balance.AverageCost
        };
    }

    public async Task<PagedResult<InventoryLedgerDto>> GetLedgerAsync(InventoryLedgerFilterDto filter, CancellationToken ct = default)
    {
        RequireTenantId();

        var query = _context.InventoryTransactions
            .Include(t => t.Branch)
            .Include(t => t.Warehouse)
            .Include(t => t.Sku)
                .ThenInclude(s => s!.Product)
            .Include(t => t.ReceiptItem)
                .ThenInclude(ri => ri!.Receipt)
            .Include(t => t.OrderItem)
                .ThenInclude(oi => oi!.Order)
            .AsNoTracking();

        if (filter.BranchId.HasValue)
            query = query.Where(t => t.BranchId == filter.BranchId.Value);

        if (filter.WarehouseId.HasValue)
            query = query.Where(t => t.WarehouseId == filter.WarehouseId.Value);

        if (filter.SkuId.HasValue)
            query = query.Where(t => t.SkuId == filter.SkuId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Type))
            query = query.Where(t => t.Type == filter.Type);

        if (filter.FromDate.HasValue)
            query = query.Where(t => t.CreatedAt >= filter.FromDate.Value);

        if (filter.ToDate.HasValue)
            query = query.Where(t => t.CreatedAt <= filter.ToDate.Value);

        var totalCount = await query.CountAsync(ct);

        var pageIndex = Math.Max(1, filter.PageIndex);
        var pageSize = Math.Max(1, Math.Min(100, filter.PageSize));

        var transactions = await query
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.SequenceNo)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // Resolve source documents for this page only. Keep tenant query filters on every lookup.
        // IN/OUT describes the direction, not the business operation (a count/transfer can have either).
        var sources = new Dictionary<Guid, (string Type, Guid Id, string Number)>();
        var transferIds = transactions.Where(t => t.TransferItemId.HasValue).Select(t => t.TransferItemId!.Value).ToArray();
        if (transferIds.Length > 0)
        {
            var transfers = await _context.StockTransferItems.AsNoTracking()
                .Where(i => transferIds.Contains(i.Id))
                .Select(i => new { i.Id, i.TransferId, i.Transfer!.Number }).ToListAsync(ct);
            foreach (var item in transfers) sources[item.Id] = ("StockTransfer", item.TransferId, item.Number);
        }

        var adjustmentItemIds = transactions.Where(t => t.AdjustmentItemId.HasValue).Select(t => t.AdjustmentItemId!.Value).ToArray();
        if (adjustmentItemIds.Length > 0)
        {
            var adjustments = await _context.InventoryAdjustmentItems.AsNoTracking()
                .Where(i => adjustmentItemIds.Contains(i.Id))
                .Select(i => new { i.Id, i.AdjustmentId, i.Adjustment!.Number }).ToListAsync(ct);
            var adjustmentIds = adjustments.Select(i => i.AdjustmentId).Distinct().ToArray();
            var counts = await _context.StockCounts.AsNoTracking()
                .Where(c => c.GeneratedAdjustmentId.HasValue && adjustmentIds.Contains(c.GeneratedAdjustmentId.Value))
                .Select(c => new { c.Id, c.Number, c.GeneratedAdjustmentId }).ToListAsync(ct);
            var countsByAdjustment = counts.ToDictionary(c => c.GeneratedAdjustmentId!.Value);
            foreach (var item in adjustments)
            {
                sources[item.Id] = countsByAdjustment.TryGetValue(item.AdjustmentId, out var count)
                    ? ("StockCount", count.Id, count.Number)
                    : ("InventoryAdjustment", item.AdjustmentId, item.Number);
            }
        }

        var countItemIds = transactions.Where(t => t.StockCountItemId.HasValue).Select(t => t.StockCountItemId!.Value).ToArray();
        if (countItemIds.Length > 0)
        {
            var counts = await _context.StockCountItems.AsNoTracking()
                .Where(i => countItemIds.Contains(i.Id))
                .Select(i => new { i.Id, i.StockCountId, i.StockCount!.Number }).ToListAsync(ct);
            foreach (var item in counts) sources[item.Id] = ("StockCount", item.StockCountId, item.Number);
        }

        var items = transactions.Select(t =>
        {
            var sourceKey = t.TransferItemId ?? t.AdjustmentItemId ?? t.StockCountItemId ?? Guid.Empty;
            var hasSource = sources.TryGetValue(sourceKey, out var source);
            return new InventoryLedgerDto
        {
            Id = t.Id,
            BranchId = t.BranchId,
            BranchName = t.Branch?.Name ?? string.Empty,
            WarehouseId = t.WarehouseId,
            WarehouseName = t.Warehouse?.Name ?? string.Empty,
            SkuId = t.SkuId,
            SkuCode = t.Sku?.SKU ?? string.Empty,
            ProductName = t.Sku?.Product?.Name ?? string.Empty,
            Type = t.Type,
            Quantity = t.Quantity,
            UnitCost = t.UnitCost,
            BalanceAfter = t.BalanceAfter,
            SequenceNo = t.SequenceNo,
            ReceiptItemId = t.ReceiptItemId,
            DocumentType = hasSource ? source.Type : t.ReceiptItemId.HasValue ? "GoodsReceipt"
                : t.OrderItemId.HasValue ? "Order" : t.SalesReturnItemId.HasValue ? "SalesReturn" : "Unknown",
            DocumentId = hasSource ? source.Id : t.ReceiptItem?.ReceiptId ?? t.OrderItem?.OrderId,
            DocumentNumber = hasSource ? source.Number : t.ReceiptItem?.Receipt?.Number ?? t.OrderItem?.Order?.Number,
            ReferenceId = t.ReferenceId,
            CreatedAt = t.CreatedAt
        };
        }).ToList();

        return new PagedResult<InventoryLedgerDto>
        {
            Items = items,
            PageIndex = pageIndex,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }
}
