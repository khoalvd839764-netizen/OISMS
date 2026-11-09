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
using OISM.Application.Features.PaymentAccounts.Dtos;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// CS-05 trên PostgreSQL thật: API tài khoản nhận tiền (phân quyền, cô lập tenant, đặt mặc định đồng thời),
/// ràng buộc DB (FK, CHECK, index mặc định) và checkout POS chuyển khoản QR (bản chụp người nhận, idempotency, tồn kho).
/// </summary>
public class PaymentAccountIntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public PaymentAccountIntegrationTests(WebApplicationFactoryWithPostgres factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
        var context = PostgresTestEnvironment.CreateRuntimeDbContext(new TestTenantService());
        await using (context)
        {
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class TestTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private async Task<string> LoginAsync(string email)
    {
        using var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = PostgresTestEnvironment.AppPassword });
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<AuthResponse>(_json))!.AccessToken;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        using var client = _factory.CreateClient();
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null) req.Content = JsonContent.Create(body);
        return await client.SendAsync(req);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage res)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }

    private static CreatePaymentAccountRequest Account(string bin, string number, bool isDefault = false) => new()
    {
        BankBin = bin,
        BankName = $"Ngân hàng {bin}",
        BankShortName = $"B{bin[^3..]}",
        AccountNumber = number,
        AccountName = "CUA HANG KIEM THU",
        IsDefault = isDefault
    };

    private async Task<PaymentAccountDto> CreateAsync(string ownerToken, string bin, string number, bool isDefault = false)
    {
        var res = await SendAsync(HttpMethod.Post, "/api/payment-accounts", ownerToken, Account(bin, number, isDefault));
        res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<PaymentAccountDto>(_json))!;
    }

    private static async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        var result = await cmd.ExecuteScalarAsync();
        return (T)Convert.ChangeType(result!, typeof(T));
    }

    [Fact]
    public async Task Rbac_OwnerManages_StaffCashierReadActiveOnly_OtherTenantGets404()
    {
        var ownerA = await LoginAsync("owner@shopa.local");
        var staffA = await LoginAsync("staff@shopa.local");
        var cashierA = await LoginAsync("cashier@shopa.local");
        var ownerB = await LoginAsync("owner@shopb.local");

        var created = await CreateAsync(ownerA, "970422", "0123456789");
        created.IsDefault.Should().BeTrue("tài khoản đầu tiên tự thành mặc định");

        (await SendAsync(HttpMethod.Get, "/api/payment-accounts", staffA)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, "/api/payment-accounts", cashierA, Account("970436", "9876543210"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, $"/api/payment-accounts/{created.Id}/set-default", staffA)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, $"/api/payment-accounts/{created.Id}/deactivate", cashierA)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        foreach (var token in new[] { staffA, cashierA })
        {
            var active = await SendAsync(HttpMethod.Get, "/api/payment-accounts/active", token);
            active.StatusCode.Should().Be(HttpStatusCode.OK);
            var list = await active.Content.ReadFromJsonAsync<List<ActivePaymentAccountDto>>(_json);
            list.Should().ContainSingle(a => a.Id == created.Id);
        }

        var listB = await SendAsync(HttpMethod.Get, "/api/payment-accounts/active", ownerB);
        (await listB.Content.ReadFromJsonAsync<List<ActivePaymentAccountDto>>(_json)).Should().BeEmpty();
        var hijack = await SendAsync(HttpMethod.Post, $"/api/payment-accounts/{created.Id}/deactivate", ownerB);
        hijack.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ErrorCodeAsync(hijack)).Should().Be("PAYMENT_ACCOUNT_NOT_FOUND");
        var rename = await SendAsync(HttpMethod.Put, $"/api/payment-accounts/{created.Id}", ownerB, new UpdatePaymentAccountRequest
        {
            BankBin = "970422", BankName = "X", BankShortName = "X", AccountNumber = "0123456789", AccountName = "X"
        });
        rename.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Validation_Duplicate409_ImmutableIdentity400_InvalidBin400()
    {
        var owner = await LoginAsync("owner@shopa.local");
        var created = await CreateAsync(owner, "970422", "0123456789");

        var dup = await SendAsync(HttpMethod.Post, "/api/payment-accounts", owner, Account("970422", "0123456789"));
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(dup)).Should().Be("PAYMENT_ACCOUNT_DUPLICATE");

        var badBin = await SendAsync(HttpMethod.Post, "/api/payment-accounts", owner, Account("12345", "0123456789"));
        badBin.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var changeNumber = await SendAsync(HttpMethod.Put, $"/api/payment-accounts/{created.Id}", owner, new UpdatePaymentAccountRequest
        {
            BankBin = "970422", BankName = "MB", BankShortName = "MB", AccountNumber = "1111111111", AccountName = "A"
        });
        changeNumber.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(changeNumber)).Should().Be("PAYMENT_ACCOUNT_IDENTITY_IMMUTABLE");

        var renamed = await SendAsync(HttpMethod.Put, $"/api/payment-accounts/{created.Id}", owner, new UpdatePaymentAccountRequest
        {
            BankBin = "970422", BankName = "Ngân hàng Quân Đội", BankShortName = "MBB", AccountNumber = "0123456789", AccountName = "Shop A Moi"
        });
        renamed.StatusCode.Should().Be(HttpStatusCode.OK, await renamed.Content.ReadAsStringAsync());
        (await renamed.Content.ReadFromJsonAsync<PaymentAccountDto>(_json))!.AccountName.Should().Be("SHOP A MOI");
    }

    [Fact]
    public async Task ConcurrentSetDefault_LeavesExactlyOneDefault_WithoutServerErrors()
    {
        var owner = await LoginAsync("owner@shopa.local");
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add((await CreateAsync(owner, "970422", $"55500000{i:D2}")).Id);
        }

        var tasks = Enumerable.Range(0, 20)
            .Select(i => SendAsync(HttpMethod.Post, $"/api/payment-accounts/{ids[i % ids.Count]}/set-default", owner))
            .ToList();
        var responses = await Task.WhenAll(tasks);

        responses.Select(r => (int)r.StatusCode).Should().OnlyContain(code => code == 200 || code == 409);
        responses.Should().Contain(r => r.StatusCode == HttpStatusCode.OK);

        var defaults = await ScalarAsync<long>(
            "SELECT count(*) FROM payment_accounts WHERE tenant_id = @t AND is_default AND is_active",
            ("t", OismsDatabaseSeeder.TenantAId));
        defaults.Should().Be(1);
    }

    [Fact]
    public async Task DatabaseRules_ForeignKeysAndChecks_MatchSchema()
    {
        (await ScalarAsync<string>(
            "SELECT confdeltype::text FROM pg_constraint WHERE conname = 'fk_payments_payment_account_id'")).Should().Be("r");
        (await ScalarAsync<string>(
            "SELECT confdeltype::text FROM pg_constraint WHERE conname = 'fk_payment_accounts_updated_by'")).Should().Be("n");
        (await ScalarAsync<string>(
            @"SELECT string_agg(a.attname, ',') FROM pg_constraint c
              JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = ANY (c.confdelsetcols)
              WHERE c.conname = 'fk_payment_accounts_updated_by'")).Should().Be("updated_by_user_id",
            "chỉ set NULL updated_by_user_id, không đụng tenant_id");
        (await ScalarAsync<string>(
            "SELECT confdeltype::text FROM pg_constraint WHERE conname = 'fk_payment_accounts_tenant_id'")).Should().Be("r");

        var owner = await LoginAsync("owner@shopa.local");
        await CreateAsync(owner, "970422", "0123456789");

        // Không thể có hai tài khoản mặc định đang dùng (ghi thẳng DB, bỏ qua service)
        var insertSecondDefault = async () =>
        {
            await using var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                @"INSERT INTO payment_accounts (tenant_id, bank_bin, bank_name, bank_short_name, account_number, account_name, is_default, is_active)
                  VALUES (@t, '970436', 'VCB', 'VCB', '9876543210', 'X', true, true)", conn);
            cmd.Parameters.AddWithValue("t", OismsDatabaseSeeder.TenantAId);
            await cmd.ExecuteNonQueryAsync();
        };
        (await insertSecondDefault.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    private async Task<(Guid BranchId, Guid SkuId, string SkuCode, decimal Price)> SeedStockAsync(decimal quantity)
    {
        var tenantService = new TestTenantService { TenantId = OismsDatabaseSeeder.TenantAId };
        var context = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService);
        await using (context)
        {
            await PriceSeed.SyncFromLegacyColumnsAsync(context);
            var branch = await context.Branches.FirstAsync(b => b.IsActive);
            var wh = await context.Warehouses.FirstAsync(w => w.BranchId == branch.Id && w.IsActive);
            var sku = await context.ProductVariants.FirstAsync(v => v.IsActive);
            var supplier = await context.Suppliers.FirstAsync(s => s.IsActive);
            var owner = await context.Users.FirstAsync(u => u.Email == "owner@shopa.local");
            await PurchaseOrderSeed.ReceiveStockAsync(context, tenantService, branch.Id, wh.Id, supplier.Id, owner.Id, (sku.Id, quantity, 50_000m));
            return (branch.Id, sku.Id, sku.SKU, sku.RetailPrice);
        }
    }

    private static PosCheckoutRequestDto QrCheckout(Guid branchId, Guid skuId, string skuCode, decimal price, Guid requestKey, Guid? accountId, string? reference) => new()
    {
        BranchId = branchId,
        Channel = "POS",
        PaymentMethod = "BANK_TRANSFER_QR",
        AmountGiven = price,
        PaymentAccountId = accountId,
        TransferReference = reference,
        RequestKey = requestKey,
        Items = [new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1, UnitPrice = price }]
    };

    private static async Task<decimal> OnHandAsync(Guid branchId, Guid skuId) =>
        await ScalarAsync<decimal>("SELECT on_hand FROM inventory_balances WHERE tenant_id = @t AND branch_id = @b AND sku_id = @s",
            ("t", OismsDatabaseSeeder.TenantAId), ("b", branchId), ("s", skuId));

    private static async Task<long> OrdersWithKeyAsync(Guid requestKey) =>
        await ScalarAsync<long>("SELECT count(*) FROM orders WHERE tenant_id = @t AND request_key = @k",
            ("t", OismsDatabaseSeeder.TenantAId), ("k", requestKey));

    [Fact]
    public async Task QrCheckout_SnapshotsReceiver_ReplayIsIdempotent_InactiveOrForeignAccountIsRejectedWithoutStockChange()
    {
        var (branchId, skuId, skuCode, price) = await SeedStockAsync(10m);
        var owner = await LoginAsync("owner@shopa.local");
        var ownerB = await LoginAsync("owner@shopb.local");
        var cashier = await LoginAsync("cashier@shopa.local");
        var accountA = await CreateAsync(owner, "970422", "0123456789");
        var accountA2 = await CreateAsync(owner, "970436", "9876543210");
        var foreign = await CreateAsync(ownerB, "970415", "111222333444");

        // 1. Checkout QR với tài khoản đã chọn: lưu bản chụp người nhận và nội dung CK
        var key = Guid.NewGuid();
        var reference = $"OISMS POS {key.ToString("N")[..8].ToUpperInvariant()}";
        var first = await SendAsync(HttpMethod.Post, "/api/pos/checkout", cashier, QrCheckout(branchId, skuId, skuCode, price, key, accountA.Id, reference));
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var firstBody = (await first.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_json))!;
        firstBody.Receipt!.ReceiverAccountNumber.Should().Be("0123456789");
        firstBody.Receipt.ReceiverBankBin.Should().Be("970422");
        firstBody.Receipt.TransferReference.Should().Be(reference);
        (await ScalarAsync<string>("SELECT receiver_account_number FROM payments p JOIN orders o ON o.id = p.order_id WHERE o.request_key = @k", ("k", key)))
            .Should().Be("0123456789");
        var onHandAfterSale = await OnHandAsync(branchId, skuId);
        onHandAfterSale.Should().Be(9m);

        // 2. Owner ngừng dùng tài khoản; replay cùng key vẫn trả kết quả cũ, không trừ kho thêm
        (await SendAsync(HttpMethod.Post, $"/api/payment-accounts/{accountA.Id}/deactivate", owner)).StatusCode.Should().Be(HttpStatusCode.OK);
        var replay = await SendAsync(HttpMethod.Post, "/api/pos/checkout", cashier, QrCheckout(branchId, skuId, skuCode, price, key, accountA.Id, reference));
        replay.StatusCode.Should().Be(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        var replayBody = (await replay.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_json))!;
        replayBody.OrderNumber.Should().Be(firstBody.OrderNumber);
        replayBody.Receipt!.ReceiverBankName.Should().NotBeNullOrWhiteSpace();
        (await OnHandAsync(branchId, skuId)).Should().Be(onHandAfterSale);

        // 3. Cùng key nhưng khác tài khoản hoặc khác nội dung CK: 409
        var otherAccount = await SendAsync(HttpMethod.Post, "/api/pos/checkout", cashier, QrCheckout(branchId, skuId, skuCode, price, key, accountA2.Id, reference));
        otherAccount.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var otherReference = await SendAsync(HttpMethod.Post, "/api/pos/checkout", cashier, QrCheckout(branchId, skuId, skuCode, price, key, accountA.Id, "OISMS POS FFFFFFFF"));
        otherReference.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 4. Key mới với tài khoản đã ngừng: 409 PAYMENT_ACCOUNT_INACTIVE, không tạo đơn, không trừ kho
        var inactiveKey = Guid.NewGuid();
        var inactive = await SendAsync(HttpMethod.Post, "/api/pos/checkout", cashier, QrCheckout(branchId, skuId, skuCode, price, inactiveKey, accountA.Id, null));
        inactive.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(inactive)).Should().Be("PAYMENT_ACCOUNT_INACTIVE");
        (await OrdersWithKeyAsync(inactiveKey)).Should().Be(0);
        (await OnHandAsync(branchId, skuId)).Should().Be(onHandAfterSale);

        // 5. Tài khoản của tenant khác: 404, không tạo đơn, không trừ kho
        var foreignKey = Guid.NewGuid();
        var foreignRes = await SendAsync(HttpMethod.Post, "/api/pos/checkout", cashier, QrCheckout(branchId, skuId, skuCode, price, foreignKey, foreign.Id, null));
        foreignRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ErrorCodeAsync(foreignRes)).Should().Be("PAYMENT_ACCOUNT_NOT_FOUND");
        (await OrdersWithKeyAsync(foreignKey)).Should().Be(0);
        (await OnHandAsync(branchId, skuId)).Should().Be(onHandAfterSale);

        // 6. Client cũ không gửi PaymentAccountId: server không tự gán tài khoản nào
        var legacyKey = Guid.NewGuid();
        var legacy = await SendAsync(HttpMethod.Post, "/api/pos/checkout", cashier, QrCheckout(branchId, skuId, skuCode, price, legacyKey, null, null));
        legacy.StatusCode.Should().Be(HttpStatusCode.OK, await legacy.Content.ReadAsStringAsync());
        (await ScalarAsync<long>(
            "SELECT count(*) FROM payments p JOIN orders o ON o.id = p.order_id WHERE o.request_key = @k AND p.payment_account_id IS NULL AND p.receiver_account_number IS NULL",
            ("k", legacyKey))).Should().Be(1);
        (await OnHandAsync(branchId, skuId)).Should().Be(onHandAfterSale - 1m);

        // 7. Chứng từ tiền mặt không được có bản chụp chuyển khoản (CHECK ở DB)
        var cashWithSnapshot = async () =>
        {
            await using var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE payments SET method = 'Cash', cash_received = amount WHERE order_id = (SELECT id FROM orders WHERE request_key = @k)", conn);
            cmd.Parameters.AddWithValue("k", key);
            await cmd.ExecuteNonQueryAsync();
        };
        (await cashWithSnapshot.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);

        // 8. Tài khoản đã có chứng từ không xóa cứng được (FK RESTRICT)
        var hardDelete = async () =>
        {
            await using var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("DELETE FROM payment_accounts WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", accountA.Id);
            await cmd.ExecuteNonQueryAsync();
        };
        (await hardDelete.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }
}
