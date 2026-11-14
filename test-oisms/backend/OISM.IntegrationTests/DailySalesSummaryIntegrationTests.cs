using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OISM.Application.Common.Helpers;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Reports.Dtos;
using OISM.Application.Features.Reports.Services;
using OISM.Domain.Common;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Kiểm thử tích hợp PostgreSQL thật cho Gói 3 Phase B (OISMS-59-G3B):
/// - AC-01: Entity DailySalesSummary khớp cột & unique index trên bảng daily_sales_summaries; tenant query filter hoạt động.
/// - AC-02/AC-03/AC-06: 2 tenant, 2 chi nhánh, đơn nhiều đơn vị, đơn tạo ngày D xác nhận D+2,
///   hàng trả D+1 cho đơn ngày D, đơn hủy, biên UTC+07 nửa mở -> khớp GetRevenueProfitReportAsync từng ngày;
///   chạy lại 2 lần giống hệt; không ghi dòng toàn 0; 2 lượt song song cùng tenant/ngày không trùng/lỗi unique;
///   tenant A không làm lộ hoặc ghi đè dữ liệu tenant B.
/// - AC-05: GET /api/reports/daily-summaries & POST /api/reports/daily-summaries/rebuild chỉ Owner (Staff/Cashier 403),
///   ProblemDetails + errorCode chuẩn, Matched/Mismatch/NotSummarized chính xác.
/// </summary>
public class DailySalesSummaryIntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public DailySalesSummaryIntegrationTests(WebApplicationFactoryWithPostgres factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
        var (ctx, _) = CreatePostgresDbContext(null);
        await using (ctx)
        {
            await OismsDatabaseSeeder.SeedAsync(ctx, PostgresTestEnvironment.AppPassword);
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class TestTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private static (AppDbContext Context, TestTenantService TenantService) CreatePostgresDbContext(Guid? tenantId = null)
    {
        var tenantService = new TestTenantService { TenantId = tenantId };
        var context = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService);
        return (context, tenantService);
    }

    private async Task<string> LoginAsync(string email)
    {
        using var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = PostgresTestEnvironment.AppPassword
        });
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>(_json);
        auth.Should().NotBeNull();
        return auth!.AccessToken;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        using var client = _factory.CreateClient();
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null)
        {
            req.Content = JsonContent.Create(body);
        }
        return await client.SendAsync(req);
    }

    private static DateTimeOffset VietnamTime(DateOnly date, int hour, int minute = 0, int second = 0) =>
        new DateTimeOffset(date.Year, date.Month, date.Day, hour, minute, second, TimeSpan.FromHours(7)).ToUniversalTime();

    // =========================================================================
    // AC-01: Khớp cột/kiểu/index PostgreSQL + Global Tenant Query Filter
    // =========================================================================
    [Fact]
    public async Task DailySalesSummaries_SchemaMatchesEfModel_AndTenantQueryFilterIsolatesRows()
    {
        var tenantA = OismsDatabaseSeeder.TenantAId;
        var tenantB = OismsDatabaseSeeder.TenantBId;

        await using var conn = new NpgsqlConnection(PostgresTestEnvironment.RuntimeConnectionString);
        await conn.OpenAsync();

        // 1. Đối chiếu cột trên information_schema.columns với EF metadata
        var dbCols = new Dictionary<string, (string DataType, int? Precision, int? Scale, string IsNullable)>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new NpgsqlCommand(@"
            SELECT column_name, data_type, numeric_precision, numeric_scale, is_nullable
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'daily_sales_summaries';", conn))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                dbCols[reader.GetString(0)] = (
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    reader.GetString(4));
            }
        }

        dbCols.Keys.Should().Contain(new[]
        {
            "id", "tenant_id", "business_date", "branch_id", "channel", "sku_id",
            "sold_quantity", "returned_quantity", "revenue", "return_amount", "cost_of_goods_sold",
            "created_at", "updated_at"
        });
        dbCols["business_date"].DataType.Should().Be("date");
        dbCols["sold_quantity"].Precision.Should().Be(18);
        dbCols["sold_quantity"].Scale.Should().Be(3);
        dbCols["returned_quantity"].Precision.Should().Be(18);
        dbCols["returned_quantity"].Scale.Should().Be(3);
        dbCols["revenue"].Precision.Should().Be(20);
        dbCols["revenue"].Scale.Should().Be(2);
        dbCols["return_amount"].Precision.Should().Be(20);
        dbCols["return_amount"].Scale.Should().Be(2);
        dbCols["cost_of_goods_sold"].Precision.Should().Be(20);
        dbCols["cost_of_goods_sold"].Scale.Should().Be(2);

        // 2. Ghi & đọc qua EF Core + kiểm chứng Global Tenant Query Filter
        Guid branchA, skuA, branchB, skuB;
        {
            var (ctxA, _) = CreatePostgresDbContext(tenantA);
            await using (ctxA)
            {
                var entityType = ctxA.Model.FindEntityType(typeof(DailySalesSummary));
                entityType.Should().NotBeNull();
                entityType!.GetTableName().Should().Be("daily_sales_summaries");
                entityType.GetIndexes().Select(i => i.GetDatabaseName()).Should().Contain(new[]
                {
                    "uq_daily_sales_summaries_1",
                    "uq_daily_sales_summaries_2"
                });

                branchA = await ctxA.Branches.Select(b => b.Id).FirstAsync();
                skuA = await ctxA.ProductVariants.Select(s => s.Id).FirstAsync();

                ctxA.DailySalesSummaries.Add(new DailySalesSummary
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantA,
                    BusinessDate = new DateOnly(2026, 3, 1),
                    BranchId = branchA,
                    Channel = "POS",
                    SkuId = skuA,
                    SoldQuantity = 12.500m,
                    ReturnedQuantity = 2.000m,
                    Revenue = 1_250_000.00m,
                    ReturnAmount = 200_000.00m,
                    CostOfGoodsSold = 500_000.00m
                });
                await ctxA.SaveChangesAsync();
            }
        }

        {
            var (ctxB, _) = CreatePostgresDbContext(tenantB);
            await using (ctxB)
            {
                branchB = await ctxB.Branches.Select(b => b.Id).FirstAsync();
                skuB = await ctxB.ProductVariants.Select(s => s.Id).FirstAsync();

                ctxB.DailySalesSummaries.Add(new DailySalesSummary
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantB,
                    BusinessDate = new DateOnly(2026, 3, 1),
                    BranchId = branchB,
                    Channel = "Admin",
                    SkuId = skuB,
                    SoldQuantity = 5m,
                    Revenue = 500_000m,
                    CostOfGoodsSold = 250_000m
                });
                await ctxB.SaveChangesAsync();
            }
        }

        // Tenant A chỉ thấy dòng của Tenant A
        {
            var (readA, _) = CreatePostgresDbContext(tenantA);
            await using (readA)
            {
                var rows = await readA.DailySalesSummaries.ToListAsync();
                rows.Should().ContainSingle();
                rows[0].TenantId.Should().Be(tenantA);
                rows[0].SoldQuantity.Should().Be(12.500m);
                rows[0].Revenue.Should().Be(1_250_000m);
            }
        }

        // Null tenant context không đọc được dòng nào qua query filter
        {
            var (readNull, _) = CreatePostgresDbContext(null);
            await using (readNull)
            {
                (await readNull.DailySalesSummaries.CountAsync()).Should().Be(0);
            }
        }
    }

    // =========================================================================
    // AC-02, AC-03, AC-04, AC-06: Multi-tenant, multi-unit, cross-day confirm & return,
    // cancelled excluded, idempotency, concurrent lock, cross-tenant isolation
    // =========================================================================
    [Fact]
    public async Task Rebuild_And_Job_MultiTenantMultiUnitAndCrossDayScenario_MatchesReportServiceAndSerializesConcurrency()
    {
        var tenantA = OismsDatabaseSeeder.TenantAId;
        var tenantB = OismsDatabaseSeeder.TenantBId;

        var dayD = new DateOnly(2026, 4, 10);
        var dayD1 = dayD.AddDays(1); // 2026-04-11
        var dayD2 = dayD.AddDays(2); // 2026-04-12
        var dayQuiet = dayD.AddDays(-1); // 2026-04-09 (chỉ có đơn Cancelled -> không ghi dòng 0)

        Guid branchA1, branchA2, skuA1, skuA2, userA;
        Guid branchB1, skuB1, userB;
        Guid deferredOrderId;

        // 1. Seed kịch bản phức tạp trên PostgreSQL
        {
            var (ctxA, _) = CreatePostgresDbContext(tenantA);
            await using (ctxA)
            {
                using var _ = ctxA.OpenPrivilegedScope();

                branchA1 = await ctxA.Branches.Select(b => b.Id).FirstAsync();
                branchA2 = Guid.NewGuid();
                ctxA.Branches.Add(new Branch
                {
                    Id = branchA2,
                    TenantId = tenantA,
                    Name = "Chi nhánh Q7 (Test Summary)",
                    IsActive = true
                });

                var skusA = await ctxA.ProductVariants.Where(s => s.IsActive).OrderBy(s => s.SKU).Take(2).ToListAsync();
                skuA1 = skusA[0].Id;
                skuA2 = skusA.Count > 1 ? skusA[1].Id : skusA[0].Id;
                userA = await ctxA.Users.Where(u => u.Role == "Owner").Select(u => u.Id).FirstAsync();
                var warehouseA = await ctxA.Warehouses.Where(w => w.BranchId == branchA1).Select(w => w.Id).FirstAsync();

                // (a) Ngày quiet: 1 đơn Cancelled -> không được ghi dòng toàn 0
                ctxA.Orders.Add(new Order
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantA,
                    BranchId = branchA1,
                    Number = "ORD-CANCEL-QUIET",
                    Channel = "POS",
                    Status = OrderStatus.Cancelled,
                    TotalAmount = 500_000m,
                    RequestKey = Guid.NewGuid(),
                    CreatedBy = userA,
                    CreatedAt = VietnamTime(dayQuiet, 14, 0),
                    CancelledAt = VietnamTime(dayQuiet, 15, 0),
                    Items =
                    {
                        new OrderItem
                        {
                            Id = Guid.NewGuid(), TenantId = tenantA, SkuId = skuA1,
                            SkuCodeSnapshot = "SKU-Q", SkuNameSnapshot = "Quiet", UnitNameSnapshot = "Cái",
                            FactorSnapshot = 1m, Quantity = 5m, UnitPrice = 100_000m, CostPrice = 50_000m
                        }
                    }
                });

                // (b) Ngày D (2026-04-10 22:30 +07 = 15:30 UTC): đơn POS nhiều đơn vị (2 thùng * Factor 24 = 48 base units)
                // Revenue = 480,000; COGS = 48 * 5,000 = 240,000
                var multiUnitOrderId = Guid.NewGuid();
                var multiUnitItemId = Guid.NewGuid();
                var multiUnitOrder = new Order
                {
                    Id = multiUnitOrderId,
                    TenantId = tenantA,
                    BranchId = branchA1,
                    Number = "ORD-D-MULTI",
                    Channel = "POS",
                    Status = OrderStatus.Completed,
                    TotalAmount = 480_000m,
                    RequestKey = Guid.NewGuid(),
                    CreatedBy = userA,
                    CreatedAt = VietnamTime(dayD, 22, 30),
                    ConfirmedAt = VietnamTime(dayD, 22, 30),
                    CompletedAt = VietnamTime(dayD, 22, 30),
                    Items =
                    {
                        new OrderItem
                        {
                            Id = multiUnitItemId,
                            TenantId = tenantA,
                            OrderId = multiUnitOrderId,
                            SkuId = skuA1,
                            SkuCodeSnapshot = "SKU-MULTI",
                            SkuNameSnapshot = "Nước ngọt",
                            UnitNameSnapshot = "Thùng 24",
                            FactorSnapshot = 24m,
                            Quantity = 2m,
                            UnitPrice = 240_000m,
                            CostPrice = 5_000m // WAC theo đơn vị gốc
                        }
                    }
                };
                ctxA.Orders.Add(multiUnitOrder);

                // (c) Ngày D: đơn Shopee ở BranchA2 đang Reserved -> CHƯA tính vào doanh thu D trước khi Confirm!
                // Đến bước sau xác nhận ở D+2 -> PHẢI tính vào ngày D (theo Order.CreatedAt)!
                deferredOrderId = Guid.NewGuid();
                ctxA.Orders.Add(new Order
                {
                    Id = deferredOrderId,
                    TenantId = tenantA,
                    BranchId = branchA2,
                    Number = "ORD-D-RESERVED",
                    Channel = "Shopee",
                    ExternalShopId = "SHOPEE-TEST-01",
                    ExternalOrderId = $"SHP-{Guid.NewGuid():N}"[..24],
                    Status = OrderStatus.Reserved,
                    TotalAmount = 300_000m,
                    RequestKey = Guid.NewGuid(),
                    CreatedBy = userA,
                    CreatedAt = VietnamTime(dayD, 23, 55), // 16:55 UTC (vẫn thuộc dayD ở UTC+07)
                    Items =
                    {
                        new OrderItem
                        {
                            Id = Guid.NewGuid(),
                            TenantId = tenantA,
                            OrderId = deferredOrderId,
                            SkuId = skuA2,
                            SkuCodeSnapshot = "SKU-D2",
                            SkuNameSnapshot = "SP Shopee",
                            UnitNameSnapshot = "Cái",
                            FactorSnapshot = 1m,
                            Quantity = 3m,
                            UnitPrice = 100_000m,
                            CostPrice = 60_000m
                        }
                    }
                });

                // (d) Ngày D+1 (2026-04-11 00:05 +07 = 2026-04-10 17:05 UTC): ranh giới nửa mở thuộc D+1!
                // Đơn Admin 1 cái @ 100,000, Cost 70,000
                ctxA.Orders.Add(new Order
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantA,
                    BranchId = branchA1,
                    Number = "ORD-D1-BOUNDARY",
                    Channel = "Admin",
                    Status = OrderStatus.Confirmed,
                    TotalAmount = 100_000m,
                    RequestKey = Guid.NewGuid(),
                    CreatedBy = userA,
                    CreatedAt = VietnamTime(dayD1, 0, 5),
                    ConfirmedAt = VietnamTime(dayD1, 0, 15),
                    Items =
                    {
                        new OrderItem
                        {
                            Id = Guid.NewGuid(), TenantId = tenantA, SkuId = skuA1,
                            SkuCodeSnapshot = "SKU-B", SkuNameSnapshot = "Boundary", UnitNameSnapshot = "Cái",
                            FactorSnapshot = 1m, Quantity = 1m, UnitPrice = 100_000m, CostPrice = 70_000m
                        }
                    }
                });

                // (e) Ngày D+1: Phiếu trả hàng 1 thùng (= 24 base units) cho đơn multi-unit ngày D,
                // xác nhận lúc D+1 10:00 +07 -> thuộc ngày D+1!
                // ReturnAmount = 240,000; ReturnCogs = 24 * 5,000 = 120,000; ReturnedQuantity = 24 base units
                ctxA.SalesReturns.Add(new SalesReturn
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantA,
                    RequestKey = Guid.NewGuid(),
                    Number = "SR-D1-MULTI",
                    OrderId = multiUnitOrderId,
                    BranchId = branchA1,
                    WarehouseId = warehouseA,
                    Status = SalesReturnStatus.Confirmed,
                    Reason = "Khách trả 1 thùng",
                    TotalRefundAmount = 240_000m,
                    RefundMethod = "Cash",
                    CreatedBy = userA,
                    ConfirmedBy = userA,
                    CreatedAt = VietnamTime(dayD1, 9, 50),
                    ConfirmedAt = VietnamTime(dayD1, 10, 0),
                    RefundedAt = VietnamTime(dayD1, 10, 0),
                    Items =
                    {
                        new SalesReturnItem
                        {
                            Id = Guid.NewGuid(),
                            TenantId = tenantA,
                            OrderId = multiUnitOrderId,
                            OrderItemId = multiUnitItemId,
                            SkuId = skuA1,
                            Quantity = 1m,
                            RefundAmount = 240_000m,
                            OriginalUnitCost = 5_000m
                        }
                    }
                });

                await ctxA.SaveChangesAsync();
            }
        }

        // Tenant B có dữ liệu riêng cùng ngày D để kiểm chứng cách ly 2 tenant
        {
            var (ctxB, _) = CreatePostgresDbContext(tenantB);
            await using (ctxB)
            {
                using var _ = ctxB.OpenPrivilegedScope();
                branchB1 = await ctxB.Branches.Select(b => b.Id).FirstAsync();
                skuB1 = await ctxB.ProductVariants.Select(s => s.Id).FirstAsync();
                userB = await ctxB.Users.Where(u => u.Role == "Owner").Select(u => u.Id).FirstAsync();

                ctxB.Orders.Add(new Order
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantB,
                    BranchId = branchB1,
                    Number = "ORD-TENB-D",
                    Channel = "POS",
                    Status = OrderStatus.Completed,
                    TotalAmount = 999_000m,
                    RequestKey = Guid.NewGuid(),
                    CreatedBy = userB,
                    CreatedAt = VietnamTime(dayD, 12, 0),
                    ConfirmedAt = VietnamTime(dayD, 12, 0),
                    CompletedAt = VietnamTime(dayD, 12, 0),
                    Items =
                    {
                        new OrderItem
                        {
                            Id = Guid.NewGuid(), TenantId = tenantB, SkuId = skuB1,
                            SkuCodeSnapshot = "SKUB", SkuNameSnapshot = "Item B", UnitNameSnapshot = "Cái",
                            FactorSnapshot = 1m, Quantity = 3m, UnitPrice = 333_000m, CostPrice = 200_000m
                        }
                    }
                });
                await ctxB.SaveChangesAsync();
            }
        }

        // 2. Chạy tổng hợp trước khi đơn ORD-D-RESERVED được xác nhận
        {
            using var scope = _factory.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<ICurrentTenantService>().SetTenant(tenantA);
            var summarySvc = scope.ServiceProvider.GetRequiredService<IDailySalesSummaryService>();

            (await summarySvc.RebuildAsync(tenantA, dayQuiet)).Should().Be(0, "Ngày chỉ có đơn Cancelled không được ghi dòng toàn 0");
            (await summarySvc.RebuildAsync(tenantA, dayD)).Should().Be(1, "Trước khi confirm, đơn Reserved chưa tính vào tổng hợp ngày D");
            (await summarySvc.RebuildAsync(tenantA, dayD1)).Should().Be(2, "Ngày D+1 có đơn Admin và phiếu trả POS cho cùng skuA1 => 2 dòng theo kênh");
        }

        // Lưu ý: dayD1 có đơn Admin (skuA1) và phiếu trả cho đơn POS (skuA1) => 2 dòng (BranchA1/Admin/skuA1 và BranchA1/POS/skuA1)!
        // Kiểm chứng lại số dòng thực tế ngày D+1 ở DB ngay:
        {
            var (checkCtx, _) = CreatePostgresDbContext(tenantA);
            await using (checkCtx)
            {
                var d1Rows = await checkCtx.DailySalesSummaries.Where(s => s.BusinessDate == dayD1).ToListAsync();
                d1Rows.Should().HaveCount(2);
                d1Rows.Should().Contain(r => r.Channel == "Admin" && r.SoldQuantity == 1m && r.Revenue == 100_000m && r.CostOfGoodsSold == 70_000m);
                d1Rows.Should().Contain(r => r.Channel == "POS" && r.ReturnedQuantity == 24m && r.ReturnAmount == 240_000m && r.CostOfGoodsSold == -120_000m);
            }
        }

        // 3. Nay xác nhận đơn ORD-D-RESERVED ở D+2 (2026-04-12) rồi chạy Hangfire Job với nowUtc = D+3 00:30 +07
        {
            var (ctxA, _) = CreatePostgresDbContext(tenantA);
            await using (ctxA)
            {
                var ord = await ctxA.Orders.FirstAsync(o => o.Id == deferredOrderId);
                ord.Status = OrderStatus.Confirmed;
                ord.ConfirmedBy = userA;
                ord.ConfirmedAt = VietnamTime(dayD2, 11, 0);
                ord.UpdatedAt = VietnamTime(dayD2, 11, 0);
                await ctxA.SaveChangesAsync();
            }
        }

        {
            using var jobScope = _factory.Services.CreateScope();
            var job = jobScope.ServiceProvider.GetRequiredService<IDailySalesSummaryJob>();
            var written = await job.ExecuteAsync(nowUtc: VietnamTime(dayD2.AddDays(1), 0, 30));
            written.Should().BeGreaterThanOrEqualTo(5); // Tenant A (2 dòng D + 2 dòng D+1) + Tenant B (1 dòng D)
        }

        // 4. Đối chiếu từng ngày [D..D+2] giữa DailySalesSummaries và ReportService.GetRevenueProfitReportAsync
        {
            using var scope = _factory.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<ICurrentTenantService>().SetTenant(tenantA);
            var summarySvc = scope.ServiceProvider.GetRequiredService<IDailySalesSummaryService>();
            var reportSvc = scope.ServiceProvider.GetRequiredService<IReportService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var comparison = await summarySvc.GetComparisonAsync(dayD, dayD2, branchId: null);
            var liveReport = await reportSvc.GetRevenueProfitReportAsync(new ReportFilterDto
            {
                StartDate = ReportTimeHelper.StartOfDayUtc(dayD.ToDateTime(TimeOnly.MinValue)),
                EndDate = ReportTimeHelper.StartOfDayUtc(dayD2.AddDays(1).ToDateTime(TimeOnly.MinValue))
            }, isOwner: true);

            comparison.Days.Should().OnlyContain(d => d.Matched && d.Status == "Matched");

            var dayDRow = comparison.Days.Single(d => d.Date == dayD);
            dayDRow.Summary!.Revenue.Should().Be(780_000m); // 480k POS + 300k Shopee
            dayDRow.Summary.CostOfGoodsSold.Should().Be(420_000m); // 240k + 180k
            dayDRow.Summary.GrossProfit.Should().Be(360_000m);

            var dayD1Row = comparison.Days.Single(d => d.Date == dayD1);
            dayD1Row.Summary!.Revenue.Should().Be(100_000m);
            dayD1Row.Summary.ReturnAmount.Should().Be(240_000m);
            dayD1Row.Summary.NetRevenue.Should().Be(-140_000m);
            dayD1Row.Summary.CostOfGoodsSold.Should().Be(-50_000m); // +70k Admin COGS - 120k POS Return COGS
            dayD1Row.Summary.GrossProfit.Should().Be(-90_000m);

            // Tổng hợp 3 ngày khớp với liveReport
            var dbRowsA = await db.DailySalesSummaries.Where(s => s.BusinessDate >= dayQuiet && s.BusinessDate <= dayD2).ToListAsync();
            dbRowsA.Sum(x => x.Revenue).Should().Be(liveReport.Revenue);
            dbRowsA.Sum(x => x.ReturnAmount).Should().Be(liveReport.ReturnAmount);
            dbRowsA.Sum(x => x.CostOfGoodsSold).Should().Be(liveReport.NetCogs);

            // Tenant A dữ liệu không chứa 999k của Tenant B
            dbRowsA.Should().OnlyContain(x => x.TenantId == tenantA);
            dbRowsA.Sum(x => x.Revenue).Should().Be(880_000m);

            // 5. Idempotency: chạy lại 2 lần cho ngày D cho kết quả giống hệt
            var count1 = await summarySvc.RebuildAsync(tenantA, dayD);
            var snapshot1 = await db.DailySalesSummaries
                .AsNoTracking()
                .Where(s => s.BusinessDate == dayD)
                .OrderBy(s => s.BranchId).ThenBy(s => s.Channel).ThenBy(s => s.SkuId)
                .Select(s => new { s.BranchId, s.Channel, s.SkuId, s.SoldQuantity, s.ReturnedQuantity, s.Revenue, s.ReturnAmount, s.CostOfGoodsSold })
                .ToListAsync();

            var count2 = await summarySvc.RebuildAsync(tenantA, dayD);
            var snapshot2 = await db.DailySalesSummaries
                .AsNoTracking()
                .Where(s => s.BusinessDate == dayD)
                .OrderBy(s => s.BranchId).ThenBy(s => s.Channel).ThenBy(s => s.SkuId)
                .Select(s => new { s.BranchId, s.Channel, s.SkuId, s.SoldQuantity, s.ReturnedQuantity, s.Revenue, s.ReturnAmount, s.CostOfGoodsSold })
                .ToListAsync();

            count2.Should().Be(count1);
            snapshot2.Should().BeEquivalentTo(snapshot1, opts => opts.WithStrictOrdering());
        }

        // 6. Chạy 2 lượt RebuildAsync song song cùng (Tenant A, dayD): tuần tự hóa bởi pg_advisory_xact_lock, không lỗi unique, không nhân đôi dòng
        var concurrentTasks = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            using var s = _factory.Services.CreateScope();
            s.ServiceProvider.GetRequiredService<ICurrentTenantService>().SetTenant(tenantA);
            var svc = s.ServiceProvider.GetRequiredService<IDailySalesSummaryService>();
            return await svc.RebuildAsync(tenantA, dayD);
        }));

        var concurrentCounts = await Task.WhenAll(concurrentTasks);
        concurrentCounts.Should().OnlyContain(c => c == 2);

        {
            var (verifyCtx, _) = CreatePostgresDbContext(tenantA);
            await using (verifyCtx)
            {
                var finalD = await verifyCtx.DailySalesSummaries.Where(s => s.BusinessDate == dayD).ToListAsync();
                finalD.Should().HaveCount(2);
                finalD.Sum(r => r.Revenue).Should().Be(780_000m);
            }

            var (verifyB, _) = CreatePostgresDbContext(tenantB);
            await using (verifyB)
            {
                var rowsB = await verifyB.DailySalesSummaries.Where(s => s.BusinessDate == dayD).ToListAsync();
                rowsB.Should().ContainSingle();
                rowsB[0].Revenue.Should().Be(999_000m);
            }
        }
    }

    // =========================================================================
    // AC-05: API Endpoints RBAC (Owner only), ProblemDetails + errorCode, Matched/Mismatch/NotSummarized
    // =========================================================================
    [Fact]
    public async Task DailySummaryEndpoints_EnforceOwnerOnly_ProblemDetails_AndStatusTransitions()
    {
        var tenantA = OismsDatabaseSeeder.TenantAId;
        var targetDate = new DateOnly(2026, 5, 15);

        var ownerAToken = await LoginAsync("owner@shopa.local");
        var staffAToken = await LoginAsync("staff@shopa.local");
        var cashierAToken = await LoginAsync("cashier@shopa.local");
        var ownerBToken = await LoginAsync("owner@shopb.local");

        // 1. Staff & Cashier nhận 403 trên cả GET và POST
        foreach (var nonOwnerToken in new[] { staffAToken, cashierAToken })
        {
            (await SendAsync(HttpMethod.Get, $"/api/reports/daily-summaries?fromDate={targetDate:yyyy-MM-dd}&toDate={targetDate:yyyy-MM-dd}", nonOwnerToken))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);

            (await SendAsync(HttpMethod.Post, "/api/reports/daily-summaries/rebuild", nonOwnerToken,
                    new RebuildDailySummaryRequestDto { FromDate = targetDate, ToDate = targetDate }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // 2. Khoảng > 31 ngày -> 400 ProblemDetails với errorCode == VALIDATION_ERROR
        var badRangeRes = await SendAsync(
            HttpMethod.Post,
            "/api/reports/daily-summaries/rebuild",
            ownerAToken,
            new RebuildDailySummaryRequestDto
            {
                FromDate = new DateOnly(2026, 5, 1),
                ToDate = new DateOnly(2026, 6, 5)
            });
        badRangeRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using (var doc = JsonDocument.Parse(await badRangeRes.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("errorCode").GetString().Should().Be("VALIDATION_ERROR");
            doc.RootElement.GetProperty("status").GetInt32().Should().Be(400);
        }

        // 3. Seed 1 đơn hàng cho Tenant A ngày targetDate -> trước rebuild trả Status = NotSummarized
        Guid branchA, skuA, userA;
        {
            var (ctx, _) = CreatePostgresDbContext(tenantA);
            await using (ctx)
            {
                using var _ = ctx.OpenPrivilegedScope();
                branchA = await ctx.Branches.Select(b => b.Id).FirstAsync();
                skuA = await ctx.ProductVariants.Select(s => s.Id).FirstAsync();
                userA = await ctx.Users.Where(u => u.Role == "Owner").Select(u => u.Id).FirstAsync();

                ctx.Orders.Add(new Order
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantA,
                    BranchId = branchA,
                    Number = "ORD-API-01",
                    Channel = "POS",
                    Status = OrderStatus.Completed,
                    TotalAmount = 200_000m,
                    RequestKey = Guid.NewGuid(),
                    CreatedBy = userA,
                    CreatedAt = VietnamTime(targetDate, 10, 0),
                    ConfirmedAt = VietnamTime(targetDate, 10, 0),
                    CompletedAt = VietnamTime(targetDate, 10, 0),
                    Items =
                    {
                        new OrderItem
                        {
                            Id = Guid.NewGuid(), TenantId = tenantA, SkuId = skuA,
                            SkuCodeSnapshot = "SKU-API", SkuNameSnapshot = "API", UnitNameSnapshot = "Cái",
                            FactorSnapshot = 1m, Quantity = 2m, UnitPrice = 100_000m, CostPrice = 50_000m
                        }
                    }
                });
                await ctx.SaveChangesAsync();
            }
        }

        var beforeRes = await SendAsync(
            HttpMethod.Get,
            $"/api/reports/daily-summaries?fromDate={targetDate:yyyy-MM-dd}&toDate={targetDate:yyyy-MM-dd}",
            ownerAToken);
        beforeRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var beforeDto = await beforeRes.Content.ReadFromJsonAsync<DailySalesSummaryComparisonDto>(_json);
        beforeDto!.Days.Should().ContainSingle();
        beforeDto.Days[0].Status.Should().Be("NotSummarized");
        beforeDto.Days[0].Summary.Should().BeNull();
        beforeDto.Days[0].Live.Revenue.Should().Be(200_000m);

        // Tenant B hoàn toàn không thấy phát sinh của Tenant A
        var tenantBRes = await SendAsync(
            HttpMethod.Get,
            $"/api/reports/daily-summaries?fromDate={targetDate:yyyy-MM-dd}&toDate={targetDate:yyyy-MM-dd}",
            ownerBToken);
        tenantBRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var tenantBDto = await tenantBRes.Content.ReadFromJsonAsync<DailySalesSummaryComparisonDto>(_json);
        tenantBDto!.Days.Should().BeEmpty();

        // 4. Owner A gọi POST rebuild -> GET trả Matched + khớp /api/reports/revenue-profit
        var rebuildRes = await SendAsync(
            HttpMethod.Post,
            "/api/reports/daily-summaries/rebuild",
            ownerAToken,
            new RebuildDailySummaryRequestDto { FromDate = targetDate, ToDate = targetDate });
        rebuildRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var rebuildBody = await rebuildRes.Content.ReadFromJsonAsync<RebuildDailySummaryResponseDto>(_json);
        rebuildBody!.DaysProcessed.Should().Be(1);
        rebuildBody.RowsWritten.Should().Be(1);

        var afterRes = await SendAsync(
            HttpMethod.Get,
            $"/api/reports/daily-summaries?fromDate={targetDate:yyyy-MM-dd}&toDate={targetDate:yyyy-MM-dd}&branchId={branchA}",
            ownerAToken);
        afterRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterDto = await afterRes.Content.ReadFromJsonAsync<DailySalesSummaryComparisonDto>(_json);
        afterDto!.Days.Should().ContainSingle();
        afterDto.Days[0].Matched.Should().BeTrue();
        afterDto.Days[0].Status.Should().Be("Matched");
        afterDto.Days[0].UpdatedAt.Should().NotBeNull();
        afterDto.Days[0].Summary!.Revenue.Should().Be(200_000m);
        afterDto.Days[0].Summary!.CostOfGoodsSold.Should().Be(100_000m);
        afterDto.Days[0].Summary!.GrossProfit.Should().Be(100_000m);

        // 5. Thêm phát sinh mới cùng ngày trước khi tính lại -> GET trả Status = Mismatch (Lệch)
        {
            var (ctx, _) = CreatePostgresDbContext(tenantA);
            await using (ctx)
            {
                using var _ = ctx.OpenPrivilegedScope();
                ctx.Orders.Add(new Order
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantA,
                    BranchId = branchA,
                    Number = "ORD-API-02",
                    Channel = "POS",
                    Status = OrderStatus.Completed,
                    TotalAmount = 50_000m,
                    RequestKey = Guid.NewGuid(),
                    CreatedBy = userA,
                    CreatedAt = VietnamTime(targetDate, 14, 0),
                    ConfirmedAt = VietnamTime(targetDate, 14, 0),
                    CompletedAt = VietnamTime(targetDate, 14, 0),
                    Items =
                    {
                        new OrderItem
                        {
                            Id = Guid.NewGuid(), TenantId = tenantA, SkuId = skuA,
                            SkuCodeSnapshot = "SKU-API", SkuNameSnapshot = "API", UnitNameSnapshot = "Cái",
                            FactorSnapshot = 1m, Quantity = 1m, UnitPrice = 50_000m, CostPrice = 20_000m
                        }
                    }
                });
                await ctx.SaveChangesAsync();
            }
        }

        var mismatchRes = await SendAsync(
            HttpMethod.Get,
            $"/api/reports/daily-summaries?fromDate={targetDate:yyyy-MM-dd}&toDate={targetDate:yyyy-MM-dd}",
            ownerAToken);
        mismatchRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var mismatchDto = await mismatchRes.Content.ReadFromJsonAsync<DailySalesSummaryComparisonDto>(_json);
        mismatchDto!.Days.Should().ContainSingle();
        mismatchDto.Days[0].Matched.Should().BeFalse();
        mismatchDto.Days[0].Status.Should().Be("Mismatch");
        mismatchDto.Days[0].Summary!.Revenue.Should().Be(200_000m);
        mismatchDto.Days[0].Live.Revenue.Should().Be(250_000m);
    }
}
