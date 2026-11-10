using OISM.Application.Features.SalesReturns.Dtos;

namespace OISM.Application.Features.SalesReturns.Services;

public interface ISalesReturnService
{
    Task<List<ReturnableItemDto>> GetReturnableItemsAsync(Guid orderId, CancellationToken ct = default);
    Task<(SalesReturnResponseDto Dto, bool IsReplay)> CreateAsync(CreateSalesReturnDto dto, Guid requestKey, Guid userId, CancellationToken ct = default);
    Task<SalesReturnResponseDto> ConfirmAsync(Guid id, string? refundMethod, Guid userId, CancellationToken ct = default);
    Task<SalesReturnResponseDto> CancelAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<PagedResult<SalesReturnResponseDto>> GetListAsync(SalesReturnFilterDto filter, CancellationToken ct = default);
    Task<SalesReturnResponseDto> GetByIdAsync(Guid id, CancellationToken ct = default);
}
