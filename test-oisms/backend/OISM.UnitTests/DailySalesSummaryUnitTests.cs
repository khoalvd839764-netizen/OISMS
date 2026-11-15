using System.Security;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Reports.Dtos;
using OISM.Application.Features.Reports.Services;
using OISM.Domain.Entities;
using OISM.Infrastructure.Jobs;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.UnitTests;

public class DailySalesSummaryUnitTests
{
    private sealed class MutableTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    [Fact]
    public async Task RebuildRangeAsync_ShouldRejectInvalidAndOver31DaysRanges()
    {
        var (context, tenantMock, _) = TestDbContextFactory.CreateInMemoryContext();
        var reportService = new Mock<IReportService>(MockBehavior.Strict);
        var service = new DailySalesSummaryService(context, tenantMock.Object, reportService.Object);

        // Thiếu fromDate hoặc toDate -> 400 VALIDATION_ERROR
        var missingAct = () => service.RebuildRangeAsync(null, new DateOnly(2026, 6, 10));
        var missingEx = await missingAct.Should().ThrowAsync<OismException>();
        missingEx.Which.StatusCode.Should().Be(400);
        missingEx.Which.ErrorCode.Should().Be("VALIDATION_ERROR");

        // fromDate > toDate -> 400 VALIDATION_ERROR
        var invertedAct = () => service.RebuildRangeAsync(new DateOnly(2026, 6, 11), new DateOnly(2026, 6, 10));
        var invertedEx = await invertedAct.Should().ThrowAsync<OismException>();
        invertedEx.Which.StatusCode.Should().Be(400);
        invertedEx.Which.ErrorCode.Should().Be("VALIDATION_ERROR");

        // > 31 ngày (32 ngày tính bao gồm cả 2 đầu) -> 400 VALIDATION_ERROR
        var overLimitAct = () => service.RebuildRangeAsync(new DateOnly(2026, 5, 1), new DateOnly(2026, 6, 1));
        var overEx = await overLimitAct.Should().ThrowAsync<OismException>();
        overEx.Which.StatusCode.Should().Be(400);
        overEx.Which.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task RebuildAsync_ShouldRejectCrossTenantContext()
    {
        var (context, tenantMock, tenantA) = TestDbContextFactory.CreateInMemoryContext();
        var tenantB = Guid.NewGuid();
        tenantMock.Setup(t => t.TenantId).Returns(tenantA);

        var reportService = new Mock<IReportService>(MockBehavior.Strict);
        var service = new DailySalesSummaryService(context, tenantMock.Object, reportService.Object);

        var act = () => service.RebuildAsync(tenantB, new DateOnly(2026, 6, 10));
        await act.Should().ThrowAsync<SecurityException>();
    }

    [Fact]
    public async Task DailySalesSummaryJob_ShouldUseVietnamYesterdayWindow_SkipInactiveTenants_AndIsolateTenantErrors()
    {
        var activeTenant1 = Guid.NewGuid();
        var activeTenant2 = Guid.NewGuid();
        var inactiveTenant = Guid.NewGuid();

        var dbName = $"daily-summary-job-{Guid.NewGuid()}";
        var rebuiltCalls = new List<(Guid TenantId, DateOnly Date)>();

        var mockSummaryService = new Mock<IDailySalesSummaryService>();
        mockSummaryService
            .Setup(s => s.RebuildAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Returns((Guid tid, DateOnly d, CancellationToken _) =>
            {
                rebuiltCalls.Add((tid, d));
                if (tid == activeTenant1 && d == new DateOnly(2026, 6, 9))
                {
                    throw new InvalidOperationException("Simulated tenant 1 transient failure");
                }
                return Task.FromResult(2);
            });

        var services = new ServiceCollection();
        services.AddScoped<ICurrentTenantService, MutableTenantService>();
        services.AddDbContext<AppDbContext>((sp, opts) =>
        {
            opts.UseInMemoryDatabase(dbName);
        });
        services.AddScoped(_ => mockSummaryService.Object);

        await using var provider = services.BuildServiceProvider();

        // Seed tenants vào DbContext InMemory (bypass filter không cần vì InMemory theo cùng dbName)
        using (var seedScope = provider.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Tenants.AddRange(
                new Tenant { Id = activeTenant1, Name = "T1", Code = "T1", IsActive = true },
                new Tenant { Id = activeTenant2, Name = "T2", Code = "T2", IsActive = true },
                new Tenant { Id = inactiveTenant, Name = "T3", Code = "T3", IsActive = false });
            await db.SaveChangesAsync();
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Reports:SummaryRebuildDays"] = "7"
            })
            .Build();

        var job = new DailySalesSummaryJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            config,
            NullLogger<DailySalesSummaryJob>.Instance);

        // 2026-06-10 17:30:00 UTC == 2026-06-11 00:30:00 UTC+07
        // Hôm qua theo UTC+07 là 2026-06-10; cửa sổ 7 ngày là 2026-06-04 .. 2026-06-10.
        var nowUtc = new DateTimeOffset(2026, 6, 10, 17, 30, 0, TimeSpan.Zero);

        var act = () => job.ExecuteAsync(nowUtc);
        var ex = await act.Should().ThrowAsync<AggregateException>();
        ex.Which.InnerExceptions.Should().ContainSingle(e => e.Message.Contains("Simulated tenant 1"));

        // Tenant inactive bị bỏ qua
        rebuiltCalls.Should().NotContain(c => c.TenantId == inactiveTenant);

        // Tenant 2 VẪN chạy đủ 7 ngày 2026-06-04 .. 2026-06-10 dù Tenant 1 lỗi
        var t2Dates = rebuiltCalls.Where(c => c.TenantId == activeTenant2).Select(c => c.Date).ToList();
        t2Dates.Should().Equal(
            new DateOnly(2026, 6, 4),
            new DateOnly(2026, 6, 5),
            new DateOnly(2026, 6, 6),
            new DateOnly(2026, 6, 7),
            new DateOnly(2026, 6, 8),
            new DateOnly(2026, 6, 9),
            new DateOnly(2026, 6, 10));
    }

