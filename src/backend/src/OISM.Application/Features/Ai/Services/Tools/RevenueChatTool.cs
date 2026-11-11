using System.Text.Json;
using OISM.Application.Common.Helpers;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Reports.Dtos;
using OISM.Application.Features.Reports.Services;

namespace OISM.Application.Features.Ai.Services.Tools;

public sealed class RevenueChatTool(IReportService reports) : IAiChatTool
{
    public LlmTool Definition { get; } = new(
        "get_revenue",
        "Báo cáo doanh thu thuần sau trả hàng và số đơn theo kỳ, theo ngày và theo kênh. Chỉ Owner có lãi gộp.",
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
        var isOwner = role == "Owner";
        var report = await reports.GetRevenueProfitReportAsync(new ReportFilterDto
        {
            BranchId = arguments.BranchId,
            StartDate = ReportTimeHelper.StartOfDayUtc(range.From.ToDateTime(TimeOnly.MinValue)),
            EndDate = ReportTimeHelper.StartOfDayUtc(range.To.AddDays(1).ToDateTime(TimeOnly.MinValue))
        }, isOwner, ct);
        var byChannel = report.ByChannel.Select(row => new ChannelRevenue(row.Channel, row.NetRevenue, row.OrderCount)).ToArray();
        var byDay = report.DailyTrend.Select(row => new DailyRevenue(row.Date, row.NetRevenue, row.OrderCount)).ToArray();

        RevenueData data = isOwner
            ? new OwnerRevenueData(report.NetRevenue, report.TotalOrders, byChannel, byDay, report.GrossProfit)
            : new RevenueData(report.NetRevenue, report.TotalOrders, byChannel, byDay);
        return new AiChatToolResult(data, range);
    }

    private sealed record ChannelRevenue(string Channel, decimal NetRevenue, int OrderCount);
    private sealed record DailyRevenue(string Date, decimal NetRevenue, int OrderCount);
    private record RevenueData(decimal NetRevenue, int OrderCount, ChannelRevenue[] ByChannel, DailyRevenue[] ByDay);
    private sealed record OwnerRevenueData(
        decimal NetRevenue, int OrderCount, ChannelRevenue[] ByChannel, DailyRevenue[] ByDay, decimal? GrossProfit)
        : RevenueData(NetRevenue, OrderCount, ByChannel, ByDay);
}
