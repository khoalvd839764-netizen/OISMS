using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;

namespace OISM.Infrastructure.Services.Ai;

public sealed class DisabledLlmClient : ILlmClient
{
    public Task<LlmResponse> ChatAsync(LlmRequest request, CancellationToken ct = default) =>
        throw new OismException("AI_DISABLED", "Trợ lý AI chưa được bật.", 503);
}
