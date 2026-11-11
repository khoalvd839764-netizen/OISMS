using FluentAssertions;
using Npgsql;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

[Collection("PostgresIntegrationTests")]
public class PriceListMigrationIntegrationTests : IAsyncLifetime
{
    private readonly string _adminConnStr = PostgresTestEnvironment.AdminDatabaseConnectionString;

    public async Task InitializeAsync()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
    }

    public async Task DisposeAsync()
    {
        var migrator = new DatabaseMigrationService();
        await migrator.UpgradeDatabaseSchemaAsync(_adminConnStr);
    }

    private async Task ResetToPrePriceListUpgradeStateAsync(NpgsqlConnection conn)
    {
        await using var cmd = new NpgsqlCommand(@"
            ALTER TABLE public.order_items DROP COLUMN IF EXISTS sku_unit_price_id CASCADE;
            ALTER TABLE public.order_items DROP COLUMN IF EXISTS price_source CASCADE;
            DROP TABLE IF EXISTS public.sku_unit_prices CASCADE;
            DROP TABLE IF EXISTS public.price_change_request_items CASCADE;
            DROP TABLE IF EXISTS public.price_change_requests CASCADE;
            DROP TABLE IF EXISTS public.price_lists CASCADE;
            DROP FUNCTION IF EXISTS public.oisms_price_change_request_guard CASCADE;
            DROP FUNCTION IF EXISTS public.oisms_sku_unit_price_guard CASCADE;
        ", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task AC02_Migration_RunTwice_DoesNotDuplicateRows()
    {
        var migrator = new DatabaseMigrationService();
        await using var conn = new NpgsqlConnection(_adminConnStr);
        await conn.OpenAsync();

        try
        {
            await ResetToPrePriceListUpgradeStateAsync(conn);

            // Tạo 1 tenant với SKU mẫu
            var tenantId = Guid.NewGuid();
            var unitId = Guid.NewGuid();
            var prodId = Guid.NewGuid();
            var skuId = Guid.NewGuid();

            await using (var seedCmd = new NpgsqlCommand(@"
                INSERT INTO public.tenants (id, name, is_active) VALUES (@tId, 'Run Twice Tenant', true);
                INSERT INTO public.units (id, tenant_id, code, name, symbol, measure_group, decimal_places, is_active)
                VALUES (@uId, @tId, 'cai', 'Cái', 'cái', 'Count', 0, true);
                INSERT INTO public.products (id, tenant_id, name, is_active)
                VALUES (@pId, @tId, 'Product Run Twice', true);
                INSERT INTO public.skus (id, tenant_id, product_id, code, retail_price, wholesale_price, base_unit_id, is_active)
                VALUES (@sId, @tId, @pId, 'SKU-RUN-TWICE', 120000, 90000, @uId, true);
                INSERT INTO public.sku_units (id, tenant_id, sku_id, unit_id, factor_to_base, can_sell, is_active)
                VALUES (gen_random_uuid(), @tId, @sId, @uId, 1, true, true);
            ", conn))
            {
                seedCmd.Parameters.AddWithValue("tId", tenantId);
                seedCmd.Parameters.AddWithValue("uId", unitId);
                seedCmd.Parameters.AddWithValue("pId", prodId);
                seedCmd.Parameters.AddWithValue("sId", skuId);
                await seedCmd.ExecuteNonQueryAsync();
            }

            // 1. Chạy upgrade lần 1
            var res1 = await migrator.UpgradeDatabaseSchemaAsync(_adminConnStr);
            res1.Success.Should().BeTrue();

            var getCount = async (string table) =>
            {
                await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM public.{table};", conn);
                return Convert.ToInt64(await cmd.ExecuteScalarAsync());
            };

            var plCount1 = await getCount("price_lists");
            var pcrCount1 = await getCount("price_change_requests");
            var pcriCount1 = await getCount("price_change_request_items");
            var supCount1 = await getCount("sku_unit_prices");

            // 2. Chạy upgrade lần 2
            var res2 = await migrator.UpgradeDatabaseSchemaAsync(_adminConnStr);
            res2.Success.Should().BeTrue();

            var plCount2 = await getCount("price_lists");
            var pcrCount2 = await getCount("price_change_requests");
            var pcriCount2 = await getCount("price_change_request_items");
            var supCount2 = await getCount("sku_unit_prices");

            plCount2.Should().Be(plCount1, "Chạy migration lần 2 không được sinh thêm bảng giá");
            pcrCount2.Should().Be(pcrCount1, "Chạy migration lần 2 không được sinh thêm phiếu");
            pcriCount2.Should().Be(pcriCount1, "Chạy migration lần 2 không được sinh thêm dòng phiếu");
            supCount2.Should().Be(supCount1, "Chạy migration lần 2 không được sinh thêm dòng giá");
        }
        finally
        {
            await migrator.UpgradeDatabaseSchemaAsync(_adminConnStr);
        }
    }

    [Fact]
    public async Task AC02_Migration_WholesalePriceZero_DoesNotCreatePriceRow()
    {
        var migrator = new DatabaseMigrationService();
        await using var conn = new NpgsqlConnection(_adminConnStr);
        await conn.OpenAsync();

        try
        {
            await ResetToPrePriceListUpgradeStateAsync(conn);

            var tenantId = Guid.NewGuid();
            var unitId = Guid.NewGuid();
            var prodId = Guid.NewGuid();
            var skuId = Guid.NewGuid();

            // Chuẩn bị 1 tenant với SKU có wholesale_price = 0, retail_price = 100000
            await using (var cmd = new NpgsqlCommand(@"
                INSERT INTO public.tenants (id, name, is_active) VALUES (@tId, 'Wholesale Zero Tenant', true);
                INSERT INTO public.units (id, tenant_id, code, name, symbol, measure_group, decimal_places, is_active)
                VALUES (@uId, @tId, 'cai', 'Cái', 'cái', 'Count', 0, true);
                INSERT INTO public.products (id, tenant_id, name, is_active)
                VALUES (@pId, @tId, 'Product Test', true);
                INSERT INTO public.skus (id, tenant_id, product_id, code, retail_price, wholesale_price, base_unit_id, is_active)
                VALUES (@sId, @tId, @pId, 'SKU-ZERO-WS', 100000, 0, @uId, true);
                INSERT INTO public.sku_units (id, tenant_id, sku_id, unit_id, factor_to_base, can_sell, is_active)
                VALUES (gen_random_uuid(), @tId, @sId, @uId, 1, true, true);
            ", conn))
            {
                cmd.Parameters.AddWithValue("tId", tenantId);
                cmd.Parameters.AddWithValue("uId", unitId);
                cmd.Parameters.AddWithValue("pId", prodId);
                cmd.Parameters.AddWithValue("sId", skuId);
                await cmd.ExecuteNonQueryAsync();
            }

            // Đảm bảo bảng giá và backfill chạy cho tenant này
            var res = await migrator.UpgradeDatabaseSchemaAsync(_adminConnStr);
            res.Success.Should().BeTrue();

            // Kiểm tra: Giá lẻ có dòng, giá sỉ KHÔNG có dòng giá
            await using (var checkCmd = new NpgsqlCommand(@"
                SELECT pl.code, p.price
                FROM public.sku_unit_prices p
                JOIN public.price_lists pl ON pl.id = p.price_list_id
                WHERE p.tenant_id = @tId AND p.sku_id = @sId;
            ", conn))
            {
                checkCmd.Parameters.AddWithValue("tId", tenantId);
                checkCmd.Parameters.AddWithValue("sId", skuId);

                var rows = new List<(string Code, decimal Price)>();
                await using var reader = await checkCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    rows.Add((reader.GetString(0), reader.GetDecimal(1)));
                }

                rows.Should().Contain(r => r.Code == "retail" && r.Price == 100000m, "Giá lẻ phải có dòng giá");
                rows.Should().NotContain(r => r.Code == "wholesale", "Giá sỉ = 0 không được có dòng giá");
            }
        }
        finally
        {
            await migrator.UpgradeDatabaseSchemaAsync(_adminConnStr);
        }
    }

    [Fact]
    public async Task AC02_Migration_SkuMissingBaseUnit_MigrationFailsAndStops()
    {
        var migrator = new DatabaseMigrationService();
        await using var conn = new NpgsqlConnection(_adminConnStr);
        await conn.OpenAsync();

        try
        {
            await ResetToPrePriceListUpgradeStateAsync(conn);

            var tenantId = Guid.NewGuid();
            var prodId = Guid.NewGuid();
            var badSkuId = Guid.NewGuid();

            // Tạo SKU thiếu base_unit_id và unit rỗng để multi_unit không tự động map
            await using (var cmd = new NpgsqlCommand(@"
                INSERT INTO public.tenants (id, name, is_active) VALUES (@tId, 'Missing Unit Tenant', true);
                INSERT INTO public.products (id, tenant_id, name, is_active)
                VALUES (@pId, @tId, 'Bad Product', true);
                INSERT INTO public.skus (id, tenant_id, product_id, code, unit, retail_price, wholesale_price, base_unit_id, is_active)
                VALUES (@sId, @tId, @pId, 'SKU-NO-BASE-UNIT', '', 50000, 30000, NULL, true);
            ", conn))
            {
                cmd.Parameters.AddWithValue("tId", tenantId);
                cmd.Parameters.AddWithValue("pId", prodId);
                cmd.Parameters.AddWithValue("sId", badSkuId);
                await cmd.ExecuteNonQueryAsync();
            }

            var res = await migrator.UpgradeDatabaseSchemaAsync(_adminConnStr);
            res.Success.Should().BeFalse("Migration phải dừng khi có SKU thiếu đơn vị gốc");
            res.Message.Should().Contain("SKU-NO-BASE-UNIT");
        }
        finally
        {
            // Xóa SKU lỗi trước khi upgrade để khôi phục schema
            await using var cleanCmd = new NpgsqlCommand("DELETE FROM public.skus WHERE code = 'SKU-NO-BASE-UNIT';", conn);
            await cleanCmd.ExecuteNonQueryAsync();
            await migrator.UpgradeDatabaseSchemaAsync(_adminConnStr);
        }
    }

    [Fact]
    public async Task AC02_Migration_ReRunSql_AfterStaffCreatedPendingSku_DoesNotGeneratePriceForPendingSku()
    {
        var migrator = new DatabaseMigrationService();
        await using var conn = new NpgsqlConnection(_adminConnStr);
        await conn.OpenAsync();

        try
        {
            await ResetToPrePriceListUpgradeStateAsync(conn);

            // 1. Chạy upgrade lần 1 để tạo bảng và backfill ban đầu
            var res1 = await migrator.UpgradeDatabaseSchemaAsync(_adminConnStr);
            res1.Success.Should().BeTrue();

            var tenantId = Guid.NewGuid();
            var unitId = Guid.NewGuid();
            var prodId = Guid.NewGuid();
            var staffSkuId = Guid.NewGuid();

            // 2. Thêm SKU mới (mô phỏng SKU do Staff tạo, giá lưu trên skus nhưng chưa được duyệt)
            await using (var cmd = new NpgsqlCommand(@"
                INSERT INTO public.tenants (id, name, is_active) VALUES (@tId, 'Staff Pending Tenant', true);
                INSERT INTO public.units (id, tenant_id, code, name, symbol, measure_group, decimal_places, is_active)
                VALUES (@uId, @tId, 'cai', 'Cái', 'cái', 'Count', 0, true);
                INSERT INTO public.products (id, tenant_id, name, is_active)
                VALUES (@pId, @tId, 'Staff Product', true);
                INSERT INTO public.skus (id, tenant_id, product_id, code, retail_price, wholesale_price, base_unit_id, is_active)
                VALUES (@sId, @tId, @pId, 'SKU-STAFF-PENDING', 250000, 180000, @uId, true);
                INSERT INTO public.sku_units (id, tenant_id, sku_id, unit_id, factor_to_base, can_sell, is_active)
                VALUES (gen_random_uuid(), @tId, @sId, @uId, 1, true, true);
            ", conn))
            {
                cmd.Parameters.AddWithValue("tId", tenantId);
                cmd.Parameters.AddWithValue("uId", unitId);
                cmd.Parameters.AddWithValue("pId", prodId);
                cmd.Parameters.AddWithValue("sId", staffSkuId);
                await cmd.ExecuteNonQueryAsync();
            }

            // 3. Chạy lại migration SQL (mô phỏng DatabaseMigrationService chạy lại khi restart app)
            var res2 = await migrator.UpgradeDatabaseSchemaAsync(_adminConnStr);
            res2.Success.Should().BeTrue();

            // 4. Xác minh: KHÔNG sinh dòng giá nào cho staffSkuId trong sku_unit_prices
            await using (var checkCmd = new NpgsqlCommand(@"
                SELECT count(*) FROM public.sku_unit_prices WHERE sku_id = @sId;
            ", conn))
            {
                checkCmd.Parameters.AddWithValue("sId", staffSkuId);
                var priceCount = Convert.ToInt64(await checkCmd.ExecuteScalarAsync());
                priceCount.Should().Be(0, "Chạy lại migration SQL tuyệt đối không được sinh dòng giá cho SKU mới tạo chưa qua duyệt");
            }
        }
        finally
        {
            await migrator.UpgradeDatabaseSchemaAsync(_adminConnStr);
        }
    }
}
