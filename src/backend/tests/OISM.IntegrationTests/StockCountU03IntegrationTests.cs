using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.StockCounts.Dtos;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Upgrade 03: lịch kiểm kê, sinh kỳ, bắt đầu đếm, nộp, duyệt, đếm lại. Chạy trên PostgreSQL thật.
/// </summary>
public partial class StockCountIntegrationTests
{
    private const string OwnerA = "owner@shopa.local";
    private const string StaffA = "staff@shopa.local";
    private const string OwnerB = "owner@shopb.local";

    private async Task<HttpClient> ClientForAsync(string email)
    {
        var token = await LoginAsAsync(email, PostgresTestEnvironment.AppPassword);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<StockCountScheduleResponseDto> CreateScheduleAsync(HttpClient owner, TestInfrastructure infra, string periodType = "EndOfMonth")
    {
        var res = await owner.PostAsJsonAsync("/api/stock-count-schedules", new CreateStockCountScheduleRequestDto
        {
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            PeriodType = periodType,
            AssigneeUserId = infra.UserId
        });
        res.IsSuccessStatusCode.Should().BeTrue(await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<StockCountScheduleResponseDto>(_jsonOptions))!;
    }

    private async Task<List<StockCountPeriodResponseDto>> GetPeriodsAsync(HttpClient client, string query = "")
    {
        var res = await client.GetAsync($"/api/stock-count-periods?pageSize=100{query}");
        res.IsSuccessStatusCode.Should().BeTrue(await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<PagedResultDto<StockCountPeriodResponseDto>>(_jsonOptions))!.Items.ToList();
    }

    private async Task<StockCountResponseDto> StartFirstPeriodAsync(HttpClient client)
    {
        var period = (await GetPeriodsAsync(client)).OrderBy(p => p.ScheduledAt).First();
        var res = await client.PostAsync($"/api/stock-count-periods/{period.Id}/start", null);
        res.IsSuccessStatusCode.Should().BeTrue(await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions))!;
    }

