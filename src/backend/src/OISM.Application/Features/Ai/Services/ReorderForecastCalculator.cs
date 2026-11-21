using System.Globalization;

namespace OISM.Application.Features.Ai.Services;

public sealed record ReorderExportPoint(DateOnly Date, decimal Quantity);

public sealed record ReorderForecastResult(
    decimal DailyVelocity,
    int ActiveDays,
    int DaysUntilStockout,
    bool NeedsReorder,
    decimal SuggestedQuantity,
    string ConfidenceLevel,
    string Reason);

/// <summary>
/// Hàm thuần tính dự báo nhu cầu nhập hàng theo cửa sổ 30 ngày tính đến mốc <c>asOf</c>.
/// Không phụ thuộc DbContext hay đồng hồ hệ thống để phục vụ cả API và backtest.
/// </summary>
public static class ReorderForecastCalculator
{
    public const string FormulaVersion = "reorder-v1";
    public const int VelocityWindowDays = 30;
    public const int NoSalesDaysUntilStockout = 999;

    private static string FormatQuantity(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    public static ReorderForecastResult Calculate(
        IReadOnlyList<ReorderExportPoint> exportPoints,
        DateOnly asOf,
        decimal available,
        decimal reorderPoint,
        int leadTimeDays,
        int safetyStockDays,
        string unitLabel = "sp")
    {
        var windowStart = asOf.AddDays(-(VelocityWindowDays - 1));
        var windowPoints = exportPoints
            .Where(p => p.Date >= windowStart && p.Date <= asOf)
            .ToList();

        var activeDays = windowPoints.Select(p => p.Date).Distinct().Count();
        var totalWindowQty = windowPoints.Sum(p => p.Quantity);
        var dailyVelocity = Math.Round(totalWindowQty / VelocityWindowDays, 2);

        int daysUntilStockout;
        if (available <= 0)
        {
            daysUntilStockout = 0;
        }
        else if (dailyVelocity > 0)
        {
            daysUntilStockout = (int)Math.Floor(available / dailyVelocity);
        }
        else
        {
            daysUntilStockout = NoSalesDaysUntilStockout;
        }

        var horizonDays = leadTimeDays + safetyStockDays;
        var needsReorder = available <= reorderPoint || (dailyVelocity > 0 && daysUntilStockout <= horizonDays);

        decimal suggestedQuantity = 0;
        string confidenceLevel = activeDays >= 14
            ? "High"
            : activeDays >= 5
                ? "Medium"
                : "Low";

        var unit = string.IsNullOrWhiteSpace(unitLabel) ? "sp" : unitLabel;
        string reason;

        if (needsReorder)
        {
            var targetBuffer = Math.Max(reorderPoint, 5m);
            var expectedDemand = Math.Ceiling(dailyVelocity * horizonDays);
            var needed = (expectedDemand + targetBuffer) - available;
            suggestedQuantity = Math.Max(1, needed);

            if (available <= 0)
            {
                reason = $"Hết hàng khả dụng (Available = {FormatQuantity(available)}). Tốc độ bán {FormatQuantity(dailyVelocity)} {unit}/ngày. Đề xuất nhập ngay để tránh gián đoạn.";
            }
            else if (available <= reorderPoint)
            {
                reason = daysUntilStockout >= NoSalesDaysUntilStockout
                    ? $"Tồn khả dụng ({FormatQuantity(available)}) chạm hoặc dưới ngưỡng cảnh báo an toàn ({FormatQuantity(reorderPoint)}). Chưa có dữ liệu bán để ước tính ngày cạn kho."
                    : $"Tồn khả dụng ({FormatQuantity(available)}) chạm hoặc dưới ngưỡng cảnh báo an toàn ({FormatQuantity(reorderPoint)}). Dự kiến cạn kho trong {daysUntilStockout} ngày.";
            }
            else
            {
                reason = $"Tốc độ bán {FormatQuantity(dailyVelocity)} {unit}/ngày. Tồn hiện tại chỉ đủ dùng {daysUntilStockout} ngày, nhỏ hơn thời gian cung ứng ({horizonDays} ngày).";
            }

            if (confidenceLevel == "Low")
            {
                reason += " (Lưu ý: Dữ liệu lịch sử xuất bán còn ít, cần tích lũy thêm để tăng độ chính xác).";
            }
        }
        else
        {
            reason = daysUntilStockout >= NoSalesDaysUntilStockout
                ? $"Tồn kho an toàn. Khả dụng ({FormatQuantity(available)}) cao hơn ngưỡng ReorderPoint ({FormatQuantity(reorderPoint)}). Chưa có dữ liệu bán trong {VelocityWindowDays} ngày gần nhất."
                : $"Tồn kho an toàn. Khả dụng ({FormatQuantity(available)}) cao hơn ngưỡng ReorderPoint ({FormatQuantity(reorderPoint)}) và đủ đáp ứng nhu cầu dự kiến {daysUntilStockout} ngày tới.";
        }

        return new ReorderForecastResult(
            dailyVelocity,
            activeDays,
            daysUntilStockout,
            needsReorder,
            suggestedQuantity,
            confidenceLevel,
            reason);
    }
}
