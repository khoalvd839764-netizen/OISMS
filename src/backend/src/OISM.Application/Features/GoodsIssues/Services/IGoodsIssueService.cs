using OISM.Application.Features.GoodsIssues.Dtos;
using OISM.Application.Features.Inventory.Dtos;

namespace OISM.Application.Features.GoodsIssues.Services;

public interface IGoodsIssueService
{
    Task<PagedResult<GoodsIssueDto>> GetGoodsIssuesAsync(GoodsIssueFilterDto filter, bool? canViewCost = null, CancellationToken ct = default);
    Task<GoodsIssueDto> GetByIdAsync(Guid id, bool? canViewCost = null, CancellationToken ct = default);
    Task<List<GoodsIssueDto>> GetByOrderIdAsync(Guid orderId, bool? canViewCost = null, CancellationToken ct = default);
    Task<List<GoodsIssueDto>> GetByOrderIdsAsync(IReadOnlyCollection<Guid> orderIds, bool? canViewCost = null, CancellationToken ct = default);
    Task<PosDailyGoodsIssueSummaryDto> GetPosDailySummaryAsync(PosDailyGoodsIssueQueryDto query, bool? canViewCost = null, CancellationToken ct = default);
}
