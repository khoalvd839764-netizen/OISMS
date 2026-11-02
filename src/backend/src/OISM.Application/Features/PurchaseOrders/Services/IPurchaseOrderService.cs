using OISM.Application.Features.PurchaseOrders.Dtos;

namespace OISM.Application.Features.PurchaseOrders.Services;

public interface IPurchaseOrderService
{
    Task<List<PurchaseOrderDto>> GetPurchaseOrdersAsync(CancellationToken ct = default);
    Task<PurchaseOrderDto?> GetPurchaseOrderByIdAsync(Guid id, CancellationToken ct = default);
    Task<PurchaseOrderDto> CreatePurchaseOrderAsync(CreatePurchaseOrderDto dto, CancellationToken ct = default);
    Task<PurchaseOrderDto?> UpdateStatusAsync(Guid id, UpdatePurchaseOrderStatusDto dto, CancellationToken ct = default);
}
