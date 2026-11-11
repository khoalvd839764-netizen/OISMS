using System.Text.Json;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Ai.Dtos;

namespace OISM.Application.Features.Ai.Services.Tools;

public sealed class ReorderSuggestionsChatTool(IAiForecastingService forecasting) : IAiChatTool
{
    public LlmTool Definition { get; } = new(
        "get_reorder_suggestions",
        "Đề xuất SKU cần nhập, số lượng nhập và lý do dựa trên dự báo tồn kho hiện tại.",
        JsonSerializer.Deserialize<JsonElement>("""
            {
              "type":"object",
              "properties":{
                "branchName":{"type":"string","description":"Tên chi nhánh người dùng nhắc tới trong câu hỏi. Để trống nếu không nhắc."},
                "leadTimeDays":{"type":"integer","minimum":1,"maximum":180,"description":"Thời gian cung ứng, mặc định 7 ngày."}
              },
              "additionalProperties":false
            }
            """));

    public AiChatToolArguments Normalize(JsonElement arguments, Guid? branchId, DateOnly today)
    {
        var leadTime = new ReorderRecommendationQueryDto().LeadTimeDays;
        if (arguments.TryGetProperty("leadTimeDays", out var value) && value.ValueKind != JsonValueKind.Null)
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out leadTime) || leadTime is < 1 or > 180)
            {
                throw AiChatToolInput.Invalid("Thời gian cung ứng phải từ 1 đến 180 ngày.");
            }
        }

        return new AiChatToolArguments(branchId, LeadTimeDays: leadTime);
    }

    public async Task<AiChatToolResult> ExecuteAsync(AiChatToolArguments arguments, string role, CancellationToken ct)
    {
        var result = await forecasting.GetReorderRecommendationsAsync(new ReorderRecommendationQueryDto
        {
            BranchId = arguments.BranchId,
            LeadTimeDays = arguments.LeadTimeDays!.Value,
            OnlyNeedsReorder = true
        }, ct);

        return new AiChatToolResult(result.Recommendations
            .OrderBy(item => item.DaysUntilStockout)
            .ThenBy(item => item.SkuCode, StringComparer.Ordinal)
            .Take(20)
            .Select(item => new
            {
                item.SkuCode,
                item.SkuName,
                item.BranchName,
                item.AvailableQuantity,
                item.DailySalesVelocity,
                item.DaysUntilStockout,
                item.SuggestedQuantity,
                item.ConfidenceLevel,
                item.Reason,
                item.BaseUnit
            }).ToArray());
    }
}
