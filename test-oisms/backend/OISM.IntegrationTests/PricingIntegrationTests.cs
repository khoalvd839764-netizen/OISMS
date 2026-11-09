using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.RegisterTenant;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Inventory.Services;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Pricing.Dtos;
using OISM.Application.Features.Webhooks.Dtos;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

[Collection("PostgresIntegrationTests")]
public class PricingIntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private class TestTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    public PricingIntegrationTests(WebApplicationFactoryWithPostgres factory)
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

    private async Task<string> CreateAndLoginUserAsync(Guid tenantId, string email, string role)
    {
        await using (var adminConn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await adminConn.OpenAsync();
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO public.users (id, tenant_id, display_name, email, password_hash, role, is_active, created_at)
                VALUES (@id, @tenantId, @name, @email, @hash, @role, true, now());
            ", adminConn);
            cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("name", $"Test {role}");
            cmd.Parameters.AddWithValue("email", email);
            cmd.Parameters.AddWithValue("hash", BCrypt.Net.BCrypt.HashPassword("User@123"));
            cmd.Parameters.AddWithValue("role", role);
            await cmd.ExecuteNonQueryAsync();
        }

        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = "User@123"
        });
        loginRes.EnsureSuccessStatusCode();

        var authData = await loginRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        return authData!.AccessToken;
    }

    private async Task<(Guid SkuId, Guid UnitId)> CreateSampleSkuAndUnitAsync(Guid tenantId, string skuCode)
    {
        var skuId = Guid.NewGuid();
        var prodId = Guid.NewGuid();
        var unitId = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO public.units (id, tenant_id, code, name, symbol, measure_group, decimal_places, is_active)
            VALUES (@uId, @tId, 'cai', 'Cái', 'cái', 'Count', 0, true)
            ON CONFLICT (tenant_id, code) DO UPDATE SET is_active = true RETURNING id;
        ", conn);
        cmd.Parameters.AddWithValue("uId", unitId);
        cmd.Parameters.AddWithValue("tId", tenantId);
        var actualUnitId = (Guid)(await cmd.ExecuteScalarAsync())!;

        await using var pCmd = new NpgsqlCommand(@"
            INSERT INTO public.products (id, tenant_id, name, is_active)
            VALUES (@pId, @tId, 'Sample Product', true);

            INSERT INTO public.skus (id, tenant_id, product_id, code, retail_price, wholesale_price, base_unit_id, is_active)
            VALUES (@sId, @tId, @pId, @code, 100000, 80000, @actualUId, true);

            INSERT INTO public.sku_units (id, tenant_id, sku_id, unit_id, factor_to_base, can_sell, is_active)
            VALUES (gen_random_uuid(), @tId, @sId, @actualUId, 1, true, true);
        ", conn);
        pCmd.Parameters.AddWithValue("pId", prodId);
        pCmd.Parameters.AddWithValue("tId", tenantId);
        pCmd.Parameters.AddWithValue("sId", skuId);
        pCmd.Parameters.AddWithValue("code", skuCode.Trim().ToUpperInvariant());
        pCmd.Parameters.AddWithValue("actualUId", actualUnitId);
        await pCmd.ExecuteNonQueryAsync();

        // Đảm bảo 2 bảng giá mặc định tồn tại
        await using var plCmd = new NpgsqlCommand(@"
            INSERT INTO public.price_lists (id, tenant_id, code, name, is_active)
            VALUES
                (gen_random_uuid(), @tId, 'retail', 'Giá lẻ', true),
                (gen_random_uuid(), @tId, 'wholesale', 'Giá sỉ', true)
            ON CONFLICT (tenant_id, code) DO NOTHING;
        ", conn);
        plCmd.Parameters.AddWithValue("tId", tenantId);
        await plCmd.ExecuteNonQueryAsync();

        return (skuId, actualUnitId);
    }

    private (AppDbContext Context, TestTenantService TenantService) CreatePostgresDbContext(Guid? tenantId = null, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        var tenantService = new TestTenantService();
        if (tenantId.HasValue)
        {
            tenantService.SetTenant(tenantId.Value);
        }
        var context = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService, interceptors);
        return (context, tenantService);
    }

    private async Task SeedDatabaseAsync()
    {
        var (context, _) = CreatePostgresDbContext(null);
        await using (context)
        {
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);
        }
    }

    private async Task<string> LoginAsAsync(string email, string password)
    {
        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = password
        });
        loginRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var authData = await loginRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        return authData!.AccessToken;
    }

    private async Task SeedStockAsync(Guid tenantId, Guid branchId, Guid warehouseId, Guid supplierId, Guid userId, Guid skuId, decimal quantity, decimal unitPrice)
    {
        var (context, tenantService) = CreatePostgresDbContext(tenantId);
        await using (context)
        {
            await PurchaseOrderSeed.ReceiveStockAsync(context, tenantService, branchId, warehouseId, supplierId, userId, (skuId, quantity, unitPrice));
        }
    }

    // =========================================================================
    // AC-03: Security, Guard Triggers & State Transitions
    // =========================================================================

    [Fact]
    public async Task AC03_RuntimeRole_CannotMutateOrDeletePrices_AndGuardTriggersEnforced()
    {
        var (token, tenantId) = await RegisterAndLoginOwnerAsync("TENANT-AC03-1", "owner_ac03_1@test.com");
        var (skuId, unitId) = await CreateSampleSkuAndUnitAsync(tenantId, "SKU-AC03-1");

        // 1. Tạo 1 giá qua Owner
        var reqKey = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("Idempotency-Key", reqKey.ToString());

        var createRes = await client.PostAsJsonAsync("/api/price-change-requests", new CreatePriceChangeRequest
        {
            Reason = "Tạo giá kiểm tra guard",
            Items = new List<CreatePriceChangeRequestItemDto>
            {
                new() { PriceListCode = "retail", SkuId = skuId, UnitId = unitId, NewPrice = 150000m }
            }
        });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createRes.Content.ReadFromJsonAsync<PriceChangeRequestResponse>(_jsonOptions);

        // 2. Kết nối bằng runtime role (oisms_runtime qua RuntimeConnectionString)
        var runtimeConnStr = PostgresTestEnvironment.RuntimeConnectionString;
        await using var conn = new NpgsqlConnection(runtimeConnStr);
        await conn.OpenAsync();

        // a. Runtime role cố tình UPDATE giá trực tiếp trong sku_unit_prices -> Phải bị trigger hoặc quyền từ chối
        var updateAction = async () =>
        {
            await using var cmd = new NpgsqlCommand("UPDATE public.sku_unit_prices SET price = 999999 WHERE sku_id = @sId;", conn);
            cmd.Parameters.AddWithValue("sId", skuId);
            await cmd.ExecuteNonQueryAsync();
        };
        await updateAction.Should().ThrowAsync<PostgresException>("Runtime role không được phép sửa giá trực tiếp");

        // b. Runtime role cố tình DELETE dòng giá -> Bị trigger oisms_deny_mutation từ chối
        var deleteAction = async () =>
        {
            await using var cmd = new NpgsqlCommand("DELETE FROM public.sku_unit_prices WHERE sku_id = @sId;", conn);
            cmd.Parameters.AddWithValue("sId", skuId);
            await cmd.ExecuteNonQueryAsync();
        };
        await deleteAction.Should().ThrowAsync<PostgresException>("Cấm xóa dòng giá");

        // c. Runtime role cố tình DELETE phiếu đổi giá -> Bị oisms_deny_mutation từ chối
        var deleteReqAction = async () =>
        {
            await using var cmd = new NpgsqlCommand("DELETE FROM public.price_change_requests WHERE id = @id;", conn);
            cmd.Parameters.AddWithValue("id", created!.Id);
            await cmd.ExecuteNonQueryAsync();
        };
        await deleteReqAction.Should().ThrowAsync<PostgresException>("Cấm xóa phiếu đổi giá");

        // d. Runtime role cố tình UPDATE dòng phiếu đổi giá -> Bị price_change_item_append_only từ chối
        var updateItemAction = async () =>
        {
            await using var cmd = new NpgsqlCommand("UPDATE public.price_change_request_items SET new_price = 888888 WHERE request_id = @id;", conn);
            cmd.Parameters.AddWithValue("id", created!.Id);
            await cmd.ExecuteNonQueryAsync();
        };
        await updateItemAction.Should().ThrowAsync<PostgresException>("Dòng phiếu đổi giá là Append-Only");
    }

    [Fact]
    public async Task AC03_Cancellation_CanOnlyCancelOnce_EffectivePriceThrows409_CanReScheduleCancelledSlot()
    {
        var (token, tenantId) = await RegisterAndLoginOwnerAsync("TENANT-AC03-2", "owner_ac03_2@test.com");
        var (skuId, unitId) = await CreateSampleSkuAndUnitAsync(tenantId, "SKU-AC03-2");

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 1. Tạo giá có hiệu lực ngay (valid_from = now)
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resEffective = await client.PostAsJsonAsync("/api/price-change-requests", new CreatePriceChangeRequest
        {
            Reason = "Giá hiệu lực ngay",
            RequestedValidFrom = null,
            Items = new List<CreatePriceChangeRequestItemDto>
            {
                new() { PriceListCode = "retail", SkuId = skuId, UnitId = unitId, NewPrice = 110000m }
            }
        });
        resEffective.StatusCode.Should().Be(HttpStatusCode.Created);

        // Lấy ID dòng giá vừa tạo
        Guid effectivePriceId;
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT id FROM public.sku_unit_prices WHERE sku_id = @sId ORDER BY created_at DESC LIMIT 1;", conn);
            cmd.Parameters.AddWithValue("sId", skuId);
            effectivePriceId = (Guid)(await cmd.ExecuteScalarAsync())!;
        }

        // Hủy giá đã có hiệu lực -> Báo PRICE_ALREADY_EFFECTIVE (409)
        var cancelEffectiveRes = await client.PostAsJsonAsync($"/api/sku-unit-prices/{effectivePriceId}/cancel", new CancelSkuUnitPriceRequestDto
        {
            Reason = "Thử hủy giá đang hiệu lực"
        });
        cancelEffectiveRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var errJson1 = await cancelEffectiveRes.Content.ReadAsStringAsync();
        errJson1.Should().Contain("PRICE_ALREADY_EFFECTIVE");

        // 2. Tạo giá lên lịch tương lai (+2 giờ)
        var scheduledValidFrom = DateTimeOffset.UtcNow.AddHours(2);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resFuture = await client.PostAsJsonAsync("/api/price-change-requests", new CreatePriceChangeRequest
        {
            Reason = "Giá tương lai",
            RequestedValidFrom = scheduledValidFrom,
            Items = new List<CreatePriceChangeRequestItemDto>
            {
                new() { PriceListCode = "retail", SkuId = skuId, UnitId = unitId, NewPrice = 130000m }
            }
        });
        resFuture.StatusCode.Should().Be(HttpStatusCode.Created);

        Guid futurePriceId;
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT id FROM public.sku_unit_prices WHERE sku_id = @sId AND valid_from = @vf;", conn);
            cmd.Parameters.AddWithValue("sId", skuId);
            cmd.Parameters.AddWithValue("vf", scheduledValidFrom);
            futurePriceId = (Guid)(await cmd.ExecuteScalarAsync())!;
        }

        // Hủy lần 1 -> Thành công (200 OK)
        var cancel1Res = await client.PostAsJsonAsync($"/api/sku-unit-prices/{futurePriceId}/cancel", new CancelSkuUnitPriceRequestDto
        {
            Reason = "Hủy lịch đổi giá đợt 1"
        });
        cancel1Res.StatusCode.Should().Be(HttpStatusCode.OK);

        // Hủy lần 2 cùng dòng giá -> Báo INVALID_STATE_TRANSITION (409)
        var cancel2Res = await client.PostAsJsonAsync($"/api/sku-unit-prices/{futurePriceId}/cancel", new CancelSkuUnitPriceRequestDto
        {
            Reason = "Hủy lần 2"
        });
        cancel2Res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var errJson2 = await cancel2Res.Content.ReadAsStringAsync();
        errJson2.Should().Contain("INVALID_STATE_TRANSITION");

        // 3. Đặt lại được mốc đã hủy: Tạo phiếu mới với cùng scheduledValidFrom -> Thành công!
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resReschedule = await client.PostAsJsonAsync("/api/price-change-requests", new CreatePriceChangeRequest
        {
            Reason = "Đặt lại mốc đã hủy",
            RequestedValidFrom = scheduledValidFrom,
            Items = new List<CreatePriceChangeRequestItemDto>
            {
                new() { PriceListCode = "retail", SkuId = skuId, UnitId = unitId, NewPrice = 135000m }
            }
        });
        resReschedule.StatusCode.Should().Be(HttpStatusCode.Created, "Mốc thời gian đã hủy phải cho phép lên lịch lại thành công");

        // 4. Trùng mốc báo PRICE_VALID_FROM_CONFLICT (409)
        // Tạo thêm 1 giá trùng mốc scheduledValidFrom khi dòng giá trước đó đang active
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var resConflict = await client.PostAsJsonAsync("/api/price-change-requests", new CreatePriceChangeRequest
        {
            Reason = "Tạo trùng mốc",
            RequestedValidFrom = scheduledValidFrom,
            Items = new List<CreatePriceChangeRequestItemDto>
            {
                new() { PriceListCode = "retail", SkuId = skuId, UnitId = unitId, NewPrice = 140000m }
            }
        });
        resConflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var conflictJson = await resConflict.Content.ReadAsStringAsync();
        conflictJson.Should().Contain("PRICE_VALID_FROM_CONFLICT");
    }

    // =========================================================================
    // AC-04: Staff vs Owner Flow, RBAC, Concurrency & Idempotency
    // =========================================================================

    [Fact]
    public async Task AC04_StaffPendingFlow_OwnerApproves_Cashier403_ConcurrentApprove()
    {
        var (ownerToken, tenantId) = await RegisterAndLoginOwnerAsync("TENANT-AC04-1", "owner_ac04_1@test.com");
        var staffToken = await CreateAndLoginUserAsync(tenantId, "staff_ac04_1@test.com", "Staff");
        var cashierToken = await CreateAndLoginUserAsync(tenantId, "cashier_ac04_1@test.com", "Cashier");
        var (skuId, unitId) = await CreateSampleSkuAndUnitAsync(tenantId, "SKU-AC04-1");

        // 1. Cashier bị 403 khi tạo phiếu
        var cashierClient = _factory.CreateClient();
        cashierClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        cashierClient.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var cashierCreateRes = await cashierClient.PostAsJsonAsync("/api/price-change-requests", new CreatePriceChangeRequest
        {
            Reason = "Thu ngân thử tạo phiếu",
            Items = new List<CreatePriceChangeRequestItemDto>
            {
                new() { PriceListCode = "retail", SkuId = skuId, UnitId = unitId, NewPrice = 120000m }
            }
        });
        cashierCreateRes.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Thu ngân không có quyền tạo phiếu đổi giá");

        // 2. Staff tạo phiếu -> Status = Pending, CHƯA đổi giá trong sku_unit_prices
        var staffClient = _factory.CreateClient();
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var staffReqKey = Guid.NewGuid();
        staffClient.DefaultRequestHeaders.Add("Idempotency-Key", staffReqKey.ToString());

        var staffCreateRes = await staffClient.PostAsJsonAsync("/api/price-change-requests", new CreatePriceChangeRequest
        {
            Reason = "Nhân viên đề xuất đổi giá",
            Items = new List<CreatePriceChangeRequestItemDto>
            {
                new() { PriceListCode = "retail", SkuId = skuId, UnitId = unitId, NewPrice = 125000m }
            }
        });
        staffCreateRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var pendingReq = await staffCreateRes.Content.ReadFromJsonAsync<PriceChangeRequestResponse>(_jsonOptions);
        pendingReq!.Status.Should().Be("Pending");

        // Xác minh trong DB: chưa có dòng giá nào cho mức 125000
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT count(*) FROM public.sku_unit_prices WHERE sku_id = @sId AND price = 125000;", conn);
            cmd.Parameters.AddWithValue("sId", skuId);
            var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            count.Should().Be(0, "Phiếu Pending tuyệt đối chưa sinh dòng giá");
        }

        // 3. Hai request duyệt đồng thời -> Chỉ một thành công, không tạo trùng dòng
        var ownerClient1 = _factory.CreateClient();
        ownerClient1.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var ownerClient2 = _factory.CreateClient();
        ownerClient2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var task1 = ownerClient1.PostAsync($"/api/price-change-requests/{pendingReq.Id}/approve", null);
        var task2 = ownerClient2.PostAsync($"/api/price-change-requests/{pendingReq.Id}/approve", null);
        var responses = await Task.WhenAll(task1, task2);

        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        successCount.Should().Be(1, "Chỉ 1 request duyệt được thành công");
        conflictCount.Should().Be(1, "Request duyệt thứ hai phải nhận 409 Conflict");

        // Xác minh chỉ có đúng 1 dòng giá được sinh ra
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT count(*) FROM public.sku_unit_prices WHERE sku_id = @sId AND price = 125000;", conn);
            cmd.Parameters.AddWithValue("sId", skuId);
            var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            count.Should().Be(1, "Duyệt đồng thời không được sinh trùng dòng giá");
        }
    }

    [Fact]
    public async Task AC04_TenantIsolation_And_IdempotencyReplayAndMismatch()
    {
        var (tokenA, tenantA) = await RegisterAndLoginOwnerAsync("TENANT-AC04-A", "owner_a@test.com");
        var (tokenB, tenantB) = await RegisterAndLoginOwnerAsync("TENANT-AC04-B", "owner_b@test.com");

        var (skuA, unitA) = await CreateSampleSkuAndUnitAsync(tenantA, "SKU-A");
        var (skuB, unitB) = await CreateSampleSkuAndUnitAsync(tenantB, "SKU-B");

        var clientA = _factory.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);

        var clientB = _factory.CreateClient();
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);

        // 1. Tenant A tạo phiếu
        var keyA = Guid.NewGuid();
        clientA.DefaultRequestHeaders.Add("Idempotency-Key", keyA.ToString());

        var createResA = await clientA.PostAsJsonAsync("/api/price-change-requests", new CreatePriceChangeRequest
        {
            Reason = "Giá Tenant A",
            Items = new List<CreatePriceChangeRequestItemDto>
            {
                new() { PriceListCode = "retail", SkuId = skuA, UnitId = unitA, NewPrice = 200000m }
            }
        });
        createResA.StatusCode.Should().Be(HttpStatusCode.Created);
        var reqA = await createResA.Content.ReadFromJsonAsync<PriceChangeRequestResponse>(_jsonOptions);

        // 2. Tenant B không xem được phiếu của Tenant A (404)
        var getByBRes = await clientB.GetAsync($"/api/price-change-requests/{reqA!.Id}");
        getByBRes.StatusCode.Should().Be(HttpStatusCode.NotFound, "Tenant khác không được xem phiếu đổi giá");

        // Tenant B không tra cứu được giá của SKU Tenant A
        var getEffectiveByBRes = await clientB.GetAsync($"/api/sku-unit-prices/effective?skuId={skuA}&priceListCode=retail");
        getEffectiveByBRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var effPricesB = await getEffectiveByBRes.Content.ReadFromJsonAsync<List<EffectivePriceDto>>(_jsonOptions);
        effPricesB.Should().BeEmpty("Tenant khác không tra cứu được giá của tenant khác");

        // 3. Retry cùng Idempotency-Key trả phiếu cũ (Replay)
        var replayRes = await clientA.PostAsJsonAsync("/api/price-change-requests", new CreatePriceChangeRequest
        {
            Reason = "Giá Tenant A",
            Items = new List<CreatePriceChangeRequestItemDto>
            {
                new() { PriceListCode = "retail", SkuId = skuA, UnitId = unitA, NewPrice = 200000m }
            }
        });
        replayRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayObj = await replayRes.Content.ReadFromJsonAsync<PriceChangeRequestResponse>(_jsonOptions);
        replayObj!.Id.Should().Be(reqA.Id);
        replayObj.IsReplay.Should().BeTrue();

        // 4. Khác payload với cùng Idempotency-Key báo IDEMPOTENCY_PAYLOAD_MISMATCH (409)
        var mismatchRes = await clientA.PostAsJsonAsync("/api/price-change-requests", new CreatePriceChangeRequest
        {
            Reason = "Giá Tenant A đã bị sửa payload",
            Items = new List<CreatePriceChangeRequestItemDto>
            {
                new() { PriceListCode = "retail", SkuId = skuA, UnitId = unitA, NewPrice = 999999m }
            }
        });
        mismatchRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var mismatchJson = await mismatchRes.Content.ReadAsStringAsync();
        mismatchJson.Should().Contain("IDEMPOTENCY_PAYLOAD_MISMATCH");
    }

    [Fact]
    public async Task AC05_InitialPrices_OwnerApproved_StaffPending_Traceable()
    {
        var (ownerToken, tenantId) = await RegisterAndLoginOwnerAsync("TENANT-AC05", "owner_ac05@test.com");
        var staffToken = await CreateAndLoginUserAsync(tenantId, "staff_ac05@test.com", "Staff");

        var ownerClient = _factory.CreateClient();
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var staffClient = _factory.CreateClient();
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);

        // 1. Owner tạo Product + Variant với giá lẻ và giá sỉ
        var ownerCreateProductRes = await ownerClient.PostAsJsonAsync("/api/products", new OISM.Application.Features.Catalog.Products.CreateProductRequest
        {
            Name = "Sản phẩm của Owner",
            Variants = new List<OISM.Application.Features.Catalog.Products.CreateVariantRequest>
            {
                new()
                {
                    SKU = "OWNER-SKU-01",
                    VariantName = "Biến thể Owner",
                    RetailPrice = 150000m,
                    WholesalePrice = 120000m,
                    Unit = "cai"
                }
            }
        });
        ownerCreateProductRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // Lấy SkuId của Owner
        Guid ownerSkuId;
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT id FROM public.skus WHERE tenant_id = @tId AND code = 'OWNER-SKU-01'", conn);
            cmd.Parameters.AddWithValue("tId", tenantId);
            ownerSkuId = (Guid)(await cmd.ExecuteScalarAsync())!;
        }

        // Owner tạo -> có giá ngay trong sku_unit_prices và phiếu Approved
        var ownerEffRes = await ownerClient.GetAsync($"/api/sku-unit-prices/effective?skuId={ownerSkuId}&priceListCode=retail");
        ownerEffRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var ownerEffList = await ownerEffRes.Content.ReadFromJsonAsync<List<EffectivePriceDto>>(_jsonOptions);
        ownerEffList.Should().NotBeEmpty("Owner tạo SKU có giá ngay");
        ownerEffList![0].Price.Should().Be(150000m);

        // 2. Staff tạo Product + Variant với giá lẻ và giá sỉ
        var staffCreateProductRes = await staffClient.PostAsJsonAsync("/api/products", new OISM.Application.Features.Catalog.Products.CreateProductRequest
        {
            Name = "Sản phẩm của Staff",
            Variants = new List<OISM.Application.Features.Catalog.Products.CreateVariantRequest>
            {
                new()
                {
                    SKU = "STAFF-SKU-01",
                    VariantName = "Biến thể Staff",
                    RetailPrice = 250000m,
                    WholesalePrice = 200000m,
                    Unit = "cai"
                }
            }
        });
        staffCreateProductRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // Lấy SkuId của Staff
        Guid staffSkuId;
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT id FROM public.skus WHERE tenant_id = @tId AND code = 'STAFF-SKU-01'", conn);
            cmd.Parameters.AddWithValue("tId", tenantId);
            staffSkuId = (Guid)(await cmd.ExecuteScalarAsync())!;
        }

        // Staff tạo -> sku_unit_prices CHƯA CÓ DÒNG NÀO, phiếu Pending
        var staffEffRes = await staffClient.GetAsync($"/api/sku-unit-prices/effective?skuId={staffSkuId}&priceListCode=retail");
        staffEffRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var staffEffList = await staffEffRes.Content.ReadFromJsonAsync<List<EffectivePriceDto>>(_jsonOptions);
        staffEffList.Should().BeEmpty("Staff tạo SKU chưa được bán, chưa có dòng giá hiệu lực");

        // Tìm phiếu Pending của Staff SKU
        Guid staffRequestId;
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(@"
                SELECT r.id, r.status, r.kind
                FROM public.price_change_requests r
                JOIN public.price_change_request_items i ON r.id = i.request_id
                WHERE r.tenant_id = @tId AND i.sku_id = @sId;
            ", conn);
            cmd.Parameters.AddWithValue("tId", tenantId);
            cmd.Parameters.AddWithValue("sId", staffSkuId);
            await using var reader = await cmd.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            staffRequestId = reader.GetGuid(0);
            reader.GetString(1).Should().Be("Pending");
            reader.GetString(2).Should().Be("Initial");
        }

        // 3. Owner duyệt phiếu của Staff
        var approveRes = await ownerClient.PostAsync($"/api/price-change-requests/{staffRequestId}/approve", null);
        approveRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Sau khi Owner duyệt, giá Staff SKU có hiệu lực ngay
        var staffEffAfterRes = await staffClient.GetAsync($"/api/sku-unit-prices/effective?skuId={staffSkuId}&priceListCode=retail");
        staffEffAfterRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var staffEffAfterList = await staffEffAfterRes.Content.ReadFromJsonAsync<List<EffectivePriceDto>>(_jsonOptions);
        staffEffAfterList.Should().NotBeEmpty("Sau khi duyệt, giá của Staff có hiệu lực");
        staffEffAfterList![0].Price.Should().Be(250000m);

        // 4. Kiểm tra mọi dòng giá trong sku_unit_prices đều truy ngược được về một phiếu hợp lệ
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(@"
                SELECT COUNT(*)
                FROM public.sku_unit_prices p
                LEFT JOIN public.price_change_request_items i ON p.source_item_id = i.id AND p.tenant_id = i.tenant_id
                LEFT JOIN public.price_change_requests r ON i.request_id = r.id AND i.tenant_id = r.tenant_id
                WHERE p.tenant_id = @tId AND (i.id IS NULL OR r.id IS NULL);
            ", conn);
            cmd.Parameters.AddWithValue("tId", tenantId);
            var orphanCount = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            orphanCount.Should().Be(0, "Mọi dòng giá phải truy ngược được về một phiếu hợp lệ");
        }
    }

    // =========================================================================
    // AC-06: Resolution boundary, Order Snapshot, Channels & PRICE_MISMATCH
    // =========================================================================

    [Fact]
    public async Task AC06_PriceResolution_OrderSnapshot_Channels_Mismatch()
    {
        await SeedDatabaseAsync();
        var tenantId = OismsDatabaseSeeder.TenantAId;
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        Guid branchId, warehouseId, supplierId, userId;
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == tenantId);
            var warehouse = await ctx.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branch.Id);
            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            var user = await ctx.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local");
            branchId = branch.Id;
            warehouseId = warehouse.Id;
            supplierId = supplier.Id;
            userId = user.Id;
        }

        var skuCode1 = $"SKU-AC06-1-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var (skuId, unitId) = await CreateSampleSkuAndUnitAsync(tenantId, skuCode1);
        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 100m, 50000m);

        var ownerClient = _factory.CreateClient();
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // 1. Ranh giới valid_from: seed 2 mốc mà không sleep
        // T1 = now - 2h (giá 100k), T2 = now + 2h (giá 150k)
        var t1 = DateTimeOffset.UtcNow.AddHours(-2);
        var t2 = DateTimeOffset.UtcNow.AddHours(2);
        Guid retailPriceListId;
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT id FROM public.price_lists WHERE tenant_id = @tId AND code = 'retail'", conn);
            cmd.Parameters.AddWithValue("tId", tenantId);
            retailPriceListId = (Guid)(await cmd.ExecuteScalarAsync())!;

            // Tạo 2 request riêng cho T1 và T2
            var req1Id = Guid.NewGuid();
            var req2Id = Guid.NewGuid();
            var item1Id = Guid.NewGuid();
            var item2Id = Guid.NewGuid();
            await using var insertCmd = new NpgsqlCommand(@"
                INSERT INTO public.price_change_requests (id, tenant_id, request_key, kind, status, reason, created_at, reviewed_at)
                VALUES
                    (@r1, @tId, gen_random_uuid(), 'Opening', 'Approved', 'Seed valid_from test 1', now(), now()),
                    (@r2, @tId, gen_random_uuid(), 'Opening', 'Approved', 'Seed valid_from test 2', now(), now());

                INSERT INTO public.price_change_request_items (id, tenant_id, request_id, price_list_id, sku_id, unit_id, new_price, created_at)
                VALUES
                    (@i1, @tId, @r1, @plId, @sId, @uId, 100000, now()),
                    (@i2, @tId, @r2, @plId, @sId, @uId, 150000, now());

                INSERT INTO public.sku_unit_prices (id, tenant_id, price_list_id, sku_id, unit_id, price, valid_from, source_item_id, created_at)
                VALUES
                    (gen_random_uuid(), @tId, @plId, @sId, @uId, 100000, @t1, @i1, now()),
                    (gen_random_uuid(), @tId, @plId, @sId, @uId, 150000, @t2, @i2, now());
            ", conn);
            insertCmd.Parameters.AddWithValue("r1", req1Id);
            insertCmd.Parameters.AddWithValue("r2", req2Id);
            insertCmd.Parameters.AddWithValue("tId", tenantId);
            insertCmd.Parameters.AddWithValue("plId", retailPriceListId);
            insertCmd.Parameters.AddWithValue("sId", skuId);
            insertCmd.Parameters.AddWithValue("uId", unitId);
            insertCmd.Parameters.AddWithValue("i1", item1Id);
            insertCmd.Parameters.AddWithValue("i2", item2Id);
            insertCmd.Parameters.AddWithValue("t1", t1);
            insertCmd.Parameters.AddWithValue("t2", t2);
            await insertCmd.ExecuteNonQueryAsync();
        }

        // Tra cứu trước mốc t2 (t2 - 1s): giá 100.000
        var beforeT2Res = await ownerClient.GetAsync($"/api/sku-unit-prices/effective?skuId={skuId}&priceListCode=retail&at={Uri.EscapeDataString(t2.AddSeconds(-1).ToString("o"))}");
        beforeT2Res.StatusCode.Should().Be(HttpStatusCode.OK);
        var beforeList = await beforeT2Res.Content.ReadFromJsonAsync<List<EffectivePriceDto>>(_jsonOptions);
        beforeList.Should().NotBeEmpty();
        beforeList![0].Price.Should().Be(100000m);

        // Tra cứu đúng mốc t2: giá 150.000
        var atT2Res = await ownerClient.GetAsync($"/api/sku-unit-prices/effective?skuId={skuId}&priceListCode=retail&at={Uri.EscapeDataString(t2.ToString("o"))}");
        atT2Res.StatusCode.Should().Be(HttpStatusCode.OK);
        var atList = await atT2Res.Content.ReadFromJsonAsync<List<EffectivePriceDto>>(_jsonOptions);
        atList.Should().NotBeEmpty();
        atList![0].Price.Should().Be(150000m);

        // 2. Đơn đang Reserved xác nhận được bằng giá đã chụp sau khi giá mới hiệu lực
        var orderReqKey = Guid.NewGuid();
        var createOrderRes = await ownerClient.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            CustomerName = "Khách Snapshot",
            CustomerPhone = "0988888888",
            ShippingAddress = "Hà Nội",
            RequestKey = orderReqKey,
            PriceListCode = "retail",
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode1, Quantity = 2, UnitPrice = 100000m }
            }
        });
        createOrderRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdOrder = await createOrderRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
        createdOrder.Should().NotBeNull();
        createdOrder!.Status.Should().Be("Reserved");
        createdOrder.TotalAmount.Should().Be(200000m);

        // Đổi giá hiệu lực hiện hành lên 250.000
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            var newReqId = Guid.NewGuid();
            var newItemId = Guid.NewGuid();
            await using var updateCmd = new NpgsqlCommand(@"
                INSERT INTO public.price_change_requests (id, tenant_id, request_key, kind, status, reason, created_by, created_at, reviewed_at)
                VALUES (@rId, @tId, gen_random_uuid(), 'Change', 'Approved', 'Tăng giá sau snapshot', @userId, now(), now());

                INSERT INTO public.price_change_request_items (id, tenant_id, request_id, price_list_id, sku_id, unit_id, new_price, created_at)
                VALUES (@iId, @tId, @rId, @plId, @sId, @uId, 250000, now());

                INSERT INTO public.sku_unit_prices (id, tenant_id, price_list_id, sku_id, unit_id, price, valid_from, source_item_id, created_at)
                VALUES (gen_random_uuid(), @tId, @plId, @sId, @uId, 250000, now() - interval '5 seconds', @iId, now());
            ", conn);
            updateCmd.Parameters.AddWithValue("rId", newReqId);
            updateCmd.Parameters.AddWithValue("tId", tenantId);
            updateCmd.Parameters.AddWithValue("userId", userId);
            updateCmd.Parameters.AddWithValue("plId", retailPriceListId);
            updateCmd.Parameters.AddWithValue("sId", skuId);
            updateCmd.Parameters.AddWithValue("uId", unitId);
            updateCmd.Parameters.AddWithValue("iId", newItemId);
            await updateCmd.ExecuteNonQueryAsync();
        }

        // Xác nhận đơn: đơn phải xác nhận thành công với giá chụp 100.000 (Tổng 200.000)
        var confirmRes = await ownerClient.PostAsync($"/api/orders/{createdOrder.Id}/confirm", null);
        confirmRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmedOrderRes = await ownerClient.GetAsync($"/api/orders/{createdOrder.Id}");
        var confirmedOrder = await confirmedOrderRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
        confirmedOrder!.Status.Should().Be("Confirmed");
        confirmedOrder.TotalAmount.Should().Be(200000m, "Giá đã chụp trong đơn không bị thay đổi khi giá mới có hiệu lực");

        // 3. POS luôn dùng retail
        var posReqKey = Guid.NewGuid();
        var posRes = await ownerClient.PostAsJsonAsync("/api/pos/checkout", new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = 250000m,
            RequestKey = posReqKey,
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode1, Quantity = 1, UnitPrice = 250000m }
            }
        });
        posRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var posResult = await posRes.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);
        posResult.Should().NotBeNull();
        posResult!.Success.Should().BeTrue();

        // Kiểm tra OrderItem của POS: PriceSource = 'PriceList', SkuUnitPriceId trỏ tới retail price
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var chkCmd = new NpgsqlCommand(@"
                SELECT oi.price_source, oi.sku_unit_price_id, pl.code
                FROM public.order_items oi
                JOIN public.sku_unit_prices sup ON oi.sku_unit_price_id = sup.id
                JOIN public.price_lists pl ON sup.price_list_id = pl.id
                WHERE oi.tenant_id = @tId AND oi.order_id = @oId;
            ", conn);
            chkCmd.Parameters.AddWithValue("tId", tenantId);
            chkCmd.Parameters.AddWithValue("oId", posResult.OrderId);
            await using var reader = await chkCmd.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetString(0).Should().Be("PriceList");
            reader.IsDBNull(1).Should().BeFalse();
            reader.GetString(2).Should().Be("retail", "POS luôn dùng bảng giá retail");
        }

        // 4. Admin chọn được wholesale
        // Cấu hình giá sỉ 180.000 cho SKU
        Guid wholesalePriceListId;
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT id FROM public.price_lists WHERE tenant_id = @tId AND code = 'wholesale'", conn);
            cmd.Parameters.AddWithValue("tId", tenantId);
            wholesalePriceListId = (Guid)(await cmd.ExecuteScalarAsync())!;

            var wsReqId = Guid.NewGuid();
            var wsItemId = Guid.NewGuid();
            await using var insertCmd = new NpgsqlCommand(@"
                INSERT INTO public.price_change_requests (id, tenant_id, request_key, kind, status, reason, created_by, created_at, reviewed_at)
                VALUES (@rId, @tId, gen_random_uuid(), 'Change', 'Approved', 'Giá sỉ admin', @userId, now(), now());

                INSERT INTO public.price_change_request_items (id, tenant_id, request_id, price_list_id, sku_id, unit_id, new_price, created_at)
                VALUES (@iId, @tId, @rId, @plId, @sId, @uId, 180000, now());

                INSERT INTO public.sku_unit_prices (id, tenant_id, price_list_id, sku_id, unit_id, price, valid_from, source_item_id, created_at)
                VALUES (gen_random_uuid(), @tId, @plId, @sId, @uId, 180000, now() - interval '5 seconds', @iId, now());
            ", conn);
            insertCmd.Parameters.AddWithValue("rId", wsReqId);
            insertCmd.Parameters.AddWithValue("tId", tenantId);
            insertCmd.Parameters.AddWithValue("userId", userId);
            insertCmd.Parameters.AddWithValue("plId", wholesalePriceListId);
            insertCmd.Parameters.AddWithValue("sId", skuId);
            insertCmd.Parameters.AddWithValue("uId", unitId);
            insertCmd.Parameters.AddWithValue("iId", wsItemId);
            await insertCmd.ExecuteNonQueryAsync();
        }

        var wholesaleOrderKey = Guid.NewGuid();
        var wsOrderRes = await ownerClient.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            CustomerName = "Đại lý bán buôn",
            CustomerPhone = "0977777777",
            RequestKey = wholesaleOrderKey,
            PriceListCode = "wholesale",
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode1, Quantity = 5, UnitPrice = 180000m }
            }
        });
        wsOrderRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var wsOrderDto = await wsOrderRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);

        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var chkCmd = new NpgsqlCommand(@"
                SELECT oi.price_source, pl.code
                FROM public.order_items oi
                JOIN public.sku_unit_prices sup ON oi.sku_unit_price_id = sup.id
                JOIN public.price_lists pl ON sup.price_list_id = pl.id
                WHERE oi.tenant_id = @tId AND oi.order_id = @oId;
            ", conn);
            chkCmd.Parameters.AddWithValue("tId", tenantId);
            chkCmd.Parameters.AddWithValue("oId", wsOrderDto!.Id);
            await using var reader = await chkCmd.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetString(0).Should().Be("PriceList");
            reader.GetString(1).Should().Be("wholesale", "Admin chọn được bảng giá wholesale");
        }

        // 5. Đơn sàn có nguồn Marketplace (qua Webhook tiếp nhận đơn TMĐT)
        var extShopId = $"SHOP_{Guid.NewGuid():N}"[..16];
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            ctx.ShopIntegrations.Add(new ShopIntegration
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Channel = "Shopee",
                ExternalShopId = extShopId,
                BranchId = branchId,
                IsActive = true,
                SecretHash = "dummy_hash",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        var marketPayload = new CanonicalWebhookPayloadDto
        {
            Channel = "Shopee",
            ExternalShopId = extShopId,
            ExternalOrderId = $"ORDER_{Guid.NewGuid():N}"[..16],
            OrderTimestamp = DateTimeOffset.UtcNow,
            TotalAmount = 165000m,
            Items = new List<CanonicalWebhookItemDto>
            {
                new() { SkuCode = skuCode1, Quantity = 1, UnitPrice = 165000m }
            }
        };

        var marketOrderRes = await ownerClient.PostAsJsonAsync("/api/webhooks/simulator/shopee", marketPayload);
        marketOrderRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var marketResult = await marketOrderRes.Content.ReadFromJsonAsync<WebhookSimulationResultDto>(_jsonOptions);
        marketResult.Should().NotBeNull();
        marketResult!.OrderId.Should().NotBeNull();
        var marketOrderId = marketResult.OrderId!.Value;

        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var chkCmd = new NpgsqlCommand(@"
                SELECT price_source, sku_unit_price_id
                FROM public.order_items
                WHERE tenant_id = @tId AND order_id = @oId;
            ", conn);
            chkCmd.Parameters.AddWithValue("tId", tenantId);
            chkCmd.Parameters.AddWithValue("oId", marketOrderId);
            await using var reader = await chkCmd.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetString(0).Should().Be("Marketplace", "Đơn sàn có nguồn Marketplace");
            reader.IsDBNull(1).Should().BeTrue("Đơn sàn không liên kết sku_unit_price_id");
        }

        // 6. PRICE_MISMATCH có extensions.currentPrices cho mọi dòng lệch
        var skuCode2 = $"SKU-AC06-2-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var (sku2Id, unit2Id) = await CreateSampleSkuAndUnitAsync(tenantId, skuCode2);
        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, sku2Id, 100m, 50000m);
        // Cấp giá retail cho SKU 2 là 300.000
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            var r2Id = Guid.NewGuid();
            var i2Id = Guid.NewGuid();
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO public.price_change_requests (id, tenant_id, request_key, kind, status, reason, created_at, reviewed_at)
                VALUES (@rId, @tId, gen_random_uuid(), 'Opening', 'Approved', 'Giá retail sku2', now(), now());

                INSERT INTO public.price_change_request_items (id, tenant_id, request_id, price_list_id, sku_id, unit_id, new_price, created_at)
                VALUES (@iId, @tId, @rId, @plId, @sId, @uId, 300000, now());

                INSERT INTO public.sku_unit_prices (id, tenant_id, price_list_id, sku_id, unit_id, price, valid_from, source_item_id, created_at)
                VALUES (gen_random_uuid(), @tId, @plId, @sId, @uId, 300000, now() - interval '5 seconds', @iId, now());
            ", conn);
            cmd.Parameters.AddWithValue("rId", r2Id);
            cmd.Parameters.AddWithValue("tId", tenantId);
            cmd.Parameters.AddWithValue("plId", retailPriceListId);
            cmd.Parameters.AddWithValue("sId", sku2Id);
            cmd.Parameters.AddWithValue("uId", unit2Id);
            cmd.Parameters.AddWithValue("iId", i2Id);
            await cmd.ExecuteNonQueryAsync();
        }

        // Gọi POS checkout với cả 2 dòng đều gửi sai giá:
        // SKU 1 (giá thật 250.000) gửi 99.000
        // SKU 2 (giá thật 300.000) gửi 199.000
        var mismatchRes = await ownerClient.PostAsJsonAsync("/api/pos/checkout", new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = 500000m,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode1, Quantity = 1, UnitPrice = 99000m },
                new() { SkuId = sku2Id, SkuCode = skuCode2, Quantity = 1, UnitPrice = 199000m }
            }
        });
        mismatchRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var mismatchJson = await mismatchRes.Content.ReadAsStringAsync();
        using var mismatchDoc = JsonDocument.Parse(mismatchJson);
        mismatchDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("PRICE_MISMATCH");
        JsonElement currentPricesProp;
        if (!mismatchDoc.RootElement.TryGetProperty("currentPrices", out currentPricesProp))
        {
            if (mismatchDoc.RootElement.TryGetProperty("extensions", out var ext))
            {
                currentPricesProp = ext.GetProperty("currentPrices");
            }
        }
        currentPricesProp.GetArrayLength().Should().Be(2, "PRICE_MISMATCH phải chứa extensions.currentPrices cho MỌI dòng lệch");
    }

    // =========================================================================
    // AC-07: Idempotency with price changes, Payload Mismatch, Unconfigured Unit
    // =========================================================================

    [Fact]
    public async Task AC07_Idempotency_PayloadMismatch_UnconfiguredUnit()
    {
        await SeedDatabaseAsync();
        var tenantId = OismsDatabaseSeeder.TenantAId;
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        Guid branchId, warehouseId, supplierId, userId;
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == tenantId);
            var warehouse = await ctx.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branch.Id);
            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            var user = await ctx.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local");
            branchId = branch.Id;
            warehouseId = warehouse.Id;
            supplierId = supplier.Id;
            userId = user.Id;
        }

        var skuCode = $"SKU-AC07-1-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var (skuId, unitId) = await CreateSampleSkuAndUnitAsync(tenantId, skuCode);
        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 100m, 50000m);

        var ownerClient = _factory.CreateClient();
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        Guid retailPriceListId;
        Guid wholesalePriceListId;
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using (var cmd = new NpgsqlCommand("SELECT id FROM public.price_lists WHERE tenant_id = @tId AND code = 'retail'", conn))
            {
                cmd.Parameters.AddWithValue("tId", tenantId);
                retailPriceListId = (Guid)(await cmd.ExecuteScalarAsync())!;
            }
            await using (var cmd = new NpgsqlCommand("SELECT id FROM public.price_lists WHERE tenant_id = @tId AND code = 'wholesale'", conn))
            {
                cmd.Parameters.AddWithValue("tId", tenantId);
                wholesalePriceListId = (Guid)(await cmd.ExecuteScalarAsync())!;
            }

            // Cấu hình giá retail ban đầu 120.000
            var rId = Guid.NewGuid();
            var iId = Guid.NewGuid();
            await using var cmdInit = new NpgsqlCommand(@"
                INSERT INTO public.price_change_requests (id, tenant_id, request_key, kind, status, reason, created_at, reviewed_at)
                VALUES (@rId, @tId, gen_random_uuid(), 'Opening', 'Approved', 'Init retail 120k', now(), now());

                INSERT INTO public.price_change_request_items (id, tenant_id, request_id, price_list_id, sku_id, unit_id, new_price, created_at)
                VALUES (@iId, @tId, @rId, @plId, @sId, @uId, 120000, now());

                INSERT INTO public.sku_unit_prices (id, tenant_id, price_list_id, sku_id, unit_id, price, valid_from, source_item_id, created_at)
                VALUES (gen_random_uuid(), @tId, @plId, @sId, @uId, 120000, now() - interval '5 seconds', @iId, now());
            ", conn);
            cmdInit.Parameters.AddWithValue("rId", rId);
            cmdInit.Parameters.AddWithValue("tId", tenantId);
            cmdInit.Parameters.AddWithValue("plId", retailPriceListId);
            cmdInit.Parameters.AddWithValue("sId", skuId);
            cmdInit.Parameters.AddWithValue("uId", unitId);
            cmdInit.Parameters.AddWithValue("iId", iId);
            await cmdInit.ExecuteNonQueryAsync();
        }

        // 1. Gửi lại cùng Idempotency-Key sau khi giá đổi: trả đơn cũ, không tạo đơn mới
        var replayKey = Guid.NewGuid();
        var checkout1 = await ownerClient.PostAsJsonAsync("/api/pos/checkout", new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = 120000m,
            RequestKey = replayKey,
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1, UnitPrice = 120000m }
            }
        });
        checkout1.StatusCode.Should().Be(HttpStatusCode.OK);
        var order1 = await checkout1.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);
        order1!.Success.Should().BeTrue();

        // Bây giờ đổi giá lên 180.000
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            var rId = Guid.NewGuid();
            var iId = Guid.NewGuid();
            await using var cmdNew = new NpgsqlCommand(@"
                INSERT INTO public.price_change_requests (id, tenant_id, request_key, kind, status, reason, created_by, created_at, reviewed_at)
                VALUES (@rId, @tId, gen_random_uuid(), 'Change', 'Approved', 'Đổi giá 180k', @userId, now(), now());

                INSERT INTO public.price_change_request_items (id, tenant_id, request_id, price_list_id, sku_id, unit_id, new_price, created_at)
                VALUES (@iId, @tId, @rId, @plId, @sId, @uId, 180000, now());

                INSERT INTO public.sku_unit_prices (id, tenant_id, price_list_id, sku_id, unit_id, price, valid_from, source_item_id, created_at)
                VALUES (gen_random_uuid(), @tId, @plId, @sId, @uId, 180000, now() - interval '5 seconds', @iId, now());
            ", conn);
            cmdNew.Parameters.AddWithValue("rId", rId);
            cmdNew.Parameters.AddWithValue("tId", tenantId);
            cmdNew.Parameters.AddWithValue("userId", userId);
            cmdNew.Parameters.AddWithValue("plId", retailPriceListId);
            cmdNew.Parameters.AddWithValue("sId", skuId);
            cmdNew.Parameters.AddWithValue("uId", unitId);
            cmdNew.Parameters.AddWithValue("iId", iId);
            await cmdNew.ExecuteNonQueryAsync();
        }

        // Gửi lại cùng RequestKey và cùng payload 120.000
        var checkoutReplay = await ownerClient.PostAsJsonAsync("/api/pos/checkout", new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = 120000m,
            RequestKey = replayKey,
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1, UnitPrice = 120000m }
            }
        });
        checkoutReplay.StatusCode.Should().Be(HttpStatusCode.OK);
        var orderReplay = await checkoutReplay.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);
        orderReplay!.OrderId.Should().Be(order1.OrderId, "Replay cùng Idempotency-Key sau khi giá đổi phải trả đơn cũ");

        // 2. Đổi bảng giá với cùng key báo IDEMPOTENCY_PAYLOAD_MISMATCH
        // Cấu hình giá sỉ
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            var rId = Guid.NewGuid();
            var iId = Guid.NewGuid();
            await using var cmdWs = new NpgsqlCommand(@"
                INSERT INTO public.price_change_requests (id, tenant_id, request_key, kind, status, reason, created_at, reviewed_at)
                VALUES (@rId, @tId, gen_random_uuid(), 'Opening', 'Approved', 'Init wholesale 100k', now(), now());

                INSERT INTO public.price_change_request_items (id, tenant_id, request_id, price_list_id, sku_id, unit_id, new_price, created_at)
                VALUES (@iId, @tId, @rId, @plId, @sId, @uId, 100000, now());

                INSERT INTO public.sku_unit_prices (id, tenant_id, price_list_id, sku_id, unit_id, price, valid_from, source_item_id, created_at)
                VALUES (gen_random_uuid(), @tId, @plId, @sId, @uId, 100000, now() - interval '5 seconds', @iId, now());
            ", conn);
            cmdWs.Parameters.AddWithValue("rId", rId);
            cmdWs.Parameters.AddWithValue("tId", tenantId);
            cmdWs.Parameters.AddWithValue("plId", wholesalePriceListId);
            cmdWs.Parameters.AddWithValue("sId", skuId);
            cmdWs.Parameters.AddWithValue("uId", unitId);
            cmdWs.Parameters.AddWithValue("iId", iId);
            await cmdWs.ExecuteNonQueryAsync();
        }

        var sharedKey = Guid.NewGuid();
        var adminOrder1 = await ownerClient.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = sharedKey,
            PriceListCode = "retail",
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1, UnitPrice = 180000m }
            }
        });
        adminOrder1.StatusCode.Should().Be(HttpStatusCode.Created);

        // Gửi lại cùng key nhưng đổi PriceListCode = "wholesale"
        var adminOrderMismatch = await ownerClient.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = sharedKey,
            PriceListCode = "wholesale",
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1, UnitPrice = 100000m }
            }
        });
        adminOrderMismatch.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var mismatchBody = await adminOrderMismatch.Content.ReadAsStringAsync();
        using var mismatchDoc = JsonDocument.Parse(mismatchBody);
        mismatchDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("IDEMPOTENCY_PAYLOAD_MISMATCH");

        // 3. Đơn vị chưa có giá báo UNIT_PRICE_NOT_CONFIGURED, không bán giá 0
        var skuNoPriceCode = $"SKU-NOPRICE-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var (skuNoPriceId, _) = await CreateSampleSkuAndUnitAsync(tenantId, skuNoPriceCode);
        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuNoPriceId, 50m, 50000m);

        var noPriceOrderRes = await ownerClient.PostAsJsonAsync("/api/pos/checkout", new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = 0m,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuNoPriceId, SkuCode = skuNoPriceCode, Quantity = 1, UnitPrice = 0m }
            }
        });
        noPriceOrderRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var noPriceJson = await noPriceOrderRes.Content.ReadAsStringAsync();
        using var noPriceDoc = JsonDocument.Parse(noPriceJson);
        noPriceDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("UNIT_PRICE_NOT_CONFIGURED");
    }

    // =========================================================================
    // AC-08: Direct Price Update Disabled (410 on /prices, 409 on Variant/Unit)
    // =========================================================================

    [Fact]
    public async Task AC08_DirectPriceUpdate_Disabled_Returns410_And409_OnMismatch()
    {
        var (ownerToken, tenantId) = await RegisterAndLoginOwnerAsync("TENANT-AC08", "owner_ac08@test.com");
        var ownerClient = _factory.CreateClient();
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // 1. Tạo sản phẩm với 1 biến thể có giá lẻ = 100.000, sỉ = 80.000
        var skuCode = $"SKU-AC08-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var createProductRes = await ownerClient.PostAsJsonAsync("/api/products", new OISM.Application.Features.Catalog.Products.CreateProductRequest
        {
            Name = "Sản phẩm AC08",
            Variants = new List<OISM.Application.Features.Catalog.Products.CreateVariantRequest>
            {
                new()
                {
                    SKU = skuCode,
                    VariantName = "Biến thể AC08",
                    RetailPrice = 100000m,
                    WholesalePrice = 80000m,
                    Unit = "cai"
                }
            }
        });
        createProductRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdProductJson = await createProductRes.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createdProductJson);
        var productId = createDoc.RootElement.GetProperty("productId").GetGuid();

        // Lấy SkuId
        Guid skuId;
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT id FROM public.skus WHERE tenant_id = @tId AND code = @code", conn);
            cmd.Parameters.AddWithValue("tId", tenantId);
            cmd.Parameters.AddWithValue("code", skuCode);
            skuId = (Guid)(await cmd.ExecuteScalarAsync())!;
        }

        // 1. PUT .../prices trả 410 Gone kèm DIRECT_PRICE_UPDATE_NOT_ALLOWED
        var putPricesRes = await ownerClient.PutAsJsonAsync($"/api/products/{productId}/variants/{skuId}/prices", new
        {
            RetailPrice = 120000m,
            WholesalePrice = 90000m
        });
        putPricesRes.StatusCode.Should().Be(HttpStatusCode.Gone, "PUT /prices phải trả 410 Gone");
        var putPricesJson = await putPricesRes.Content.ReadAsStringAsync();
        using var putPricesDoc = JsonDocument.Parse(putPricesJson);
        putPricesDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("DIRECT_PRICE_UPDATE_NOT_ALLOWED");

        // 2. Sửa SKU với giá khác báo DIRECT_PRICE_UPDATE_NOT_ALLOWED (409), giá vẫn không đổi
        var updateSkuMismatchRes = await ownerClient.PutAsJsonAsync($"/api/products/{productId}/variants/{skuId}", new OISM.Application.Features.Catalog.Products.UpdateVariantRequest
        {
            VariantName = "Đổi tên kèm sửa giá",
            RetailPrice = 999999m,
            WholesalePrice = 80000m
        });
        updateSkuMismatchRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var updateSkuMismatchJson = await updateSkuMismatchRes.Content.ReadAsStringAsync();
        using var updateSkuDoc = JsonDocument.Parse(updateSkuMismatchJson);
        updateSkuDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("DIRECT_PRICE_UPDATE_NOT_ALLOWED");

        // Kiểm tra giá trong DB vẫn không đổi (vẫn 100.000)
        var getEffRes = await ownerClient.GetAsync($"/api/sku-unit-prices/effective?skuId={skuId}&priceListCode=retail");
        getEffRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var effList = await getEffRes.Content.ReadFromJsonAsync<List<EffectivePriceDto>>(_jsonOptions);
        effList![0].Price.Should().Be(100000m, "Giá vẫn không đổi sau khi request bị từ chối");

        // Sửa SKU với cùng giá hoặc không gửi giá -> thành công 200 OK
        var updateSkuOkRes = await ownerClient.PutAsJsonAsync($"/api/products/{productId}/variants/{skuId}", new OISM.Application.Features.Catalog.Products.UpdateVariantRequest
        {
            VariantName = "Đổi tên hợp lệ",
            RetailPrice = 100000m,
            WholesalePrice = 80000m
        });
        updateSkuOkRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Sửa đơn vị quy đổi với giá khác báo DIRECT_PRICE_UPDATE_NOT_ALLOWED (409)
        // Tạo thêm 1 đơn vị quy đổi (hộp) có giá 500.000
        Guid hopUnitId;
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT id FROM public.units WHERE tenant_id = @tId AND code = 'hop'", conn);
            cmd.Parameters.AddWithValue("tId", tenantId);
            hopUnitId = (Guid)(await cmd.ExecuteScalarAsync())!;
        }

        var addUnitRes = await ownerClient.PostAsJsonAsync($"/api/products/{productId}/variants/{skuId}/units", new OISM.Application.Features.Catalog.Units.CreateSkuUnitRequest
        {
            UnitId = hopUnitId,
            FactorToBase = 10m,
            SalePrice = 500000m
        });
        addUnitRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var addedUnit = await addUnitRes.Content.ReadFromJsonAsync<OISM.Application.Features.Catalog.Units.SkuUnitResponse>(_jsonOptions);

        // Sửa đơn vị quy đổi với giá khác 500.000 (ví dụ 600.000) -> 409 DIRECT_PRICE_UPDATE_NOT_ALLOWED
        var updateUnitMismatchRes = await ownerClient.PutAsJsonAsync($"/api/products/{productId}/variants/{skuId}/units/{addedUnit!.Id}", new OISM.Application.Features.Catalog.Units.UpdateSkuUnitRequest
        {
            SalePrice = 600000m
        });
        updateUnitMismatchRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var updateUnitMismatchJson = await updateUnitMismatchRes.Content.ReadAsStringAsync();
        using var updateUnitDoc = JsonDocument.Parse(updateUnitMismatchJson);
        updateUnitDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("DIRECT_PRICE_UPDATE_NOT_ALLOWED");

        // Sửa đơn vị quy đổi với cùng giá 500.000 -> 200 OK
        var updateUnitOkRes = await ownerClient.PutAsJsonAsync($"/api/products/{productId}/variants/{skuId}/units/{addedUnit.Id}", new OISM.Application.Features.Catalog.Units.UpdateSkuUnitRequest
        {
            SalePrice = 500000m
        });
        updateUnitOkRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 410 đi theo chuẩn ProblemDetails, vẫn giữ "message" cho frontend hiện tại đọc
        putPricesDoc.RootElement.GetProperty("status").GetInt32().Should().Be(410);
        putPricesDoc.RootElement.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
        putPricesDoc.RootElement.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
    }

    // =========================================================================
    // Hồi quy sau review: đường ghi cũ, dữ liệu phiếu, đọc giá dưới khóa
    // =========================================================================

    private async Task<HttpResponseMessage> PostPriceChangeAsync(HttpClient client, object body, Guid? requestKey = null)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/price-change-requests")
        {
            Content = JsonContent.Create(body)
        };
        message.Headers.Add("Idempotency-Key", (requestKey ?? Guid.NewGuid()).ToString());
        return await client.SendAsync(message);
    }

    private static object PriceChangeBody(Guid skuId, Guid unitId, decimal newPrice) => new
    {
        reason = "Đổi giá trong test",
        items = new[] { new { priceListCode = "retail", skuId, unitId, newPrice } }
    };

    private async Task<(HttpClient Client, Guid BranchId, Guid SkuId, Guid UnitId, string SkuCode)> SetupSellableSkuAsync(string prefix, decimal price)
    {
        await SeedDatabaseAsync();
        var tenantId = OismsDatabaseSeeder.TenantAId;
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        Guid branchId, warehouseId, supplierId, userId;
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            warehouseId = (await ctx.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branch.Id)).Id;
            supplierId = (await ctx.Suppliers.FirstAsync(s => s.TenantId == tenantId)).Id;
            userId = (await ctx.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local")).Id;
        }

        var skuCode = $"{prefix}-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var (skuId, unitId) = await CreateSampleSkuAndUnitAsync(tenantId, skuCode);
        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 100m, 50000m);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        (await PostPriceChangeAsync(client, PriceChangeBody(skuId, unitId, price))).StatusCode.Should().Be(HttpStatusCode.Created);

        return (client, branchId, skuId, unitId, skuCode);
    }

    /// <summary>Giữ khóa advisory của SKU giống luồng bán, chờ đến khi request bán đang đứng đợi khóa này.</summary>
    private static async Task<NpgsqlTransaction> HoldSkuLockAsync(NpgsqlConnection conn, Guid branchId, Guid skuId)
    {
        var tx = await conn.BeginTransactionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@t::text || '/' || @b::text || '/' || @s::text, 0));", conn, tx);
        cmd.Parameters.AddWithValue("t", OismsDatabaseSeeder.TenantAId.ToString());
        cmd.Parameters.AddWithValue("b", branchId.ToString());
        cmd.Parameters.AddWithValue("s", skuId.ToString());
        await cmd.ExecuteNonQueryAsync();
        return tx;
    }

    private static async Task WaitForBlockedAdvisoryLockAsync()
    {
        await using var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString);
        await conn.OpenAsync();
        for (var i = 0; i < 200; i++)
        {
            await using var cmd = new NpgsqlCommand(@"
                SELECT count(*) FROM pg_locks l
                JOIN pg_database d ON d.oid = l.database
                WHERE l.locktype = 'advisory' AND NOT l.granted AND d.datname = current_database();", conn);
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0)
            {
                return;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException("Request bán không đứng đợi khóa SKU.");
    }

    [Fact]
    public async Task Review_UpdateVariant_LegacyPriceOrZero_AfterPriceChange_Returns409()
    {
        var (ownerToken, tenantId) = await RegisterAndLoginOwnerAsync("TENANT-RV01", "owner_rv01@test.com");
        var owner = _factory.CreateClient();
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var skuCode = $"SKU-RV01-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var created = await owner.PostAsJsonAsync("/api/products", new OISM.Application.Features.Catalog.Products.CreateProductRequest
        {
            Name = "Sản phẩm RV01",
            Variants = new() { new() { SKU = skuCode, VariantName = "RV01", RetailPrice = 100000m, Unit = "cai" } }
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        using var createdDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var productId = createdDoc.RootElement.GetProperty("productId").GetGuid();
        var product = await owner.GetFromJsonAsync<OISM.Application.Features.Catalog.Products.ProductResponse>($"/api/products/{productId}", _jsonOptions);
        var variant = product!.Variants.Single();

        (await PostPriceChangeAsync(owner, PriceChangeBody(variant.Id, variant.BaseUnitId!.Value, 150000m))).StatusCode.Should().Be(HttpStatusCode.Created);

        // Cột cũ vẫn là 100.000 nhưng giá hiện hành là 150.000: gửi lại 100.000 hoặc 0 đều phải bị chặn, không được báo thành công giả
        foreach (var stale in new[] { 100000m, 0m })
        {
            var res = await owner.PutAsJsonAsync($"/api/products/{productId}/variants/{variant.Id}", new OISM.Application.Features.Catalog.Products.UpdateVariantRequest
            {
                VariantName = "RV01",
                RetailPrice = stale
            });
            res.StatusCode.Should().Be(HttpStatusCode.Conflict, $"{stale} khác giá hiện hành 150.000");
        }

        (await owner.PutAsJsonAsync($"/api/products/{productId}/variants/{variant.Id}", new OISM.Application.Features.Catalog.Products.UpdateVariantRequest
        {
            VariantName = "RV01",
            RetailPrice = 150000m
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        // SKU do Staff tạo: giá đang chờ duyệt nên chưa có giá hiện hành, gửi 0 được bỏ qua, gửi lại giá chờ duyệt bị chặn
        var staffToken = await CreateAndLoginUserAsync(tenantId, "staff_rv01@test.com", "Staff");
        var staff = _factory.CreateClient();
        staff.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var staffSku = $"SKU-RV01S-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var staffCreated = await staff.PostAsJsonAsync($"/api/products/{productId}/variants", new OISM.Application.Features.Catalog.Products.CreateVariantRequest
        {
            SKU = staffSku,
            VariantName = "RV01 Staff",
            Color = "Đỏ",
            RetailPrice = 70000m,
            Unit = "cai"
        });
        staffCreated.StatusCode.Should().Be(HttpStatusCode.Created, await staffCreated.Content.ReadAsStringAsync());
        var staffVariant = await staffCreated.Content.ReadFromJsonAsync<OISM.Application.Features.Catalog.Products.ProductVariantResponse>(_jsonOptions);
        staffVariant!.RetailPrice.Should().BeNull();
        staffVariant.BaseUnitPriceMissing.Should().Be(true);

        (await owner.PutAsJsonAsync($"/api/products/{productId}/variants/{staffVariant.Id}", new OISM.Application.Features.Catalog.Products.UpdateVariantRequest
        {
            VariantName = "RV01 Staff",
            Color = "Đỏ",
            RetailPrice = 70000m
        })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var zeroRes = await owner.PutAsJsonAsync($"/api/products/{productId}/variants/{staffVariant.Id}", new OISM.Application.Features.Catalog.Products.UpdateVariantRequest
        {
            VariantName = "RV01 Staff",
            Color = "Đỏ",
            RetailPrice = 0m
        });
        zeroRes.StatusCode.Should().Be(HttpStatusCode.OK, await zeroRes.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Review_CreatePriceChange_IgnoresClientKind_RejectsLongReason_ReplaysSubMicrosecondValidFrom()
    {
        var (client, _, skuId, unitId, _) = await SetupSellableSkuAsync("SKU-RV02", 100000m);

        // Client không được tự đặt loại phiếu
        var kindRes = await PostPriceChangeAsync(client, new
        {
            kind = "KhongHopLe",
            reason = "Thử gửi loại phiếu",
            items = new[] { new { priceListCode = "retail", skuId, unitId, newPrice = 110000m } }
        });
        kindRes.StatusCode.Should().Be(HttpStatusCode.Created, await kindRes.Content.ReadAsStringAsync());
        (await kindRes.Content.ReadFromJsonAsync<PriceChangeRequestResponse>(_jsonOptions))!.Kind.Should().Be("Change");

        // Lý do quá dài là lỗi dữ liệu 400, không phải 500
        var longReason = await PostPriceChangeAsync(client, new
        {
            reason = new string('x', 501),
            items = new[] { new { priceListCode = "retail", skuId, unitId, newPrice = 120000m } }
        });
        longReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // PostgreSQL chỉ giữ tới micro giây: gửi lại đúng payload có 7 chữ số lẻ vẫn phải là replay
        var key = Guid.NewGuid();
        var validFrom = DateTimeOffset.Parse("2099-01-01T00:00:00.1234567+00:00");
        var body = new
        {
            reason = "Lên lịch giá",
            requestedValidFrom = validFrom,
            items = new[] { new { priceListCode = "retail", skuId, unitId, newPrice = 130000m } }
        };
        (await PostPriceChangeAsync(client, body, key)).StatusCode.Should().Be(HttpStatusCode.Created);
        var replay = await PostPriceChangeAsync(client, body, key);
        replay.StatusCode.Should().Be(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Review_AdminOrder_UnknownPriceList_Returns404()
    {
        var (client, branchId, skuId, _, skuCode) = await SetupSellableSkuAsync("SKU-RV03", 100000m);

        var res = await client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            CustomerName = "Khách RV03",
            CustomerPhone = "0988888888",
            ShippingAddress = "Hà Nội",
            RequestKey = Guid.NewGuid(),
            PriceListCode = "khongco",
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1, UnitPrice = 100000m } }
        });

        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("PRICE_LIST_NOT_FOUND");
    }

    [Theory]
    [InlineData(150000, "PRICE_MISMATCH")]
    [InlineData(100000, null)]
    public async Task Review_AdminOrder_ReadsPriceAfterSkuLock(int approvedPrice, string? expectedError)
    {
        var (client, branchId, skuId, unitId, skuCode) = await SetupSellableSkuAsync("SKU-RV04", 100000m);
        var requestKey = Guid.NewGuid();

        await using var lockConn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString);
        await lockConn.OpenAsync();
        await using var lockTx = await HoldSkuLockAsync(lockConn, branchId, skuId);

        var orderTask = client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            CustomerName = "Khách RV04",
            CustomerPhone = "0988888888",
            ShippingAddress = "Hà Nội",
            RequestKey = requestKey,
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1, UnitPrice = 100000m } }
        });
        await WaitForBlockedAdvisoryLockAsync();

        // Giá mới có hiệu lực trong lúc đơn còn đợi khóa SKU
        var approved = await PostPriceChangeAsync(client, PriceChangeBody(skuId, unitId, approvedPrice));
        approved.StatusCode.Should().Be(HttpStatusCode.Created);
        await lockTx.CommitAsync();

        var res = await orderTask;
        await using var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString);
        await conn.OpenAsync();

        if (expectedError != null)
        {
            res.StatusCode.Should().Be(HttpStatusCode.BadRequest, await res.Content.ReadAsStringAsync());
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("errorCode").GetString().Should().Be(expectedError);
            doc.RootElement.GetProperty("currentPrices")[0].GetProperty("currentPrice").GetDecimal().Should().Be(approvedPrice);

            await using var countCmd = new NpgsqlCommand("SELECT count(*) FROM public.orders WHERE request_key = @k", conn);
            countCmd.Parameters.AddWithValue("k", requestKey);
            Convert.ToInt64(await countCmd.ExecuteScalarAsync()).Should().Be(0, "đơn bị từ chối không được ghi xuống CSDL");
            return;
        }

        // Giá không đổi nhưng đã có dòng giá mới: đơn phải chụp đúng dòng đang hiệu lực
        res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        await using var snapshotCmd = new NpgsqlCommand(@"
            SELECT i.sku_unit_price_id = (
                SELECT p.id FROM public.sku_unit_prices p
                WHERE p.sku_id = i.sku_id AND p.unit_id = @u AND p.cancelled_at IS NULL
                ORDER BY p.valid_from DESC LIMIT 1)
            FROM public.order_items i
            JOIN public.orders o ON o.id = i.order_id
            WHERE o.request_key = @k", conn);
        snapshotCmd.Parameters.AddWithValue("u", unitId);
        snapshotCmd.Parameters.AddWithValue("k", requestKey);
        ((bool)(await snapshotCmd.ExecuteScalarAsync())!).Should().BeTrue();
    }

    [Fact]
    public async Task Review_PosCheckout_ConcurrentWithApproval_UsesPriceEffectiveAfterSkuLock()
    {
        var (client, branchId, skuId, unitId, skuCode) = await SetupSellableSkuAsync("SKU-RV05", 100000m);

        await using var lockConn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString);
        await lockConn.OpenAsync();
        await using var lockTx = await HoldSkuLockAsync(lockConn, branchId, skuId);

        var checkoutTask = client.PostAsJsonAsync("/api/pos/checkout", new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = 100000m,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1, UnitPrice = 100000m } }
        });
        await WaitForBlockedAdvisoryLockAsync();

        (await PostPriceChangeAsync(client, PriceChangeBody(skuId, unitId, 150000m))).StatusCode.Should().Be(HttpStatusCode.Created);
        await lockTx.CommitAsync();

        var res = await checkoutTask;
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest, await res.Content.ReadAsStringAsync());
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("PRICE_MISMATCH");
        doc.RootElement.GetProperty("currentPrices")[0].GetProperty("currentPrice").GetDecimal().Should().Be(150000m);
    }
}
