using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.PurchaseOrders.Dtos;

namespace OISM.Application.Features.PurchaseOrders.Services;

public interface IPurchaseOrderService
{
    Task<PurchaseOrderResponseDto> CreateAsync(CreatePurchaseOrderDto dto, Guid requestKey, Guid userId, CancellationToken ct = default, bool isOwner = true);
    Task<PurchaseOrderResponseDto> UpdateDraftAsync(Guid id, UpdatePurchaseOrderDraftDto dto, Guid userId, CancellationToken ct = default, bool isOwner = true);
    Task<PurchaseOrderResponseDto> SubmitAsync(Guid id, Guid userId, CancellationToken ct = default, bool isOwner = false);
    Task<PurchaseOrderResponseDto> ReturnAsync(Guid id, string? reason, Guid userId, CancellationToken ct = default);
    Task<PurchaseOrderResponseDto> ApproveAsync(Guid id, bool confirmZeroPrice, Guid userId, CancellationToken ct = default, int? version = null, bool confirmLossPrice = false);
    Task<PurchaseOrderResponseDto> UpdateSalePriceConfigsAsync(Guid id, UpdatePurchaseOrderSalePriceConfigsDto dto, Guid userId, CancellationToken ct = default);
    Task<PurchaseOrderResponseDto> CloseAsync(Guid id, string? reason, Guid userId, CancellationToken ct = default);
    Task<PurchaseOrderResponseDto> CancelAsync(Guid id, Guid userId, CancellationToken ct = default, bool isOwner = true);
    Task<PagedResult<PurchaseOrderResponseDto>> GetPurchaseOrdersAsync(PurchaseOrderFilterDto filter, CancellationToken ct = default, bool isOwner = true);
    Task<PurchaseOrderResponseDto?> GetPurchaseOrderByIdAsync(Guid id, CancellationToken ct = default, bool isOwner = true);
    Task<PurchaseOrderPriceSuggestionDto> GetPriceSuggestionAsync(PurchaseOrderPriceSuggestionQueryDto query, CancellationToken ct = default);
}
