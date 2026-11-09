using FluentAssertions;
using OISM.Application.Features.StockCounts.Services;
using Xunit;

namespace OISM.UnitTests;

public class StockCountPeriodCalculatorTests
{
    [Theory]
    [InlineData(2026, 2, 28)]
    [InlineData(2028, 2, 29)]
    [InlineData(2026, 4, 30)]
    [InlineData(2026, 12, 31)]
    public void EndOfMonth_LayDungNgayCuoiThang(int year, int month, int lastDay)
    {
        var (key, scheduledAt, _) = StockCountPeriodCalculator.Compute("EndOfMonth", null, new TimeOnly(8, 0), 3, year, month);

        key.Should().Be($"{year:D4}-{month:D2}-END");
        scheduledAt.ToOffset(TimeSpan.FromHours(7)).Should().Be(new DateTimeOffset(year, month, lastDay, 8, 0, 0, TimeSpan.FromHours(7)));
    }

    [Fact]
    public void MidMonth_QuyDoiGioCuaHangSangUtc_VaCongHan()
    {
        var (key, scheduledAt, dueAt) = StockCountPeriodCalculator.Compute("MidMonth", 15, new TimeOnly(1, 30), 3, 2026, 11);

        key.Should().Be("2026-11-MID");
        scheduledAt.Offset.Should().Be(TimeSpan.Zero);
        scheduledAt.Should().Be(new DateTimeOffset(2026, 11, 14, 18, 30, 0, TimeSpan.Zero), "01:30 ngày 15 giờ +07:00 là 18:30 ngày 14 giờ UTC");
        dueAt.Should().Be(scheduledAt.AddDays(3));
    }

    [Fact]
    public void LoaiKyKhongHopLe_NemLoi()
    {
        var act = () => StockCountPeriodCalculator.Compute("AdHoc", null, new TimeOnly(8, 0), 3, 2026, 11);

        act.Should().Throw<ArgumentException>();
    }
}
