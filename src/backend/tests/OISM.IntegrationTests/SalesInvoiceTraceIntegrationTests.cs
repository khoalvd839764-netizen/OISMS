using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.SalesInvoices.Dtos;
using OISM.Application.Features.StockCounts.Dtos;
using OISM.Application.Features.StockTransfers.Dtos;
using OISM.Application.Features.Webhooks.Dtos;
using OISM.Application.Features.Webhooks.Services;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using OISM.Infrastructure.Services;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// OISMS-47-U02: nâng cấp từ schema trước U02, đơn sàn và /process, đủ các loại truy vết,
/// giữ nguyên dữ liệu đã chụp khi in lại, phân quyền và cô lập tenant trên mọi endpoint.
/// </summary>
[Collection("PostgresIntegrationTests")]
public class SalesInvoiceTraceIntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class TestTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private sealed record Infra(Guid TenantId, Guid BranchId, Guid WarehouseId, Guid SupplierId, Guid OwnerId, Guid StaffId, Guid CashierId, Guid SkuId, string SkuCode, decimal Price);

    public SalesInvoiceTraceIntegrationTests(WebApplicationFactoryWithPostgres factory)
    {
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => PostgresTestEnvironment.ResetTestDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static (AppDbContext Context, TestTenantService TenantService) CreateContext(Guid? tenantId = null)
    {
        var ts = new TestTenantService();
        if (tenantId.HasValue)
        {
            ts.SetTenant(tenantId.Value);
        }
        return (PostgresTestEnvironment.CreateRuntimeDbContext(ts), ts);
    }

    private async Task<Infra> SetupAsync(decimal initialStock = 100m)
    {
        var (seedContext, _) = CreateContext();
        await using (seedContext)
        {
            await OismsDatabaseSeeder.SeedAsync(seedContext, PostgresTestEnvironment.AppPassword);
        }

        var tenantId = OismsDatabaseSeeder.TenantAId;
        var (context, tenantService) = CreateContext(tenantId);
        await using (context)
        {
            var branch = await context.Branches.FirstAsync(b => b.TenantId == tenantId && b.IsActive);
            var warehouse = await context.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branch.Id && w.IsActive);
            var supplier = await context.Suppliers.FirstAsync(s => s.TenantId == tenantId && s.IsActive);
            var owner = await context.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local");
            var staff = await context.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "staff@shopa.local");
            var cashier = await context.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "cashier@shopa.local");
            var sku = await context.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);

            await PriceSeed.SyncFromLegacyColumnsAsync(context);
            await PurchaseOrderSeed.ReceiveStockAsync(context, tenantService, branch.Id, warehouse.Id, supplier.Id, staff.Id, (sku.Id, initialStock, 50_000m));

            return new Infra(tenantId, branch.Id, warehouse.Id, supplier.Id, owner.Id, staff.Id, cashier.Id, sku.Id, sku.SKU, sku.RetailPrice);
        }
    }

    private async Task LoginAsync(string email)
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var res = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = PostgresTestEnvironment.AppPassword });
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
    }

    private async Task<PosCheckoutResponseDto> CheckoutPosAsync(Infra infra, decimal quantity = 1m)
    {
        await LoginAsync("cashier@shopa.local");
        var res = await _client.PostAsJsonAsync("/api/pos/checkout", new PosCheckoutRequestDto
        {
            BranchId = infra.BranchId,
            RequestKey = Guid.NewGuid(),
            PaymentMethod = "Cash",
            AmountGiven = 10_000_000m,
            CustomerName = "Khách U02",
            CustomerPhone = "0909000111",
            Items = new List<OrderItemRequestDto> { new() { SkuId = infra.SkuId, SkuCode = infra.SkuCode, Quantity = quantity, UnitPrice = infra.Price } }
        });
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions))!;
    }

    private async Task<DocumentTraceDto> GetTraceAsync(string url)
    {
        var res = await _client.GetAsync(url);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<DocumentTraceDto>(_jsonOptions))!;
    }

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage res)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("errorCode").GetString()!;
    }

    private static async Task<int> CountInvoicesAsync(Guid tenantId)
    {
        var (context, _) = CreateContext(tenantId);
        await using (context)
        {
            return await context.SalesInvoices.CountAsync();
        }
    }

    // =========================================================================
    // AC-01: Đi từ schema trước U02 qua UpgradeDatabaseSchemaAsync phải khớp schema mới
    // =========================================================================
    [Fact]
    public async Task AC01_UpgradeFromPreU02Schema_TwiceIdempotent_MatchesFreshSchemaExactly()
    {
        const string downgradeSql = @"
DROP TABLE public.sales_invoices;
ALTER TABLE public.orders DROP COLUMN confirmed_by;
ALTER TABLE public.stock_transfers DROP COLUMN dispatched_by;
CREATE OR REPLACE FUNCTION public.oisms_protect_order() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
BEGIN
  IF (NEW.tenant_id,NEW.branch_id) IS DISTINCT FROM (OLD.tenant_id,OLD.branch_id)
     OR (OLD.confirmed_at IS NOT NULL AND NEW.confirmed_at IS DISTINCT FROM OLD.confirmed_at) THEN
    RAISE EXCEPTION 'Order identity and confirmed timestamp are immutable' USING ERRCODE='23514';
  END IF;
  RETURN NEW;
END $$;
CREATE OR REPLACE FUNCTION public.oisms_protect_order_item() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$ BEGIN RETURN NEW; END $$;";

        var migrator = new DatabaseMigrationService();
        var freshDb = $"oisms_u02_fresh_{Guid.NewGuid():N}";
        var upgradedDb = $"oisms_u02_upgraded_{Guid.NewGuid():N}";
        string Conn(string db) => PostgresTestEnvironment.BuildConnectionString(
            PostgresTestEnvironment.Host, PostgresTestEnvironment.Port, db, PostgresTestEnvironment.AdminUser, PostgresTestEnvironment.AdminPassword);

        await using (var server = new NpgsqlConnection(PostgresTestEnvironment.AdminServerConnectionString))
        {
            await server.OpenAsync();
            foreach (var db in new[] { freshDb, upgradedDb })
            {
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{db}\" ENCODING 'UTF8';", server);
                await create.ExecuteNonQueryAsync();
            }
        }

        try
        {
            (await migrator.InitializeOrBaselineDatabaseAsync(Conn(freshDb))).Success.Should().BeTrue();
            (await migrator.InitializeOrBaselineDatabaseAsync(Conn(upgradedDb))).Success.Should().BeTrue();

            await using var upgraded = new NpgsqlConnection(Conn(upgradedDb));
            await using var fresh = new NpgsqlConnection(Conn(freshDb));
            await upgraded.OpenAsync();
            await fresh.OpenAsync();

            await using (var downgrade = new NpgsqlCommand(downgradeSql, upgraded))
            {
                await downgrade.ExecuteNonQueryAsync();
            }

            var first = await migrator.UpgradeDatabaseSchemaAsync(Conn(upgradedDb));
            first.Success.Should().BeTrue(first.Message);
            var second = await migrator.UpgradeDatabaseSchemaAsync(Conn(upgradedDb));
            second.Success.Should().BeTrue(second.Message);

            var validation = await DatabaseMigrationService.ValidateSchemaComprehensiveAsync(upgraded, null, default);
            validation.DriftErrors.Should().BeEmpty();

            var snapshotQueries = new Dictionary<string, string>
            {
                ["tables"] = "SELECT table_name FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE'",
                ["columns"] = @"SELECT table_name||'.'||column_name||':'||data_type||':'||coalesce(character_maximum_length::text,'')||':'||coalesce(numeric_precision::text,'')||':'||coalesce(numeric_scale::text,'')||':'||is_nullable||':'||coalesce(column_default,'')
                                FROM information_schema.columns WHERE table_schema='public' AND table_name IN ('sales_invoices','orders','stock_transfers')",
                ["constraints"] = @"SELECT conrelid::regclass::text||':'||conname||':'||pg_get_constraintdef(oid) FROM pg_constraint
                                    WHERE connamespace='public'::regnamespace AND (conrelid::regclass::text IN ('sales_invoices','orders','stock_transfers') OR confrelid::regclass::text='sales_invoices')",
                ["triggers"] = @"SELECT pg_get_triggerdef(oid) FROM pg_trigger WHERE NOT tgisinternal AND tgrelid::regclass::text IN ('sales_invoices','orders','stock_transfers','order_items')",
                ["indexes"] = @"SELECT indexdef FROM pg_indexes WHERE schemaname='public' AND tablename IN ('sales_invoices','orders','stock_transfers')",
                ["grants"] = @"SELECT table_name||':'||privilege_type FROM information_schema.role_table_grants WHERE grantee='oisms_runtime' AND table_name IN ('sales_invoices','orders','stock_transfers')",
                ["functions"] = @"SELECT proname||':'||prosrc||':'||coalesce(array_to_string(proconfig,','),'') FROM pg_proc WHERE proname IN ('oisms_protect_order','oisms_protect_order_item','oisms_deny_mutation')",
            };

            foreach (var (name, sql) in snapshotQueries)
            {
                var upgradedRows = await ReadSortedAsync(upgraded, sql);
                var freshRows = await ReadSortedAsync(fresh, sql);
                upgradedRows.Should().NotBeEmpty(name);
                upgradedRows.Should().Equal(freshRows, $"phần '{name}' của database nâng cấp phải giống database tạo mới");
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var server = new NpgsqlConnection(PostgresTestEnvironment.AdminServerConnectionString);
            await server.OpenAsync();
            foreach (var db in new[] { freshDb, upgradedDb })
            {
                await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{db}\" WITH (FORCE);", server);
                await drop.ExecuteNonQueryAsync();
            }
        }
    }

    private static async Task<List<string>> ReadSortedAsync(NpgsqlConnection connection, string sql)
    {
        var rows = new List<string>();
        await using var cmd = new NpgsqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }
        rows.Sort(StringComparer.Ordinal);
        return rows;
    }

    // =========================================================================
    // AC-02: Đơn sàn sau Confirm và đơn đi qua /process đều có đúng 1 hóa đơn
    // =========================================================================
    [Fact]
    public async Task AC02_MarketplaceOrder_ConfirmAndProcess_EachHaveExactlyOneInvoice()
    {
        var infra = await SetupAsync();
        var rawSecret = $"shopee_secret_{Guid.NewGuid():N}";
        var shopId = $"SHOP_{Guid.NewGuid():N}"[..16];

        var (context, _) = CreateContext(infra.TenantId);
        await using (context)
        {
            context.ShopIntegrations.Add(new ShopIntegration
            {
                Id = Guid.NewGuid(),
                TenantId = infra.TenantId,
                Channel = "Shopee",
                ExternalShopId = shopId,
                BranchId = infra.BranchId,
                IsActive = true,
                SecretHash = WebhookSecretHasher.HashSecret(rawSecret),
                CreatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        async Task<Guid> CreateMarketplaceOrderAsync(string externalOrderId)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/simulator/shopee");
            req.Headers.Add("X-Webhook-Secret", rawSecret);
            req.Content = JsonContent.Create(new CanonicalWebhookPayloadDto
            {
                Channel = "Shopee",
                ExternalShopId = shopId,
                ExternalOrderId = externalOrderId,
                CustomerName = "Khách sàn U02",
                CustomerPhone = "0987654321",
                ShippingAddress = "123 Đường Test, Quận 1",
                OrderTimestamp = DateTimeOffset.UtcNow,
                TotalAmount = 150_000m,
                Items = new List<CanonicalWebhookItemDto> { new() { SkuCode = infra.SkuCode, Quantity = 2, UnitPrice = 75_000m } }
            });
            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());

            var (ctx, _) = CreateContext(infra.TenantId);
            await using (ctx)
            {
                return await ctx.Orders.Where(o => o.ExternalOrderId == externalOrderId).Select(o => o.Id).SingleAsync();
            }
        }

        var confirmOrderId = await CreateMarketplaceOrderAsync($"EXT-{Guid.NewGuid():N}"[..20]);
        var processOrderId = await CreateMarketplaceOrderAsync($"EXT-{Guid.NewGuid():N}"[..20]);

        await LoginAsync("staff@shopa.local");
        var confirm = await _client.PostAsJsonAsync($"/api/orders/{confirmOrderId}/confirm", new ConfirmOrderRequestDto());
        confirm.StatusCode.Should().Be(HttpStatusCode.OK, await confirm.Content.ReadAsStringAsync());
        var process = await _client.PostAsync($"/api/orders/{processOrderId}/process", null);
        process.StatusCode.Should().Be(HttpStatusCode.OK, await process.Content.ReadAsStringAsync());
        var processAgain = await _client.PostAsync($"/api/orders/{processOrderId}/process", null);
        processAgain.StatusCode.Should().Be(HttpStatusCode.OK, await processAgain.Content.ReadAsStringAsync());

        var (verify, _) = CreateContext(infra.TenantId);
        await using (verify)
        {
            var invoices = await verify.SalesInvoices.Where(s => s.OrderId == confirmOrderId || s.OrderId == processOrderId).ToListAsync();
            invoices.Should().HaveCount(2);
            invoices.Should().OnlyContain(s => s.Channel == "Shopee" && s.IssuedBy == infra.StaffId && s.PaymentMethodSnapshot == null && s.CustomerNameSnapshot == "Khách sàn U02");
            invoices.Select(s => s.OrderId).Should().BeEquivalentTo(new[] { confirmOrderId, processOrderId });
        }

        // Đơn sàn chưa thanh toán: không được hiện thành tiền mặt khi in.
        var invoiceId = (await GetTraceAsync($"/api/orders/{confirmOrderId}/trace")).SalesInvoice!.Id;
        var receipt = await _client.GetFromJsonAsync<SalesInvoiceReceiptDto>($"/api/sales-invoices/{invoiceId}/receipt", _jsonOptions);
        receipt!.PaymentMethod.Should().BeNull();
    }

    // =========================================================================
    // AC-05: Trigger chặn đúng lý do; in lại không đọc dữ liệu gốc đã đổi
    // =========================================================================
    [Fact]
    public async Task AC05_ReprintIgnoresLaterMasterDataChanges_AndGuardsRejectForTheRightReason()
    {
        var infra = await SetupAsync();
        await using var admin = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString);
        await admin.OpenAsync();

        // Chi nhánh chưa có địa chỉ lúc lập hóa đơn.
        await ExecuteAsync(admin, "UPDATE public.branches SET address = NULL WHERE id = @id", ("id", infra.BranchId));

        var pos = await CheckoutPosAsync(infra, 2m);
        Guid invoiceId;
        var (context, _) = CreateContext(infra.TenantId);
        await using (context)
        {
            invoiceId = await context.SalesInvoices.Where(s => s.OrderId == pos.OrderId).Select(s => s.Id).SingleAsync();
        }
        var before = await _client.GetFromJsonAsync<SalesInvoiceReceiptDto>($"/api/sales-invoices/receipt?number={pos.Receipt.ReceiptNumber}", _jsonOptions);
        before!.InvoiceId.Should().Be(invoiceId);
        before.BranchAddress.Should().BeNull();

        await ExecuteAsync(admin, @"
            UPDATE public.branches SET name = 'Tên mới U02', address = 'Địa chỉ mới U02' WHERE id = @id;
            UPDATE public.users SET display_name = 'Thu ngân đổi tên' WHERE id = @cashier;
            UPDATE public.skus SET code = 'SKU-MOI-U02' WHERE id = @sku;
            UPDATE public.products SET name = 'Tên hàng mới U02' WHERE id = (SELECT product_id FROM public.skus WHERE id = @sku);
            UPDATE public.sku_unit_prices SET cancelled_at = now(), cancelled_by = @cashier, cancel_reason = 'Đổi giá U02' WHERE sku_id = @sku;",
            ("id", infra.BranchId), ("cashier", infra.CashierId), ("sku", infra.SkuId));

        var after = await _client.GetFromJsonAsync<SalesInvoiceReceiptDto>($"/api/sales-invoices/{invoiceId}/receipt", _jsonOptions);
        JsonSerializer.Serialize(after).Should().Be(JsonSerializer.Serialize(before), "hóa đơn in lại phải giống hệt bản đầu dù dữ liệu gốc đã đổi");

        await using var runtime = new NpgsqlConnection(PostgresTestEnvironment.RuntimeConnectionString);
        await runtime.OpenAsync();

        (await SqlStateAsync(runtime, "UPDATE public.sales_invoices SET total_amount = 0 WHERE id = @id", ("id", invoiceId))).Should().Be("42501");
        (await SqlStateAsync(runtime, "DELETE FROM public.sales_invoices WHERE id = @id", ("id", invoiceId))).Should().Be("42501");
        (await SqlStateAsync(runtime, "UPDATE public.orders SET confirmed_by = @user WHERE id = @id", ("user", infra.OwnerId), ("id", pos.OrderId))).Should().Be("23514");
        (await SqlStateAsync(runtime, "UPDATE public.order_items SET sku_code_snapshot = 'X' WHERE order_id = @id", ("id", pos.OrderId))).Should().Be("23514");
        (await SqlStateAsync(runtime, "UPDATE public.order_items SET sku_name_snapshot = 'X' WHERE order_id = @id", ("id", pos.OrderId))).Should().Be("23514");

        // Trigger append-only vẫn chặn ngay cả tài khoản có đủ quyền trên bảng.
        (await SqlStateAsync(admin, "UPDATE public.sales_invoices SET total_amount = 0 WHERE id = @id", ("id", invoiceId))).Should().NotBeNull();
        (await SqlStateAsync(admin, "DELETE FROM public.sales_invoices WHERE id = @id", ("id", invoiceId))).Should().NotBeNull();
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<string?> SqlStateAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        try
        {
            await ExecuteAsync(connection, sql, parameters);
            return null;
        }
        catch (PostgresException ex)
        {
            return ex.SqlState;
        }
    }

    // =========================================================================
    // AC-07: Đủ loại nguồn truy vết; chuyển kho, kiểm kê không sinh hóa đơn
    // =========================================================================
    [Fact]
    public async Task AC07_TraceFromLedger_GoodsReceiptTransferStockCount_NoInvoiceAndCorrectActor()
    {
        var infra = await SetupAsync();
        await LoginAsync("staff@shopa.local");

        // Phiếu nhập
        Guid receiptLedgerId;
        var (ctx1, _) = CreateContext(infra.TenantId);
        await using (ctx1)
        {
            receiptLedgerId = await ctx1.InventoryTransactions.Where(t => t.ReceiptItemId != null).Select(t => t.Id).FirstAsync();
        }
        var receiptTrace = await GetTraceAsync($"/api/inventory/ledger/{receiptLedgerId}/trace");
        receiptTrace.Source.Type.Should().Be("GoodsReceipt");
        receiptTrace.Source.Number.Should().NotBeNullOrEmpty();
        receiptTrace.Source.ActorUserId.Should().Be(infra.StaffId);
        receiptTrace.SalesInvoice.Should().BeNull();
        receiptTrace.Lines.Should().Contain(l => l.LedgerId == receiptLedgerId);

        // Chuyển kho
        var toBranchId = Guid.NewGuid();
        var toWarehouseId = Guid.NewGuid();
        var (ctx2, _) = CreateContext(infra.TenantId);
        await using (ctx2)
        {
            ctx2.Branches.Add(new Branch { Id = toBranchId, TenantId = infra.TenantId, Name = "Chi nhánh đích U02", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
            ctx2.Warehouses.Add(new Warehouse { Id = toWarehouseId, TenantId = infra.TenantId, BranchId = toBranchId, Name = "Kho đích U02", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
            await ctx2.SaveChangesAsync();
        }

        var createTransfer = await _client.PostAsJsonAsync("/api/stock-transfers", new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchId,
            ToBranchId = toBranchId,
            FromWarehouseId = infra.WarehouseId,
            ToWarehouseId = toWarehouseId,
            Items = new List<StockTransferItemRequestDto> { new() { SkuId = infra.SkuId, Quantity = 5m } }
        });
        createTransfer.StatusCode.Should().Be(HttpStatusCode.Created, await createTransfer.Content.ReadAsStringAsync());
        var transfer = (await createTransfer.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions))!;

        var dispatch = await _client.PostAsJsonAsync($"/api/stock-transfers/{transfer.Id}/dispatch", new DispatchStockTransferRequestDto());
        dispatch.StatusCode.Should().Be(HttpStatusCode.OK, await dispatch.Content.ReadAsStringAsync());

        await LoginAsync("owner@shopa.local");
        var receive = await _client.PostAsJsonAsync($"/api/stock-transfers/{transfer.Id}/receive", new ReceiveStockTransferRequestDto());
        receive.StatusCode.Should().Be(HttpStatusCode.OK, await receive.Content.ReadAsStringAsync());

        Guid transferOutLedgerId;
        var (ctx3, _) = CreateContext(infra.TenantId);
        await using (ctx3)
        {
            transferOutLedgerId = await ctx3.InventoryTransactions.Where(t => t.TransferItemId != null && t.Type == "OUT").Select(t => t.Id).SingleAsync();
        }
        var transferTrace = await GetTraceAsync($"/api/inventory/ledger/{transferOutLedgerId}/trace");
        transferTrace.Source.Type.Should().Be("StockTransfer");
        transferTrace.Source.Id.Should().Be(transfer.Id);
        transferTrace.Source.Status.Should().Be("Received");
        transferTrace.Source.ActorUserId.Should().Be(infra.StaffId, "người chịu trách nhiệm dòng OUT là người xuất, không phải người nhận");
        transferTrace.Source.ActorName.Should().NotBeNullOrEmpty();
        transferTrace.Source.ActedAt.Should().NotBeNull();
        transferTrace.SalesInvoice.Should().BeNull();
        transferTrace.Lines.Select(l => l.Type).Should().BeEquivalentTo(new[] { "OUT", "IN" });

        // Kiểm kê thiếu
        var createCount = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = infra.BranchId,
            WarehouseId = infra.WarehouseId,
            SkuIds = new List<Guid> { infra.SkuId }
        });
        createCount.IsSuccessStatusCode.Should().BeTrue(await createCount.Content.ReadAsStringAsync());
        var count = (await createCount.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions))!;
        var record = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/record", new RecordStockCountBatchRequestDto
        {
            Items = new List<RecordStockCountItemRequestDto> { new() { SkuId = infra.SkuId, CountedQuantity = 90m } }
        });
        record.IsSuccessStatusCode.Should().BeTrue(await record.Content.ReadAsStringAsync());
        var confirmCount = await _client.PostAsJsonAsync($"/api/stock-counts/{count.Id}/confirm", new ConfirmStockCountRequestDto());
        confirmCount.IsSuccessStatusCode.Should().BeTrue(await confirmCount.Content.ReadAsStringAsync());

        Guid countLedgerId;
        var (ctx4, _) = CreateContext(infra.TenantId);
        await using (ctx4)
        {
            countLedgerId = await ctx4.InventoryTransactions.Where(t => t.AdjustmentItemId != null && t.Type == "OUT").Select(t => t.Id).SingleAsync();
        }
        var countTrace = await GetTraceAsync($"/api/inventory/ledger/{countLedgerId}/trace");
        countTrace.Source.Type.Should().Be("StockCount");
        countTrace.Source.Id.Should().Be(count.Id);
        countTrace.Source.Number.Should().Be(count.Number);
        countTrace.Source.ActorUserId.Should().Be(infra.OwnerId);
        countTrace.Source.ActorName.Should().NotBeNullOrEmpty();
        countTrace.Source.ActedAt.Should().NotBeNull();
        countTrace.SalesInvoice.Should().BeNull();
        countTrace.Lines.Should().ContainSingle(l => l.LedgerId == countLedgerId);

        (await CountInvoicesAsync(infra.TenantId)).Should().Be(0, "nhập kho, chuyển kho, kiểm kê không được sinh hóa đơn bán");
    }

    // =========================================================================
    // AC-07: Truy vết đơn, người thực hiện đơn cũ, dữ liệu kèm theo ở API đơn và sổ kho
    // =========================================================================
    [Fact]
    public async Task AC07_OrderTrace_LegacyActorFallback_AndInvoiceLinkOnOrderAndLedgerApis()
    {
        var infra = await SetupAsync();
        var pos = await CheckoutPosAsync(infra);

        await LoginAsync("staff@shopa.local");
        var trace = await GetTraceAsync($"/api/orders/{pos.OrderId}/trace");
        trace.SalesInvoice!.Number.Should().Be(pos.Receipt.ReceiptNumber);
        trace.Source.ActorUserId.Should().Be(infra.CashierId);
        trace.Lines.Should().NotBeEmpty();
        trace.Lines.Should().OnlyContain(l => l.Type == "OUT" && l.OrderItemId != null && l.UnitCost == 50_000m);

        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/orders/{pos.OrderId}");
        detail.GetProperty("salesInvoiceNumber").GetString().Should().Be(pos.Receipt.ReceiptNumber);
        detail.GetProperty("confirmedBy").GetGuid().Should().Be(infra.CashierId);

        var ledger = await _client.GetFromJsonAsync<JsonElement>($"/api/inventory/ledger?skuId={infra.SkuId}&pageSize=50");
        var ledgerItems = ledger.GetProperty("items").EnumerateArray().ToList();
        ledgerItems.Should().Contain(i => i.GetProperty("salesInvoiceNumber").GetString() == pos.Receipt.ReceiptNumber);
        ledgerItems.Where(i => i.GetProperty("documentType").GetString() != "Order")
            .Should().OnlyContain(i => i.GetProperty("salesInvoiceNumber").ValueKind == JsonValueKind.Null);

        // Đơn trước nâng cấp: POS lấy người tạo, online không có người thực hiện, không 500.
        var legacyPosId = Guid.NewGuid();
        var legacyOnlineId = Guid.NewGuid();
        await using (var admin = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await admin.OpenAsync();
            await using var tx = await admin.BeginTransactionAsync();
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO public.orders (id, tenant_id, number, branch_id, channel, status, total_amount, request_key, created_by, created_at, confirmed_at)
                VALUES (@pos, @t, 'POS-LEGACY-U02', @b, 'POS', 'Confirmed', 100000, gen_random_uuid(), @cashier, NOW(), NOW()),
                       (@online, @t, 'ORD-LEGACY-U02', @b, 'Admin', 'Confirmed', 100000, gen_random_uuid(), @staff, NOW(), NOW());
                INSERT INTO public.order_items (tenant_id, order_id, sku_id, sku_code_snapshot, sku_name_snapshot, quantity, unit_price, cost_price)
                VALUES (@t, @pos, @s, 'SKU-OLD', 'Hàng cũ', 1, 100000, 50000), (@t, @online, @s, 'SKU-OLD', 'Hàng cũ', 1, 100000, 50000);", admin, tx);
            cmd.Parameters.AddWithValue("pos", legacyPosId);
            cmd.Parameters.AddWithValue("online", legacyOnlineId);
            cmd.Parameters.AddWithValue("t", infra.TenantId);
            cmd.Parameters.AddWithValue("b", infra.BranchId);
            cmd.Parameters.AddWithValue("s", infra.SkuId);
            cmd.Parameters.AddWithValue("cashier", infra.CashierId);
            cmd.Parameters.AddWithValue("staff", infra.StaffId);
            await cmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }

        var legacyPos = await GetTraceAsync($"/api/orders/{legacyPosId}/trace");
        legacyPos.SalesInvoice.Should().BeNull();
        legacyPos.Source.ActorUserId.Should().Be(infra.CashierId);

        var legacyOnline = await GetTraceAsync($"/api/orders/{legacyOnlineId}/trace");
        legacyOnline.SalesInvoice.Should().BeNull();
        legacyOnline.Source.ActorUserId.Should().BeNull();
    }

    // =========================================================================
    // AC-07: Phân quyền, cô lập tenant, mã lỗi và bộ lọc thời gian trên mọi endpoint
    // =========================================================================
    [Fact]
    public async Task AC07_RbacTenantIsolationErrorCodesAndDateFilter_OnEveryEndpoint()
    {
        var infra = await SetupAsync();
        var pos = await CheckoutPosAsync(infra);
        var number = pos.Receipt.ReceiptNumber;

        Guid invoiceId, ledgerId;
        var (ctx, _) = CreateContext(infra.TenantId);
        await using (ctx)
        {
            invoiceId = await ctx.SalesInvoices.Where(s => s.OrderId == pos.OrderId).Select(s => s.Id).SingleAsync();
            ledgerId = await ctx.InventoryTransactions.Where(t => t.OrderItemId != null).Select(t => t.Id).SingleAsync();
        }

        var traceUrls = new[] { $"/api/sales-invoices/{invoiceId}/trace", $"/api/orders/{pos.OrderId}/trace", $"/api/inventory/ledger/{ledgerId}/trace" };

        // Thu ngân
        foreach (var url in traceUrls.Append("/api/sales-invoices").Append($"/api/sales-invoices/{invoiceId}"))
        {
            (await _client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden, url);
        }
        foreach (var url in new[] { $"/api/sales-invoices/{invoiceId}/receipt", $"/api/sales-invoices/receipt?number={number}" })
        {
            var res = await _client.GetAsync(url);
            res.StatusCode.Should().Be(HttpStatusCode.OK, url);
            var json = await res.Content.ReadAsStringAsync();
            json.Should().NotContainEquivalentOf("cost").And.NotContainEquivalentOf("cogs");
        }

        // Tenant khác
        await LoginAsync("owner@shopb.local");
        foreach (var url in traceUrls.Append($"/api/sales-invoices/{invoiceId}").Append($"/api/sales-invoices/{invoiceId}/receipt").Append($"/api/sales-invoices/receipt?number={number}"))
        {
            (await _client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound, url);
        }
        var crossList = await _client.GetFromJsonAsync<JsonElement>($"/api/sales-invoices?number={number}");
        crossList.GetProperty("totalCount").GetInt32().Should().Be(0);

        // Mã lỗi
        await LoginAsync("owner@shopa.local");
        var missingLedger = await _client.GetAsync($"/api/inventory/ledger/{Guid.NewGuid()}/trace");
        missingLedger.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ErrorCodeAsync(missingLedger)).Should().Be("LEDGER_ENTRY_NOT_FOUND");
        var missingInvoice = await _client.GetAsync($"/api/sales-invoices/{Guid.NewGuid()}/receipt");
        (await ErrorCodeAsync(missingInvoice)).Should().Be("SALES_INVOICE_NOT_FOUND");
        var missingOrder = await _client.GetAsync($"/api/orders/{Guid.NewGuid()}/trace");
        (await ErrorCodeAsync(missingOrder)).Should().Be("ORDER_NOT_FOUND");

        // Lọc theo số, đơn, chi nhánh và thời gian nửa mở, kể cả mốc có múi giờ +07:00.
        var issuedAt = pos.Receipt.CreatedAt;
        string Q(DateTimeOffset value) => Uri.EscapeDataString(value.ToString("O"));
        async Task<int> TotalAsync(string query) => (await _client.GetFromJsonAsync<JsonElement>($"/api/sales-invoices?{query}")).GetProperty("totalCount").GetInt32();

        (await TotalAsync($"number={number}")).Should().Be(1);
        (await TotalAsync($"orderId={pos.OrderId}")).Should().Be(1);
        (await TotalAsync($"branchId={infra.BranchId}")).Should().Be(1);
        (await TotalAsync($"orderId={Guid.NewGuid()}")).Should().Be(0);
        var saigon = TimeSpan.FromHours(7);
        (await TotalAsync($"fromDate={Q(issuedAt.ToOffset(saigon).AddMinutes(-1))}&toDate={Q(issuedAt.ToOffset(saigon).AddMinutes(1))}")).Should().Be(1);
        (await TotalAsync($"fromDate={Q(issuedAt.AddMinutes(-1))}&toDate={Q(issuedAt.AddMinutes(-1))}")).Should().Be(0);
    }
}
