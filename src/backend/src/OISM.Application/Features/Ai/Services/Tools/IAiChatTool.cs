using System.Globalization;
using System.Text.Json;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Ai.Dtos;

namespace OISM.Application.Features.Ai.Services.Tools;

public interface IAiChatTool
{
    LlmTool Definition { get; }

    AiChatToolArguments Normalize(JsonElement arguments, Guid? branchId, DateOnly today);

    Task<AiChatToolResult> ExecuteAsync(AiChatToolArguments arguments, string role, CancellationToken ct);
}

public sealed record AiChatToolArguments(
    Guid? BranchId, int? LeadTimeDays = null, AiChatDateRange? DateRange = null);

public sealed record AiChatToolResult(object Data, AiChatDateRange? DateRange = null);

internal static class AiChatToolInput
{
    public static JsonElement Parse(string json)
    {
        try
        {
            var value = JsonSerializer.Deserialize<JsonElement>(json);
            if (value.ValueKind == JsonValueKind.Object)
            {
                return value;
            }
        }
        catch (JsonException)
        {
            // Return a stable business error without including provider-supplied arguments.
        }

        throw Invalid("Tham số công cụ phải là một đối tượng JSON.");
    }

    public static string? BranchName(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("branchName", out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : throw Invalid("Tên chi nhánh không hợp lệ.");
    }

    public static AiChatDateRange DateRange(JsonElement arguments, DateOnly today)
    {
        var from = ReadDate(arguments, "from", today);
        var to = ReadDate(arguments, "to", today);
        if (from > to || to.DayNumber - from.DayNumber >= 92)
        {
            throw Invalid("Khoảng ngày phải theo thứ tự và tối đa 92 ngày.");
        }

        if (from == DateOnly.MinValue || to == DateOnly.MaxValue)
        {
            throw Invalid("Khoảng ngày vượt giới hạn tra cứu.");
        }

        return new AiChatDateRange(from, to);
    }

    private static DateOnly ReadDate(JsonElement arguments, string field, DateOnly today)
    {
        if (!arguments.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return today;
        }

        return value.ValueKind == JsonValueKind.String &&
            DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw Invalid("Ngày phải có định dạng yyyy-MM-dd.");
    }

    public static OismException Invalid(string detail) => new("AI_INVALID_REQUEST", detail, 400);
}
