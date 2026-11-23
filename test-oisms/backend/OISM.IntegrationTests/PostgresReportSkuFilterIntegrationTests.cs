using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Reports.Dtos;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Kiểm thử tích hợp PostgreSQL cho tính năng lọc báo cáo doanh thu/COGS/lãi gộp theo SKU (OISMS-66-FIN-A).
/// Bao phủ AC-01, AC-04, AC-05, AC-06.
/// </summary>
[Collection("PostgresIntegrationTests")]
public class PostgresReportSkuFilterIntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class TestTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    public PostgresReportSkuFilterIntegrationTests(WebApplicationFactoryWithPostgres factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private (AppDbContext Context, TestTenantService TenantService) CreatePostgresDbContext(Guid? tenantId = null)
    {
        var ts = new TestTenantService();
        if (tenantId.HasValue)
        {
            ts.SetTenant(tenantId.Value);
        }

        return (PostgresTestEnvironment.CreateRuntimeDbContext(ts), ts);
    }

    private async Task SeedAsync()
    {
        var (ctx, _) = CreatePostgresDbContext(null);
        await using (ctx)
        {
            await OismsDatabaseSeeder.SeedAsync(ctx, PostgresTestEnvironment.AppPassword);
        }
    }

    private async Task<string> LoginAsAsync(string email, string password)
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var res = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = password
        });
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var login = await res.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        login.Should().NotBeNull();
        return login!.AccessToken;
    }

    [Fact]
    public async Task RevenueProfit_WithOver20Skus_Returns400ValidationErrorProblemDetails_AC01()
    {
        await SeedAsync();

        var token = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var query = string.Join("&", Enumerable.Range(0, 21).Select(_ => $"skuIds={Guid.NewGuid()}"));
        var response = await _client.GetAsync($"/api/reports/revenue-profit?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("errorCode", out var errCode).Should().BeTrue();
        errCode.GetString().Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task RevenueProfit_NonExistentOrOtherTenantSku_ReturnsZerosWithoutError_AC04()
    {
        await SeedAsync();

        var otherTenantId = Guid.NewGuid();
        Guid otherTenantSkuId;
        var (ctx, ts) = CreatePostgresDbContext(null);
        await using (ctx)
        {
            var otherTenant = new Tenant { Id = otherTenantId, Name = "Other Tenant", IsActive = true };
            ctx.Tenants.Add(otherTenant);
            await ctx.SaveChangesAsync();

            ts.SetTenant(otherTenantId);
            var otherProduct = new Product { Id = Guid.NewGuid(), TenantId = otherTenantId, Name = "Other Prod" };
            var otherSku = new ProductVariant
            {
                Id = Guid.NewGuid(),
                TenantId = otherTenantId,
                ProductId = otherProduct.Id,
                SKU = "OTHER-SKU-01"
            };
            ctx.Products.Add(otherProduct);
            ctx.Skus.Add(otherSku);
            await ctx.SaveChangesAsync();
            otherTenantSkuId = otherSku.Id;
        }

        var token = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var nonExistentId = Guid.NewGuid();
        var response = await _client.GetAsync($"/api/reports/revenue-profit?skuIds={nonExistentId}&skuIds={otherTenantSkuId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = await response.Content.ReadFromJsonAsync<RevenueProfitReportDto>(_jsonOptions);
        report.Should().NotBeNull();
        report!.Revenue.Should().Be(0m);
        report.NetRevenue.Should().Be(0m);
        report.TotalOrders.Should().Be(0);
        report.SkuBreakdown.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task RevenueProfit_WithSkuFilter_StaffMasksCogsAndCashierForbidden_AC05()
    {
        await SeedAsync();

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid seededSkuId;
        var (ctx, _) = CreatePostgresDbContext(tenantId);
        await using (ctx)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == tenantId);
            var sku = await ctx.Skus.FirstAsync(s => s.TenantId == tenantId);
            seededSkuId = sku.Id;

            var staffOrderCreatedAt = new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime();
            var order = new Order
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Number = "ORD-PG-STAFF-01",
                BranchId = branch.Id,
                Channel = "POS",
                Status = "Completed",
                TotalAmount = 300_000m,
                CreatedAt = staffOrderCreatedAt,
                ConfirmedAt = staffOrderCreatedAt,
                CompletedAt = staffOrderCreatedAt
            };
            order.Items.Add(new OrderItem
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                OrderId = order.Id,
                SkuId = sku.Id,
                SkuCodeSnapshot = sku.SKU,
                SkuNameSnapshot = "Seeded SKU",
                UnitNameSnapshot = "Cái",
                FactorSnapshot = 1m,
                Quantity = 2m,
                UnitPrice = 150_000m,
                CostPrice = 90_000m
            });
            ctx.Orders.Add(order);
            await ctx.SaveChangesAsync();
        }

        // Staff
        var staffToken = await LoginAsAsync("staff@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var staffRes = await _client.GetAsync($"/api/reports/revenue-profit?skuIds={seededSkuId}");
        staffRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var staffReport = await staffRes.Content.ReadFromJsonAsync<RevenueProfitReportDto>(_jsonOptions);
        staffReport.Should().NotBeNull();
        staffReport!.NetRevenue.Should().Be(300_000m);
        staffReport.CostOfGoodsSold.Should().BeNull();
        staffReport.ReturnCogs.Should().BeNull();
        staffReport.NetCogs.Should().BeNull();
        staffReport.GrossProfit.Should().BeNull();
        staffReport.SkuBreakdown.Should().ContainSingle();
        staffReport.SkuBreakdown![0].CostOfGoodsSold.Should().BeNull();
        staffReport.SkuBreakdown![0].ReturnCogs.Should().BeNull();
        staffReport.SkuBreakdown![0].NetCogs.Should().BeNull();
        staffReport.SkuBreakdown![0].GrossProfit.Should().BeNull();

        // Cashier -> 403
        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        var cashierRes = await _client.GetAsync($"/api/reports/revenue-profit?skuIds={seededSkuId}");
        cashierRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RevenueProfit_CombinedSkuBranchChannelAndUtc7HalfOpenBoundary_FiltersInPostgres_AC06()
    {
        await SeedAsync();

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branch1Id;
        Guid sku1Id;
        var (ctx, _) = CreatePostgresDbContext(tenantId);
        await using (ctx)
        {
            var branches = await ctx.Branches.Where(b => b.TenantId == tenantId).ToListAsync();
            var branch1 = branches[0];
            branch1Id = branch1.Id;

            var branch2 = branches.Count > 1
                ? branches[1]
                : new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "CN Phu", IsActive = true };
            if (branches.Count <= 1)
            {
                ctx.Branches.Add(branch2);
            }

            var sku1 = await ctx.Skus.FirstAsync(s => s.TenantId == tenantId);
            sku1Id = sku1.Id;

            var tIn = new DateTimeOffset(2026, 11, 10, 23, 59, 59, TimeSpan.FromHours(7)).ToUniversalTime();
            var inRangeOrder = new Order
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Number = "ORD-BOUND-IN",
                BranchId = branch1.Id,
                Channel = "POS",
                Status = "Completed",
                TotalAmount = 120_000m,
                CreatedAt = tIn,
                ConfirmedAt = tIn,
                CompletedAt = tIn
            };
            inRangeOrder.Items.Add(new OrderItem
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                OrderId = inRangeOrder.Id,
                SkuId = sku1.Id,
                SkuCodeSnapshot = sku1.SKU,
                SkuNameSnapshot = "SKU 1",
                UnitNameSnapshot = "Cái",
                FactorSnapshot = 1m,
                Quantity = 1m,
                UnitPrice = 120_000m,
                CostPrice = 70_000m
            });

            // Exactly at half-open end boundary (2026-11-11 00:00:00+07:00) -> excluded
            var tOut = new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime();
            var outOfRangeOrder = new Order
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Number = "ORD-BOUND-OUT",
                BranchId = branch1.Id,
                Channel = "POS",
                Status = "Completed",
                TotalAmount = 999_000m,
                CreatedAt = tOut,
                ConfirmedAt = tOut,
                CompletedAt = tOut
            };
            outOfRangeOrder.Items.Add(new OrderItem
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                OrderId = outOfRangeOrder.Id,
                SkuId = sku1.Id,
                SkuCodeSnapshot = sku1.SKU,
                SkuNameSnapshot = "SKU 1",
                UnitNameSnapshot = "Cái",
                FactorSnapshot = 1m,
                Quantity = 1m,
                UnitPrice = 999_000m,
                CostPrice = 500_000m
            });

            // Different channel -> excluded
            var tMid = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime();
            var shopeeOrder = new Order
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Number = "ORD-CHAN-OUT",
                BranchId = branch1.Id,
                Channel = "Admin",
                Status = "Completed",
                TotalAmount = 500_000m,
                CreatedAt = tMid,
                ConfirmedAt = tMid,
                CompletedAt = tMid
            };
            shopeeOrder.Items.Add(new OrderItem
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                OrderId = shopeeOrder.Id,
                SkuId = sku1.Id,
                SkuCodeSnapshot = sku1.SKU,
                SkuNameSnapshot = "SKU 1",
                UnitNameSnapshot = "Cái",
                FactorSnapshot = 1m,
                Quantity = 1m,
                UnitPrice = 500_000m,
                CostPrice = 200_000m
            });

            // Different branch -> excluded
            var branch2Order = new Order
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Number = "ORD-BR-OUT",
                BranchId = branch2.Id,
                Channel = "POS",
                Status = "Completed",
                TotalAmount = 400_000m,
                CreatedAt = tMid,
                ConfirmedAt = tMid,
                CompletedAt = tMid
            };
            branch2Order.Items.Add(new OrderItem
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                OrderId = branch2Order.Id,
                SkuId = sku1.Id,
                SkuCodeSnapshot = sku1.SKU,
                SkuNameSnapshot = "SKU 1",
                UnitNameSnapshot = "Cái",
                FactorSnapshot = 1m,
                Quantity = 1m,
                UnitPrice = 400_000m,
                CostPrice = 100_000m
            });

            ctx.Orders.AddRange(inRangeOrder, outOfRangeOrder, shopeeOrder, branch2Order);
            await ctx.SaveChangesAsync();
        }

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var start = Uri.EscapeDataString("2026-11-10T00:00:00+07:00");
        var end = Uri.EscapeDataString("2026-11-11T00:00:00+07:00");
        var res = await _client.GetAsync($"/api/reports/revenue-profit?skuIds={sku1Id}&branchId={branch1Id}&channel=POS&startDate={start}&endDate={end}");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = await res.Content.ReadFromJsonAsync<RevenueProfitReportDto>(_jsonOptions);
        report.Should().NotBeNull();
        report!.Revenue.Should().Be(120_000m);
        report.CostOfGoodsSold.Should().Be(70_000m);
        report.GrossProfit.Should().Be(50_000m);
        report.TotalOrders.Should().Be(1);
        report.SkuBreakdown.Should().ContainSingle(x => x.SkuId == sku1Id && x.Revenue == 120_000m);
    }

    [Fact]
    public async Task RevenueProfit_MatchesHandCalculatedSnapshot_AcrossBranchesDiscountAndReturn_FinJ_AC02()
    {
        await SeedAsync();

        Guid tenantId;
        Guid ownerId;
        Guid branch1Id;
        Guid branch2Id;
        Guid sku1Id;
        Guid sku2Id;

        var (seedCtx, _) = CreatePostgresDbContext(null);
        await using (seedCtx)
        {
            var owner = await seedCtx.Users.IgnoreQueryFilters().FirstAsync(u => u.Email == "owner@shopa.local");
            tenantId = owner.TenantId;
            ownerId = owner.Id;
        }

        var (ctx, _) = CreatePostgresDbContext(tenantId);
        await using (ctx)
        {
            var branch1 = await ctx.Branches.FirstAsync();
            branch1Id = branch1.Id;
            var warehouse1 = await ctx.Warehouses.FirstAsync(w => w.BranchId == branch1Id);
            var branch2 = new Branch
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Chi nhánh B (FIN-J)",
                Address = "HCM",
                IsActive = true
            };
            ctx.Branches.Add(branch2);
            branch2Id = branch2.Id;

            var prod = new Product { Id = Guid.NewGuid(), TenantId = tenantId, Name = "SP FIN-J" };
            var sku1 = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, ProductId = prod.Id, SKU = "FINJ-SKU-01" };
            var sku2 = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenantId, ProductId = prod.Id, SKU = "FINJ-SKU-02" };
            ctx.Products.Add(prod);
            ctx.Skus.AddRange(sku1, sku2);
            var skus = new List<ProductVariant> { sku1, sku2 };
            sku1Id = sku1.Id;
            sku2Id = sku2.Id;

            var tDay1Order1 = DateTimeOffset.Parse("2025-05-01T10:00:00+07:00").ToUniversalTime();
            var tDay1Order2 = DateTimeOffset.Parse("2025-05-01T14:00:00+07:00").ToUniversalTime();
            var tDay2Return = DateTimeOffset.Parse("2025-05-02T09:00:00+07:00").ToUniversalTime();

            // Đơn 1 ở Chi nhánh 1 (chiết khấu cấp đơn 30k trên 300k -> TotalAmount = 270k)
            var order1 = new Order
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BranchId = branch1Id,
                Number = "FINJ-ORD-01",
                Channel = "POS",
                Status = "Completed",
                TotalAmount = 270_000m,
                CreatedAt = tDay1Order1,
                ConfirmedAt = tDay1Order1,
                CompletedAt = tDay1Order1
            };
            var order1Item1 = new OrderItem
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                OrderId = order1.Id,
                SkuId = sku1Id,
                SkuCodeSnapshot = skus[0].SKU,
                SkuNameSnapshot = "SKU 1",
                UnitNameSnapshot = "Cái",
                FactorSnapshot = 1m,
                Quantity = 2m,
                UnitPrice = 100_000m,
                CostPrice = 60_000m
            };
            var order1Item2 = new OrderItem
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                OrderId = order1.Id,
                SkuId = sku2Id,
                SkuCodeSnapshot = skus[1].SKU,
                SkuNameSnapshot = "SKU 2",
                UnitNameSnapshot = "Cái",
                FactorSnapshot = 1m,
                Quantity = 1m,
                UnitPrice = 100_000m,
                CostPrice = 50_000m
            };
            order1.Items.Add(order1Item1);
            order1.Items.Add(order1Item2);

            // Đơn 2 ở Chi nhánh 2
            var order2 = new Order
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BranchId = branch2Id,
                Number = "FINJ-ORD-02",
                Channel = "Admin",
                Status = "Confirmed",
                TotalAmount = 100_000m,
                CreatedAt = tDay1Order2,
                ConfirmedAt = tDay1Order2
            };
            order2.Items.Add(new OrderItem
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                OrderId = order2.Id,
                SkuId = sku1Id,
                SkuCodeSnapshot = skus[0].SKU,
                SkuNameSnapshot = "SKU 1",
                UnitNameSnapshot = "Cái",
                FactorSnapshot = 1m,
                Quantity = 1m,
                UnitPrice = 100_000m,
                CostPrice = 60_000m
            });

            // Phiếu trả hàng ở ngày 2 (khác ngày bán) cho SKU 1 của Đơn 1
            var salesReturn = new SalesReturn
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BranchId = branch1Id,
                WarehouseId = warehouse1.Id,
                OrderId = order1.Id,
                Number = "FINJ-RET-01",
                Status = "Confirmed",
                TotalRefundAmount = 90_000m,
                ConfirmedAt = tDay2Return,
                ConfirmedBy = ownerId,
                CreatedBy = ownerId,
                CreatedAt = tDay2Return
            };
            salesReturn.Items.Add(new SalesReturnItem
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                SalesReturnId = salesReturn.Id,
                OrderId = order1.Id,
                OrderItemId = order1Item1.Id,
                SkuId = sku1Id,
                Quantity = 1m,
                RefundAmount = 90_000m,
                OriginalUnitCost = 60_000m
            });

            ctx.Orders.AddRange(order1, order2);
            ctx.SalesReturns.Add(salesReturn);
            await ctx.SaveChangesAsync();
        }

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var start = Uri.EscapeDataString("2025-05-01T00:00:00+07:00");
        var end = Uri.EscapeDataString("2025-05-03T00:00:00+07:00");

        // 1. Không lọc SKU
        var unfilteredRes = await _client.GetAsync($"/api/reports/revenue-profit?startDate={start}&endDate={end}");
        unfilteredRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var unfiltered = await unfilteredRes.Content.ReadFromJsonAsync<RevenueProfitReportDto>(_jsonOptions);
        unfiltered.Should().NotBeNull();
        unfiltered!.Revenue.Should().Be(370_000m);
        unfiltered.ReturnAmount.Should().Be(90_000m);
        unfiltered.NetRevenue.Should().Be(280_000m);
        unfiltered.CostOfGoodsSold.Should().Be(230_000m);
        unfiltered.ReturnCogs.Should().Be(60_000m);
        unfiltered.NetCogs.Should().Be(170_000m);
        unfiltered.GrossProfit.Should().Be(110_000m);
        unfiltered.TotalOrders.Should().Be(2);
        unfiltered.ByBranch.Should().HaveCount(2);
        unfiltered.ByBranch.Should().Contain(b => b.BranchId == branch1Id && b.NetRevenue == 180_000m && b.NetCogs == 110_000m && b.GrossProfit == 70_000m);
        unfiltered.ByBranch.Should().Contain(b => b.BranchId == branch2Id && b.NetRevenue == 100_000m && b.NetCogs == 60_000m && b.GrossProfit == 40_000m);
        unfiltered.DailyTrend.Should().HaveCount(2);
        unfiltered.DailyTrend[0].Date.Should().Be("2025-05-01");
        unfiltered.DailyTrend[0].GrossProfit.Should().Be(140_000m);
        unfiltered.DailyTrend[1].Date.Should().Be("2025-05-02");
        unfiltered.DailyTrend[1].NetRevenue.Should().Be(-90_000m);
        unfiltered.DailyTrend[1].NetCogs.Should().Be(-60_000m);
        unfiltered.DailyTrend[1].GrossProfit.Should().Be(-30_000m);

        // 2. Lọc theo SKU 1
        var filteredRes = await _client.GetAsync($"/api/reports/revenue-profit?skuIds={sku1Id}&startDate={start}&endDate={end}");
        filteredRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var filtered = await filteredRes.Content.ReadFromJsonAsync<RevenueProfitReportDto>(_jsonOptions);
        filtered.Should().NotBeNull();
        filtered!.Revenue.Should().Be(280_000m);
        filtered.ReturnAmount.Should().Be(90_000m);
        filtered.NetRevenue.Should().Be(190_000m);
        filtered.CostOfGoodsSold.Should().Be(180_000m);
        filtered.ReturnCogs.Should().Be(60_000m);
        filtered.NetCogs.Should().Be(120_000m);
        filtered.GrossProfit.Should().Be(70_000m);
        filtered.TotalOrders.Should().Be(2);
        filtered.SkuBreakdown.Should().ContainSingle(s =>
            s.SkuId == sku1Id
            && s.Revenue == 280_000m
            && s.ReturnAmount == 90_000m
            && s.NetRevenue == 190_000m
            && s.CostOfGoodsSold == 180_000m
            && s.ReturnCogs == 60_000m
            && s.NetCogs == 120_000m
            && s.GrossProfit == 70_000m
            && s.OrderCount == 2);
    }
}
