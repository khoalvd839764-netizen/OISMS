using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Branches;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Users;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Bộ kiểm thử tích hợp PostgreSQL cho CS-02 Phân công chi nhánh nhân viên (OISMS-59-G1B):
/// AC-01: Nâng cấp schema từ trước CS-02 chạy 2 lần idempotent, khớp schema tạo mới.
/// AC-02: GET/POST/PUT /api/users với branchIds, null giữ nguyên, [] thay rỗng, Owner xóa/bỏ qua, lỗi 400 BRANCH_NOT_FOUND không ghi DB.
/// AC-03: Cô lập multi-tenant trên API phân công chi nhánh.
/// AC-04: GET /api/branches lọc theo chi nhánh được gán cho Cashier, giữ nguyên cho Cashier rỗng / Staff / Owner.
/// AC-05: POST /api/orders/checkout và POST /api/pos/checkout chặn Cashier ở chi nhánh ngoài danh sách với 403 BRANCH_NOT_ASSIGNED, không ghi đơn/sổ kho.
/// </summary>
[Collection("PostgresIntegrationTests")]
public sealed class UserBranchAssignmentIntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public UserBranchAssignmentIntegrationTests(WebApplicationFactoryWithPostgres factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
        await using var ctx = PostgresTestEnvironment.CreateRuntimeDbContext(new TestTenant());
        await OismsDatabaseSeeder.SeedAsync(ctx, PostgresTestEnvironment.AppPassword);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class TestTenant(Guid? tenantId = null) : ICurrentTenantService
    {
        public Guid? TenantId { get; private set; } = tenantId;
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private async Task<HttpClient> CreateClientForUserAsync(string email, string? password = null)
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = password ?? PostgresTestEnvironment.AppPassword
        });
        var body = await res.Content.ReadAsStringAsync();
        res.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var auth = JsonSerializer.Deserialize<AuthResponse>(body, _jsonOptions)!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
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
    // AC-01: Nâng cấp schema user_branches từ trước CS-02 chạy 2 lần idempotent
    // =========================================================================
    [Fact]
    public async Task AC01_UpgradeFromPreUserBranchesSchema_TwiceIdempotent_MatchesFreshSchemaExactly()
    {
        const string downgradeSql = "DROP TABLE IF EXISTS public.user_branches;";

        var migrator = new DatabaseMigrationService();
        var freshDb = $"oisms_ub_fresh_{Guid.NewGuid():N}";
        var upgradedDb = $"oisms_ub_upgraded_{Guid.NewGuid():N}";
        string Conn(string db) => PostgresTestEnvironment.BuildConnectionString(
            PostgresTestEnvironment.Host,
            PostgresTestEnvironment.Port,
            db,
            PostgresTestEnvironment.AdminUser,
            PostgresTestEnvironment.AdminPassword);

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
                ["columns"] = @"SELECT table_name||'.'||column_name||':'||data_type||':'||is_nullable||':'||coalesce(column_default,'')
                                FROM information_schema.columns WHERE table_schema='public' AND table_name='user_branches'",
                ["constraints"] = @"SELECT conrelid::regclass::text||':'||conname||':'||pg_get_constraintdef(oid) FROM pg_constraint
                                    WHERE connamespace='public'::regnamespace AND conrelid='public.user_branches'::regclass",
                ["indexes"] = "SELECT indexdef FROM pg_indexes WHERE schemaname='public' AND tablename='user_branches'",
                ["grants"] = "SELECT table_name||':'||privilege_type FROM information_schema.role_table_grants WHERE grantee='oisms_runtime' AND table_name='user_branches'"
            };

            foreach (var (name, sql) in snapshotQueries)
            {
                var upgradedRows = await ReadSortedAsync(upgraded, sql);
                var freshRows = await ReadSortedAsync(fresh, sql);
                upgradedRows.Should().NotBeEmpty(name);
                upgradedRows.Should().Equal(freshRows, $"phần '{name}' của database nâng cấp phải khớp database tạo mới");
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

    // =========================================================================
    // AC-02 & AC-03: CRUD User branchIds + Multi-tenant isolation
    // =========================================================================
    [Fact]
    public async Task AC02_AC03_UsersApi_BranchAssignments_PutSemantics_Validation_AndTenantIsolation()
    {
        Guid branchA1;
        Guid branchA2 = Guid.NewGuid();
        Guid branchB1;
        Guid ownerAId;

        await using (var db = PostgresTestEnvironment.CreateRuntimeDbContext(new TestTenant()))
        {
            branchA1 = await db.Branches.IgnoreQueryFilters()
                .Where(b => b.TenantId == OismsDatabaseSeeder.TenantAId)
                .Select(b => b.Id)
                .SingleAsync();
            branchB1 = await db.Branches.IgnoreQueryFilters()
                .Where(b => b.TenantId == OismsDatabaseSeeder.TenantBId)
                .Select(b => b.Id)
                .SingleAsync();
            ownerAId = await db.Users.IgnoreQueryFilters()
                .Where(u => u.TenantId == OismsDatabaseSeeder.TenantAId && u.Role == "Owner")
                .Select(u => u.Id)
                .SingleAsync();
        }

        await using (var dbA = PostgresTestEnvironment.CreateRuntimeDbContext(new TestTenant(OismsDatabaseSeeder.TenantAId)))
        {
            dbA.Branches.Add(new Branch
            {
                Id = branchA2,
                TenantId = OismsDatabaseSeeder.TenantAId,
                Name = "Chi nhánh Quận 7",
                Address = "456 Nguyễn Văn Linh",
                IsActive = true
            });
            await dbA.SaveChangesAsync();
        }

        using var ownerAClient = await CreateClientForUserAsync("owner@shopa.local");
        using var ownerBClient = await CreateClientForUserAsync("owner@shopb.local");

        // 1. GET /api/users: danh sách ban đầu trả branchIds rỗng
        var listRes = await ownerAClient.GetAsync("/api/users");
        listRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var initialUsers = await listRes.Content.ReadFromJsonAsync<List<UserResponse>>(_jsonOptions);
        initialUsers.Should().NotBeNull();
        initialUsers!.Should().OnlyContain(u => u.TenantId == OismsDatabaseSeeder.TenantAId && u.BranchIds.Count == 0);

        // 2. POST /api/users với branchId thuộc Tenant B (AC-03) hoặc không tồn tại -> 400 BRANCH_NOT_FOUND, không ghi DB
        var countBeforeInvalid = initialUsers.Count;
        var invalidBranchCreate = await ownerAClient.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Email = "bad-branch-cashier@shopa.local",
            DisplayName = "Thu ngân sai chi nhánh",
            Password = "Password@123",
            Role = "Cashier",
            BranchIds = [branchA1, branchB1]
        });
        invalidBranchCreate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using (var errDoc = JsonDocument.Parse(await invalidBranchCreate.Content.ReadAsStringAsync()))
        {
            errDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("BRANCH_NOT_FOUND");
        }

        var missingBranchCreate = await ownerAClient.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Email = "missing-branch-cashier@shopa.local",
            DisplayName = "Thu ngân thiếu chi nhánh",
            Password = "Password@123",
            Role = "Cashier",
            BranchIds = [Guid.NewGuid()]
        });
        missingBranchCreate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using (var errDoc = JsonDocument.Parse(await missingBranchCreate.Content.ReadAsStringAsync()))
        {
            errDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("BRANCH_NOT_FOUND");
        }

        var afterInvalidUsers = await ownerAClient.GetFromJsonAsync<List<UserResponse>>("/api/users", _jsonOptions);
        afterInvalidUsers!.Count.Should().Be(countBeforeInvalid);

        // 3. POST /api/users thành công với [branchA1] -> GET /api/users/{id} trả đúng [branchA1]
        var createRes = await ownerAClient.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Email = "cashier-a1@shopa.local",
            DisplayName = "Thu ngân Quận 1",
            Password = "Password@123",
            Role = "Cashier",
            BranchIds = [branchA1]
        });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createRes.Content.ReadFromJsonAsync<UserResponse>(_jsonOptions);
        created!.BranchIds.Should().BeEquivalentTo([branchA1]);

        var getById = await ownerAClient.GetFromJsonAsync<UserResponse>($"/api/users/{created.Id}", _jsonOptions);
        getById!.BranchIds.Should().BeEquivalentTo([branchA1]);

        // 4. PUT với branchIds = null -> giữ nguyên [branchA1]
        var putNullRes = await ownerAClient.PutAsJsonAsync($"/api/users/{created.Id}", new UpdateUserRequest
        {
            DisplayName = "Thu ngân Quận 1 (đã đổi tên)",
            Role = "Cashier",
            IsActive = true,
            BranchIds = null
        });
        putNullRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterPutNull = await putNullRes.Content.ReadFromJsonAsync<UserResponse>(_jsonOptions);
        afterPutNull!.BranchIds.Should().BeEquivalentTo([branchA1]);

        // 5. PUT với branchId Tenant B -> 400 BRANCH_NOT_FOUND và không ghi thay đổi nào
        var putCrossTenant = await ownerAClient.PutAsJsonAsync($"/api/users/{created.Id}", new UpdateUserRequest
        {
            DisplayName = "Không được đổi tên",
            Role = "Cashier",
            IsActive = true,
            BranchIds = [branchB1]
        });
        putCrossTenant.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var unchangedAfterError = await ownerAClient.GetFromJsonAsync<UserResponse>($"/api/users/{created.Id}", _jsonOptions);
        unchangedAfterError!.DisplayName.Should().Be("Thu ngân Quận 1 (đã đổi tên)");
        unchangedAfterError.BranchIds.Should().BeEquivalentTo([branchA1]);

        // 6. PUT với branchIds = [branchA2] -> thay thế toàn bộ thành [branchA2]
        var putReplaceRes = await ownerAClient.PutAsJsonAsync($"/api/users/{created.Id}", new UpdateUserRequest
        {
            DisplayName = "Thu ngân Quận 7",
            Role = "Cashier",
            IsActive = true,
            BranchIds = [branchA2]
        });
        putReplaceRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterReplace = await putReplaceRes.Content.ReadFromJsonAsync<UserResponse>(_jsonOptions);
        afterReplace!.BranchIds.Should().BeEquivalentTo([branchA2]);

        // 7. PUT với branchIds = [] -> xóa phân công về rỗng (mọi chi nhánh)
        var putClearRes = await ownerAClient.PutAsJsonAsync($"/api/users/{created.Id}", new UpdateUserRequest
        {
            DisplayName = "Thu ngân toàn hệ thống",
            Role = "Cashier",
            IsActive = true,
            BranchIds = []
        });
        putClearRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterClear = await putClearRes.Content.ReadFromJsonAsync<UserResponse>(_jsonOptions);
        afterClear!.BranchIds.Should().BeEmpty();

        // 8. Owner role -> PUT với branchIds khác rỗng sẽ bỏ qua & xóa mọi phân công cũ nếu có
        var putOwnerRes = await ownerAClient.PutAsJsonAsync($"/api/users/{ownerAId}", new UpdateUserRequest
        {
            DisplayName = "Chủ shop Thời trang A",
            Role = "Owner",
            IsActive = true,
            BranchIds = [branchA1, branchA2]
        });
        putOwnerRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var ownerAfter = await putOwnerRes.Content.ReadFromJsonAsync<UserResponse>(_jsonOptions);
        ownerAfter!.BranchIds.Should().BeEmpty();

        // 9. Tenant B gán branchB1 cho nhân viên Tenant B -> GET /api/users Tenant A không lộ thông tin Tenant B
        var createBRes = await ownerBClient.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Email = "cashier-b1@shopb.local",
            DisplayName = "Thu ngân Tenant B",
            Password = "Password@123",
            Role = "Cashier",
            BranchIds = [branchB1]
        });
        createBRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var tenantAList = await ownerAClient.GetFromJsonAsync<List<UserResponse>>("/api/users", _jsonOptions);
        tenantAList!.Should().NotContain(u => u.TenantId == OismsDatabaseSeeder.TenantBId || u.BranchIds.Contains(branchB1));
    }

    // =========================================================================
    // AC-04 & AC-05: GET /api/branches & POS Checkout enforcement
    // =========================================================================
    [Fact]
    public async Task AC04_AC05_GetBranchesAndCheckout_ShouldRestrictAssignedCashier_AndAllowUnassignedOrStaffOrOwner()
    {
        Guid branchA1;
        Guid warehouseA1;
        Guid branchA2 = Guid.NewGuid();
        Guid warehouseA2 = Guid.NewGuid();
        Guid supplierA;
        Guid ownerAId;
        Guid staffAId;
        Guid skuAId;
        string skuCode;
        decimal retailPrice;

        await using (var db = PostgresTestEnvironment.CreateRuntimeDbContext(new TestTenant()))
        {
            branchA1 = await db.Branches.IgnoreQueryFilters()
                .Where(b => b.TenantId == OismsDatabaseSeeder.TenantAId)
                .Select(b => b.Id)
                .SingleAsync();
            warehouseA1 = await db.Warehouses.IgnoreQueryFilters()
                .Where(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branchA1)
                .Select(w => w.Id)
                .SingleAsync();
            supplierA = await db.Suppliers.IgnoreQueryFilters()
                .Where(s => s.TenantId == OismsDatabaseSeeder.TenantAId)
                .Select(s => s.Id)
                .SingleAsync();
            ownerAId = await db.Users.IgnoreQueryFilters()
                .Where(u => u.TenantId == OismsDatabaseSeeder.TenantAId && u.Role == "Owner")
                .Select(u => u.Id)
                .SingleAsync();
            staffAId = await db.Users.IgnoreQueryFilters()
                .Where(u => u.TenantId == OismsDatabaseSeeder.TenantAId && u.Role == "Staff")
                .Select(u => u.Id)
                .SingleAsync();
            var sku = await db.ProductVariants.IgnoreQueryFilters()
                .FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);
            skuAId = sku.Id;
            skuCode = sku.SKU;
            retailPrice = sku.RetailPrice;
        }

        // Tạo chi nhánh A2 + kho A2 + nhập tồn cho cả A1 và A2
        await using (var dbA = PostgresTestEnvironment.CreateRuntimeDbContext(new TestTenant(OismsDatabaseSeeder.TenantAId)))
        {
            dbA.Branches.Add(new Branch
            {
                Id = branchA2,
                TenantId = OismsDatabaseSeeder.TenantAId,
                Name = "Chi nhánh Quận 7",
                IsActive = true
            });
            dbA.Warehouses.Add(new Warehouse
            {
                Id = warehouseA2,
                TenantId = OismsDatabaseSeeder.TenantAId,
                BranchId = branchA2,
                Name = "Kho Quận 7",
                IsActive = true
            });
            await dbA.SaveChangesAsync();
        }

        var tenantA = new TestTenant(OismsDatabaseSeeder.TenantAId);
        await using (var stockDb = PostgresTestEnvironment.CreateRuntimeDbContext(tenantA))
        {
            await PriceSeed.SyncFromLegacyColumnsAsync(stockDb);
            await PurchaseOrderSeed.ReceiveStockAsync(stockDb, tenantA, branchA1, warehouseA1, supplierA, ownerAId, (skuAId, 10m, 150000m));
            await PurchaseOrderSeed.ReceiveStockAsync(stockDb, tenantA, branchA2, warehouseA2, supplierA, ownerAId, (skuAId, 10m, 150000m));
        }

        using var ownerClient = await CreateClientForUserAsync("owner@shopa.local");

        // Tạo Cashier bị giới hạn chỉ Branch A1
        var createRestrictedCashier = await ownerClient.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            Email = "restricted-cashier@shopa.local",
            DisplayName = "Thu ngân chỉ Q1",
            Password = "Password@123",
            Role = "Cashier",
            BranchIds = [branchA1]
        });
        createRestrictedCashier.StatusCode.Should().Be(HttpStatusCode.Created);

        // Gán Branch A1 cho Staff để kiểm tra quyết định: Staff được lưu gán nhưng chưa bị lọc
        var staffUpdate = await ownerClient.PutAsJsonAsync($"/api/users/{staffAId}", new UpdateUserRequest
        {
            DisplayName = "Nhân viên kho shop A",
            Role = "Staff",
            IsActive = true,
            BranchIds = [branchA1]
        });
        staffUpdate.StatusCode.Should().Be(HttpStatusCode.OK);
        var staffUpdatedDto = await staffUpdate.Content.ReadFromJsonAsync<UserResponse>(_jsonOptions);
        staffUpdatedDto!.BranchIds.Should().BeEquivalentTo([branchA1]);

        using var restrictedCashierClient = await CreateClientForUserAsync("restricted-cashier@shopa.local", "Password@123");
        using var unassignedCashierClient = await CreateClientForUserAsync("cashier@shopa.local");
        using var staffClient = await CreateClientForUserAsync("staff@shopa.local");

        // --- AC-04: GET /api/branches ---
        var restrictedBranches = await restrictedCashierClient.GetFromJsonAsync<List<BranchResponse>>("/api/branches", _jsonOptions);
        restrictedBranches!.Select(b => b.Id).Should().BeEquivalentTo([branchA1]);

        var unassignedBranches = await unassignedCashierClient.GetFromJsonAsync<List<BranchResponse>>("/api/branches", _jsonOptions);
        unassignedBranches!.Select(b => b.Id).Should().BeEquivalentTo([branchA1, branchA2]);

        var staffBranches = await staffClient.GetFromJsonAsync<List<BranchResponse>>("/api/branches", _jsonOptions);
        staffBranches!.Select(b => b.Id).Should().BeEquivalentTo([branchA1, branchA2]);

        var ownerBranches = await ownerClient.GetFromJsonAsync<List<BranchResponse>>("/api/branches", _jsonOptions);
        ownerBranches!.Select(b => b.Id).Should().BeEquivalentTo([branchA1, branchA2]);

        // --- AC-05: Checkout enforcement ---
        int beforeOrdersCount;
        int beforeLedgerCount;
        decimal beforeA2OnHand;
        decimal beforeA2Reserved;
        await using (var checkDb = PostgresTestEnvironment.CreateRuntimeDbContext(new TestTenant(OismsDatabaseSeeder.TenantAId)))
        {
            beforeOrdersCount = await checkDb.Orders.CountAsync();
            beforeLedgerCount = await checkDb.InventoryTransactions.CountAsync();
            var balA2 = await checkDb.InventoryBalances.SingleAsync(ib => ib.BranchId == branchA2 && ib.SkuId == skuAId);
            beforeA2OnHand = balA2.OnHand;
            beforeA2Reserved = balA2.Reserved;
        }

        PosCheckoutRequestDto BuildCheckoutDto(Guid targetBranchId) => new()
        {
            BranchId = targetBranchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPrice,
            RequestKey = Guid.NewGuid(),
            Items = [new OrderItemRequestDto { SkuId = skuAId, SkuCode = skuCode, Quantity = 1, UnitPrice = retailPrice }]
        };

        // 1. Cashier giới hạn Q1 gọi POST /api/orders/checkout ở branchA2 -> 403 BRANCH_NOT_ASSIGNED
        var aliasBlocked = await restrictedCashierClient.PostAsJsonAsync("/api/orders/checkout", BuildCheckoutDto(branchA2));
        aliasBlocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using (var doc = JsonDocument.Parse(await aliasBlocked.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("errorCode").GetString().Should().Be("BRANCH_NOT_ASSIGNED");
        }

        // 2. Cashier giới hạn Q1 gọi POST /api/pos/checkout ở branchA2 -> 403 BRANCH_NOT_ASSIGNED
        var canonicalBlocked = await restrictedCashierClient.PostAsJsonAsync("/api/pos/checkout", BuildCheckoutDto(branchA2));
        canonicalBlocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using (var doc = JsonDocument.Parse(await canonicalBlocked.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("errorCode").GetString().Should().Be("BRANCH_NOT_ASSIGNED");
        }

        // Xác minh không tạo đơn, không ghi ledger, không đổi tồn ở branchA2
        await using (var checkDb = PostgresTestEnvironment.CreateRuntimeDbContext(new TestTenant(OismsDatabaseSeeder.TenantAId)))
        {
            (await checkDb.Orders.CountAsync()).Should().Be(beforeOrdersCount);
            (await checkDb.InventoryTransactions.CountAsync()).Should().Be(beforeLedgerCount);
            var balA2 = await checkDb.InventoryBalances.SingleAsync(ib => ib.BranchId == branchA2 && ib.SkuId == skuAId);
            balA2.OnHand.Should().Be(beforeA2OnHand);
            balA2.Reserved.Should().Be(beforeA2Reserved);
        }

        // 3. Cashier giới hạn Q1 checkout ở branchA1 (được gán) -> 200 OK
        var allowedCheckout = await restrictedCashierClient.PostAsJsonAsync("/api/pos/checkout", BuildCheckoutDto(branchA1));
        allowedCheckout.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Cashier không gán chi nhánh nào checkout ở branchA2 -> 200 OK
        var unassignedCheckout = await unassignedCashierClient.PostAsJsonAsync("/api/orders/checkout", BuildCheckoutDto(branchA2));
        unassignedCheckout.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Staff (có gán Q1 nhưng chưa áp giới hạn cho Staff) checkout ở branchA2 -> 200 OK
        var staffCheckout = await staffClient.PostAsJsonAsync("/api/pos/checkout", BuildCheckoutDto(branchA2));
        staffCheckout.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
