using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Simulator.Dtos;

namespace OISM.Application.Features.Simulator.Services;

public interface ISimulatorService
{
    Task<OrderDetailDto> IngestWebhookOrderAsync(string channel, WebhookOrderPayloadDto payload, Guid? userId, CancellationToken ct = default);
    Task<SimulationBatchResultDto> RunConcurrencySimulationAsync(SimulationBatchRequestDto request, Guid? userId, CancellationToken ct = default);
}
