using System.Text.Json;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Reports.Services;

namespace OISM.Application.Features.Ai.Services.Tools;

public sealed class LowStockChatTool(IReportService reports) : IAiChatTool
{
    public LlmTool Definition { get; } = new(
        "get_low_stock",
        "Liệt kê SKU chạm hoặc dưới ngưỡng tồn kho an toàn và tồn khả dụng theo chi nhánh.",
        JsonSerializer.Deserialize<JsonElement>("""
            {"type":"object","properties":{"branchName":{"type":"string","description":"Tên chi nhánh người dùng nhắc tới trong câu hỏi. Để trống nếu không nhắc."}},"additionalProperties":false}
            """));

    public AiChatToolArguments Normalize(JsonElement arguments, Guid? branchId, DateOnly today) => new(branchId);

    public async Task<AiChatToolResult> ExecuteAsync(AiChatToolArguments arguments, string role, CancellationToken ct)
    {
        var alerts = await reports.GetStockAlertsAsync(arguments.BranchId, ct);
        return new AiChatToolResult(alerts.Select(alert => new
        {
            alert.SkuCode,
            alert.SkuName,
            alert.BranchName,
            alert.Available,
            alert.ReorderPoint,
            alert.AlertLevel
        }).ToArray());
    }
}
