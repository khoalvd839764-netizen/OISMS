using System.Text.Json;

namespace OISM.Application.Common.Interfaces;

public interface ILlmClient
{
    Task<LlmResponse> ChatAsync(LlmRequest request, CancellationToken ct = default);
}

/// <summary>
/// Danh sách model AI đang cấu hình đủ key để người dùng chọn trong khung chat.
/// </summary>
public interface ILlmModelCatalog
{
    IReadOnlyList<AiModelOption> GetAvailableModels();
}

public sealed record AiModelOption(string Id, string Label, bool IsDefault);

/// <param name="Model">Mã model người dùng chọn (ví dụ "groq", "gemini"); null dùng model mặc định.</param>
public sealed record LlmRequest(
    string SystemPrompt,
    IReadOnlyList<LlmMessage> Messages,
    IReadOnlyList<LlmTool> Tools,
    string? Model = null);

public sealed record LlmMessage(
    string Role,
    string? Content,
    IReadOnlyList<LlmToolCall>? ToolCalls = null,
    string? ToolCallId = null);

public sealed record LlmTool(string Name, string Description, JsonElement Parameters);

/// <param name="ExtraContent">Dữ liệu riêng của nhà cung cấp gắn với lời gọi tool (Gemini: thought_signature),
/// phải gửi lại nguyên văn ở lượt kế tiếp.</param>
public sealed record LlmToolCall(string Id, string Name, string Arguments, JsonElement? ExtraContent = null);

public sealed record LlmTokenUsage(int PromptTokens, int CompletionTokens, int TotalTokens);

public sealed record LlmResponse(
    string? Text,
    IReadOnlyList<LlmToolCall> ToolCalls,
    string? FinishReason,
    LlmTokenUsage? Usage);
