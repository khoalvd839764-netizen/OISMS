using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OISM.Application;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Ai.Dtos;
using OISM.Application.Features.Ai.Services;
using OISM.Application.Features.Ai.Services.Tools;
using OISM.Infrastructure;
using OISM.Infrastructure.Ai;
using OISM.Infrastructure.Services.Ai;

if (Environment.GetEnvironmentVariable("AI_LIVE_EVAL") != "1" ||
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("Ai__ApiKey")))
{
    Console.WriteLine("NOT RUN (cần AI_LIVE_EVAL=1 và Ai__ApiKey trong môi trường).");
    return;
}

var suitePath = Path.Combine(AppContext.BaseDirectory, "chatbot-questions.json");
var suite = JsonSerializer.Deserialize<EvalSuite>(await File.ReadAllTextAsync(suitePath),
    new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
var config = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Ai:Enabled"] = "true",
        ["Database:Host"] = "unused.invalid"
    }).Build();
using var services = new ServiceCollection().AddHttpContextAccessor().AddInfrastructureServices(config).AddApplicationServices()
    .BuildServiceProvider();
using var scope = services.CreateScope();
var provider = scope.ServiceProvider;
var firstRound = new FirstRoundClient(provider.GetRequiredService<ILlmClient>());
var chat = new AiChatService(firstRound, new UserGuideProvider(), Options.Create(new AiChatLimits()),
    provider.GetRequiredService<AiChatToolRunner>(), new EvalClock(suite.ReferenceDate));
var tools = provider.GetServices<IAiChatTool>().ToDictionary(tool => tool.Definition.Name);

var toolCorrect = 0;
var dateCorrect = 0;
foreach (var test in suite.Questions)
{
    try
    {
        await chat.ChatAsync(new AiChatRequest([new("user", test.Question)]), "Owner");
        var actual = firstRound.Calls;
        var toolMatch = test.ExpectedTool is null
            ? actual.Count == 0
            : actual.Count == 1 && actual[0].Name == test.ExpectedTool;
        toolCorrect += toolMatch ? 1 : 0;

        var ranges = actual.Where(call => tools.ContainsKey(call.Name))
            .Select(call => tools[call.Name].Normalize(ParseArguments(call.Arguments), null, suite.ReferenceDate).DateRange)
            .OfType<AiChatDateRange>().ToArray();
        var dateMatch = test.From is null && test.To is null
            ? ranges.Length == 0
            : ranges.Length == 1 && ranges[0].From == test.From && ranges[0].To == test.To;
        dateCorrect += dateMatch ? 1 : 0;
        Console.WriteLine($"#{test.Id} tool={(toolMatch ? "PASS" : "FAIL")} ngày={(dateMatch ? "PASS" : "FAIL")}");
    }
    catch (OismException ex)
    {
        Console.WriteLine($"#{test.Id} FAIL {ex.ErrorCode}");
    }
    catch (JsonException)
    {
        Console.WriteLine($"#{test.Id} FAIL tham số công cụ sai định dạng.");
    }
}

var model = provider.GetRequiredService<IOptionsMonitor<AiOptions>>().CurrentValue.Model;
Console.WriteLine($"Model {model} | Đúng tool {toolCorrect}/{suite.Questions.Length} ({(double)toolCorrect / suite.Questions.Length:P0})"
    + $" | Đúng ngày {dateCorrect}/{suite.Questions.Length} ({(double)dateCorrect / suite.Questions.Length:P0})");

static JsonElement ParseArguments(string json)
{
    var arguments = JsonSerializer.Deserialize<JsonElement>(json);
    return arguments.ValueKind == JsonValueKind.Object ? arguments : throw new JsonException();
}

internal sealed record EvalSuite(DateOnly ReferenceDate, EvalQuestion[] Questions);
internal sealed record EvalQuestion(int Id, string Question, string? ExpectedTool, DateOnly? From, DateOnly? To);

internal sealed class EvalClock(DateOnly today) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(today.ToDateTime(new TimeOnly(5, 0), DateTimeKind.Utc));
}

internal sealed class FirstRoundClient(ILlmClient provider) : ILlmClient
{
    public IReadOnlyList<LlmToolCall> Calls { get; private set; } = [];

    public async Task<LlmResponse> ChatAsync(LlmRequest request, CancellationToken ct = default)
    {
        var response = await provider.ChatAsync(request, ct);
        Calls = response.ToolCalls;
        return response with { ToolCalls = [] };
    }
}
