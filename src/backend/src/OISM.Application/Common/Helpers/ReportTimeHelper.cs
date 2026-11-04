namespace OISM.Application.Common.Helpers;

/// <summary>
/// Quy đổi thời gian cho báo cáo theo múi giờ Asia/Saigon (UTC+7).
/// </summary>
public static class ReportTimeHelper
{
    private static readonly TimeSpan SaigonOffset = TimeSpan.FromHours(7);

    /// <summary>
    /// Ngày hiện tại theo giờ Việt Nam.
    /// </summary>
    public static DateTime Today() => DateTimeOffset.UtcNow.ToOffset(SaigonOffset).Date;

    /// <summary>
    /// Thời điểm 00:00 giờ Việt Nam của một ngày, trả về dạng UTC để truy vấn cột timestamptz.
    /// </summary>
    public static DateTimeOffset StartOfDayUtc(DateTime localDate) =>
        new DateTimeOffset(DateTime.SpecifyKind(localDate.Date, DateTimeKind.Unspecified), SaigonOffset).ToUniversalTime();

    /// <summary>
    /// Ngày theo giờ Việt Nam của một thời điểm.
    /// </summary>
    public static DateOnly ToLocalDate(DateTimeOffset value) =>
        DateOnly.FromDateTime(value.ToOffset(SaigonOffset).DateTime);
}
