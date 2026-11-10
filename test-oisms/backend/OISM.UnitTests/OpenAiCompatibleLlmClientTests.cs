using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Infrastructure;
using OISM.Infrastructure.Services.Ai;

namespace OISM.UnitTests;

public sealed class OpenAiCompatibleLlmClientTests
{
    private static readonly LlmRequest Request = new("Quy tắc", [new("user", "Xin chào")], []);

    [Fact]
    public async Task ChatAsync_PostsSystemPromptAndBearer_ParsesTextAndUsage()
    {
        using var handler = new FakeHandler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://example.invalid/openai/v1/chat/completions", request.RequestUri?.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("unit-test-placeholder", request.Headers.Authorization?.Parameter);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("Quy tắc", body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
            Assert.Equal("user", body.RootElement.GetProperty("messages")[1].GetProperty("role").GetString());
            Assert.Equal(1024, body.RootElement.GetProperty("max_tokens").GetInt32());
            return Response(HttpStatusCode.OK, """
                {"choices":[{"message":{"content":"Xin chào"},"finish_reason":"stop"}],
                 "usage":{"prompt_tokens":2,"completion_tokens":3,"total_tokens":5}}
                """);
        });
        using var httpClient = new HttpClient(handler);
        var result = await CreateClient(httpClient).ChatAsync(Request);

        Assert.Equal("Xin chào", result.Text);
        Assert.Empty(result.ToolCalls);
        Assert.Equal("stop", result.FinishReason);
        Assert.Equal(new LlmTokenUsage(2, 3, 5), result.Usage);
    }

    [Fact]
    public async Task ChatAsync_ParsesToolCalls_AndSerializesToolHistory()
    {
        using var schema = JsonDocument.Parse("""{"type":"object"}""");
        var call = new LlmToolCall("old-call", "get_low_stock", "{}");
        var chat = Request with
        {
            Messages = [.. Request.Messages, new("assistant", null, [call]), new("tool", "{}", ToolCallId: call.Id)],
            Tools = [new("get_low_stock", "Tồn kho thấp", schema.RootElement)]
        };

        using var handler = new FakeHandler(async (request, ct) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var root = body.RootElement;
            Assert.Equal("get_low_stock", root.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
            Assert.Equal("old-call", root.GetProperty("messages")[2].GetProperty("tool_calls")[0].GetProperty("id").GetString());
            Assert.Equal("old-call", root.GetProperty("messages")[3].GetProperty("tool_call_id").GetString());
            return Response(HttpStatusCode.OK, """
                {"choices":[{"message":{"content":null,"tool_calls":[{"id":"call-1",
                 "type":"function","function":{"name":"get_low_stock","arguments":"{}"}}]},
                 "finish_reason":"tool_calls"}]}
                """);
        });
        using var httpClient = new HttpClient(handler);
        var result = await CreateClient(httpClient).ChatAsync(chat);

        Assert.Null(result.Text);
        Assert.Equal(new LlmToolCall("call-1", "get_low_stock", "{}"), Assert.Single(result.ToolCalls));
        Assert.Equal("tool_calls", result.FinishReason);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "upstream details", "AI_MODEL_QUOTA", 429)]
    [InlineData(HttpStatusCode.InternalServerError, "upstream details", "AI_UPSTREAM_ERROR", 502)]
    [InlineData(HttpStatusCode.OK, "upstream details", "AI_UPSTREAM_ERROR", 502)]
    [InlineData(HttpStatusCode.OK, """{"choices":[{}]}""", "AI_UPSTREAM_ERROR", 502)]
    public async Task ChatAsync_MapsBadResponses(HttpStatusCode status, string body, string errorCode, int errorStatus)
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(Response(status, body)));
        using var httpClient = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<OismException>(() => CreateClient(httpClient).ChatAsync(Request));

        Assert.Equal(errorCode, error.ErrorCode);
        Assert.Equal(errorStatus, error.StatusCode);
        Assert.DoesNotContain(body, error.Message);
    }

    [Theory]
    // Gemini Free Tier báo hết lượt theo phút trong thân lỗi; Groq dùng header Retry-After.
    [InlineData("""{"error":{"message":"Quota exceeded for metric ... per minute. Please retry in 0.4s."}}""", null)]
    [InlineData("""{"error":{"details":[{"retryDelay":"0s"}]}}""", null)]
    [InlineData("{}", 1)]
    public async Task ChatAsync_WaitsAndRetriesOnceWhenProviderAsksForShortDelay(string body, int? retryAfterSeconds)
    {
        var attempts = 0;
        using var handler = new FakeHandler((_, _) =>
        {
            attempts++;
            if (attempts == 1)
            {
                var limited = Response(HttpStatusCode.TooManyRequests, body);
                if (retryAfterSeconds is { } seconds)
                {
                    limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
                }
                return Task.FromResult(limited);
            }
            return Task.FromResult(Response(HttpStatusCode.OK, """{"choices":[{"message":{"content":"ok"},"finish_reason":"stop"}]}"""));
        });
        using var httpClient = new HttpClient(handler);

        var result = await CreateClient(httpClient).ChatAsync(Request);

        Assert.Equal("ok", result.Text);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task ChatAsync_ReportsModelQuotaWithoutWaitingForDailyLimit()
    {
        var attempts = 0;
        using var handler = new FakeHandler((_, _) =>
        {
            attempts++;
            return Task.FromResult(Response(HttpStatusCode.TooManyRequests,
                """{"error":{"message":"Requests per day limit exceeded on Free Tier. Please retry in 28m24.16s."}}"""));
        });
        using var httpClient = new HttpClient(handler);

        var started = DateTime.UtcNow;
        var error = await Assert.ThrowsAsync<OismException>(() => CreateClient(httpClient).ChatAsync(Request));

        Assert.Equal("AI_MODEL_QUOTA", error.ErrorCode);
        Assert.Equal(429, error.StatusCode);
        Assert.Contains("Groq", error.Message);
        Assert.DoesNotContain("Free Tier", error.Message);
        Assert.Equal(1, attempts);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ChatAsync_MapsTimeout()
    {
        using var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException();
        });
        using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(20) };

        var error = await Assert.ThrowsAsync<OismException>(() => CreateClient(httpClient).ChatAsync(Request));

        Assert.Equal("AI_UPSTREAM_ERROR", error.ErrorCode);
        Assert.Equal(502, error.StatusCode);
    }

    [Theory]
    [InlineData(false, "unit-test-placeholder", null, new string[0])]
    [InlineData(true, null, null, new string[0])]
    [InlineData(true, "  ", "  ", new string[0])]
    [InlineData(true, "unit-test-placeholder", null, new[] { "groq" })]
    [InlineData(true, null, "unit-test-placeholder", new[] { "gemini" })]
    [InlineData(true, "unit-test-placeholder", "unit-test-placeholder", new[] { "groq", "gemini" })]
    [InlineData(false, "unit-test-placeholder", "unit-test-placeholder", new string[0])]
    public async Task Infrastructure_ListsOnlyProvidersWithKey(bool enabled, string? groqKey, string? geminiKey, string[] expected)
    {
        using var services = Services(enabled, groqKey, geminiKey);
        var models = services.GetRequiredService<ILlmModelCatalog>().GetAvailableModels();

        Assert.Equal(expected, models.Select(model => model.Id));
        if (expected.Length > 0)
        {
            Assert.Equal(expected[0], Assert.Single(models, model => model.IsDefault).Id);
            Assert.IsType<LlmRouter>(services.GetRequiredService<ILlmClient>());
        }
        else
        {
            var error = await Assert.ThrowsAsync<OismException>(() => services.GetRequiredService<ILlmClient>().ChatAsync(Request));
            Assert.Equal("AI_DISABLED", error.ErrorCode);
            Assert.Equal(503, error.StatusCode);
        }
    }

    [Fact]
    public async Task Router_SendsChosenModelToItsProvider_AndRejectsUnknownModel()
    {
        var calls = new List<(string Url, string Key, string Model)>();
        using var handler = new FakeHandler(async (request, ct) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            calls.Add((request.RequestUri!.AbsoluteUri, request.Headers.Authorization!.Parameter!,
                body.RootElement.GetProperty("model").GetString()!));
            return Response(HttpStatusCode.OK, """{"choices":[{"message":{"content":"ok"},"finish_reason":"stop"}]}""");
        });
        using var httpClient = new HttpClient(handler);
        var options = new Mock<IOptionsMonitor<AiOptions>>();
        options.SetupGet(o => o.CurrentValue).Returns(new AiOptions
        {
            Enabled = true,
            ApiKey = "groq-placeholder",
            BaseUrl = "https://groq.invalid/v1",
            Model = "openai/gpt-oss-120b",
            DefaultProvider = "gemini",
            Providers = new(StringComparer.OrdinalIgnoreCase)
            {
                ["gemini"] = new() { Label = "Gemini", BaseUrl = "https://gemini.invalid/openai", Model = "gemini-3.5-flash", ApiKey = "gemini-placeholder" }
            }
        });
        var router = new LlmRouter(new OpenAiCompatibleLlmClient(httpClient, options.Object), options.Object);

        await router.ChatAsync(Request);
        await router.ChatAsync(Request with { Model = "groq" });
        await router.ChatAsync(Request with { Model = "GEMINI" });
        var error = await Assert.ThrowsAsync<OismException>(() => router.ChatAsync(Request with { Model = "openai" }));

        Assert.Equal(
        [
            ("https://gemini.invalid/openai/chat/completions", "gemini-placeholder", "gemini-3.5-flash"),
            ("https://groq.invalid/v1/chat/completions", "groq-placeholder", "openai/gpt-oss-120b"),
            ("https://gemini.invalid/openai/chat/completions", "gemini-placeholder", "gemini-3.5-flash")
        ], calls);
        Assert.Equal("AI_INVALID_REQUEST", error.ErrorCode);
        Assert.Equal(400, error.StatusCode);
        Assert.Equal("gemini", Assert.Single(router.GetAvailableModels(), model => model.IsDefault).Id);
    }

    [Fact]
    public async Task ChatAsync_ReturnsProviderExtraContentWithNextToolTurn()
    {
        // Gemini 3 trả thought_signature trong extra_content; thiếu ở lượt sau thì Gemini báo 400.
        var bodies = new List<JsonElement>();
        using var handler = new FakeHandler(async (request, ct) =>
        {
            bodies.Add(JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone());
            return Response(HttpStatusCode.OK, bodies.Count == 1
                ? """
                  {"choices":[{"message":{"tool_calls":[{"id":"call-1","type":"function",
                   "function":{"name":"get_revenue","arguments":"{}"},
                   "extra_content":{"google":{"thought_signature":"sig-123"}}}]},"finish_reason":"tool_calls"}]}
                  """
                : """{"choices":[{"message":{"content":"Doanh thu"},"finish_reason":"stop"}]}""");
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var first = await client.ChatAsync(Request);
        var call = Assert.Single(first.ToolCalls);
        Assert.Equal("sig-123", call.ExtraContent!.Value.GetProperty("google").GetProperty("thought_signature").GetString());
        await client.ChatAsync(Request with
        {
            Messages = [.. Request.Messages, new("assistant", null, first.ToolCalls), new("tool", "{}", ToolCallId: call.Id)]
        });

        var echoed = bodies[1].GetProperty("messages")[2].GetProperty("tool_calls")[0];
        Assert.Equal("sig-123", echoed.GetProperty("extra_content").GetProperty("google").GetProperty("thought_signature").GetString());
        Assert.False(bodies[0].GetProperty("messages")[1].TryGetProperty("tool_calls", out _));
    }

    private static ServiceProvider Services(bool enabled, string? groqKey, string? geminiKey)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Host"] = "unused",
            ["Ai:Enabled"] = enabled.ToString(),
            ["Ai:ApiKey"] = groqKey,
            ["Ai:Providers:gemini:BaseUrl"] = "https://gemini.invalid/openai",
            ["Ai:Providers:gemini:Model"] = "gemini-3.5-flash",
            ["Ai:Providers:gemini:ApiKey"] = geminiKey
        }).Build();
        return new ServiceCollection().AddInfrastructureServices(configuration).BuildServiceProvider();
    }

    private static OpenAiCompatibleLlmClient CreateClient(HttpClient httpClient)
    {
        var options = new Mock<IOptionsMonitor<AiOptions>>();
        options.SetupGet(o => o.CurrentValue).Returns(new AiOptions
        {
            ApiKey = "unit-test-placeholder",
            BaseUrl = "https://example.invalid/openai/v1/"
        });
        return new OpenAiCompatibleLlmClient(httpClient, options.Object);
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json) };

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            send(request, ct);
    }
}
