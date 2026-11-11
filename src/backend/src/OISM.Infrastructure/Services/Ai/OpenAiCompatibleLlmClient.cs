using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;

namespace OISM.Infrastructure.Services.Ai;

/// <summary>
/// Gọi endpoint chat/completions theo chuẩn OpenAI (Groq, Gemini, NVIDIA, OpenRouter…).
/// </summary>
public sealed class OpenAiCompatibleLlmClient(HttpClient httpClient, IOptionsMonitor<AiOptions> options) : ILlmClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Gọi nhà cung cấp chính khai báo ở các khóa Ai:BaseUrl, Ai:Model, Ai:ApiKey.</summary>
    public Task<LlmResponse> ChatAsync(LlmRequest request, CancellationToken ct = default)
    {
        var settings = options.CurrentValue;
        return ChatAsync(request, new AiProviderSettings(
            AiOptions.PrimaryProviderId, settings.Label, settings.BaseUrl, settings.Model, settings.ApiKey ?? string.Empty,
            settings.MaxTokens, settings.TimeoutSeconds), ct);
    }

    public async Task<LlmResponse> ChatAsync(LlmRequest request, AiProviderSettings provider, CancellationToken ct = default)
    {
        var messages = request.Messages.Prepend(new LlmMessage("system", request.SystemPrompt));
        var body = new
        {
            provider.Model,
            provider.MaxTokens,
            Messages = messages.Select(message => new
            {
                message.Role,
                message.Content,
                message.ToolCallId,
                ToolCalls = message.ToolCalls?.Select(call => new
                {
                    call.Id,
                    Type = "function",
                    Function = new { call.Name, call.Arguments },
                    // Gemini 3 bắt buộc gửi lại thought_signature của lời gọi tool, thiếu thì trả 400.
                    call.ExtraContent
                })
            }),
            Tools = request.Tools.Count == 0 ? null : request.Tools.Select(tool => new
            {
                Type = "function",
                Function = new { tool.Name, tool.Description, tool.Parameters }
            })
        };

        // Mỗi nhà cung cấp có thời gian chờ riêng (Gemini suy luận lâu hơn Groq).
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(provider.TimeoutSeconds));
        try
        {
            using var response = await SendWithShortRetryAsync(body, provider, timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                throw UpstreamError();
            }

            var result = await response.Content.ReadFromJsonAsync<Completion>(JsonOptions, timeout.Token);
            var choice = result?.Choices?.FirstOrDefault();
            var message = choice?.Message;
            if (message is null || (message.Content is null && message.ToolCalls is not { Length: > 0 }))
            {
                throw UpstreamError();
            }

            var calls = message.ToolCalls?.Select(ParseToolCall).ToArray() ?? [];
            return new LlmResponse(message.Content, calls, choice!.FinishReason, result!.Usage);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw UpstreamError();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or NotSupportedException)
        {
            throw UpstreamError();
        }
    }

    /// <summary>Nhà cung cấp chỉ yêu cầu chờ tối đa chừng này thì tự chờ rồi gửi lại một lần.</summary>
    internal static readonly TimeSpan MaxShortRetryDelay = TimeSpan.FromSeconds(10);

    private static readonly Regex RetryInSeconds = new(@"retry in (\d+(?:\.\d+)?)s|""retryDelay""\s*:\s*""(\d+(?:\.\d+)?)s""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// 429 của nhà cung cấp là hết lượt của model (theo phút hoặc ngày), khác giới hạn 10 câu/phút của OISMS.
    /// Lượt theo phút thường chỉ cần chờ vài giây; lượt theo ngày thì báo AI_MODEL_QUOTA để người dùng đổi model.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithShortRetryAsync(object body, AiProviderSettings provider, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{provider.BaseUrl.TrimEnd('/')}/chat/completions");
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
            httpRequest.Content = JsonContent.Create(body, options: JsonOptions);
            var response = await httpClient.SendAsync(httpRequest, ct);
            if (response.StatusCode != HttpStatusCode.TooManyRequests)
            {
                return response;
            }

            var delay = await ReadRetryDelayAsync(response, ct);
            response.Dispose();
            if (attempt == 0 && delay is { } wait && wait <= MaxShortRetryDelay)
            {
                await Task.Delay(wait + TimeSpan.FromMilliseconds(250), ct);
                continue;
            }

            throw new OismException("AI_MODEL_QUOTA",
                $"Model {provider.Label} đã hết lượt của nhà cung cấp. Chọn model khác hoặc thử lại sau.", 429);
        }
    }

    private static async Task<TimeSpan?> ReadRetryDelayAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return delta;
        }

        var text = await response.Content.ReadAsStringAsync(ct);
        var match = RetryInSeconds.Match(text);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    private static LlmToolCall ParseToolCall(ToolCall? call)
    {
        if (string.IsNullOrWhiteSpace(call?.Id) || string.IsNullOrWhiteSpace(call.Function?.Name)
            || call.Function.Arguments is null)
        {
            throw UpstreamError();
        }

        JsonElement? extra = call.ExtraContent is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } value
            ? value.Clone()
            : null;
        return new LlmToolCall(call.Id, call.Function.Name, call.Function.Arguments, extra);
    }

    private static OismException UpstreamError() =>
        new("AI_UPSTREAM_ERROR", "Trợ lý AI tạm thời không trả lời được.", 502);

    private sealed record Completion(Choice?[]? Choices, LlmTokenUsage? Usage);
    private sealed record Choice(CompletionMessage? Message, string? FinishReason);
    private sealed record CompletionMessage(string? Content, ToolCall?[]? ToolCalls);
    private sealed record ToolCall(string? Id, FunctionCall? Function, JsonElement? ExtraContent);
    private sealed record FunctionCall(string? Name, string? Arguments);
}