    private async Task<StockCountResponseDto> CreateAdHocAsync(HttpClient client, TestInfrastructure infra, params Guid[] skuIds)
    {
        var res = await client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = skuIds.ToList(),
            Reason = "Nghi thất thoát"
        });
        res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions))!;
    }

    private static async Task RecordAsync(HttpClient client, Guid countId, params (Guid SkuId, decimal Qty)[] rows)
    {
        var res = await client.PostAsJsonAsync($"/api/stock-counts/{countId}/record", new RecordStockCountBatchRequestDto
        {
            Items = rows.Select(r => new RecordStockCountItemRequestDto { SkuId = r.SkuId, CountedQuantity = r.Qty }).ToList()
        });
        res.IsSuccessStatusCode.Should().BeTrue(await res.Content.ReadAsStringAsync());
    }

    private async Task<(decimal OnHand, int Ledger, int Adjustments)> ReadStockAsync(TestInfrastructure infra, Guid skuId)
    {
        var (ctx, _) = CreatePostgresDbContext(infra.TenantId);
        await using (ctx)
        {
            var onHand = (await ctx.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == infra.WarehouseA1Id && w.SkuId == skuId)).OnHand;
            var ledger = await ctx.InventoryTransactions.CountAsync(t => t.SkuId == skuId);
            var adjustments = await ctx.InventoryAdjustments.CountAsync();
            return (onHand, ledger, adjustments);
        }
    }

    private async Task<StockCountResponseDto> GetCountAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<StockCountResponseDto>($"/api/stock-counts/{id}", _jsonOptions))!;

    // ---------------------------------------------------------------------
    // Lịch và sinh kỳ
    // ---------------------------------------------------------------------

    [Fact]
    public async Task U03_Schedule_TaoLich_SinhKy_TrungBiChan_StaffKhongTaoDuoc()
    {
        var infra = await SetupInfrastructureAsync();
        using var owner = await ClientForAsync(OwnerA);
        using var staff = await ClientForAsync(StaffA);

        var created = await CreateScheduleAsync(owner, infra, "MidMonth");
        created.DayOfMonth.Should().Be(15);
        created.DueDays.Should().Be(3);
        created.LocalTime.Should().Be(new TimeOnly(8, 0));

        var periods = await GetPeriodsAsync(staff);
        periods.Should().NotBeEmpty();
        periods.Should().OnlyContain(p => p.Status == "Planned" && p.PeriodKey.EndsWith("-MID"));

        var dup = await owner.PostAsJsonAsync("/api/stock-count-schedules", new CreateStockCountScheduleRequestDto
        {
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            PeriodType = "MidMonth",
            AssigneeUserId = infra.UserId
        });
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await dup.Content.ReadAsStringAsync()).Should().Contain("STOCK_COUNT_SCHEDULE_DUPLICATE");

        var staffCreate = await staff.PostAsJsonAsync("/api/stock-count-schedules", new CreateStockCountScheduleRequestDto
        {
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            PeriodType = "EndOfMonth",
            AssigneeUserId = infra.UserId
        });
        staffCreate.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var badCase = await owner.PostAsJsonAsync("/api/stock-count-schedules", new CreateStockCountScheduleRequestDto
        {
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            PeriodType = "endofmonth",
            AssigneeUserId = infra.UserId
        });
        badCase.StatusCode.Should().Be(HttpStatusCode.BadRequest, "giá trị sai chữ hoa thường không được lọt xuống ràng buộc DB");
    }

    [Fact]
    public async Task U03_Job_ChayLaiVaSongSong_KhongSinhKyTrung()
    {
        var infra = await SetupInfrastructureAsync();
        using var owner = await ClientForAsync(OwnerA);
        await CreateScheduleAsync(owner, infra);

        var now = DateTimeOffset.UtcNow.AddMonths(2);

        async Task<int> RunJobAsync()
        {
            using var scope = _factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IStockCountPeriodJob>().ExecuteAsync(now);
        }

        var parallel = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => RunJobAsync()));
        parallel.Sum().Should().BeGreaterThan(0, "tháng mới phải được sinh kỳ");
        (await RunJobAsync()).Should().Be(0, "chạy lại không được sinh thêm");

        var periods = await GetPeriodsAsync(owner);
        periods.Select(p => p.PeriodKey).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task U03_SuaVaTatLich_ChiAnhHuongKyTuongLaiChuaBatDau()
    {
        var infra = await SetupInfrastructureAsync();
        using var owner = await ClientForAsync(OwnerA);
        var schedule = await CreateScheduleAsync(owner, infra);

        var update = await owner.PutAsJsonAsync($"/api/stock-count-schedules/{schedule.Id}", new UpdateStockCountScheduleRequestDto { LocalTime = new TimeOnly(21, 15), DueDays = 5 });
        update.IsSuccessStatusCode.Should().BeTrue(await update.Content.ReadAsStringAsync());

        var periods = await GetPeriodsAsync(owner);
        periods.Should().OnlyContain(p => p.ScheduledAt.ToOffset(TimeSpan.FromHours(7)).Hour == 21 && p.DueAt == p.ScheduledAt.AddDays(5));

        var off = await owner.PutAsJsonAsync($"/api/stock-count-schedules/{schedule.Id}", new UpdateStockCountScheduleRequestDto { IsActive = false });
        off.IsSuccessStatusCode.Should().BeTrue(await off.Content.ReadAsStringAsync());
        (await GetPeriodsAsync(owner)).Should().OnlyContain(p => p.Status == "Cancelled");

        var on = await owner.PutAsJsonAsync($"/api/stock-count-schedules/{schedule.Id}", new UpdateStockCountScheduleRequestDto { IsActive = true });
        on.IsSuccessStatusCode.Should().BeTrue(await on.Content.ReadAsStringAsync());
        var revived = await GetPeriodsAsync(owner);
        revived.Should().OnlyContain(p => p.Status == "Planned");
        revived.Select(p => p.PeriodKey).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task U03_LocKy_ThoiGianCoMuiGio07_KhongLoi_VaQuaHanTinhKhiDoc()
    {
        var infra = await SetupInfrastructureAsync();
        using var owner = await ClientForAsync(OwnerA);
        await CreateScheduleAsync(owner, infra);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddYears(-1).ToOffset(TimeSpan.FromHours(7)).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddYears(1).ToOffset(TimeSpan.FromHours(7)).ToString("O"));
        (await GetPeriodsAsync(owner, $"&from={from}&to={to}")).Should().NotBeEmpty();

        (await GetPeriodsAsync(owner, "&overdue=true")).Should().BeEmpty();

        await using var admin = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString);
        await admin.OpenAsync();
        await using (var cmd = new NpgsqlCommand("UPDATE public.stock_count_periods SET scheduled_at = now() - interval '10 days', due_at = now() - interval '7 days'", admin))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        (await GetPeriodsAsync(owner, "&overdue=true")).Should().NotBeEmpty().And.OnlyContain(p => p.IsOverdue);
    }

    // ---------------------------------------------------------------------
    // Bắt đầu đếm
    // ---------------------------------------------------------------------

    [Fact]
    public async Task U03_Start_ChupSoDuLucBam_GoiLaiTraPhieuCu_HuyRoiStartLai()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);
        using var owner = await ClientForAsync(OwnerA);
        await CreateScheduleAsync(owner, infra);

        // Giao dịch giữa lúc sinh kỳ và lúc bắt đầu đếm không được gây lệch
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 5m, 50000m);

        var period = (await GetPeriodsAsync(owner)).OrderBy(p => p.ScheduledAt).First();
        var first = await owner.PostAsync($"/api/stock-count-periods/{period.Id}/start", null);
        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        var count = (await first.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions))!;
        count.CountType.Should().Be("EndOfMonth");
        count.PeriodId.Should().Be(period.Id);
        count.Items.Single(i => i.SkuId == infra.Sku1Id).ExpectedQuantity.Should().Be(15m);
        count.Items.Should().OnlyContain(i => i.SnapshotAt != null);

        var replay = await owner.PostAsync($"/api/stock-count-periods/{period.Id}/start", null);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayed = (await replay.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions))!;
        replayed.Id.Should().Be(count.Id);
        replayed.IsReplay.Should().BeTrue();

        (await GetPeriodsAsync(owner)).Single(p => p.Id == period.Id).Status.Should().Be("InProgress");

        var cancel = await owner.PostAsJsonAsync($"/api/stock-counts/{count.Id}/cancel", new CancelStockCountRequestDto { Reason = "Làm lại" });
        cancel.IsSuccessStatusCode.Should().BeTrue(await cancel.Content.ReadAsStringAsync());
        (await GetPeriodsAsync(owner)).Single(p => p.Id == period.Id).Status.Should().Be("Planned");

        var again = await owner.PostAsync($"/api/stock-count-periods/{period.Id}/start", null);
        again.StatusCode.Should().Be(HttpStatusCode.Created);
        (await again.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions))!.Id.Should().NotBe(count.Id);
    }

    [Fact]
    public async Task U03_Start_SongSong_ChiMotPhieu()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);
        using var owner = await ClientForAsync(OwnerA);
        await CreateScheduleAsync(owner, infra);
        var period = (await GetPeriodsAsync(owner)).OrderBy(p => p.ScheduledAt).First();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => owner.PostAsync($"/api/stock-count-periods/{period.Id}/start", null)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(9);

        var (ctx, _) = CreatePostgresDbContext(infra.TenantId);
        await using (ctx)
        {
            (await ctx.StockCounts.CountAsync(c => c.PeriodId == period.Id)).Should().Be(1);
        }
    }

    [Fact]
    public async Task U03_DotXuat_ThieuLyDo_Bi400_CoLyDoThiLuu()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);
        using var owner = await ClientForAsync(OwnerA);

        var missing = await owner.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchAId,
            WarehouseId = infra.WarehouseA1Id,
            SkuIds = new List<Guid> { infra.Sku1Id },
            Reason = "   "
        });
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var count = await CreateAdHocAsync(owner, infra, infra.Sku1Id);
        count.CountType.Should().Be("AdHoc");
        count.AdHocReason.Should().Be("Nghi thất thoát");
        count.PeriodId.Should().BeNull();
    }

    // ---------------------------------------------------------------------
    // Nộp và duyệt
    // ---------------------------------------------------------------------

    [Fact]
    public async Task U03_ChuaDuyetChuaGhiTon_DuyetXongGhiSoVaHoanTatKy()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);
        using var owner = await ClientForAsync(OwnerA);
        using var staff = await ClientForAsync(StaffA);
        await CreateScheduleAsync(owner, infra);

        var count = await StartFirstPeriodAsync(staff);
        await RecordAsync(staff, count.Id, (infra.Sku1Id, 7m));
        var before = await ReadStockAsync(infra, infra.Sku1Id);

        // Từ Counting chưa chốt được
        var early = await owner.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        early.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await early.Content.ReadAsStringAsync()).Should().Contain("INVALID_STATE_TRANSITION");

        var submit = await staff.PostAsync($"/api/stock-counts/{count.Id}/submit", null);
        submit.IsSuccessStatusCode.Should().BeTrue(await submit.Content.ReadAsStringAsync());
        var submitted = (await submit.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions))!;
        submitted.Status.Should().Be("PendingApproval");
        submitted.SubmittedAt.Should().NotBeNull();

        (await ReadStockAsync(infra, infra.Sku1Id)).Should().Be(before, "đã nộp nhưng chưa duyệt thì không được ghi tồn, sổ kho, phiếu điều chỉnh");

        // Đã nộp thì không ghi số được nữa
        var lateRecord = await staff.PostAsJsonAsync($"/api/stock-counts/{count.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.Sku1Id, CountedQuantity = 8m } }
        });
        lateRecord.StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await staff.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await staff.PostAsJsonAsync($"/api/stock-counts/{count.Id}/request-recount", new RequestRecountRequestDto { SkuIds = new List<Guid> { infra.Sku1Id }, Reason = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var confirm = await owner.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        confirm.IsSuccessStatusCode.Should().BeTrue(await confirm.Content.ReadAsStringAsync());

        var after = await ReadStockAsync(infra, infra.Sku1Id);
        after.OnHand.Should().Be(7m);
        after.Ledger.Should().Be(before.Ledger + 1);
        after.Adjustments.Should().Be(before.Adjustments + 1);

        var detail = await GetCountAsync(owner, count.Id);
        detail.Status.Should().Be("Confirmed");
        detail.Note.Should().Contain("Kiểm kê định kỳ", "ghi chú gốc không được bị ghi đè");
        detail.Approvals!.Select(a => a.Action).Should().Equal("Submitted", "Approved");
        detail.Approvals!.Last().Differences.Single(d => d.SkuId == infra.Sku1Id).Delta.Should().Be(-3m);

        var period = (await GetPeriodsAsync(owner)).Single(p => p.Id == detail.PeriodId);
        period.Status.Should().Be("Completed");
        period.CompletedAt.Should().NotBeNull();
        period.CountedByUserIds.Should().ContainSingle();
        period.CountedByUserIds.Should().NotContain(infra.UserId);
        period.ConfirmedBy.Should().Be(infra.UserId);
        period.DifferenceSkuCount.Should().Be(1);

        var restart = await owner.PostAsync($"/api/stock-count-periods/{period.Id}/start", null);
        restart.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task U03_TuDuyet_MotOwnerPhaiCoLyDo_HaiOwnerBiChan()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);
        using var owner = await ClientForAsync(OwnerA);

        var count = await CreateAdHocAsync(owner, infra, infra.Sku1Id);
        await RecordAsync(owner, count.Id, (infra.Sku1Id, 9m));
        (await owner.PostAsync($"/api/stock-counts/{count.Id}/submit", null)).IsSuccessStatusCode.Should().BeTrue();

        var noNote = await owner.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        noNote.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await noNote.Content.ReadAsStringAsync()).Should().Contain("STOCK_COUNT_APPROVAL_REASON_REQUIRED");

        var (ctx, _) = CreatePostgresDbContext(infra.TenantId);
        await using (ctx)
        {
            ctx.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                TenantId = infra.TenantId,
                Email = "owner2@shopa.local",
                PasswordHash = "khong-dang-nhap",
                DisplayName = "Owner thứ hai",
                Role = "Owner",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        var blocked = await owner.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto { Note = "Có lý do" });
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await blocked.Content.ReadAsStringAsync()).Should().Contain("STOCK_COUNT_SELF_APPROVAL");
        (await ReadStockAsync(infra, infra.Sku1Id)).OnHand.Should().Be(10m);
    }

    // ---------------------------------------------------------------------
    // Lệch sequence và đếm lại
    // ---------------------------------------------------------------------

    [Fact]
    public async Task U03_Lech_BaoDuSku_DemLaiDungSku_GiuSoCu_RoiChotDuoc()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku2Id, 10m, 50000m);
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku3Id, 10m, 50000m);
        using var owner = await ClientForAsync(OwnerA);
        using var staff = await ClientForAsync(StaffA);

        var count = await CreateAdHocAsync(staff, infra, infra.Sku1Id, infra.Sku2Id, infra.Sku3Id);
        await RecordAsync(staff, count.Id, (infra.Sku1Id, 9m), (infra.Sku2Id, 9m), (infra.Sku3Id, 10m));

        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 5m, 50000m);
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku2Id, 5m, 50000m);

        var submit = await staff.PostAsync($"/api/stock-counts/{count.Id}/submit", null);
        submit.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await submit.Content.ReadAsStringAsync();
        body.Should().Contain("STOCK_COUNT_SEQUENCE_DRIFT").And.Contain(infra.Sku1Code).And.Contain(infra.Sku2Code);
        body.Should().NotContain(infra.Sku3Code);

        var detail = await GetCountAsync(staff, count.Id);
        detail.Items.Where(i => i.IsDrifted).Select(i => i.SkuId).Should().BeEquivalentTo(new[] { infra.Sku1Id, infra.Sku2Id });

        var notDrifted = await staff.PostAsJsonAsync($"/api/stock-counts/{count.Id}/recount", new RecountDriftedRequestDto { SkuIds = new List<Guid> { infra.Sku3Id } });
        notDrifted.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await notDrifted.Content.ReadAsStringAsync()).Should().Contain("STOCK_COUNT_SKU_NOT_DRIFTED");

        var recount = await staff.PostAsJsonAsync($"/api/stock-counts/{count.Id}/recount", new RecountDriftedRequestDto { SkuIds = new List<Guid> { infra.Sku1Id, infra.Sku2Id } });
        recount.IsSuccessStatusCode.Should().BeTrue(await recount.Content.ReadAsStringAsync());

        detail = await GetCountAsync(staff, count.Id);
        var sku1 = detail.Items.Single(i => i.SkuId == infra.Sku1Id);
        sku1.ExpectedQuantity.Should().Be(15m);
        sku1.CountedQuantity.Should().BeNull();
        sku1.CountedBy.Should().BeNull();
        sku1.IsDrifted.Should().BeFalse();
        detail.Items.Single(i => i.SkuId == infra.Sku3Id).CountedQuantity.Should().Be(10m, "SKU không lệch giữ nguyên số đếm");
        detail.Approvals!.Single(a => a.Action == "Recounted").Differences.Single(d => d.SkuId == infra.Sku1Id).CountedQuantity.Should().Be(9m, "số đếm cũ phải còn trong hồ sơ");

        (await staff.PostAsync($"/api/stock-counts/{count.Id}/submit", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "còn dòng chưa đếm");

        await RecordAsync(staff, count.Id, (infra.Sku1Id, 14m), (infra.Sku2Id, 15m));
        (await staff.PostAsync($"/api/stock-counts/{count.Id}/submit", null)).IsSuccessStatusCode.Should().BeTrue();

        // Lệch phát sinh sau khi nộp: bước duyệt vẫn chặn
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku3Id, 1m, 50000m);
        var driftAtApproval = await owner.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        driftAtApproval.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await driftAtApproval.Content.ReadAsStringAsync()).Should().Contain("STOCK_COUNT_SEQUENCE_DRIFT").And.Contain(infra.Sku3Code);

        var noReason = await owner.PostAsJsonAsync($"/api/stock-counts/{count.Id}/request-recount", new RequestRecountRequestDto { SkuIds = new List<Guid> { infra.Sku3Id }, Reason = " " });
        noReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var requested = await owner.PostAsJsonAsync($"/api/stock-counts/{count.Id}/request-recount", new RequestRecountRequestDto { SkuIds = new List<Guid> { infra.Sku3Id }, Reason = "Có nhập thêm" });
        requested.IsSuccessStatusCode.Should().BeTrue(await requested.Content.ReadAsStringAsync());
        var back = (await requested.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions))!;
        back.Status.Should().Be("Counting");
        back.SubmittedAt.Should().BeNull();

        await RecordAsync(staff, count.Id, (infra.Sku3Id, 11m));
        (await staff.PostAsync($"/api/stock-counts/{count.Id}/submit", null)).IsSuccessStatusCode.Should().BeTrue();
        var finalConfirm = await owner.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        finalConfirm.IsSuccessStatusCode.Should().BeTrue(await finalConfirm.Content.ReadAsStringAsync());

        (await ReadStockAsync(infra, infra.Sku1Id)).OnHand.Should().Be(14m);
        (await ReadStockAsync(infra, infra.Sku2Id)).OnHand.Should().Be(15m);
        (await ReadStockAsync(infra, infra.Sku3Id)).OnHand.Should().Be(11m);
    }

    [Fact]
    public async Task U03_Duyet_SongSong_ChiGhiSoMotLan()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);
        using var owner = await ClientForAsync(OwnerA);
        using var staff = await ClientForAsync(StaffA);

        var count = await CreateAdHocAsync(staff, infra, infra.Sku1Id);
        await RecordAsync(staff, count.Id, (infra.Sku1Id, 8m));
        (await staff.PostAsync($"/api/stock-counts/{count.Id}/submit", null)).IsSuccessStatusCode.Should().BeTrue();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => owner.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto())));
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(9);

        (await ReadStockAsync(infra, infra.Sku1Id)).OnHand.Should().Be(8m);
        (await GetCountAsync(owner, count.Id)).Approvals!.Count(a => a.Action == "Approved").Should().Be(1);
    }

    // ---------------------------------------------------------------------
    // Tenant và bảo vệ dữ liệu
    // ---------------------------------------------------------------------

    [Fact]
    public async Task U03_Tenant_KhacKhongThayKhongSuaDuoc()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);
        using var owner = await ClientForAsync(OwnerA);
        var schedule = await CreateScheduleAsync(owner, infra);
        var period = (await GetPeriodsAsync(owner)).First();

        using var other = await ClientForAsync(OwnerB);
        (await GetPeriodsAsync(other)).Should().NotContain(p => p.Id == period.Id);
        (await other.PutAsJsonAsync($"/api/stock-count-schedules/{schedule.Id}", new UpdateStockCountScheduleRequestDto { IsActive = false })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PostAsync($"/api/stock-count-periods/{period.Id}/start", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var count = await StartFirstPeriodAsync(owner);
        (await other.PostAsync($"/api/stock-counts/{count.Id}/submit", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PostAsJsonAsync($"/api/stock-counts/{count.Id}/recount", new RecountDriftedRequestDto { SkuIds = new List<Guid> { infra.Sku1Id } })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PostAsJsonAsync($"/api/stock-counts/{count.Id}/request-recount", new RequestRecountRequestDto { SkuIds = new List<Guid> { infra.Sku1Id }, Reason = "x" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task U03_Approvals_ChiThem_DuoiRoleRuntime()
    {
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);
        using var staff = await ClientForAsync(StaffA);
        var count = await CreateAdHocAsync(staff, infra, infra.Sku1Id);
        await RecordAsync(staff, count.Id, (infra.Sku1Id, 8m));
        (await staff.PostAsync($"/api/stock-counts/{count.Id}/submit", null)).IsSuccessStatusCode.Should().BeTrue();

        foreach (var sql in new[]
                 {
                     "UPDATE public.stock_count_approvals SET reason = 'sua'",
                     "DELETE FROM public.stock_count_approvals"
                 })
        {
            await using var conn = new NpgsqlConnection(PostgresTestEnvironment.RuntimeConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            var act = () => cmd.ExecuteNonQueryAsync();
            await act.Should().ThrowAsync<PostgresException>();
        }

        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.RuntimeConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("DELETE FROM public.stock_count_periods", conn);
            var act = () => cmd.ExecuteNonQueryAsync();
            await act.Should().ThrowAsync<PostgresException>("runtime không có quyền xóa kỳ");
        }
    }

    [Fact]
    public async Task U03_Migration_TuSchemaTruocU03_ChayHaiLan_PhieuCuVanDiTiepDuoc()
    {
        PostgresTestEnvironment.AssertSafeTestTarget(PostgresTestEnvironment.DatabaseName);
        var infra = await SetupInfrastructureAsync();
        await SeedStockAsync(infra.TenantId, infra.BranchAId, infra.WarehouseA1Id, infra.SupplierId, infra.UserId, infra.Sku1Id, 10m, 50000m);
        using var staffBefore = await ClientForAsync(StaffA);
        var legacy = await CreateAdHocAsync(staffBefore, infra, infra.Sku1Id);
        await RecordAsync(staffBefore, legacy.Id, (infra.Sku1Id, 9m));

        var adminConnStr = PostgresTestEnvironment.AdminDatabaseConnectionString;
        await using (var admin = new NpgsqlConnection(adminConnStr))
        {
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand(@"
                DROP TABLE public.stock_count_approvals;
                ALTER TABLE public.stock_counts
                    DROP CONSTRAINT ck_stock_counts_type_period,
                    DROP CONSTRAINT ck_stock_counts_count_type,
                    DROP CONSTRAINT fk_stock_counts_period,
                    DROP CONSTRAINT fk_stock_counts_submitted_by,
                    DROP CONSTRAINT ck_stock_counts_status_values,
                    DROP COLUMN count_type, DROP COLUMN ad_hoc_reason, DROP COLUMN period_id, DROP COLUMN submitted_by, DROP COLUMN submitted_at,
                    ADD CONSTRAINT ck_stock_counts_status_values CHECK (status IN ('Draft', 'Counting', 'Confirmed', 'Cancelled'));
                ALTER TABLE public.stock_count_items
                    DROP CONSTRAINT fk_stock_count_items_counted_by,
                    DROP COLUMN counted_by, DROP COLUMN counted_at, DROP COLUMN snapshot_at;
                DROP TABLE public.stock_count_periods;
                DROP TABLE public.stock_count_schedules;", admin);
            await drop.ExecuteNonQueryAsync();
        }

        var migrator = new DatabaseMigrationService();
        for (var i = 0; i < 2; i++)
        {
            var upgraded = await migrator.UpgradeDatabaseSchemaAsync(adminConnStr);
            upgraded.Success.Should().BeTrue(upgraded.Message);
        }

        var check = await migrator.InitializeOrBaselineDatabaseAsync(adminConnStr);
        check.Success.Should().BeTrue(check.Message);

        using var owner = await ClientForAsync(OwnerA);
        using var staff = await ClientForAsync(StaffA);
        var detail = await GetCountAsync(staff, legacy.Id);
        detail.Status.Should().Be("Counting");
        detail.CountType.Should().Be("AdHoc");
        detail.Items.Should().OnlyContain(i => i.SnapshotAt != null);

        await RecordAsync(staff, legacy.Id, (infra.Sku1Id, 9m));
        (await staff.PostAsync($"/api/stock-counts/{legacy.Id}/submit", null)).IsSuccessStatusCode.Should().BeTrue();
        var confirm = await owner.PostAsJsonAsync($"/api/stock-counts/{legacy.Id}/confirm", new ConfirmStockCountRequestDto());
        confirm.IsSuccessStatusCode.Should().BeTrue(await confirm.Content.ReadAsStringAsync());
        (await ReadStockAsync(infra, infra.Sku1Id)).OnHand.Should().Be(9m);
    }
}
