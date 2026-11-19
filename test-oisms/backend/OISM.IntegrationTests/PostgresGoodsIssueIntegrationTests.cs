using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.GoodsIssues.Dtos;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.SalesInvoices.Dtos;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Bộ kiểm thử tích hợp trên PostgreSQL 16 thật cho OISMS-66-L2-G3 (Upgrade 13):
/// Phiếu xuất kho 02-VT lưu kèm dòng sổ OUT, in đơn lẻ/hàng loạt, bảng kê xuất bán POS theo ngày.
/// Bao phủ đầy đủ AC-01 -> AC-06.
/// </summary>
[Collection("PostgresIntegrationTests")]
public class PostgresGoodsIssueIntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class TestTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    public PostgresGoodsIssueIntegrationTests(WebApplicationFactoryWithPostgres factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
    }

    public async Task DisposeAsync()
    {
        try
        {
            await using var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(@"
                DROP TRIGGER IF EXISTS oisms_test_fail_goods_issue_trg ON public.goods_issue_items;
                DROP FUNCTION IF EXISTS public.oisms_test_fail_goods_issue();
            ", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private (AppDbContext Context, TestTenantService TenantService) CreatePostgresDbContext(Guid? tenantId = null)
    {
        var ts = new TestTenantService();
        if (tenantId.HasValue)
        {
            ts.SetTenant(tenantId.Value);
        }
        var ctx = PostgresTestEnvironment.CreateRuntimeDbContext(ts);
        return (ctx, ts);
    }

    private async Task SeedDatabaseAsync()
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
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        return auth!.AccessToken;
    }

    private async Task SeedStockAsync(
        Guid tenantId,
        Guid branchId,
        Guid warehouseId,
        Guid supplierId,
        Guid userId,
        Guid skuId,
        decimal quantity,
        decimal unitPrice)
    {
        var (context, tenantService) = CreatePostgresDbContext(tenantId);
        await using (context)
        {
            await PriceSeed.SyncFromLegacyColumnsAsync(context);
            await PurchaseOrderSeed.ReceiveStockAsync(context, tenantService, branchId, warehouseId, supplierId, userId, (skuId, quantity, unitPrice));
        }
    }

    private static string LoadGoodsIssuesMigrationSql()
    {
        var startDirs = new[] { AppDomain.CurrentDomain.BaseDirectory, Directory.GetCurrentDirectory() };
        foreach (var startDir in startDirs)
        {
            var dir = new DirectoryInfo(startDir);
            for (int i = 0; i < 8 && dir != null; i++)
            {
                var candidate = Path.Combine(dir.FullName, "docker", "postgres", "migrations", "20261119_goods_issues.sql");
                if (File.Exists(candidate))
                {
                    return DatabaseMigrationService.CleanDdlForConnectedDatabase(File.ReadAllText(candidate));
                }
                dir = dir.Parent;
            }
        }
        throw new FileNotFoundException("Không tìm thấy file migration docker/postgres/migrations/20261119_goods_issues.sql.");
    }

    // =========================================================================
    // AC-01: MIGRATION 20261119_goods_issues.sql CHẠY 2 LẦN TRÊN DB TRỐNG PASS,
    //        TỪ CHỐI NẾU ĐÃ CÓ DÒNG OUT BÁN HÀNG CHƯA CÓ PHIẾU, APPEND-ONLY
    // =========================================================================
    [Fact]
    public async Task AC01_MigrationIdempotentTwiceOnCleanDb_RejectsExistingUnlinkedSalesOut_AndEnforcesAppendOnly()
    {
        await SeedDatabaseAsync();

        var migrationSql = LoadGoodsIssuesMigrationSql();

        // 1. Chạy migration 2 lần liên tiếp trên DB sạch -> cả 2 lần đều thành công, không lỗi
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            for (int run = 1; run <= 2; run++)
            {
                await using var cmd = new NpgsqlCommand(migrationSql, conn);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        DatabaseMigrationService.RequiredTables.Length.Should().Be(50);
        DatabaseMigrationService.RequiredTables.Should().Contain(["goods_issues", "goods_issue_items"]);

        // 2. Tạo 1 đơn POS để có dòng OUT + phiếu xuất kho hợp lệ
        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, cashierId, skuId;
        string skuCode;
        decimal price;

        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            var wh = await ctx.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId);
            warehouseId = wh.Id;
            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            supplierId = supplier.Id;
            var user = await ctx.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "cashier@shopa.local");
            cashierId = user.Id;
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = sku.Id;
            skuCode = sku.SKU;
            price = sku.RetailPrice;
        }

        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, cashierId, skuId, 50m, 25000m);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        var posRes = await _client.PostAsJsonAsync("/api/pos/checkout", new PosCheckoutRequestDto
        {
            BranchId = branchId,
            RequestKey = Guid.NewGuid(),
            PaymentMethod = "Cash",
            AmountGiven = price * 2m,
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2m, UnitPrice = price } }
        });
        posRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var posData = await posRes.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);

        Guid goodsIssueId;
        Guid goodsIssueItemId;
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var gi = await ctx.GoodsIssues.Include(g => g.Items).FirstAsync(g => g.OrderId == posData!.OrderId);
            goodsIssueId = gi.Id;
            goodsIssueItemId = gi.Items.First().Id;
        }

        // 3. Dưới tài khoản runtime (oisms_test_app): UPDATE và DELETE trên goods_issues & goods_issue_items bị chặn
        await using (var runtimeConn = new NpgsqlConnection(PostgresTestEnvironment.RuntimeConnectionString))
        {
            await runtimeConn.OpenAsync();

            Func<Task> actUpdateIssue = async () =>
            {
                await using var cmd = new NpgsqlCommand("UPDATE public.goods_issues SET number = 'PXK-HACK' WHERE id = @id;", runtimeConn);
                cmd.Parameters.AddWithValue("id", goodsIssueId);
                await cmd.ExecuteNonQueryAsync();
            };
            await actUpdateIssue.Should().ThrowAsync<PostgresException>();

            Func<Task> actDeleteIssueItem = async () =>
            {
                await using var cmd = new NpgsqlCommand("DELETE FROM public.goods_issue_items WHERE id = @id;", runtimeConn);
                cmd.Parameters.AddWithValue("id", goodsIssueItemId);
                await cmd.ExecuteNonQueryAsync();
            };
            await actDeleteIssueItem.Should().ThrowAsync<PostgresException>();
        }

        // 4. Nếu DB có dòng inventory_transactions có order_item_id mà chưa có phiếu xuất kho (hoặc trước khi có bảng goods_issue_items),
        //    chạy migration phải RAISE EXCEPTION hướng dẫn reset DB
        await using (var adminConn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await adminConn.OpenAsync();
            await using var tx = await adminConn.BeginTransactionAsync();
            try
            {
                // Mô phỏng trạng thái DB cũ trước nâng cấp: xóa tạm bảng goods_issue_items & goods_issues trong transaction khi đã có dòng OUT bán hàng
                await using (var dropCmd = new NpgsqlCommand("DROP TABLE public.goods_issue_items; DROP TABLE public.goods_issues;", adminConn, tx))
                {
                    await dropCmd.ExecuteNonQueryAsync();
                }

                Func<Task> actMigrationOnLegacyOut = async () =>
                {
                    await using var migCmd = new NpgsqlCommand(migrationSql, adminConn, tx);
                    await migCmd.ExecuteNonQueryAsync();
                };

                var ex = await actMigrationOnLegacyOut.Should().ThrowAsync<PostgresException>();
                ex.Which.MessageText.Should().Contain("order_item_id").And.Contain("reset");
            }
            finally
            {
                await tx.RollbackAsync();
            }
        }
    }

    // =========================================================================
    // AC-02 & AC-03: POS CHECKOUT, ADMIN CONFIRM, ĐƠN NHIỀU KHO (PXK-<INV>-1, -2),
    //                KHỚP 1-1 DÒNG SỔ OUT VÀ GIÁ VỐN 6 CHỮ SỐ THẬP PHÂN
    // =========================================================================
    [Fact]
    public async Task AC02_AC03_SingleAndMultiWarehouse_CreateExact1To1GoodsIssueItems_WithDeterministicWarehouseOrder()
    {
        await SeedDatabaseAsync();
        var staffToken = await LoginAsAsync("staff@shopa.local", PostgresTestEnvironment.AppPassword);
        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, wh1Id, wh2Id, supplierId, staffId, skuId;
        string skuCode, wh1Name;
        decimal price;
        const decimal exactWac = 34567.123456m;

        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            var wh1 = await ctx.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId);
            wh1Id = wh1.Id;
            wh1Name = wh1.Name;

            var wh2 = new Warehouse
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BranchId = branchId,
                Name = "Kho Phụ Chi Nhánh 1",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(1)
            };
            ctx.Warehouses.Add(wh2);
            await ctx.SaveChangesAsync();
            wh2Id = wh2.Id;

            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            supplierId = supplier.Id;
            var user = await ctx.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "staff@shopa.local");
            staffId = user.Id;
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = sku.Id;
            skuCode = sku.SKU;
            price = sku.RetailPrice;
        }

        // Nhập 5 SP vào Kho 1 và 5 SP vào Kho 2 với cùng đơn giá vốn 34567.123456m
        await SeedStockAsync(tenantId, branchId, wh1Id, supplierId, staffId, skuId, 5m, exactWac);
        await SeedStockAsync(tenantId, branchId, wh2Id, supplierId, staffId, skuId, 5m, exactWac);

        // 1. Đơn POS xuất từ 1 kho (SL = 3 <= 5) -> Số phiếu PXK-<số hóa đơn> (không hậu tố -1)
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        var posRes = await _client.PostAsJsonAsync("/api/pos/checkout", new PosCheckoutRequestDto
        {
            BranchId = branchId,
            RequestKey = Guid.NewGuid(),
            PaymentMethod = "Cash",
            AmountGiven = 2000000m,
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 3m, UnitPrice = price } }
        });
        posRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var posData = await posRes.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);

        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var inv = await ctx.SalesInvoices.FirstAsync(s => s.OrderId == posData!.OrderId);
            var issues = await ctx.GoodsIssues.Include(g => g.Items).Where(g => g.OrderId == posData!.OrderId).ToListAsync();

            issues.Should().HaveCount(1, "Xuất từ 1 kho tạo đúng 1 phiếu xuất kho");
            issues[0].Number.Should().Be($"PXK-{inv.Number}");
            issues[0].SalesInvoiceId.Should().Be(inv.Id);
            issues[0].Items.Should().HaveCount(1);
            issues[0].Items.First().Quantity.Should().Be(3m);
            issues[0].Items.First().BaseQuantity.Should().Be(3m);
            issues[0].Items.First().UnitCost.Should().Be(exactWac);
        }

        // 2. Đơn Admin xuất từ 2 kho (còn 2 ở kho đầu + 5 ở kho thứ hai, đặt mua 6 SP -> bắt buộc phân bổ qua cả 2 kho)
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var createOrderRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 6m, UnitPrice = price } }
        });
        createOrderRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var adminOrder = await createOrderRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);

        var confirmRes = await _client.PostAsJsonAsync($"/api/orders/{adminOrder!.Id}/confirm", new ConfirmOrderRequestDto());
        confirmRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var expectedSortedWhIds = new[] { wh1Id, wh2Id }.OrderBy(id => id).ToArray();

        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var inv = await ctx.SalesInvoices.FirstAsync(s => s.OrderId == adminOrder.Id);
            var issues = await ctx.GoodsIssues
                .Include(g => g.Items)
                .Where(g => g.OrderId == adminOrder.Id)
                .OrderBy(g => g.WarehouseId)
                .ToListAsync();

            issues.Should().HaveCount(2, "Đơn xuất từ 2 kho phải tạo 2 phiếu xuất kho riêng theo từng kho");
            issues[0].WarehouseId.Should().Be(expectedSortedWhIds[0]);
            issues[0].Number.Should().Be($"PXK-{inv.Number}-1");
            issues[0].WarehouseNameSnapshot.Should().NotBeNullOrWhiteSpace();

            issues[1].WarehouseId.Should().Be(expectedSortedWhIds[1]);
            issues[1].Number.Should().Be($"PXK-{inv.Number}-2");
            issues[1].WarehouseNameSnapshot.Should().NotBeNullOrWhiteSpace();

            issues.SelectMany(g => g.Items).Sum(i => i.BaseQuantity).Should().Be(6m);

            // Kiểm tra bất biến toàn hệ thống: dòng OUT bán hàng không có phiếu = 0
            var unlinkedOutCount = await ctx.InventoryTransactions
                .Where(t => t.TenantId == tenantId && t.Type == "OUT" && t.OrderItemId != null)
                .CountAsync(t => !ctx.GoodsIssueItems.Any(gii => gii.TenantId == tenantId && gii.InventoryTransactionId == t.Id));

            unlinkedOutCount.Should().Be(0, "Mọi dòng OUT bán hàng đều phải có đúng 1 dòng phiếu xuất kho");
        }
    }

    // =========================================================================
    // AC-04: IDEMPOTENT REPLAY KHÔNG TẠO THÊM PHIẾU & LỖI GIỮA CHỪNG ROLLBACK SẠCH
    // =========================================================================
    [Fact]
    public async Task AC04_IdempotentReplayDoesNotDuplicateGoodsIssue_AndMidTransactionFailureRollsBackCompletely()
    {
        await SeedDatabaseAsync();
        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        var staffToken = await LoginAsAsync("staff@shopa.local", PostgresTestEnvironment.AppPassword);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, staffId, skuId;
        string skuCode;
        decimal price;

        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            var wh = await ctx.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId);
            warehouseId = wh.Id;
            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            supplierId = supplier.Id;
            var user = await ctx.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "staff@shopa.local");
            staffId = user.Id;
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = sku.Id;
            skuCode = sku.SKU;
            price = sku.RetailPrice;
        }

        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, staffId, skuId, 100m, 40000m);

        // 1. Replay POS checkout cùng RequestKey -> chỉ có đúng 1 phiếu xuất kho
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        var reqKey = Guid.NewGuid();
        var dto = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            RequestKey = reqKey,
            PaymentMethod = "Cash",
            AmountGiven = 1000000m,
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2m, UnitPrice = price } }
        };

        var res1 = await _client.PostAsJsonAsync("/api/pos/checkout", dto);
        res1.StatusCode.Should().Be(HttpStatusCode.OK);
        var data1 = await res1.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);

        var res2 = await _client.PostAsJsonAsync("/api/pos/checkout", dto);
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        var data2 = await res2.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);

        data2!.OrderId.Should().Be(data1!.OrderId);

        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var giCount = await ctx.GoodsIssues.CountAsync(g => g.OrderId == data1.OrderId);
            giCount.Should().Be(1, "Replay cùng RequestKey tuyệt đối không sinh thêm phiếu xuất kho");
        }

        // 2. Lỗi giữa chừng khi chèn goods_issue_items -> rollback toàn bộ (không còn phiếu xuất, hóa đơn, hay dòng OUT mồ côi)
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(@"
                CREATE OR REPLACE FUNCTION public.oisms_test_fail_goods_issue() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Simulated goods_issue_items failure for AC-04' USING ERRCODE='23514';
                END $$;
                DROP TRIGGER IF EXISTS oisms_test_fail_goods_issue_trg ON public.goods_issue_items;
                CREATE TRIGGER oisms_test_fail_goods_issue_trg BEFORE INSERT ON public.goods_issue_items
                FOR EACH ROW EXECUTE FUNCTION public.oisms_test_fail_goods_issue();
            ", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        try
        {
            var failPosKey = Guid.NewGuid();
            var failPosRes = await _client.PostAsJsonAsync("/api/pos/checkout", new PosCheckoutRequestDto
            {
                BranchId = branchId,
                RequestKey = failPosKey,
                PaymentMethod = "Cash",
                AmountGiven = 1000000m,
                Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1m, UnitPrice = price } }
            });
            failPosRes.IsSuccessStatusCode.Should().BeFalse();

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
            var createRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
            {
                BranchId = branchId,
                Channel = "Admin",
                RequestKey = Guid.NewGuid(),
                Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1m, UnitPrice = price } }
            });
            var reservedOrder = await createRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);

            var failConfirmRes = await _client.PostAsJsonAsync($"/api/orders/{reservedOrder!.Id}/confirm", new ConfirmOrderRequestDto());
            failConfirmRes.IsSuccessStatusCode.Should().BeFalse();

            await using (var ctx = CreatePostgresDbContext(tenantId).Context)
            {
                (await ctx.Orders.AnyAsync(o => o.RequestKey == failPosKey)).Should().BeFalse();
                (await ctx.GoodsIssues.AnyAsync(g => g.OrderId == reservedOrder.Id)).Should().BeFalse();
                (await ctx.SalesInvoices.AnyAsync(s => s.OrderId == reservedOrder.Id)).Should().BeFalse();

                var dbOrder = await ctx.Orders.FirstAsync(o => o.Id == reservedOrder.Id);
                dbOrder.Status.Should().Be("Reserved");
            }
        }
        finally
        {
            await using var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString);
            await conn.OpenAsync();
            await using var dropCmd = new NpgsqlCommand(@"
                DROP TRIGGER IF EXISTS oisms_test_fail_goods_issue_trg ON public.goods_issue_items;
                DROP FUNCTION IF EXISTS public.oisms_test_fail_goods_issue();
            ", conn);
            await dropCmd.ExecuteNonQueryAsync();
        }
    }

    // =========================================================================
    // AC-05 & AC-06: API XEM PHIẾU, BẢNG KÊ POS THEO NGÀY (UTC+7), CHE GIÁ STAFF,
    //                CASHIER 403, SỔ CÁI & TRUY VẾT CÓ SỐ PHIẾU XUẤT
    // =========================================================================
    [Fact]
    public async Task AC05_AC06_GoodsIssueApis_PosDailySummaryUtcPlus7_OwnerHasCost_StaffNull_Cashier403_LedgerAndTraceIncludeIssueNumber()
    {
        await SeedDatabaseAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        var staffToken = await LoginAsAsync("staff@shopa.local", PostgresTestEnvironment.AppPassword);
        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        var tenantBToken = await LoginAsAsync("owner@shopb.local", PostgresTestEnvironment.AppPassword);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, staffId, skuId;
        string skuCode;
        decimal price;
        const decimal unitCostWac = 45000m;

        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            var wh = await ctx.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId);
            warehouseId = wh.Id;
            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            supplierId = supplier.Id;
            var user = await ctx.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "staff@shopa.local");
            staffId = user.Id;
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = sku.Id;
            skuCode = sku.SKU;
            price = sku.RetailPrice;
        }

        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, staffId, skuId, 100m, unitCostWac);

        // Tạo 2 đơn POS và 1 đơn Admin đã xác nhận trong cùng ngày hôm nay (UTC+7)
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        var pos1Res = await _client.PostAsJsonAsync("/api/pos/checkout", new PosCheckoutRequestDto
        {
            BranchId = branchId,
            RequestKey = Guid.NewGuid(),
            PaymentMethod = "Cash",
            AmountGiven = 1000000m,
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2m, UnitPrice = price } }
        });
        pos1Res.StatusCode.Should().Be(HttpStatusCode.OK);
        var pos1 = await pos1Res.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);

        var pos2Res = await _client.PostAsJsonAsync("/api/pos/checkout", new PosCheckoutRequestDto
        {
            BranchId = branchId,
            RequestKey = Guid.NewGuid(),
            PaymentMethod = "Cash",
            AmountGiven = 1000000m,
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 3m, UnitPrice = price } }
        });
        pos2Res.StatusCode.Should().Be(HttpStatusCode.OK);
        var pos2 = await pos2Res.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var adminOrderRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 4m, UnitPrice = price } }
        });
        var adminOrder = await adminOrderRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
        var confirmAdminRes = await _client.PostAsJsonAsync($"/api/orders/{adminOrder!.Id}/confirm", new ConfirmOrderRequestDto());
        confirmAdminRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var vnToday = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).ToString("yyyy-MM-dd");

        // 1. Cashier gọi các API phiếu xuất kho và bảng kê POS -> 403 Forbidden
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        (await _client.GetAsync($"/api/goods-issues/by-order/{pos1!.OrderId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.GetAsync($"/api/goods-issues/pos-daily-summary?date={vnToday}&branchId={branchId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 2. Owner xem phiếu theo đơn, chi tiết phiếu, in hàng loạt và bảng kê POS theo ngày -> có đầy đủ UnitCost và TotalCost
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var ownerIssuesRes = await _client.GetAsync($"/api/goods-issues/by-order/{pos1.OrderId}");
        ownerIssuesRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var ownerIssues = await ownerIssuesRes.Content.ReadFromJsonAsync<List<GoodsIssueDto>>(_jsonOptions);
        ownerIssues.Should().HaveCount(1);
        var issue1 = ownerIssues![0];
        issue1.Number.Should().Be($"PXK-{pos1.Receipt.ReceiptNumber}");
        issue1.TotalCost.Should().Be(2m * unitCostWac);
        issue1.Items[0].UnitCost.Should().Be(unitCostWac);
        issue1.Items[0].TotalCost.Should().Be(2m * unitCostWac);

        var ownerDetailRes = await _client.GetAsync($"/api/goods-issues/{issue1.Id}");
        ownerDetailRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var ownerDetail = await ownerDetailRes.Content.ReadFromJsonAsync<GoodsIssueDto>(_jsonOptions);
        ownerDetail!.TotalCost.Should().Be(2m * unitCostWac);

        var bulkRes = await _client.PostAsJsonAsync("/api/goods-issues/by-orders", new BulkGoodsIssueByOrdersRequestDto
        {
            OrderIds = new List<Guid> { pos1.OrderId, pos2!.OrderId, adminOrder.Id }
        });
        bulkRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var bulkIssues = await bulkRes.Content.ReadFromJsonAsync<List<GoodsIssueDto>>(_jsonOptions);
        bulkIssues.Should().HaveCount(3);

        var ownerSummaryRes = await _client.GetAsync($"/api/goods-issues/pos-daily-summary?date={vnToday}&branchId={branchId}");
        ownerSummaryRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var ownerSummary = await ownerSummaryRes.Content.ReadFromJsonAsync<PosDailyGoodsIssueSummaryDto>(_jsonOptions);
        ownerSummary.Should().NotBeNull();
        ownerSummary!.TotalOrders.Should().Be(2, "Chỉ tính 2 đơn POS trong ngày, không trộn đơn Admin");
        ownerSummary.TotalInvoices.Should().Be(2);
        ownerSummary.TotalBaseQuantity.Should().Be(5m, "Tổng SL xuất bán POS trong ngày = 2 + 3 = 5");
        ownerSummary.TotalCost.Should().Be(5m * unitCostWac);
        ownerSummary.Items.Should().ContainSingle(i => i.SkuId == skuId && i.TotalBaseQuantity == 5m && i.UnitCost == unitCostWac && i.TotalCost == 5m * unitCostWac);
        ownerSummary.Invoices.Should().HaveCount(2);

        // 3. Staff xem phiếu xuất kho và bảng kê POS -> UnitCost và TotalCost đều là null
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var staffIssueRes = await _client.GetAsync($"/api/goods-issues/{issue1.Id}");
        staffIssueRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var staffIssue = await staffIssueRes.Content.ReadFromJsonAsync<GoodsIssueDto>(_jsonOptions);
        staffIssue!.TotalCost.Should().BeNull("Staff không được xem tổng giá vốn trên phiếu xuất kho");
        staffIssue.Items.Should().AllSatisfy(i =>
        {
            i.UnitCost.Should().BeNull("Staff không được xem đơn giá vốn trên dòng phiếu xuất");
            i.TotalCost.Should().BeNull("Staff không được xem thành tiền giá vốn trên dòng phiếu xuất");
        });

        var staffSummaryRes = await _client.GetAsync($"/api/goods-issues/pos-daily-summary?date={vnToday}&branchId={branchId}");
        staffSummaryRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var staffSummary = await staffSummaryRes.Content.ReadFromJsonAsync<PosDailyGoodsIssueSummaryDto>(_jsonOptions);
        staffSummary!.TotalBaseQuantity.Should().Be(5m);
        staffSummary.TotalCost.Should().BeNull("Staff không được xem tổng giá vốn trên bảng kê POS");
        staffSummary.Items.Should().AllSatisfy(i =>
        {
            i.UnitCost.Should().BeNull();
            i.TotalCost.Should().BeNull();
        });
        staffSummary.Invoices.Should().AllSatisfy(inv => inv.TotalCost.Should().BeNull());

        // 4. AC-06: Sổ cái (/api/inventory/ledger) và Truy vết (/api/orders/{id}/trace, /api/inventory/ledger/{id}/trace) có số phiếu xuất
        var ledgerRes = await _client.GetAsync($"/api/inventory/ledger?branchId={branchId}&type=OUT");
        ledgerRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var ledgerPage = await ledgerRes.Content.ReadFromJsonAsync<PagedResult<InventoryLedgerDto>>(_jsonOptions);
        var pos1LedgerLine = ledgerPage!.Items.First(l => l.DocumentId == pos1.OrderId);
        pos1LedgerLine.GoodsIssueId.Should().Be(issue1.Id);
        pos1LedgerLine.GoodsIssueNumber.Should().Be(issue1.Number);

        var traceRes = await _client.GetAsync($"/api/orders/{pos1.OrderId}/trace");
        traceRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var traceDto = await traceRes.Content.ReadFromJsonAsync<DocumentTraceDto>(_jsonOptions);
        traceDto!.Lines.Should().ContainSingle();
        traceDto.Lines[0].GoodsIssueId.Should().Be(issue1.Id);
        traceDto.Lines[0].GoodsIssueNumber.Should().Be(issue1.Number);

        var ledgerTraceRes = await _client.GetAsync($"/api/inventory/ledger/{pos1LedgerLine.Id}/trace");
        ledgerTraceRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var ledgerTraceDto = await ledgerTraceRes.Content.ReadFromJsonAsync<DocumentTraceDto>(_jsonOptions);
        ledgerTraceDto!.Lines[0].GoodsIssueNumber.Should().Be(issue1.Number);

        // 5. Cô lập Tenant: Tenant B truy vấn phiếu của Tenant A -> 404 NotFound
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantBToken);
        (await _client.GetAsync($"/api/goods-issues/{issue1.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.GetAsync($"/api/goods-issues/by-order/{pos1.OrderId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
