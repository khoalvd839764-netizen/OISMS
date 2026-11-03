namespace OISM.Application.Features.StockCounts.Services;

/// <summary>
/// Bộ tính toán kỳ kiểm kê thuần túy theo múi giờ cửa hàng (+07:00).
/// Hỗ trợ chính xác các tháng 28, 29, 30, 31 ngày (OISMS-47-U03).
/// </summary>
public static class StockCountPeriodCalculator
{
    private static readonly TimeSpan StoreOffset = TimeSpan.FromHours(7);

    public static (string PeriodKey, DateTimeOffset ScheduledAtUtc, DateTimeOffset DueAtUtc) Compute(
        string periodType,
        short? dayOfMonth,
        TimeOnly localTime,
        short dueDays,
        int year,
        int month)
    {
        string periodKey;
        int day;

        if (string.Equals(periodType, "MidMonth", StringComparison.OrdinalIgnoreCase))
        {
            day = dayOfMonth.HasValue && dayOfMonth.Value >= 1 && dayOfMonth.Value <= 28
                ? dayOfMonth.Value
                : 15;
            periodKey = $"{year:D4}-{month:D2}-MID";
        }
        else if (string.Equals(periodType, "EndOfMonth", StringComparison.OrdinalIgnoreCase))
        {
            day = DateTime.DaysInMonth(year, month);
            periodKey = $"{year:D4}-{month:D2}-END";
        }
        else
        {
            throw new ArgumentException($"Loại kỳ không hợp lệ: '{periodType}'. Chỉ hỗ trợ 'MidMonth' hoặc 'EndOfMonth'.", nameof(periodType));
        }

        var localDateTimeOffset = new DateTimeOffset(year, month, day, localTime.Hour, localTime.Minute, localTime.Second, StoreOffset);
        var scheduledAtUtc = localDateTimeOffset.ToUniversalTime();
        var dueAtUtc = scheduledAtUtc.AddDays(dueDays);

        return (periodKey, scheduledAtUtc, dueAtUtc);
    }
}
