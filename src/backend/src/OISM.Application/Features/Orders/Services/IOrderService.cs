using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Orders.Dtos;

namespace OISM.Application.Features.Orders.Services;

public interface IOrderService
{
    Task<PagedResult<OrderSummaryDto>> GetOrdersAsync(OrderFilterDto filter, CancellationToken ct = default);
    Task<OrderDetailDto?> GetOrderByIdAsync(Guid id, CancellationToken ct = default);
    Task<OrderDetailDto> CreateOrderAsync(CreateOrderDto dto, Guid? userId, CancellationToken ct = default);
    Task<OrderDetailDto> ConfirmOrderAsync(Guid id, Guid? userId, CancellationToken ct = default);
    Task<OrderDetailDto> ProcessOrderAsync(Guid id, Guid? userId, CancellationToken ct = default);
    Task<OrderDetailDto> CompleteOrderAsync(Guid id, Guid? userId, CancellationToken ct = default);
    Task<OrderDetailDto> CancelOrderAsync(Guid id, Guid? userId, CancellationToken ct = default);
}
