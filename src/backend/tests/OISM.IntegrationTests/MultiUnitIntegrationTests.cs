using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.RegisterTenant;
using OISM.Application.Features.Catalog.Products;
using OISM.Application.Features.Catalog.Units;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

public class MultiUnitIntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public MultiUnitIntegrationTests(WebApplicationFactoryWithPostgres factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(string Token, Guid TenantId)> RegisterAndLoginOwnerAsync(string tenantCode, string email)
    {
        var regRequest = new RegisterTenantRequest
        {
            TenantName = $"Tenant {tenantCode}",
            TenantCode = tenantCode,
            FullName = "Store Owner",
            Email = email,
            Password = "Password@123"
        };
        var regRes = await _client.PostAsJsonAsync("/api/auth/register-tenant", regRequest);
        regRes.EnsureSuccessStatusCode();

        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = "Password@123"
        });
        loginRes.EnsureSuccessStatusCode();

        var authData = await loginRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        return (authData!.AccessToken, authData.User.TenantId);
    }

    private async Task<string> CreateAndLoginCashierAsync(Guid tenantId, string email)
    {
        await using (var adminConn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await adminConn.OpenAsync();
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO public.users (id, tenant_id, display_name, email, password_hash, role, is_active, created_at)
                VALUES (@id, @tenantId, 'Test Cashier', @email, @hash, 'Cashier', true, now());
            ", adminConn);
            cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("email", email);
            cmd.Parameters.AddWithValue("hash", BCrypt.Net.BCrypt.HashPassword("Cashier@123"));
            await cmd.ExecuteNonQueryAsync();
        }

        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = "Cashier@123"
        });
        loginRes.EnsureSuccessStatusCode();

        var authData = await loginRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        return authData!.AccessToken;
    }

    // =========================================================================
    // AC-01: Schema & Migration Idempotency
    // =========================================================================
    [Fact]
    public async Task AC01_SchemaAndMigration_ThreeTablesAndFksExist_MigrationIsIdempotent()
    {
        var migrator = new DatabaseMigrationService();
        var adminConnStr = PostgresTestEnvironment.AdminDatabaseConnectionString;

        // 1. Chạy upgrade 2 lần liên tiếp -> Đều thành công (Idempotent)
        var upgrade1 = await migrator.UpgradeDatabaseSchemaAsync(adminConnStr);
        upgrade1.Success.Should().BeTrue();

        var upgrade2 = await migrator.UpgradeDatabaseSchemaAsync(adminConnStr);
        upgrade2.Success.Should().BeTrue("Upgrade migration lần 2 phải hoàn toàn idempotent.");

        // 2. Chạy baseline validator -> Success = true, table count = 37
        var baseline = await migrator.InitializeOrBaselineDatabaseAsync(adminConnStr);
        baseline.Success.Should().BeTrue();
        baseline.TableCount.Should().Be(DatabaseMigrationService.RequiredTables.Length);

        // 3. Kiểm tra 3 bảng tồn tại và các composite foreign keys
        await using var conn = new NpgsqlConnection(adminConnStr);
        await conn.OpenAsync();

        var tables = new[] { "units", "sku_units", "sku_unit_factor_history" };
        foreach (var tbl in tables)
        {
            await using var cmd = new NpgsqlCommand("SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = @tbl;", conn);
            cmd.Parameters.AddWithValue("tbl", tbl);
            var exists = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            exists.Should().Be(1, $"Bảng {tbl} phải tồn tại.");
        }

        // Composite foreign keys check
        var fks = new[] { "fk_sku_units_unit_id", "fk_sku_units_sku_id", "fk_sku_unit_factor_history_sku_unit_id" };
        foreach (var fk in fks)
        {
            await using var cmd = new NpgsqlCommand("SELECT count(*) FROM information_schema.table_constraints WHERE constraint_type = 'FOREIGN KEY' AND constraint_name = @fk;", conn);
            cmd.Parameters.AddWithValue("fk", fk);
            var exists = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            exists.Should().Be(1, $"Khóa ngoại {fk} phải tồn tại.");
        }
    }

    // =========================================================================
    // AC-02: Legacy Backfill & Ledger Immutability
    // =========================================================================
    [Fact]
    public async Task AC02_LegacyBackfillAndLedgerImmutability_StockAndWacAndLedgerCountUnchanged()
    {
        var adminConnStr = PostgresTestEnvironment.AdminDatabaseConnectionString;
        var migrator = new DatabaseMigrationService();

        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var orderItemId = Guid.NewGuid();
        var receiptId = Guid.NewGuid();
        var receiptItemId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();

        // 1. Chèn dữ liệu legacy vào database
        await using (var conn = new NpgsqlConnection(adminConnStr))
        {
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO public.tenants (id, name, is_active, created_at)
                VALUES (@tid, 'Tenant Legacy', true, now());

                INSERT INTO public.users (id, tenant_id, display_name, email, password_hash, role, is_active, created_at)
                VALUES (@uid, @tid, 'Admin', 'admin_leg@test.com', 'hash', 'Owner', true, now());

                INSERT INTO public.branches (id, tenant_id, name, is_active, created_at)
                VALUES (@bid, @tid, 'Branch 1', true, now());

                INSERT INTO public.warehouses (id, tenant_id, branch_id, name, is_active, created_at)
                VALUES (@wid, @tid, @bid, 'Warehouse 1', true, now());

                INSERT INTO public.suppliers (id, tenant_id, name, is_active, created_at)
                VALUES (@supId, @tid, 'Supplier 1', true, now());

                INSERT INTO public.products (id, tenant_id, name, is_active, created_at)
                VALUES (@pid, @tid, 'Legacy Product', true, now());

                INSERT INTO public.skus (id, tenant_id, product_id, code, attributes, unit, retail_price, wholesale_price, is_active, created_at)
                VALUES (@sid, @tid, @pid, 'SKU-LEG-01', '{}'::jsonb, 'kg', 50000, 45000, true, now());

                -- Inventory balance (branch level) & Warehouse balance
                INSERT INTO public.inventory_balances (id, tenant_id, branch_id, sku_id, on_hand, reserved, average_cost, last_sequence, created_at)
                VALUES (gen_random_uuid(), @tid, @bid, @sid, 100, 0, 30000, 1, now());

                INSERT INTO public.warehouse_inventory_balances (id, tenant_id, branch_id, warehouse_id, sku_id, on_hand, reserved, last_sequence, created_at)
                VALUES (gen_random_uuid(), @tid, @bid, @wid, @sid, 100, 0, 1, now());

                -- Confirmed goods receipt
                INSERT INTO public.goods_receipts (id, tenant_id, number, branch_id, warehouse_id, supplier_id, status, created_by, confirmed_by, confirmed_at, created_at)
                VALUES (@recId, @tid, 'GR-LEG-01', @bid, @wid, @supId, 'Confirmed', @uid, @uid, now(), now());

                INSERT INTO public.goods_receipt_items (id, tenant_id, receipt_id, sku_id, quantity, unit_price, created_at)
                VALUES (@recItemId, @tid, @recId, @sid, 100, 30000, now());

                -- Confirmed order
                INSERT INTO public.orders (id, tenant_id, number, branch_id, channel, status, total_amount, request_key, confirmed_at, created_at)
                VALUES (@ordId, @tid, 'ORD-LEG-01', @bid, 'POS', 'Confirmed', 100000, gen_random_uuid(), now(), now());

                INSERT INTO public.order_items (id, tenant_id, order_id, sku_id, sku_code_snapshot, sku_name_snapshot, quantity, unit_price, cost_price, created_at)
                VALUES (@ordItemId, @tid, @ordId, @sid, 'SKU-LEG-01', 'Legacy Item', 2, 50000, 30000, now());

                -- Inventory transactions (Ledger)
                INSERT INTO public.inventory_transactions (id, tenant_id, branch_id, warehouse_id, sku_id, type, quantity, unit_cost, balance_after, sequence_no, receipt_item_id, created_at)
                VALUES (gen_random_uuid(), @tid, @bid, @wid, @sid, 'IN', 100, 30000, 100, 1, @recItemId, now());
            ", conn);

            cmd.Parameters.AddWithValue("tid", tenantId);
            cmd.Parameters.AddWithValue("uid", userId);
            cmd.Parameters.AddWithValue("bid", branchId);
            cmd.Parameters.AddWithValue("wid", warehouseId);
            cmd.Parameters.AddWithValue("supId", supplierId);
            cmd.Parameters.AddWithValue("pid", productId);
            cmd.Parameters.AddWithValue("sid", skuId);
            cmd.Parameters.AddWithValue("recId", receiptId);
            cmd.Parameters.AddWithValue("recItemId", receiptItemId);
            cmd.Parameters.AddWithValue("ordId", orderId);
            cmd.Parameters.AddWithValue("ordItemId", orderItemId);
            await cmd.ExecuteNonQueryAsync();
        }

        // Lưu lại trạng thái ledger trước upgrade
        int ledgerCountBefore;
        decimal onHandBefore, avgCostBefore, totalAmountBefore, costPriceBefore, unitCostBefore, balanceAfterBefore;
        await using (var conn = new NpgsqlConnection(adminConnStr))
        {
            await conn.OpenAsync();
            await using var countCmd = new NpgsqlCommand("SELECT count(*) FROM public.inventory_transactions WHERE tenant_id = @tid;", conn);
            countCmd.Parameters.AddWithValue("tid", tenantId);
            ledgerCountBefore = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

            await using var balCmd = new NpgsqlCommand("SELECT on_hand, average_cost FROM public.inventory_balances WHERE tenant_id = @tid AND sku_id = @sid;", conn);
            balCmd.Parameters.AddWithValue("tid", tenantId);
            balCmd.Parameters.AddWithValue("sid", skuId);
            await using (var reader = await balCmd.ExecuteReaderAsync())
            {
                await reader.ReadAsync();
                onHandBefore = reader.GetDecimal(0);
                avgCostBefore = reader.GetDecimal(1);
            }

            await using var ordCmd = new NpgsqlCommand("SELECT total_amount FROM public.orders WHERE id = @id;", conn);
            ordCmd.Parameters.AddWithValue("id", orderId);
            totalAmountBefore = (decimal)(await ordCmd.ExecuteScalarAsync())!;

            await using var oiCmd = new NpgsqlCommand("SELECT cost_price FROM public.order_items WHERE id = @id;", conn);
            oiCmd.Parameters.AddWithValue("id", orderItemId);
            costPriceBefore = (decimal)(await oiCmd.ExecuteScalarAsync())!;

            await using var itCmd = new NpgsqlCommand("SELECT unit_cost, balance_after FROM public.inventory_transactions WHERE tenant_id = @tid;", conn);
            itCmd.Parameters.AddWithValue("tid", tenantId);
            await using (var reader = await itCmd.ExecuteReaderAsync())
            {
                await reader.ReadAsync();
                unitCostBefore = reader.GetDecimal(0);
                balanceAfterBefore = reader.GetDecimal(1);
            }
        }

        // 2. Chạy migration upgrade
        var upgradeResult = await migrator.UpgradeDatabaseSchemaAsync(adminConnStr);
        upgradeResult.Success.Should().BeTrue();

        // 3. Kiểm tra kết quả sau upgrade
        await using (var conn = new NpgsqlConnection(adminConnStr))
        {
            await conn.OpenAsync();

            // Order item đã backfill
            await using var oiCmd = new NpgsqlCommand("SELECT unit_id, factor_snapshot, base_quantity, quantity FROM public.order_items WHERE id = @id;", conn);
            oiCmd.Parameters.AddWithValue("id", orderItemId);
            await using (var reader = await oiCmd.ExecuteReaderAsync())
            {
                await reader.ReadAsync();
                reader.IsDBNull(0).Should().BeFalse("unit_id phải được backfill");
                reader.GetDecimal(1).Should().Be(1m, "factor_snapshot phải là 1");
                reader.GetDecimal(2).Should().Be(reader.GetDecimal(3), "base_quantity phải bằng quantity");
            }

            // Goods receipt item đã backfill
            await using var griCmd = new NpgsqlCommand("SELECT unit_id, factor_snapshot, base_quantity, quantity FROM public.goods_receipt_items WHERE id = @id;", conn);
            griCmd.Parameters.AddWithValue("id", receiptItemId);
            await using (var reader = await griCmd.ExecuteReaderAsync())
            {
                await reader.ReadAsync();
                reader.IsDBNull(0).Should().BeFalse("unit_id phải được backfill");
                reader.GetDecimal(1).Should().Be(1m, "factor_snapshot phải là 1");
                reader.GetDecimal(2).Should().Be(reader.GetDecimal(3), "base_quantity phải bằng quantity");
            }

            // Ledger và balances KHÔNG ĐỔI
            await using var countCmd = new NpgsqlCommand("SELECT count(*) FROM public.inventory_transactions WHERE tenant_id = @tid;", conn);
            countCmd.Parameters.AddWithValue("tid", tenantId);
            var ledgerCountAfter = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
            ledgerCountAfter.Should().Be(ledgerCountBefore, "Số dòng ledger tuyệt đối không được đổi.");

            await using var balCmd = new NpgsqlCommand("SELECT on_hand, average_cost FROM public.inventory_balances WHERE tenant_id = @tid AND sku_id = @sid;", conn);
            balCmd.Parameters.AddWithValue("tid", tenantId);
            balCmd.Parameters.AddWithValue("sid", skuId);
            await using (var reader = await balCmd.ExecuteReaderAsync())
            {
                await reader.ReadAsync();
                reader.GetDecimal(0).Should().Be(onHandBefore);
                reader.GetDecimal(1).Should().Be(avgCostBefore);
            }

            await using var ordCmd = new NpgsqlCommand("SELECT total_amount FROM public.orders WHERE id = @id;", conn);
            ordCmd.Parameters.AddWithValue("id", orderId);
            var totalAmountAfter = (decimal)(await ordCmd.ExecuteScalarAsync())!;
            totalAmountAfter.Should().Be(totalAmountBefore, "total_amount không được đổi sau migration.");

            await using var oiCheckCmd = new NpgsqlCommand("SELECT cost_price FROM public.order_items WHERE id = @id;", conn);
            oiCheckCmd.Parameters.AddWithValue("id", orderItemId);
            var costPriceAfter = (decimal)(await oiCheckCmd.ExecuteScalarAsync())!;
            costPriceAfter.Should().Be(costPriceBefore, "cost_price không được đổi sau migration.");

            await using var itCmd = new NpgsqlCommand("SELECT unit_cost, balance_after FROM public.inventory_transactions WHERE tenant_id = @tid;", conn);
            itCmd.Parameters.AddWithValue("tid", tenantId);
            await using (var reader = await itCmd.ExecuteReaderAsync())
            {
                await reader.ReadAsync();
                reader.GetDecimal(0).Should().Be(unitCostBefore, "unit_cost không được đổi sau migration.");
                reader.GetDecimal(1).Should().Be(balanceAfterBefore, "balance_after không được đổi sau migration.");
            }
        }
    }

    // =========================================================================
    // AC-03: Check Stock with BaseQuantity, Snapshot Immutability, and Order Items Uniqueness
    // =========================================================================
    [Fact]
    public async Task AC03_CheckStockWithBaseQuantity_ConfirmedOrderProtection_OrderItemsUniqueness()
    {
        var adminConnStr = PostgresTestEnvironment.AdminDatabaseConnectionString;
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var unitKgId = Guid.NewGuid();
        var unitBaoId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var receiptId = Guid.NewGuid();
        var receiptItemId = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(adminConnStr);
        await conn.OpenAsync();

        // 1. Setup seed data
        await using (var cmd = new NpgsqlCommand(@"
            INSERT INTO public.tenants (id, name, is_active, created_at)
            VALUES (@tid, 'Tenant Stock Test', true, now());

            INSERT INTO public.users (id, tenant_id, email, password_hash, display_name, role, is_active, created_at)
            VALUES (@uid, @tid, 'user-stock@test.com', 'hash', 'Stock User', 'Owner', true, now());

            INSERT INTO public.suppliers (id, tenant_id, name, is_active, created_at)
            VALUES (@supId, @tid, 'Supplier Stock', true, now());

            INSERT INTO public.branches (id, tenant_id, name, is_active, created_at)
            VALUES (@bid, @tid, 'Branch 1', true, now());

            INSERT INTO public.warehouses (id, tenant_id, branch_id, name, is_active, created_at)
            VALUES (@wid, @tid, @bid, 'Warehouse 1', true, now());

            INSERT INTO public.units (id, tenant_id, code, name, symbol, measure_group, decimal_places, is_active)
            VALUES (@ukg, @tid, 'kg', 'Kilôgam', 'kg', 'Mass', 3, true),
                   (@ubao, @tid, 'bao', 'Bao', 'bao', 'Packaging', 0, true);

            INSERT INTO public.products (id, tenant_id, name, is_active, created_at)
            VALUES (@pid, @tid, 'Rice', true, now());

            INSERT INTO public.skus (id, tenant_id, product_id, code, attributes, retail_price, wholesale_price, unit, base_unit_id, is_active, created_at)
            VALUES (@sid, @tid, @pid, 'RICE-01', '{}'::jsonb, 20000, 18000, 'kg', @ukg, true, now());

            INSERT INTO public.sku_units (id, tenant_id, sku_id, unit_id, factor_to_base, can_purchase, can_sell, quantity_decimal_places, version, is_active)
            VALUES (gen_random_uuid(), @tid, @sid, @ukg, 1, true, true, 3, 1, true),
                   (gen_random_uuid(), @tid, @sid, @ubao, 25, true, true, 0, 1, true);

            -- Tồn kho ban đầu: 100 kg
            INSERT INTO public.inventory_balances (id, tenant_id, branch_id, sku_id, on_hand, reserved, average_cost, last_sequence, created_at)
            VALUES (gen_random_uuid(), @tid, @bid, @sid, 100, 0, 15000, 1, now());

            INSERT INTO public.warehouse_inventory_balances (id, tenant_id, branch_id, warehouse_id, sku_id, on_hand, reserved, last_sequence, created_at)
            VALUES (gen_random_uuid(), @tid, @bid, @wid, @sid, 100, 0, 1, now());

            INSERT INTO public.goods_receipts (id, tenant_id, number, branch_id, warehouse_id, supplier_id, status, created_by, confirmed_by, confirmed_at, created_at)
            VALUES (@recId, @tid, 'GR-STOCK-01', @bid, @wid, @supId, 'Confirmed', @uid, @uid, now(), now());

            INSERT INTO public.goods_receipt_items (id, tenant_id, receipt_id, sku_id, quantity, unit_price, created_at)
            VALUES (@recItemId, @tid, @recId, @sid, 100, 15000, now());

            INSERT INTO public.inventory_transactions (id, tenant_id, branch_id, warehouse_id, sku_id, type, quantity, unit_cost, balance_after, sequence_no, receipt_item_id, created_at)
            VALUES (gen_random_uuid(), @tid, @bid, @wid, @sid, 'IN', 100, 15000, 100, 1, @recItemId, now());

            -- Tạo đơn hàng Draft
            INSERT INTO public.orders (id, tenant_id, number, branch_id, channel, status, total_amount, request_key, created_at)
            VALUES (@ordId, @tid, 'ORD-STOCK-01', @bid, 'POS', 'Draft', 500000, gen_random_uuid(), now());
        ", conn))
        {
            cmd.Parameters.AddWithValue("tid", tenantId);
            cmd.Parameters.AddWithValue("uid", userId);
            cmd.Parameters.AddWithValue("supId", supplierId);
            cmd.Parameters.AddWithValue("recId", receiptId);
            cmd.Parameters.AddWithValue("recItemId", receiptItemId);
            cmd.Parameters.AddWithValue("bid", branchId);
            cmd.Parameters.AddWithValue("wid", warehouseId);
            cmd.Parameters.AddWithValue("ukg", unitKgId);
            cmd.Parameters.AddWithValue("ubao", unitBaoId);
            cmd.Parameters.AddWithValue("pid", productId);
            cmd.Parameters.AddWithValue("sid", skuId);
            cmd.Parameters.AddWithValue("ordId", orderId);
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. oisms_check_stock so với base_quantity:
        // Đặt 1 bao hệ số 25 -> base_quantity = 25.
        var item1Id = Guid.NewGuid();
        await using (var insertItem1 = new NpgsqlCommand(@"
            INSERT INTO public.order_items (id, tenant_id, order_id, sku_id, sku_code_snapshot, sku_name_snapshot, unit_id, factor_snapshot, quantity, unit_price, cost_price, created_at)
            VALUES (@id, @tid, @ordId, @sid, 'RICE-01', 'Gạo 1 bao', @ubao, 25, 1, 500000, 375000, now());
        ", conn))
        {
            insertItem1.Parameters.AddWithValue("id", item1Id);
            insertItem1.Parameters.AddWithValue("tid", tenantId);
            insertItem1.Parameters.AddWithValue("ordId", orderId);
            insertItem1.Parameters.AddWithValue("sid", skuId);
            insertItem1.Parameters.AddWithValue("ubao", unitBaoId);
            await insertItem1.ExecuteNonQueryAsync();
        }

        // Tạo giữ chỗ:
        // Giữ chỗ 25 kg trên dòng 1 bao hệ số 25 (base_quantity = 25) -> Thành công!
        await using (var tx1 = await conn.BeginTransactionAsync())
        {
            await using var updateBal1 = new NpgsqlCommand(@"
                UPDATE public.warehouse_inventory_balances SET reserved = 25 WHERE tenant_id = @tid AND sku_id = @sid AND warehouse_id = @wid;
                UPDATE public.inventory_balances SET reserved = 25 WHERE tenant_id = @tid AND sku_id = @sid AND branch_id = @bid;
                INSERT INTO public.reservations (id, tenant_id, order_id, order_item_id, branch_id, warehouse_id, sku_id, quantity, status, expires_at, created_at)
                VALUES (gen_random_uuid(), @tid, @ordId, @itemId, @bid, @wid, @sid, 25, 'Active', now() + interval '1 hour', now());
            ", conn, tx1);
            updateBal1.Parameters.AddWithValue("tid", tenantId);
            updateBal1.Parameters.AddWithValue("ordId", orderId);
            updateBal1.Parameters.AddWithValue("itemId", item1Id);
            updateBal1.Parameters.AddWithValue("bid", branchId);
            updateBal1.Parameters.AddWithValue("wid", warehouseId);
            updateBal1.Parameters.AddWithValue("sid", skuId);
            await updateBal1.ExecuteNonQueryAsync();

            var actCommit = async () => await tx1.CommitAsync();
            await actCommit.Should().NotThrowAsync("Giữ chỗ 25 trên dòng 1 bao hệ số 25 (base_quantity = 25) phải vượt qua oisms_check_stock.");
        }

        // Giữ chỗ 26 kg trên dòng 1 bao hệ số 25 (base_quantity = 25) -> Bị oisms_check_stock từ chối!
        await using (var tx2 = await conn.BeginTransactionAsync())
        {
            await using var updateBal2 = new NpgsqlCommand(@"
                UPDATE public.warehouse_inventory_balances SET reserved = 26 WHERE tenant_id = @tid AND sku_id = @sid AND warehouse_id = @wid;
                UPDATE public.inventory_balances SET reserved = 26 WHERE tenant_id = @tid AND sku_id = @sid AND branch_id = @bid;
                UPDATE public.reservations SET quantity = 26 WHERE tenant_id = @tid AND order_item_id = @itemId;
            ", conn, tx2);
            updateBal2.Parameters.AddWithValue("tid", tenantId);
            updateBal2.Parameters.AddWithValue("ordId", orderId);
            updateBal2.Parameters.AddWithValue("itemId", item1Id);
            updateBal2.Parameters.AddWithValue("bid", branchId);
            updateBal2.Parameters.AddWithValue("wid", warehouseId);
            updateBal2.Parameters.AddWithValue("sid", skuId);
            await updateBal2.ExecuteNonQueryAsync();

            var actCommit2 = async () => await tx2.CommitAsync();
            await actCommit2.Should().ThrowAsync<PostgresException>()
                .Where(ex => ex.Message.Contains("Reservation allocation exceeds order quantity") || ex.SqlState == "23514");
        }

        // 3. Đơn đã confirm không đổi được factor_snapshot, unit_id
        await using (var confirmOrderCmd = new NpgsqlCommand(@"
            UPDATE public.orders SET status = 'Confirmed', confirmed_at = now() WHERE id = @ordId;
        ", conn))
        {
            confirmOrderCmd.Parameters.AddWithValue("ordId", orderId);
            await confirmOrderCmd.ExecuteNonQueryAsync();
        }

        await using (var updateSnapshotCmd = new NpgsqlCommand(@"
            UPDATE public.order_items SET factor_snapshot = 30 WHERE id = @id;
        ", conn))
        {
            updateSnapshotCmd.Parameters.AddWithValue("id", item1Id);
            var actUpdate = async () => await updateSnapshotCmd.ExecuteNonQueryAsync();
            await actUpdate.Should().ThrowAsync<PostgresException>()
                .Where(ex => ex.Message.Contains("Cannot modify confirmed order item") || ex.SqlState == "23514");
        }

        await using (var updateUnitIdCmd = new NpgsqlCommand(@"
            UPDATE public.order_items SET unit_id = @newUnitId WHERE id = @id;
        ", conn))
        {
            updateUnitIdCmd.Parameters.AddWithValue("newUnitId", unitKgId);
            updateUnitIdCmd.Parameters.AddWithValue("id", item1Id);
            var actUpdateUnit = async () => await updateUnitIdCmd.ExecuteNonQueryAsync();
            await actUpdateUnit.Should().ThrowAsync<PostgresException>()
                .Where(ex => ex.Message.Contains("Cannot modify confirmed order item") || ex.Message.Contains("immutable") || ex.SqlState == "23514");
        }

        // 4. Cùng SKU có 2 dòng khác unit_id trong một đơn được chấp nhận; 2 dòng trùng SKU và cùng NULL bị từ chối
        var order2Id = Guid.NewGuid();
        await using (var order2Cmd = new NpgsqlCommand(@"
            INSERT INTO public.orders (id, tenant_id, number, branch_id, channel, status, total_amount, request_key, created_at)
            VALUES (@ordId, @tid, 'ORD-STOCK-02', @bid, 'POS', 'Draft', 0, gen_random_uuid(), now());
            INSERT INTO public.order_items (id, tenant_id, order_id, sku_id, sku_code_snapshot, sku_name_snapshot, unit_id, factor_snapshot, quantity, unit_price, cost_price, created_at)
            VALUES (gen_random_uuid(), @tid, @ordId, @sid, 'RICE-01', 'Gạo', @ukg, 1, 1, 20000, 15000, now()),
                   (gen_random_uuid(), @tid, @ordId, @sid, 'RICE-01', 'Gạo', @ubao, 25, 1, 500000, 375000, now());
        ", conn))
        {
            order2Cmd.Parameters.AddWithValue("ordId", order2Id);
            order2Cmd.Parameters.AddWithValue("tid", tenantId);
            order2Cmd.Parameters.AddWithValue("bid", branchId);
            order2Cmd.Parameters.AddWithValue("sid", skuId);
            order2Cmd.Parameters.AddWithValue("ukg", unitKgId);
            order2Cmd.Parameters.AddWithValue("ubao", unitBaoId);
            var actDiffUnits = async () => await order2Cmd.ExecuteNonQueryAsync();
            await actDiffUnits.Should().NotThrowAsync("Cùng SKU có 2 dòng khác unit_id trong một đơn phải hợp lệ.");
        }

        // 2 dòng trùng SKU và cùng NULL unit_id bị từ chối bởi uq_order_items_2
        var order3Id = Guid.NewGuid();
        await using (var order3Cmd = new NpgsqlCommand(@"
            INSERT INTO public.orders (id, tenant_id, number, branch_id, channel, status, total_amount, request_key, created_at)
            VALUES (@ordId, @tid, 'ORD-STOCK-03', @bid, 'POS', 'Draft', 0, gen_random_uuid(), now());
            INSERT INTO public.order_items (id, tenant_id, order_id, sku_id, sku_code_snapshot, sku_name_snapshot, unit_id, factor_snapshot, quantity, unit_price, cost_price, created_at)
            VALUES (gen_random_uuid(), @tid, @ordId, @sid, 'RICE-01', 'Gạo', NULL, 1, 1, 20000, 15000, now());
            INSERT INTO public.order_items (id, tenant_id, order_id, sku_id, sku_code_snapshot, sku_name_snapshot, unit_id, factor_snapshot, quantity, unit_price, cost_price, created_at)
            VALUES (gen_random_uuid(), @tid, @ordId, @sid, 'RICE-01', 'Gạo', NULL, 1, 1, 20000, 15000, now());
        ", conn))
        {
            order3Cmd.Parameters.AddWithValue("ordId", order3Id);
            order3Cmd.Parameters.AddWithValue("tid", tenantId);
            order3Cmd.Parameters.AddWithValue("bid", branchId);
            order3Cmd.Parameters.AddWithValue("sid", skuId);
            var actNullUnits = async () => await order3Cmd.ExecuteNonQueryAsync();
            await actNullUnits.Should().ThrowAsync<PostgresException>()
                .Where(ex => ex.SqlState == "23505" && ex.ConstraintName == "uq_order_items_2");
        }
    }

    // =========================================================================
    // AC-04: Computed Column & Cross-Tenant Isolation
    // =========================================================================
    [Fact]
    public async Task AC04_ComputedColumn_AndTenantIsolation()
    {
        var (tokenA, tenantAId) = await RegisterAndLoginOwnerAsync("tenant_a", "owner_a@test.com");
        var (tokenB, tenantBId) = await RegisterAndLoginOwnerAsync("tenant_b", "owner_b@test.com");

        // 1. Tenant A tạo đơn vị tính
        var clientA = _factory.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);

        var createUnitRes = await clientA.PostAsJsonAsync("/api/units", new CreateUnitRequest
        {
            Code = "thung_a",
            Name = "Thùng của A",
            Symbol = "thg",
            MeasureGroup = "Packaging",
            DecimalPlaces = 0
        });
        createUnitRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var unitA = await createUnitRes.Content.ReadFromJsonAsync<UnitResponse>(_jsonOptions);

        // 2. Tenant B không thấy đơn vị tính của Tenant A (Global Query Filter)
        var clientB = _factory.CreateClient();
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);

        var listBRes = await clientB.GetFromJsonAsync<List<UnitResponse>>("/api/units");
        listBRes.Should().NotContain(u => u.Id == unitA!.Id || u.Code == "thung_a");

        // 3. Tenant B không thể tham chiếu đơn vị của Tenant A (FK ghép tenant_id chặn)
        // Tạo sản phẩm cho Tenant B
        var prodBRes = await clientB.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Sản phẩm B",
            Variants = new List<CreateVariantRequest>
            {
                new() { SKU = "SKU-B01", VariantName = "SKU B", RetailPrice = 10000 }
            }
        });
        prodBRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var prodBJson = await prodBRes.Content.ReadFromJsonAsync<JsonElement>();
        var prodBId = prodBJson.GetProperty("productId").GetGuid();
        var prodB = await clientB.GetFromJsonAsync<ProductResponse>($"/api/products/{prodBId}");
        var skuBId = prodB!.Variants.First().Id;

        await using (var adminConn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await adminConn.OpenAsync();
            await using var crossTenantCmd = new NpgsqlCommand(@"
                INSERT INTO public.sku_units (id, tenant_id, sku_id, unit_id, factor_to_base, version, is_active)
                VALUES (gen_random_uuid(), @tenantB, @skuB, @unitA, 1, 1, true);
            ", adminConn);
            crossTenantCmd.Parameters.AddWithValue("tenantB", tenantBId);
            crossTenantCmd.Parameters.AddWithValue("skuB", skuBId);
            crossTenantCmd.Parameters.AddWithValue("unitA", unitA!.Id);

            var actCross = async () => await crossTenantCmd.ExecuteNonQueryAsync();
            await actCross.Should().ThrowAsync<PostgresException>()
                .Where(ex => ex.SqlState == "23503" && ex.ConstraintName == "fk_sku_units_unit_id");
        }

        // 4. base_quantity là cột generated, client không ghi đè trực tiếp được
        await using (var adminConn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await adminConn.OpenAsync();
            await using var overwriteCmd = new NpgsqlCommand(@"
                INSERT INTO public.order_items (id, tenant_id, order_id, sku_id, sku_code_snapshot, sku_name_snapshot, unit_id, factor_snapshot, quantity, base_quantity, unit_price, cost_price, created_at)
                VALUES (gen_random_uuid(), @tid, gen_random_uuid(), gen_random_uuid(), 'SKU', 'Name', NULL, 1, 5, 999, 10000, 5000, now());
            ", adminConn);
            overwriteCmd.Parameters.AddWithValue("tid", tenantAId);

            var actOverwrite = async () => await overwriteCmd.ExecuteNonQueryAsync();
            await actOverwrite.Should().ThrowAsync<PostgresException>()
                .Where(ex => ex.SqlState == "428C9" || ex.SqlState == "42601" || ex.Message.Contains("generated always") || ex.Message.Contains("base_quantity"));
        }
    }

    // =========================================================================
    // AC-05: Validator, Triggers & Append-Only Permissions
    // =========================================================================
    [Fact]
    public async Task AC05_Validator_Triggers_AndAppendOnlyPermissions()
    {
        var migrator = new DatabaseMigrationService();
        var adminConnStr = PostgresTestEnvironment.AdminDatabaseConnectionString;

        // 1. Validator phát hiện khi thiếu trigger factor_history_append_only
        await using (var adminConn = new NpgsqlConnection(adminConnStr))
        {
            await adminConn.OpenAsync();
            await using var dropTrigger = new NpgsqlCommand("DROP TRIGGER IF EXISTS factor_history_append_only ON public.sku_unit_factor_history;", adminConn);
            await dropTrigger.ExecuteNonQueryAsync();
        }

        var validationFail = await migrator.InitializeOrBaselineDatabaseAsync(adminConnStr);
        validationFail.Success.Should().BeFalse();
        validationFail.Errors.Should().Contain(e => e.Contains("factor_history_append_only"));

        // 2. Chạy upgrade -> Vượt qua kiểm tra
        var upgradeRes = await migrator.UpgradeDatabaseSchemaAsync(adminConnStr);
        upgradeRes.Success.Should().BeTrue();

        var validationPass = await migrator.InitializeOrBaselineDatabaseAsync(adminConnStr);
        validationPass.Success.Should().BeTrue();

        // 3. oisms_runtime không có UPDATE, DELETE trên sku_unit_factor_history
        await using (var runtimeConn = new NpgsqlConnection(PostgresTestEnvironment.RuntimeConnectionString))
        {
            await runtimeConn.OpenAsync();

            await using var updateCmd = new NpgsqlCommand("UPDATE public.sku_unit_factor_history SET new_factor = 2;", runtimeConn);
            var actUpdate = async () => await updateCmd.ExecuteNonQueryAsync();
            await actUpdate.Should().ThrowAsync<PostgresException>()
                .Where(ex => ex.SqlState == "42501" || ex.Message.Contains("permission denied"));

            await using var deleteCmd = new NpgsqlCommand("DELETE FROM public.sku_unit_factor_history;", runtimeConn);
            var actDelete = async () => await deleteCmd.ExecuteNonQueryAsync();
            await actDelete.Should().ThrowAsync<PostgresException>()
                .Where(ex => ex.SqlState == "42501" || ex.Message.Contains("permission denied"));

            await using var truncateCmd = new NpgsqlCommand("TRUNCATE TABLE public.sku_unit_factor_history;", runtimeConn);
            var actTruncate = async () => await truncateCmd.ExecuteNonQueryAsync();
            await actTruncate.Should().ThrowAsync<PostgresException>()
                .Where(ex => ex.SqlState == "42501" || ex.Message.Contains("permission denied"));
        }
    }

    // =========================================================================
    // AC-06: API /api/units RBAC & Duplication
    // =========================================================================
    [Fact]
    public async Task AC06_ApiUnits_RbacAndDuplication()
    {
        var (ownerToken, tenantId) = await RegisterAndLoginOwnerAsync("tenant_rbac", "owner_rbac@test.com");
        var cashierToken = await CreateAndLoginCashierAsync(tenantId, "cashier_rbac@test.com");

        var ownerClient = _factory.CreateClient();
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var cashierClient = _factory.CreateClient();
        cashierClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);

        // 1. Cashier gọi GET /api/units -> 200 OK
        var cashierGetRes = await cashierClient.GetAsync("/api/units");
        cashierGetRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Cashier gọi POST /api/units -> 403 Forbidden
        var cashierPostRes = await cashierClient.PostAsJsonAsync("/api/units", new CreateUnitRequest
        {
            Code = "pack",
            Name = "Gói",
            Symbol = "gói",
            MeasureGroup = "Packaging",
            DecimalPlaces = 0
        });
        cashierPostRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 3. Owner gọi POST /api/units -> 201 Created
        var ownerPostRes = await ownerClient.PostAsJsonAsync("/api/units", new CreateUnitRequest
        {
            Code = "pack",
            Name = "Gói",
            Symbol = "gói",
            MeasureGroup = "Packaging",
            DecimalPlaces = 0
        });
        ownerPostRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // 4. Trùng code -> 409 Conflict (UNIT_CODE_DUPLICATE)
        var duplicateRes = await ownerClient.PostAsJsonAsync("/api/units", new CreateUnitRequest
        {
            Code = "pack",
            Name = "Gói trùng",
            Symbol = "gói",
            MeasureGroup = "Packaging",
            DecimalPlaces = 0
        });
        duplicateRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var errContent = await duplicateRes.Content.ReadAsStringAsync();
        errContent.Should().Contain("UNIT_CODE_DUPLICATE");
    }

    // =========================================================================
    // AC-07: SkuUnits Lifecycle, Incompatible Group, Version History, Barcode, Base Unit Locked
    // =========================================================================
    [Fact]
    public async Task AC07_SkuUnitsLifecycle_GroupCompatibility_HistoryVersion_BarcodeDuplicate_BaseUnitLocked()
    {
        var (ownerToken, tenantId) = await RegisterAndLoginOwnerAsync("tenant_sku_unit", "owner_sku_u@test.com");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // Đảm bảo default units đã có
        var units = await client.GetFromJsonAsync<List<UnitResponse>>("/api/units");
        var unitKg = units!.First(u => u.Code == "kg");
        var unitTan = units!.First(u => u.Code == "tan");
        var unitBao = units!.First(u => u.Code == "bao");
        var unitLit = units!.First(u => u.Code == "lit");

        // 1. Tạo sản phẩm gạo với base_unit_id là kg
        var createProductRes = await client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Gạo ST25",
            Variants = new List<CreateVariantRequest>
            {
                new()
                {
                    SKU = "GAO-ST25",
                    VariantName = "Gạo ST25 Bao",
                    RetailPrice = 25000,
                    BaseUnitId = unitKg.Id
                }
            }
        });
        createProductRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var prodJson = await createProductRes.Content.ReadFromJsonAsync<JsonElement>();
        var prodId = prodJson.GetProperty("productId").GetGuid();
        var product = await client.GetFromJsonAsync<ProductResponse>($"/api/products/{prodId}");
        var variant = product!.Variants.First();
        variant.BaseUnitCode.Should().Be("kg");
        variant.Units.Should().ContainSingle(u => u.IsBase && u.UnitCode == "kg" && u.FactorToBase == 1m);

        // 2. Bật tấn hệ số 1000 và bao hệ số 25 -> Thành công
        var addTanRes = await client.PostAsJsonAsync($"/api/products/{product.Id}/variants/{variant.Id}/units", new CreateSkuUnitRequest
        {
            UnitId = unitTan.Id,
            FactorToBase = 1000,
            CanPurchase = true,
            CanSell = true,
            QuantityDecimalPlaces = 3
        });
        addTanRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var skuUnitTan = await addTanRes.Content.ReadFromJsonAsync<SkuUnitResponse>(_jsonOptions);

        var addBaoRes = await client.PostAsJsonAsync($"/api/products/{product.Id}/variants/{variant.Id}/units", new CreateSkuUnitRequest
        {
            UnitId = unitBao.Id,
            FactorToBase = 25,
            CanPurchase = true,
            CanSell = true,
            QuantityDecimalPlaces = 0,
            Barcode = "8931112223334"
        });
        addBaoRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var skuUnitBao = await addBaoRes.Content.ReadFromJsonAsync<SkuUnitResponse>(_jsonOptions);

        // 3. Bật lít (Volume) trên gạo (Mass) -> 422 UNIT_GROUP_INCOMPATIBLE
        var addLitRes = await client.PostAsJsonAsync($"/api/products/{product.Id}/variants/{variant.Id}/units", new CreateSkuUnitRequest
        {
            UnitId = unitLit.Id,
            FactorToBase = 1,
            CanPurchase = true,
            CanSell = true
        });
        addLitRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var errLit = await addLitRes.Content.ReadAsStringAsync();
        errLit.Should().Contain("UNIT_GROUP_INCOMPATIBLE");

        // 4. Đổi hệ số tăng version và thêm dòng lịch sử
        var updateTanRes = await client.PutAsJsonAsync($"/api/products/{product.Id}/variants/{variant.Id}/units/{skuUnitTan!.Id}", new UpdateSkuUnitRequest
        {
            FactorToBase = 1050
        });
        updateTanRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedTan = await updateTanRes.Content.ReadFromJsonAsync<SkuUnitResponse>(_jsonOptions);
        updatedTan!.Version.Should().Be(2);
        updatedTan.FactorToBase.Should().Be(1050);

        var historyTan = await client.GetFromJsonAsync<List<SkuUnitFactorHistoryResponse>>($"/api/products/{product.Id}/variants/{variant.Id}/units/{skuUnitTan.Id}/history");
        historyTan.Should().HaveCount(2); // Initial (v1) + Updated (v2)

        // 5. Không sửa được hệ số đơn vị gốc
        var baseSkuUnit = variant.Units.First(u => u.IsBase);
        var updateBaseRes = await client.PutAsJsonAsync($"/api/products/{product.Id}/variants/{variant.Id}/units/{baseSkuUnit.Id}", new UpdateSkuUnitRequest
        {
            FactorToBase = 2
        });
        updateBaseRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errBase = await updateBaseRes.Content.ReadAsStringAsync();
        errBase.Should().Contain("INVALID_UNIT_FACTOR");

        // 6. Trùng mã vạch quy cách bị 409
        var unitThung = units!.First(u => u.Code == "thung");
        var dupBarcodeRes = await client.PostAsJsonAsync($"/api/products/{product.Id}/variants/{variant.Id}/units", new CreateSkuUnitRequest
        {
            UnitId = unitThung.Id,
            FactorToBase = 50,
            Barcode = "8931112223334" // Trùng với skuUnitBao
        });
        dupBarcodeRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var errDup = await dupBarcodeRes.Content.ReadAsStringAsync();
        errDup.Should().Contain("DUPLICATE_BARCODE");

        // Chiều mã SKU trùng mã quy cách khi Create
        var dupSkuBarcodeRes = await client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Gạo khác",
            Variants = new List<CreateVariantRequest>
            {
                new()
                {
                    SKU = "GAO-KHAC",
                    VariantName = "Gạo khác",
                    Barcode = "8931112223334", // Trùng mã bao đã tạo ở trên
                    RetailPrice = 25000,
                    BaseUnitId = unitKg.Id
                }
            }
        });
        dupSkuBarcodeRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var errDupSku = await dupSkuBarcodeRes.Content.ReadAsStringAsync();
        errDupSku.Should().Contain("DUPLICATE_BARCODE");

        // 7. Đổi đơn vị gốc khi đã có giao dịch bị BASE_UNIT_LOCKED
        // Giả lập giao dịch trên variant: thêm một order_item cho variant này
        await using (var adminConn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await adminConn.OpenAsync();
            var testBranchId = Guid.NewGuid();
            var testOrderId = Guid.NewGuid();
            await using var txCmd = new NpgsqlCommand(@"
                INSERT INTO public.branches (id, tenant_id, name, is_active, created_at)
                VALUES (@bid, @tid, 'Branch Test Tx', true, now());

                INSERT INTO public.orders (id, tenant_id, number, branch_id, channel, status, total_amount, request_key, created_at)
                VALUES (@ordId, @tid, 'ORD-TX-01', @bid, 'POS', 'Draft', 25000, gen_random_uuid(), now());

                INSERT INTO public.order_items (id, tenant_id, order_id, sku_id, sku_code_snapshot, sku_name_snapshot, quantity, unit_price, cost_price, created_at)
                VALUES (gen_random_uuid(), @tid, @ordId, @sid, 'GAO-ST25', 'Gạo', 1, 25000, 20000, now());
            ", adminConn);
            txCmd.Parameters.AddWithValue("bid", testBranchId);
            txCmd.Parameters.AddWithValue("tid", tenantId);
            txCmd.Parameters.AddWithValue("ordId", testOrderId);
            txCmd.Parameters.AddWithValue("sid", variant.Id);
            await txCmd.ExecuteNonQueryAsync();
        }

        var updateVariantRes = await client.PutAsJsonAsync($"/api/products/{product.Id}/variants/{variant.Id}", new UpdateVariantRequest
        {
            VariantName = variant.VariantName,
            RetailPrice = variant.RetailPrice,
            WholesalePrice = variant.WholesalePrice,
            BaseUnitId = unitTan.Id
        });
        updateVariantRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var errLocked = await updateVariantRes.Content.ReadAsStringAsync();
        errLocked.Should().Contain("BASE_UNIT_LOCKED");
    }

    // =========================================================================
    // F-01: Auto-seeding 11 default units for new tenants
    // =========================================================================
    [Fact]
    public async Task F01_DefaultUnits_SeededProperly_AcrossCreationScenarios()
    {
        // 1. Tạo SKU trước rồi GET -> có đủ kg, bao, lit
        var (tokenA, tenantA) = await RegisterAndLoginOwnerAsync("t_f01_a", "owner_f01_a@test.com");
        var clientA = _factory.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);

        var prodARes = await clientA.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Sản phẩm A",
            Variants = new List<CreateVariantRequest>
            {
                new() { SKU = "SKU-A-01", VariantName = "A1", RetailPrice = 10000, Unit = "piece" }
            }
        });
        prodARes.StatusCode.Should().Be(HttpStatusCode.Created);

        var listA = await clientA.GetFromJsonAsync<List<UnitResponse>>("/api/units");
        listA.Should().Contain(u => u.Code == "kg");
        listA.Should().Contain(u => u.Code == "bao");
        listA.Should().Contain(u => u.Code == "lit");

        // 2. Tạo SKU với unit "kg" trước -> kg thuộc nhóm Mass (không bị gán Count)
        var (tokenB, tenantB) = await RegisterAndLoginOwnerAsync("t_f01_b", "owner_f01_b@test.com");
        var clientB = _factory.CreateClient();
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);

        var prodBRes = await clientB.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Sản phẩm B",
            Variants = new List<CreateVariantRequest>
            {
                new() { SKU = "SKU-B-01", VariantName = "B1", RetailPrice = 10000, Unit = "kg" }
            }
        });
        prodBRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var listB = await clientB.GetFromJsonAsync<List<UnitResponse>>("/api/units");
        var kgUnit = listB!.First(u => u.Code == "kg");
        kgUnit.MeasureGroup.Should().Be("Mass");

        // 3. POST đơn vị tùy chỉnh trước rồi GET -> vẫn có kg và đầy đủ default units
        var (tokenC, tenantC) = await RegisterAndLoginOwnerAsync("t_f01_c", "owner_f01_c@test.com");
        var clientC = _factory.CreateClient();
        clientC.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenC);

        var customUnitRes = await clientC.PostAsJsonAsync("/api/units", new CreateUnitRequest
        {
            Code = "custom_box",
            Name = "Hộp đặc biệt",
            Symbol = "hdb",
            MeasureGroup = "Packaging",
            DecimalPlaces = 0
        });
        customUnitRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var listC = await clientC.GetFromJsonAsync<List<UnitResponse>>("/api/units");
        listC.Should().Contain(u => u.Code == "custom_box");
        listC.Should().Contain(u => u.Code == "kg");
        listC.Should().Contain(u => u.Code == "lit");
    }

    // =========================================================================
    // F-02: Concurrency & Transactional Lock
    // =========================================================================
    [Fact]
    public async Task F02_Concurrency_GetUnitsAndPostProducts_NoDuplicateCodes()
    {
        // 12 GET /api/units đồng thời trên tenant mới đều 200
        var (tokenGet, tenantGetId) = await RegisterAndLoginOwnerAsync("t_f02_get", "owner_f02_get@test.com");
        var clientGet = _factory.CreateClient();
        clientGet.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenGet);

        var getTasks = Enumerable.Range(0, 12).Select(_ => clientGet.GetAsync("/api/units")).ToList();
        var getResponses = await Task.WhenAll(getTasks);
        foreach (var res in getResponses)
        {
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // 8 POST /api/products đồng thời trên tenant mới đều 201
        var (tokenPost, tenantPostId) = await RegisterAndLoginOwnerAsync("t_f02_post", "owner_f02_post@test.com");
        var clientPost = _factory.CreateClient();
        clientPost.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenPost);

        var postTasks = Enumerable.Range(1, 8).Select(i => clientPost.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = $"Sản phẩm {i}",
            Variants = new List<CreateVariantRequest>
            {
                new() { SKU = $"SKU-CONCUR-{i}", VariantName = $"Var {i}", RetailPrice = 10000 * i, Unit = "kg" }
            }
        })).ToList();

        var postResponses = await Task.WhenAll(postTasks);
        foreach (var res in postResponses)
        {
            res.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // Sau đó không có unit code nào bị trùng lặp trong database
        await using var adminConn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString);
        await adminConn.OpenAsync();
        foreach (var tid in new[] { tenantGetId, tenantPostId })
        {
            await using var dupCmd = new NpgsqlCommand("SELECT code, count(*) FROM public.units WHERE tenant_id = @tid GROUP BY code HAVING count(*) > 1;", adminConn);
            dupCmd.Parameters.AddWithValue("tid", tid);
            await using var reader = await dupCmd.ExecuteReaderAsync();
            reader.HasRows.Should().BeFalse("Không được có mã đơn vị trùng lặp.");
        }
    }

    // =========================================================================
    // F-03: Default Switch & Fallback to Base Unit
    // =========================================================================
    [Fact]
    public async Task F03_DefaultUnitSwitch_AndFallbackToBaseWhenDisabled()
    {
        var (token, tenantId) = await RegisterAndLoginOwnerAsync("t_f03", "owner_f03@test.com");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var units = await client.GetFromJsonAsync<List<UnitResponse>>("/api/units");
        var unitKg = units!.First(u => u.Code == "kg");
        var unitBao = units!.First(u => u.Code == "bao");

        var createProdRes = await client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Gạo F03",
            Variants = new List<CreateVariantRequest>
            {
                new() { SKU = "GAO-F03", VariantName = "Gạo", RetailPrice = 20000, BaseUnitId = unitKg.Id }
            }
        });
        createProdRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var prodJson = await createProdRes.Content.ReadFromJsonAsync<JsonElement>();
        var prodId = prodJson.GetProperty("productId").GetGuid();
        var product = await client.GetFromJsonAsync<ProductResponse>($"/api/products/{prodId}");
        var variant = product!.Variants.First();

        // Thêm bao
        var addBaoRes = await client.PostAsJsonAsync($"/api/products/{prodId}/variants/{variant.Id}/units", new CreateSkuUnitRequest
        {
            UnitId = unitBao.Id,
            FactorToBase = 25,
            CanPurchase = true,
            CanSell = true
        });
        addBaoRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var baoSkuUnit = await addBaoRes.Content.ReadFromJsonAsync<SkuUnitResponse>(_jsonOptions);
        var kgSkuUnit = variant.Units.First(u => u.IsBase);

        // 1. Đổi mặc định: bao -> kg -> bao cho cả bán và mua đều 200
        // Đặt bao làm default
        var setBaoDefRes = await client.PutAsJsonAsync($"/api/products/{prodId}/variants/{variant.Id}/units/{baoSkuUnit!.Id}", new UpdateSkuUnitRequest
        {
            IsDefaultSale = true,
            IsDefaultPurchase = true
        });
        setBaoDefRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Đặt kg làm default
        var setKgDefRes = await client.PutAsJsonAsync($"/api/products/{prodId}/variants/{variant.Id}/units/{kgSkuUnit.Id}", new UpdateSkuUnitRequest
        {
            IsDefaultSale = true,
            IsDefaultPurchase = true
        });
        setKgDefRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Đặt bao làm default lần nữa
        var setBaoDefAgain = await client.PutAsJsonAsync($"/api/products/{prodId}/variants/{variant.Id}/units/{baoSkuUnit.Id}", new UpdateSkuUnitRequest
        {
            IsDefaultSale = true,
            IsDefaultPurchase = true
        });
        setBaoDefAgain.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Tắt đơn vị đang mặc định (bao) -> dòng gốc (kg) trở thành mặc định
        var disableBaoRes = await client.PutAsJsonAsync($"/api/products/{prodId}/variants/{variant.Id}/units/{baoSkuUnit.Id}", new UpdateSkuUnitRequest
        {
            IsActive = false
        });
        disableBaoRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedUnits = await client.GetFromJsonAsync<List<SkuUnitResponse>>($"/api/products/{prodId}/variants/{variant.Id}/units");
        var kgAfter = updatedUnits!.First(u => u.IsBase);
        kgAfter.IsDefaultSale.Should().BeTrue("Khi tắt đơn vị mặc định, dòng gốc phải trở thành mặc định bán.");
        kgAfter.IsDefaultPurchase.Should().BeTrue("Khi tắt đơn vị mặc định, dòng gốc phải trở thành mặc định mua.");
    }

    // =========================================================================
    // F-04: Barcode Collision on Variant Self SkuUnits
    // =========================================================================
    [Fact]
    public async Task F04_PutVariant_UsingBarcodeOfOwnBaoUnit_Returns409DuplicateBarcode()
    {
        var (token, tenantId) = await RegisterAndLoginOwnerAsync("t_f04", "owner_f04@test.com");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var units = await client.GetFromJsonAsync<List<UnitResponse>>("/api/units");
        var unitKg = units!.First(u => u.Code == "kg");
        var unitBao = units!.First(u => u.Code == "bao");

        var createProdRes = await client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Gạo F04",
            Variants = new List<CreateVariantRequest>
            {
                new() { SKU = "GAO-F04", VariantName = "Gạo", Barcode = "8930000000041", RetailPrice = 20000, BaseUnitId = unitKg.Id }
            }
        });
        createProdRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var prodJson = await createProdRes.Content.ReadFromJsonAsync<JsonElement>();
        var prodId = prodJson.GetProperty("productId").GetGuid();
        var product = await client.GetFromJsonAsync<ProductResponse>($"/api/products/{prodId}");
        var variant = product!.Variants.First();

        // Thêm bao với barcode 8930000000042
        var addBaoRes = await client.PostAsJsonAsync($"/api/products/{prodId}/variants/{variant.Id}/units", new CreateSkuUnitRequest
        {
            UnitId = unitBao.Id,
            FactorToBase = 25,
            Barcode = "8930000000042",
            CanPurchase = true,
            CanSell = true
        });
        addBaoRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // PUT variant dùng mã của bao chính nó -> 409 DUPLICATE_BARCODE
        var putRes = await client.PutAsJsonAsync($"/api/products/{prodId}/variants/{variant.Id}", new UpdateVariantRequest
        {
            VariantName = variant.VariantName,
            Barcode = "8930000000042",
            RetailPrice = variant.RetailPrice,
            WholesalePrice = variant.WholesalePrice
        });
        putRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var errContent = await putRes.Content.ReadAsStringAsync();
        errContent.Should().Contain("DUPLICATE_BARCODE");
    }

    // =========================================================================
    // F-06: Audit Fields ChangedBy and UpdatedBy
    // =========================================================================
    [Fact]
    public async Task F06_AuditFields_ChangedByAndUpdatedBy_RecordJwtUserId()
    {
        var (token, tenantId) = await RegisterAndLoginOwnerAsync("t_f06", "owner_f06@test.com");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var units = await client.GetFromJsonAsync<List<UnitResponse>>("/api/units");
        var unitKg = units!.First(u => u.Code == "kg");
        var unitBao = units!.First(u => u.Code == "bao");

        var createProdRes = await client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Gạo F06",
            Variants = new List<CreateVariantRequest>
            {
                new() { SKU = "GAO-F06", VariantName = "Gạo", RetailPrice = 20000, BaseUnitId = unitKg.Id }
            }
        });
        createProdRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var prodJson = await createProdRes.Content.ReadFromJsonAsync<JsonElement>();
        var prodId = prodJson.GetProperty("productId").GetGuid();
        var product = await client.GetFromJsonAsync<ProductResponse>($"/api/products/{prodId}");
        var variant = product!.Variants.First();

        // 1. Thêm bao -> History có ChangedBy đúng owner
        var addBaoRes = await client.PostAsJsonAsync($"/api/products/{prodId}/variants/{variant.Id}/units", new CreateSkuUnitRequest
        {
            UnitId = unitBao.Id,
            FactorToBase = 25,
            CanPurchase = true,
            CanSell = true
        });
        addBaoRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var baoSkuUnit = await addBaoRes.Content.ReadFromJsonAsync<SkuUnitResponse>(_jsonOptions);

        // 2. Cập nhật bao -> sku_units.updated_by và history.changed_by đúng owner
        var updateBaoRes = await client.PutAsJsonAsync($"/api/products/{prodId}/variants/{variant.Id}/units/{baoSkuUnit!.Id}", new UpdateSkuUnitRequest
        {
            FactorToBase = 26
        });
        updateBaoRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Kiểm tra database
        await using var adminConn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString);
        await adminConn.OpenAsync();

        // Lấy owner id
        await using var userCmd = new NpgsqlCommand("SELECT id FROM public.users WHERE tenant_id = @tid AND email = 'owner_f06@test.com';", adminConn);
        userCmd.Parameters.AddWithValue("tid", tenantId);
        var ownerUserId = (Guid)(await userCmd.ExecuteScalarAsync())!;

        // sku_units.updated_by
        await using var skuUnitCmd = new NpgsqlCommand("SELECT updated_by FROM public.sku_units WHERE id = @id;", adminConn);
        skuUnitCmd.Parameters.AddWithValue("id", baoSkuUnit.Id);
        var updatedBy = (Guid)(await skuUnitCmd.ExecuteScalarAsync())!;
        updatedBy.Should().Be(ownerUserId);

        // sku_unit_factor_history.changed_by
        await using var histCmd = new NpgsqlCommand("SELECT changed_by FROM public.sku_unit_factor_history WHERE sku_unit_id = @id ORDER BY new_version;", adminConn);
        histCmd.Parameters.AddWithValue("id", baoSkuUnit.Id);
        await using var histReader = await histCmd.ExecuteReaderAsync();
        while (await histReader.ReadAsync())
        {
            histReader.IsDBNull(0).Should().BeFalse();
            histReader.GetGuid(0).Should().Be(ownerUserId);
        }
    }

    // =========================================================================
    // F-07: Input Validation Boundaries
    // =========================================================================
    [Fact]
    public async Task F07_InputValidation_FactorDigits_SalePrice_BarcodeAndCodeLength()
    {
        var (token, tenantId) = await RegisterAndLoginOwnerAsync("t_f07", "owner_f07@test.com");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var units = await client.GetFromJsonAsync<List<UnitResponse>>("/api/units");
        var unitKg = units!.First(u => u.Code == "kg");
        var unitBao = units!.First(u => u.Code == "bao");
        var unitTan = units!.First(u => u.Code == "tan");

        var createProdRes = await client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Gạo F07",
            Variants = new List<CreateVariantRequest>
            {
                new() { SKU = "GAO-F07", VariantName = "Gạo", RetailPrice = 20000, BaseUnitId = unitKg.Id }
            }
        });
        createProdRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var prodJson = await createProdRes.Content.ReadFromJsonAsync<JsonElement>();
        var prodId = prodJson.GetProperty("productId").GetGuid();
        var product = await client.GetFromJsonAsync<ProductResponse>($"/api/products/{prodId}");
        var variant = product!.Variants.First();

        // 1. Factor > 12 chữ số nguyên -> INVALID_UNIT_FACTOR (400)
        var invalidFactorInt = await client.PostAsJsonAsync($"/api/products/{prodId}/variants/{variant.Id}/units", new CreateSkuUnitRequest
        {
            UnitId = unitBao.Id,
            FactorToBase = 1_000_000_000_000m
        });
        invalidFactorInt.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalidFactorInt.Content.ReadAsStringAsync()).Should().Contain("INVALID_UNIT_FACTOR");

        // 2. Factor > 6 chữ số thập phân -> INVALID_UNIT_FACTOR (400)
        var invalidFactorDec = await client.PostAsJsonAsync($"/api/products/{prodId}/variants/{variant.Id}/units", new CreateSkuUnitRequest
        {
            UnitId = unitTan.Id,
            FactorToBase = 0.1234567m
        });
        invalidFactorDec.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalidFactorDec.Content.ReadAsStringAsync()).Should().Contain("INVALID_UNIT_FACTOR");

        // 3. SalePrice âm -> 400
        var negPriceRes = await client.PostAsJsonAsync($"/api/products/{prodId}/variants/{variant.Id}/units", new CreateSkuUnitRequest
        {
            UnitId = unitBao.Id,
            FactorToBase = 25,
            SalePrice = -100
        });
        negPriceRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 4. Barcode > 128 ký tự -> 400
        var longBarcodeRes = await client.PostAsJsonAsync($"/api/products/{prodId}/variants/{variant.Id}/units", new CreateSkuUnitRequest
        {
            UnitId = unitBao.Id,
            FactorToBase = 25,
            Barcode = new string('A', 129)
        });
        longBarcodeRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 5. Code > 30 ký tự khi UpdateUnit -> 400
        var unitToUpdate = units!.First(u => u.Code == "bao");
        var longCodeRes = await client.PutAsJsonAsync($"/api/units/{unitToUpdate.Id}", new UpdateUnitRequest
        {
            Code = new string('C', 31),
            Name = "Tên",
            Symbol = "T"
        });
        longCodeRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // =========================================================================
    // F-08: Base Unit Lock on Active Conversion Units
    // =========================================================================
    [Fact]
    public async Task F08_BaseUnitChange_BlockedByActiveConversionUnits_AllowedWhenDeactivated()
    {
        var (token, tenantId) = await RegisterAndLoginOwnerAsync("t_f08", "owner_f08@test.com");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var units = await client.GetFromJsonAsync<List<UnitResponse>>("/api/units");
        var unitKg = units!.First(u => u.Code == "kg");
        var unitBao = units!.First(u => u.Code == "bao");
        var unitTan = units!.First(u => u.Code == "tan");

        var createProdRes = await client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Gạo F08",
            Variants = new List<CreateVariantRequest>
            {
                new() { SKU = "GAO-F08", VariantName = "Gạo", RetailPrice = 20000, BaseUnitId = unitKg.Id }
            }
        });
        createProdRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var prodJson = await createProdRes.Content.ReadFromJsonAsync<JsonElement>();
        var prodId = prodJson.GetProperty("productId").GetGuid();
        var product = await client.GetFromJsonAsync<ProductResponse>($"/api/products/{prodId}");
        var variant = product!.Variants.First();

        // Thêm bao (đang kích hoạt, chưa có giao dịch)
        var addBaoRes = await client.PostAsJsonAsync($"/api/products/{prodId}/variants/{variant.Id}/units", new CreateSkuUnitRequest
        {
            UnitId = unitBao.Id,
            FactorToBase = 25,
            CanPurchase = true,
            CanSell = true
        });
        addBaoRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var baoSkuUnit = await addBaoRes.Content.ReadFromJsonAsync<SkuUnitResponse>(_jsonOptions);

        // 1. Đổi BaseUnitId khi còn đơn vị bao đang bật -> 409 BASE_UNIT_LOCKED
        var updateBlockedRes = await client.PutAsJsonAsync($"/api/products/{prodId}/variants/{variant.Id}", new UpdateVariantRequest
        {
            VariantName = variant.VariantName,
            RetailPrice = variant.RetailPrice,
            WholesalePrice = variant.WholesalePrice,
            BaseUnitId = unitTan.Id
        });
        updateBlockedRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await updateBlockedRes.Content.ReadAsStringAsync()).Should().Contain("BASE_UNIT_LOCKED");

        // 2. Tắt đơn vị bao (IsActive = false)
        var deactivateRes = await client.PutAsJsonAsync($"/api/products/{prodId}/variants/{variant.Id}/units/{baoSkuUnit!.Id}", new UpdateSkuUnitRequest
        {
            IsActive = false
        });
        deactivateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Đổi BaseUnitId khi không còn đơn vị quy đổi đang bật -> 200 OK
        var updateSuccessRes = await client.PutAsJsonAsync($"/api/products/{prodId}/variants/{variant.Id}", new UpdateVariantRequest
        {
            VariantName = variant.VariantName,
            RetailPrice = variant.RetailPrice,
            WholesalePrice = variant.WholesalePrice,
            BaseUnitId = unitTan.Id
        });
        updateSuccessRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedProd = await client.GetFromJsonAsync<ProductResponse>($"/api/products/{prodId}");
        var updatedVar = updatedProd!.Variants.First();
        updatedVar.BaseUnitCode.Should().Be("tan");
    }

    // =========================================================================
    // Review: Tenant khác không dùng được đơn vị và SKU của nhau qua API
    // =========================================================================
    [Fact]
    public async Task Review_TenantKhac_KhongDungDuocDonViVaSkuCuaNhauQuaApi()
    {
        var (tokenA, _) = await RegisterAndLoginOwnerAsync("tenant_cross_a", "owner_cross_a@test.com");
        var (tokenB, _) = await RegisterAndLoginOwnerAsync("tenant_cross_b", "owner_cross_b@test.com");

        var clientA = _factory.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var clientB = _factory.CreateClient();
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);

        var unitsA = await clientA.GetFromJsonAsync<List<UnitResponse>>("/api/units", _jsonOptions);
        var unitsB = await clientB.GetFromJsonAsync<List<UnitResponse>>("/api/units", _jsonOptions);
        var baoA = unitsA!.Single(u => u.Code == "bao").Id;
        var kgA = unitsA!.Single(u => u.Code == "kg").Id;
        var kgB = unitsB!.Single(u => u.Code == "kg").Id;

        async Task<(Guid ProductId, Guid VariantId)> CreateSkuAsync(HttpClient client, string sku, Guid baseUnitId)
        {
            var res = await client.PostAsJsonAsync("/api/products", new CreateProductRequest
            {
                Name = sku,
                Variants = new List<CreateVariantRequest> { new() { SKU = sku, VariantName = sku, RetailPrice = 1000, BaseUnitId = baseUnitId } }
            });
            res.StatusCode.Should().Be(HttpStatusCode.Created);
            var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("productId").GetGuid();
            var product = await client.GetFromJsonAsync<ProductResponse>($"/api/products/{id}", _jsonOptions);
            return (id, product!.Variants.Single().Id);
        }

        var (productA, variantA) = await CreateSkuAsync(clientA, "CROSS-A", kgA);
        var (productB, variantB) = await CreateSkuAsync(clientB, "CROSS-B", kgB);
        var addUnit = new CreateSkuUnitRequest { UnitId = baoA, FactorToBase = 25, QuantityDecimalPlaces = 0 };

        (await clientB.PutAsJsonAsync($"/api/units/{baoA}", new UpdateUnitRequest { Name = "Hack" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await clientB.PostAsJsonAsync($"/api/products/{productB}/variants/{variantB}/units", addUnit))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await clientB.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Cross",
            Variants = new List<CreateVariantRequest> { new() { SKU = "CROSS-X", VariantName = "x", RetailPrice = 1, BaseUnitId = baoA } }
        })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await clientB.GetAsync($"/api/products/{productA}/variants/{variantA}/units"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await clientB.PostAsJsonAsync($"/api/products/{productA}/variants/{variantA}/units", addUnit))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
