using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Helpers;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Ai.Dtos;
using OISM.Application.Features.Ai.Services.Tools;

namespace OISM.Application.Features.Ai.Services;

public sealed class StockMovementQuery(IAppDbContext db, ICurrentTenantService tenant) : IStockMovementQuery
{
    public async Task<StockMovementResult> GetAsync(AiChatDateRange range, Guid? branchId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not { } tenantId || tenantId == Guid.Empty)
        {
            throw AiChatToolInput.Invalid("Không xác định được cửa hàng hiện tại.");
        }

        var start = ReportTimeHelper.StartOfDayUtc(range.From.ToDateTime(TimeOnly.MinValue));
        var end = ReportTimeHelper.StartOfDayUtc(range.To.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var query = db.InventoryTransactions.AsNoTracking()
            .Where(row => row.TenantId == tenantId && row.CreatedAt >= start && row.CreatedAt < end);
        if (branchId.HasValue)
        {
            query = query.Where(row => row.BranchId == branchId.Value);
        }

        var rows = await query.Select(row => new MovementRow(
            row.CreatedAt,
            row.BranchId,
            row.Branch == null ? string.Empty : row.Branch.Name,
            row.SkuId,
            row.Sku == null ? string.Empty : row.Sku.SKU,
            row.Sku == null ? string.Empty : row.Sku.VariantName,
            row.Sku == null ? "piece" : row.Sku.Unit,
            row.Type == "IN",
            row.TransferItemId.HasValue,
            row.SalesReturnItemId.HasValue,
            row.Quantity)).ToListAsync(ct);

        var daily = rows.GroupBy(row => new { Date = ReportTimeHelper.ToLocalDate(row.CreatedAt), row.BranchId, row.Branch })
            .OrderBy(group => group.Key.Date).ThenBy(group => group.Key.Branch)
            .Select(group => new StockMovementDay(group.Key.Date, group.Key.Branch, SumByUnit(group))).ToArray();

        var top = rows.Where(row => !row.IsIn)
            .GroupBy(row => new { row.SkuId, row.SkuCode, row.SkuName, row.BaseUnit })
            .Select(group => new StockMovementSku(group.Key.SkuCode, group.Key.SkuName, group.Key.BaseUnit, group.Sum(row => row.Quantity)))
            .OrderByDescending(item => item.Out).ThenBy(item => item.SkuCode)
            .Take(10).ToArray();

        return new StockMovementResult(SumByUnit(rows), daily, top);
    }

    private static StockMovementQuantity[] SumByUnit(IEnumerable<MovementRow> rows) => rows
        .GroupBy(row => row.BaseUnit)
        .OrderBy(group => group.Key)
        .Select(group => new StockMovementQuantity(
            group.Key,
            group.Sum(row => row.IsIn ? row.Quantity : 0),
            group.Sum(row => row.IsIn ? 0 : row.Quantity),
            group.Sum(row => row.IsTransfer && row.IsIn ? row.Quantity : 0),
            group.Sum(row => row.IsTransfer && !row.IsIn ? row.Quantity : 0),
            group.Sum(row => row.IsSalesReturn ? row.Quantity : 0)))
        .ToArray();

    private sealed record MovementRow(
        DateTimeOffset CreatedAt, Guid BranchId, string Branch, Guid SkuId, string SkuCode, string SkuName,
        string BaseUnit, bool IsIn, bool IsTransfer, bool IsSalesReturn, decimal Quantity);
}
