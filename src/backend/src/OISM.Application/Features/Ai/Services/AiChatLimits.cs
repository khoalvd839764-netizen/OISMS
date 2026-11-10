namespace OISM.Application.Features.Ai.Services;

public sealed class AiChatLimits
{
    public int MaxHistoryMessages { get; set; } = 10;
    public int MaxToolRounds { get; set; } = 3;
}
