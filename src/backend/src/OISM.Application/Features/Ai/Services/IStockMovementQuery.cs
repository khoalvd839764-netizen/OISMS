using OISM.Application.Features.Ai.Dtos;

namespace OISM.Application.Features.Ai.Services;

public interface IStockMovementQuery
{
    Task<StockMovementResult> GetAsync(AiChatDateRange range, Guid? branchId, CancellationToken ct = default);
}

public sealed record StockMovementResult(
    IReadOnlyList<StockMovementQuantity> Totals,
    IReadOnlyList<StockMovementDay> DailyByBranch,
    IReadOnlyList<StockMovementSku> TopOutboundSkus);

public sealed record StockMovementDay(DateOnly Date, string Branch, IReadOnlyList<StockMovementQuantity> Quantities);

public sealed record StockMovementQuantity(
    string BaseUnit, decimal In, decimal Out, decimal TransferIn, decimal TransferOut, decimal SalesReturnIn);

public sealed record StockMovementSku(string SkuCode, string SkuName, string BaseUnit, decimal Out);
