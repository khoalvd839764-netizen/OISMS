namespace OISM.Application.Features.Ai.Dtos;

public sealed record AiChatRequest(
    IReadOnlyList<AiChatMessageDto?>? Messages, Guid? BranchId = null, bool ConfirmBranchChoice = false,
    string? Model = null);
public sealed record AiChatMessageDto(string? Role, string? Content);
public sealed record AiChatDateRange(DateOnly From, DateOnly To);
public sealed record AiChatBranchChoice(Guid Id, string Name);
public sealed record AiChatSource(string Id, string Title);
public sealed record AiChatResponse(
    string Reply, IReadOnlyList<string> UsedTools, AiChatDateRange? DateRange,
    string? Scope, IReadOnlyList<AiChatBranchChoice> BranchChoices, string? Model = null,
    IReadOnlyList<AiChatSource>? Sources = null);
