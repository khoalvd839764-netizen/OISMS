using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Upgrade 14 (OISMS-66-U14-D): Kiểm thử OismsDocumentSeeder trên PostgreSQL thật:
/// - AC-01 (Q12): Mỗi tenant demo có đủ chuỗi chứng từ (PO -> GR, POS, Online Order, StockTransfer 2 bước, StockCount có thừa & thiếu, SalesReturn một phần, InventoryAdjustment).
/// - AC-02 (Q12): Idempotent — chạy 2 lần liên tiếp, lần 2 không tạo thêm chứng từ hay dòng inventory_transactions.
/// - AC-03: Mọi dòng sổ có DocumentType != Unknown; dòng OUT bán hàng có GoodsIssueId; on_hand = balance_after cuối mỗi (BranchId, SkuId); Available >= 0.
/// </summary>
[Collection("PostgresIntegrationTests")]
public class PostgresU14DocumentSeederIntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class TestTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    public PostgresU14DocumentSeederIntegrationTests(WebApplicationFactoryWithPostgres factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => PostgresTestEnvironment.ResetTestDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private AppDbContext CreateDb(Guid? tenantId = null)
    {
        var ts = new TestTenantService();
        if (tenantId.HasValue) ts.SetTenant(tenantId.Value);
        return PostgresTestEnvironment.CreateRuntimeDbContext(ts);
    }

    private async Task<string> LoginAsAsync(string email)
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var res = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = PostgresTestEnvironment.AppPassword
        });
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        return auth!.AccessToken;
    }

    [Fact]
    public async Task SeedAsync_Twice_IsIdempotent_AndSatisfiesLedgerAndBalanceInvariants()
    {
        // 1. Seed danh mục nền + chạy OismsDocumentSeeder lần 1
        await using (var db = CreateDb(null))
        {
            await OismsDatabaseSeeder.SeedAsync(db, PostgresTestEnvironment.AppPassword);
        }

        await OismsDocumentSeeder.SeedAsync(_factory.Services);

        int txCount1, poCount1, grCount1, orderCount1, giCount1, stCount1, scCount1, adjCount1, srCount1;
        await using (var db = CreateDb(null))
        {
            txCount1 = await db.InventoryTransactions.IgnoreQueryFilters().CountAsync();
            poCount1 = await db.PurchaseOrders.IgnoreQueryFilters().CountAsync();
            grCount1 = await db.GoodsReceipts.IgnoreQueryFilters().CountAsync();
            orderCount1 = await db.Orders.IgnoreQueryFilters().CountAsync();
            giCount1 = await db.GoodsIssues.IgnoreQueryFilters().CountAsync();
            stCount1 = await db.StockTransfers.IgnoreQueryFilters().CountAsync();
            scCount1 = await db.StockCounts.IgnoreQueryFilters().CountAsync();
            adjCount1 = await db.InventoryAdjustments.IgnoreQueryFilters().CountAsync();
            srCount1 = await db.SalesReturns.IgnoreQueryFilters().CountAsync();
        }

        poCount1.Should().BeGreaterThanOrEqualTo(2);
        grCount1.Should().BeGreaterThanOrEqualTo(2);
        orderCount1.Should().BeGreaterThanOrEqualTo(4); // 2 POS + 2 Online
        giCount1.Should().BeGreaterThanOrEqualTo(4);
        stCount1.Should().BeGreaterThanOrEqualTo(2);
        scCount1.Should().BeGreaterThanOrEqualTo(2);
        adjCount1.Should().BeGreaterThanOrEqualTo(2);
        srCount1.Should().BeGreaterThanOrEqualTo(2);
        txCount1.Should().BeGreaterThan(0);

        // 2. Chạy lại lần 2 -> tuyệt đối không tạo thêm bản ghi (AC-02)
        await OismsDocumentSeeder.SeedAsync(_factory.Services);

        await using (var db = CreateDb(null))
        {
            (await db.InventoryTransactions.IgnoreQueryFilters().CountAsync()).Should().Be(txCount1);
            (await db.PurchaseOrders.IgnoreQueryFilters().CountAsync()).Should().Be(poCount1);
            (await db.GoodsReceipts.IgnoreQueryFilters().CountAsync()).Should().Be(grCount1);
            (await db.Orders.IgnoreQueryFilters().CountAsync()).Should().Be(orderCount1);
            (await db.GoodsIssues.IgnoreQueryFilters().CountAsync()).Should().Be(giCount1);
            (await db.StockTransfers.IgnoreQueryFilters().CountAsync()).Should().Be(stCount1);
            (await db.StockCounts.IgnoreQueryFilters().CountAsync()).Should().Be(scCount1);
            (await db.InventoryAdjustments.IgnoreQueryFilters().CountAsync()).Should().Be(adjCount1);
            (await db.SalesReturns.IgnoreQueryFilters().CountAsync()).Should().Be(srCount1);
        }

        // 3. Kiểm tra từng tenant: sổ kho qua API + invariant on_hand == balance_after cuối + Available >= 0 (AC-03)
        foreach (var (tenantId, ownerEmail) in new[]
        {
            (OismsDatabaseSeeder.TenantAId, "owner@shopa.local"),
            (OismsDatabaseSeeder.TenantBId, "owner@shopb.local"),
        })
        {
            var token = await LoginAsAsync(ownerEmail);
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var ledgerRes = await _client.GetAsync("/api/inventory/ledger?pageSize=100");
            ledgerRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var ledger = await ledgerRes.Content.ReadFromJsonAsync<PagedResult<InventoryLedgerDto>>(_jsonOptions);
            ledger.Should().NotBeNull();
            ledger!.Items.Should().NotBeEmpty();

            ledger.Items.Should().AllSatisfy(item =>
            {
                item.DocumentType.Should().NotBe("Unknown", $"dòng sổ {item.Id} của {ownerEmail} không được là Unknown");
                // Mọi dòng sổ phải trỏ được tới chứng từ để nút "Chứng từ" mở mẫu in (kể cả trả hàng).
                item.DocumentId.Should().NotBeNull($"dòng sổ {item.Id} ({item.DocumentType}) phải có DocumentId");
                if (item.DocumentType == "Order" && item.Type == "OUT")
                {
                    item.GoodsIssueId.Should().NotBeNull($"dòng OUT bán hàng {item.Id} phải có GoodsIssueId");
                }
            });

            var docTypes = ledger.Items.Select(i => i.DocumentType).Distinct().ToHashSet();
            docTypes.Should().Contain(new[] { "GoodsReceipt", "Order", "StockTransfer", "StockCount", "SalesReturn" });

            await using var db = CreateDb(tenantId);
            var balances = await db.InventoryBalances.Where(b => b.TenantId == tenantId).ToListAsync();
            foreach (var bal in balances)
            {
                (bal.OnHand - bal.Reserved).Should().BeGreaterThanOrEqualTo(0m);
                var lastTx = await db.InventoryTransactions
                    .Where(t => t.TenantId == tenantId && t.BranchId == bal.BranchId && t.SkuId == bal.SkuId)
                    .OrderByDescending(t => t.SequenceNo)
                    .FirstOrDefaultAsync();
                if (lastTx != null)
                {
                    bal.OnHand.Should().Be(lastTx.BalanceAfter);
                }
            }

            var whBalances = await db.WarehouseInventoryBalances.Where(w => w.TenantId == tenantId).ToListAsync();
            whBalances.Should().AllSatisfy(w => (w.OnHand - w.Reserved).Should().BeGreaterThanOrEqualTo(0m));
        }
    }
}
