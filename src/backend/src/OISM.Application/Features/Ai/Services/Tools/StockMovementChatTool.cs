using System.Text.Json;
using OISM.Application.Common.Interfaces;

namespace OISM.Application.Features.Ai.Services.Tools;

public sealed class StockMovementChatTool(IStockMovementQuery query) : IAiChatTool
{
    public LlmTool Definition { get; } = new(
        "get_stock_movement",
        "Tổng lượng nhập và xuất trong kỳ theo đơn vị gốc, ngày và chi nhánh. Tổng đã bao gồm các dòng chuyển kho và trả hàng được ghi riêng. Có tối đa 10 SKU xuất nhiều nhất.",
        JsonSerializer.Deserialize<JsonElement>("""
            {
              "type":"object",
              "properties":{
                "branchName":{"type":"string","description":"Tên chi nhánh người dùng nhắc tới trong câu hỏi. Để trống nếu không nhắc."},
                "from":{"type":"string","format":"date","description":"Ngày bắt đầu, yyyy-MM-dd theo giờ Việt Nam, mặc định hôm nay."},
                "to":{"type":"string","format":"date","description":"Ngày cuối được tính, yyyy-MM-dd theo giờ Việt Nam, mặc định hôm nay."}
              },
              "additionalProperties":false
            }
            """));

    public AiChatToolArguments Normalize(JsonElement arguments, Guid? branchId, DateOnly today) =>
        new(branchId, DateRange: AiChatToolInput.DateRange(arguments, today));

    public async Task<AiChatToolResult> ExecuteAsync(AiChatToolArguments arguments, string role, CancellationToken ct)
    {
        var range = arguments.DateRange!;
        return new AiChatToolResult(await query.GetAsync(range, arguments.BranchId, ct), range);
    }
}
