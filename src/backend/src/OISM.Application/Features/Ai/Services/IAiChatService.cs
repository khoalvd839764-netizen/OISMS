using OISM.Application.Features.Ai.Dtos;

namespace OISM.Application.Features.Ai.Services;

public interface IAiChatService
{
    Task<AiChatResponse> ChatAsync(AiChatRequest? request, string role, CancellationToken ct = default);
}