    [Fact]
    public void UseOismHangfire_ShouldRegisterDailySalesSummaryJob_WithCron30PastMidnight_AndVietnamTimezone()
    {
        var recorded = new List<(string Id, string Cron, TimeZoneInfo TimeZone)>();
        var recurringManagerMock = new Mock<IRecurringJobManager>();
        recurringManagerMock
            .Setup(m => m.AddOrUpdate(
                It.IsAny<string>(),
                It.IsAny<Job>(),
                It.IsAny<string>(),
                It.IsAny<RecurringJobOptions>()))
            .Callback<string, Job, string, RecurringJobOptions>((id, _, cron, opts) =>
            {
                recorded.Add((id, cron, opts.TimeZone));
            });

        var services = new ServiceCollection();
        services.AddSingleton(recurringManagerMock.Object);
        using var provider = services.BuildServiceProvider();

        var appBuilderMock = new Mock<IApplicationBuilder>();
        appBuilderMock.SetupGet(a => a.ApplicationServices).Returns(provider);

        appBuilderMock.Object.UseOismHangfire();

        recorded.Should().ContainSingle(r => r.Id == HangfireConfigurationExtensions.DailySalesSummaryJobId);
        var summaryJob = recorded.Single(r => r.Id == HangfireConfigurationExtensions.DailySalesSummaryJobId);
        summaryJob.Id.Should().Be("oisms-daily-sales-summary");
        summaryJob.Cron.Should().Be("30 0 * * *");
        summaryJob.TimeZone.BaseUtcOffset.Should().Be(TimeSpan.FromHours(7));
    }

    [Fact]
    public async Task GetComparisonAsync_WacCostWithSixDecimals_RoundedPerRow_ShouldStillBeMatched()
    {
        // Giá vốn WAC 6 chữ số lẻ: 3 dòng SKU × 33.333,333333 → bảng tổng hợp lưu 33.333,33 mỗi dòng (99.999,99)
        // trong khi báo cáo trực tiếp cộng chưa làm tròn (99.999,999999 → 100.000,00). Chênh do làm tròn, không phải lệch.
        var (context, tenantMock, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        var date = new DateOnly(2026, 11, 10);
        var branchId = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
        {
            context.DailySalesSummaries.Add(new DailySalesSummary
            {
                Id = Guid.NewGuid(), TenantId = tenantId, BusinessDate = date, BranchId = branchId,
                Channel = "POS", SkuId = Guid.NewGuid(), SoldQuantity = 1m,
                Revenue = 50_000m, CostOfGoodsSold = 33_333.33m, CreatedAt = DateTimeOffset.UtcNow
            });
        }
        await context.SaveChangesAsync();

        var reportService = new Mock<IReportService>();
        reportService
            .Setup(r => r.GetRevenueProfitReportAsync(It.IsAny<ReportFilterDto>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RevenueProfitReportDto
            {
                DailyTrend = new List<DailyRevenueBreakdownDto>
                {
                    new()
                    {
                        Date = "2026-11-10", Revenue = 150_000m, NetRevenue = 150_000m,
                        NetCogs = 99_999.999999m, GrossProfit = 50_000.000001m
                    }
                }
            });

        var service = new DailySalesSummaryService(context, tenantMock.Object, reportService.Object);
        var result = await service.GetComparisonAsync(date, date, null);

        result.Days.Should().ContainSingle();
        result.Days[0].Status.Should().Be("Matched");
        result.Days[0].Matched.Should().BeTrue();
    }
}
