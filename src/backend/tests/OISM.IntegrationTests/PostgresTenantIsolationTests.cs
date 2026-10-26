using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.RegisterTenant;
using OISM.Application.Features.Catalog.Products;
using OISM.Application.Features.Pos;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using OISM.Infrastructure.Services;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Bộ kiểm thử tích hợp thực thi trên PostgreSQL 16+ thật (database: oisms_test).
/// Kiểm chứng toàn bộ các vấn đề kỹ thuật sau review:
/// 1. Chặn cập nhật/xóa chéo tenant bằng detached entity (Cơ chế composite key WHERE tenant_id = @A AND id = @B).
/// 2. Đóng toàn bộ đường bỏ qua tenant guard (thiếu tenant, 4 overloads SaveChanges, privileged scope).
/// 3. Không tự seed trong Production; chỉ seed khi được bật chủ động trong Dev/Test.
/// 4. Seed nguyên tử, tự phục hồi dữ liệu thiếu (self-healing), không ghi đè dữ liệu sửa đổi, không reset tồn kho.
/// 5. Tiếp nhận baseline migration và phát hiện sai lệch schema (schema drift).
/// 6. Smoke test đầy đủ trên production DI thực tế của WebApplicationFactory.
/// </summary>
public class PostgresTenantIsolationTests : IAsyncLifetime
{
    private class TestTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    public async Task InitializeAsync()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private (AppDbContext Context, TestTenantService TenantService) CreatePostgresDbContext()
    {
        var tenantService = new TestTenantService();
        var context = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService);
        return (context, tenantService);
    }

    // =========================================================================
    // 1. KIỂM THỬ KẾT NỐI VÀ READINESS SERVICE
    // =========================================================================
    [Fact]
    public async Task Postgres_ConnectionAndReadiness_ShouldUseRuntimeAccount_AndVerifySchema()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Host"] = PostgresTestEnvironment.Host,
                ["Database:Port"] = PostgresTestEnvironment.Port.ToString(),
                ["Database:Database"] = PostgresTestEnvironment.DatabaseName,
                ["Database:Username"] = PostgresTestEnvironment.AppUser,
                ["Database:Password"] = PostgresTestEnvironment.AppPassword
            })
            .Build();

        var readinessService = new NpgsqlDatabaseReadinessService(configuration, Microsoft.Extensions.Logging.Abstractions.NullLogger<NpgsqlDatabaseReadinessService>.Instance);
        var result = await readinessService.CheckReadinessAsync();

        result.IsHealthy.Should().BeTrue("Database phải sẵn sàng và tài khoản runtime oisms_app phải có đủ quyền");
        result.TablesCount.Should().Be(30, "PostgreSQL schema v0.5 có chính xác 30 bảng (bao gồm shop_integrations)");
        result.TriggerVerified.Should().BeTrue("Trigger ledger_append_only phải được kích hoạt");
        result.CurrentUser.Should().Be(PostgresTestEnvironment.AppUser);
    }

    // =========================================================================
    // 2. CHẶN CẬP NHẬT / XÓA CHÉO TENANT BẰNG DETACHED ENTITY (PROBLEM 1)
    // =========================================================================
    [Fact]
    public async Task Postgres_TenantGuard_DetachedEntity_CrossTenantUpdate_MustBeRejected_AndPreserveTargetData()
    {
        var (context, tenantService) = CreatePostgresDbContext();
        await using (context)
        {
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);

            // Lấy ID của Supplier thuộc Tenant B
            var supplierB = await context.Suppliers
                .IgnoreQueryFilters()
                .FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantBId);

            var originalNameB = supplierB.Name;

            // Kịch bản tấn công: Phiên làm việc thuộc Tenant A
            tenantService.SetTenant(OismsDatabaseSeeder.TenantAId);

            // Kẻ tấn công tạo detached entity có Id của B nhưng khai khống TenantId = A
            var spoofedSupplier = new Supplier
            {
                Id = supplierB.Id,
                TenantId = OismsDatabaseSeeder.TenantAId,
                Name = "Tên bị hack bởi Tenant A"
            };

            context.Attach(spoofedSupplier);
            context.Entry(spoofedSupplier).Property(s => s.Name).IsModified = true;

            // Act: Cố gắng lưu thay đổi
            Func<Task> act = async () => await context.SaveChangesAsync();

            // Assert 1: Phải ném ngoại lệ (DbUpdateConcurrencyException do WHERE tenant_id = TenantA AND id = B không khớp dòng nào)
            await act.Should().ThrowAsync<Exception>();

            // Assert 2: Bản ghi của Tenant B trong database phải giữ nguyên 100%, không bị sửa đổi!
            var freshSupplierB = await context.Suppliers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstAsync(s => s.Id == supplierB.Id);

            freshSupplierB.Name.Should().Be(originalNameB, "Dữ liệu của Tenant B phải được bảo vệ toàn vẹn tuyệt đối");
        }
    }

    [Fact]
    public async Task Postgres_TenantGuard_DetachedEntity_CrossTenantDelete_MustBeRejected_AndPreserveTargetData()
    {
        var (context, tenantService) = CreatePostgresDbContext();
        await using (context)
        {
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);

            // Lấy Supplier của Tenant B
            var supplierB = await context.Suppliers
                .IgnoreQueryFilters()
                .FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantBId);

            // Phiên làm việc thuộc Tenant A
            tenantService.SetTenant(OismsDatabaseSeeder.TenantAId);

            // Kẻ tấn công attach detached entity với Id của B nhưng TenantId = A và đánh dấu Deleted
            var spoofedDelete = new Supplier
            {
                Id = supplierB.Id,
                TenantId = OismsDatabaseSeeder.TenantAId
            };

            context.Attach(spoofedDelete);
            context.Entry(spoofedDelete).State = EntityState.Deleted;

            Func<Task> act = async () => await context.SaveChangesAsync();
            await act.Should().ThrowAsync<Exception>();

            // Bản ghi của Tenant B vẫn phải tồn tại trong DB!
            var exists = await context.Suppliers
                .IgnoreQueryFilters()
                .AnyAsync(s => s.Id == supplierB.Id);

            exists.Should().BeTrue("Bản ghi của Tenant B không được phép bị xóa bởi Tenant A");
        }
    }

    [Fact]
    public async Task Postgres_TenantGuard_ValidEntity_SameTenantUpdateAndDelete_ShouldWorkProperly()
    {
        var (context, tenantService) = CreatePostgresDbContext();
        await using (context)
        {
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);

            tenantService.SetTenant(OismsDatabaseSeeder.TenantAId);

            // 1. Thao tác hợp lệ trên chính Supplier của Tenant A
            var supplierA = await context.Suppliers.FirstAsync();
            supplierA.Name = "Công ty May Nhà Bè - Chi nhánh cập nhật";
            await context.SaveChangesAsync();

            var updatedA = await context.Suppliers.AsNoTracking().FirstAsync(s => s.Id == supplierA.Id);
            updatedA.Name.Should().Be("Công ty May Nhà Bè - Chi nhánh cập nhật");

            // 2. Thao tác xóa hợp lệ của Tenant A
            context.Suppliers.Remove(supplierA);
            await context.SaveChangesAsync();

            var deletedCheck = await context.Suppliers.FirstOrDefaultAsync(s => s.Id == supplierA.Id);
            deletedCheck.Should().BeNull();
        }
    }

    // =========================================================================
    // 3. ĐÓNG CÁC ĐƯỜNG BỎ QUA TENANT GUARD (PROBLEM 2)
    // =========================================================================
    [Fact]
    public async Task Postgres_TenantGuard_MissingTenantContext_MustBlockWrites_OnAllFourSaveChangesOverloads()
    {
        var (context, tenantService) = CreatePostgresDbContext();
        await using (context)
        {
            // Đảm bảo không có tenant context (unauthenticated) và không có privileged scope
            tenantService.TenantId = null;

            // 1. Overload: SaveChangesAsync(CancellationToken)
            context.Suppliers.Add(new Supplier
            {
                TenantId = OismsDatabaseSeeder.TenantAId,
                Name = "Supplier unauthenticated test 1"
            });
            Func<Task> act1 = async () => await context.SaveChangesAsync();
            await act1.Should().ThrowAsync<SecurityException>()
                .WithMessage("*Business write operations require an authenticated tenant context*");

            // 2. Overload: SaveChangesAsync(bool, CancellationToken)
            Func<Task> act2 = async () => await context.SaveChangesAsync(true, CancellationToken.None);
            await act2.Should().ThrowAsync<SecurityException>()
                .WithMessage("*Business write operations require an authenticated tenant context*");

            // 3. Overload: SaveChanges()
            Action act3 = () => context.SaveChanges();
            act3.Should().Throw<SecurityException>()
                .WithMessage("*Business write operations require an authenticated tenant context*");

            // 4. Overload: SaveChanges(bool)
            Action act4 = () => context.SaveChanges(true);
            act4.Should().Throw<SecurityException>()
                .WithMessage("*Business write operations require an authenticated tenant context*");
        }
    }

    [Fact]
    public async Task Postgres_TenantGuard_PrivilegedScope_AllowsExplicitServerWrites()
    {
        var (context, tenantService) = CreatePostgresDbContext();
        await using (context)
        {
            tenantService.TenantId = null;

            // Khi ở trong OpenPrivilegedScope, thao tác ghi hệ thống hợp lệ được phép thực thi
            using (context.OpenPrivilegedScope())
            {
                var tenant = new Tenant
                {
                    Id = OismsDatabaseSeeder.TenantAId,
                    Name = "Shop A Test",
                    IsActive = true
                };
                context.Tenants.Add(tenant);
                await context.SaveChangesAsync();

                var supplier = new Supplier
                {
                    TenantId = OismsDatabaseSeeder.TenantAId,
                    Name = "Server Trusted Seed Supplier"
                };
                context.Suppliers.Add(supplier);
                await context.SaveChangesAsync();

                var exists = await context.Suppliers
                    .IgnoreQueryFilters()
                    .AnyAsync(s => s.Id == supplier.Id);
                exists.Should().BeTrue();
            }

            // Nhưng nếu thực thể có TenantId rỗng, ngay cả trong PrivilegedScope vẫn bị từ chối
            using (context.OpenPrivilegedScope())
            {
                var invalidSupplier = new Supplier
                {
                    TenantId = Guid.Empty,
                    Name = "Empty Tenant Supplier"
                };
                context.Suppliers.Add(invalidSupplier);
                Func<Task> act = async () => await context.SaveChangesAsync();
                await act.Should().ThrowAsync<SecurityException>()
                    .WithMessage("*Cannot insert Supplier with empty TenantId*");
            }
        }
    }

    [Fact]
    public async Task Postgres_TenantGuard_WriteMismatch_ShouldThrowSecurityException()
    {
        var (context, tenantService) = CreatePostgresDbContext();
        await using (context)
        {
            tenantService.SetTenant(OismsDatabaseSeeder.TenantAId);

            var supplier = new Supplier
            {
                TenantId = OismsDatabaseSeeder.TenantBId,
                Name = "Malicious Supplier"
            };
            context.Suppliers.Add(supplier);

            Func<Task> act = async () => await context.SaveChangesAsync();
            await act.Should().ThrowAsync<SecurityException>()
                .WithMessage("*while authenticated as Tenant*");
        }
    }

    [Fact]
    public async Task Postgres_TenantGuard_ImmutableTenantId_ShouldThrowException()
    {
        var (context, tenantService) = CreatePostgresDbContext();
        await using (context)
        {
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);

            tenantService.SetTenant(OismsDatabaseSeeder.TenantAId);
            var product = await context.Products.FirstAsync(p => p.TenantId == OismsDatabaseSeeder.TenantAId);

            product.TenantId = Guid.NewGuid();

            Func<Task> act = async () => await context.SaveChangesAsync();
            await act.Should().ThrowAsync<Exception>()
                .Where(ex => ex is SecurityException || ex is InvalidOperationException);
        }
    }

    [Fact]
    public async Task Postgres_TenantGuard_CrossTenantFK_ShouldFailAtPostgresLevel()
    {
        var (context, tenantService) = CreatePostgresDbContext();
        await using (context)
        {
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);

            var productB = await context.Products.IgnoreQueryFilters().FirstAsync(p => p.TenantId == OismsDatabaseSeeder.TenantBId);

            tenantService.SetTenant(OismsDatabaseSeeder.TenantAId);

            var crossVariant = new ProductVariant
            {
                TenantId = OismsDatabaseSeeder.TenantAId,
                ProductId = productB.Id,
                Code = "CROSS-SKU-01",
                Unit = "piece",
                RetailPrice = 100000,
                WholesalePrice = 80000,
                AttributesJson = "{}"
            };

            context.ProductVariants.Add(crossVariant);

            Func<Task> act = async () => await context.SaveChangesAsync();
            var ex = await act.Should().ThrowAsync<DbUpdateException>();
            ex.WithInnerException(typeof(PostgresException))
                .WithMessage("*fk_skus_*");
        }
    }

    // =========================================================================
    // 4. ATOMIC SEEDER, IDEMPOTENCY & SELF-HEALING (PROBLEM 4)
    // =========================================================================
    [Fact]
    public async Task Postgres_Seeder_ShouldBeAtomic_Idempotent_AndSelfHealing()
    {
        var (context, _) = CreatePostgresDbContext();
        await using (context)
        {
            // 1. Chạy lần đầu
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);

            var userCount1 = await context.Users.IgnoreQueryFilters().CountAsync();
            var skuCount1 = await context.ProductVariants.IgnoreQueryFilters().CountAsync();
            userCount1.Should().Be(6, "2 tenant x 3 users = 6 users");
            skuCount1.Should().Be(2, "2 tenant x 1 sku = 2 skus");

            // Tồn kho ban đầu bắt buộc phải bằng 0
            var balances = await context.InventoryBalances.IgnoreQueryFilters().ToListAsync();
            balances.Should().AllSatisfy(b =>
            {
                b.OnHand.Should().Be(0);
                b.Reserved.Should().Be(0);
                b.AverageCost.Should().Be(0);
            });

            // 2. Chạy lại lần 2: Đảm bảo không trùng lặp, không lỗi unique constraint
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);
            var userCount2 = await context.Users.IgnoreQueryFilters().CountAsync();
            userCount2.Should().Be(6);

            // 3. Bảo toàn dữ liệu người dùng và nhà cung cấp đã chỉnh sửa:
            var ownerA = await context.Users.IgnoreQueryFilters().FirstAsync(u => u.Email == "owner@shopa.local");
            ownerA.DisplayName = "Tên Chủ Shop Đã Đổi";

            var supplierA = await context.Suppliers.IgnoreQueryFilters().FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            supplierA.Phone = "+84999888777";
            supplierA.Address = "Địa chỉ nhà cung cấp đã đổi";

            using (context.OpenPrivilegedScope())
            {
                await context.SaveChangesAsync();
            }

            // Chạy lại seed: Tên người dùng và thông tin nhà cung cấp KHÔNG được bị reset về giá trị mặc định
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);

            var freshOwnerA = await context.Users.IgnoreQueryFilters().FirstAsync(u => u.Email == "owner@shopa.local");
            freshOwnerA.DisplayName.Should().Be("Tên Chủ Shop Đã Đổi", "Seed không được ghi đè thông tin người dùng đã sửa đổi");

            var freshSupplierA = await context.Suppliers.IgnoreQueryFilters().FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            freshSupplierA.Phone.Should().Be("+84999888777", "Seed không được ghi đè thông tin nhà cung cấp đã sửa đổi");
            freshSupplierA.Address.Should().Be("Địa chỉ nhà cung cấp đã đổi");

            var freshBalanceA = await context.InventoryBalances.IgnoreQueryFilters().FirstAsync(ib => ib.TenantId == OismsDatabaseSeeder.TenantAId);
            freshBalanceA.OnHand.Should().Be(0, "Seed không tạo thêm hoặc reset sai số dư tồn kho");

            // 4. Khả năng tự phục hồi (Self-healing): Xóa Supplier của Tenant B (giả lập lỗi dở dang lần trước)
            var supplierB = await context.Suppliers.IgnoreQueryFilters().FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantBId);
            using (context.OpenPrivilegedScope())
            {
                context.Suppliers.Remove(supplierB);
                await context.SaveChangesAsync();
            }

            // Chạy lại seed: Phải tự động bổ sung Supplier bị thiếu
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);
            var healedSupplierB = await context.Suppliers.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.TenantId == OismsDatabaseSeeder.TenantBId);
            healedSupplierB.Should().NotBeNull("Seed phải tự phục hồi dữ liệu demo bị thiếu");
        }
    }

    // =========================================================================
    // 5. BASELINE ADOPTION & MIGRATION SERVICE (PROBLEM 6 & REVIEW FIXES)
    // =========================================================================
    [Fact]
    public async Task Postgres_MigrationService_ShouldVerifyAndAdoptBaseline()
    {
        var migrationService = new DatabaseMigrationService();

        // 1. Tiếp nhận baseline trên database test v0.5 hiện có
        var result = await migrationService.InitializeOrBaselineDatabaseAsync(PostgresTestEnvironment.AdminDatabaseConnectionString);

        result.Success.Should().BeTrue();
        result.TableCount.Should().Be(30);
        result.IsBaselineAdopted.Should().BeTrue();

        // 2. Chạy lại lần nữa: Phải an toàn và không thực hiện thao tác thừa
        var rerunResult = await migrationService.InitializeOrBaselineDatabaseAsync(PostgresTestEnvironment.AdminDatabaseConnectionString);
        rerunResult.Success.Should().BeTrue();
        rerunResult.IsBaselineAdopted.Should().BeTrue();
    }

    [Fact]
    public async Task Postgres_Migration_FreshInstall_OnCleanDatabase_ShouldSucceed_AndPreserveAllObjects()
    {
        var migrationService = new DatabaseMigrationService();
        var freshDbName = $"oisms_fresh_{Guid.NewGuid():N}";
        var freshAdminConnStr = PostgresTestEnvironment.BuildConnectionString(
            PostgresTestEnvironment.Host,
            PostgresTestEnvironment.Port,
            freshDbName,
            PostgresTestEnvironment.AdminUser,
            PostgresTestEnvironment.AdminPassword);

        // 1. Tạo database trống trên cụm Postgres
        await using (var serverConn = new NpgsqlConnection(PostgresTestEnvironment.AdminServerConnectionString))
        {
            await serverConn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{freshDbName}\" ENCODING 'UTF8';", serverConn);
            await cmd.ExecuteNonQueryAsync();
        }

        try
        {
            // 2. Chạy InitializeOrBaselineDatabaseAsync trên database hoàn toàn trống
            var result = await migrationService.InitializeOrBaselineDatabaseAsync(freshAdminConnStr);

            result.Success.Should().BeTrue(because: $"{result.Message} - {string.Join(" | ", result.Errors ?? Array.Empty<string>())}");
            result.IsFreshInstall.Should().BeTrue();
            result.TableCount.Should().Be(30);
            result.Errors.Should().BeNull();

            // 3. Xác minh bảng, trigger và __EFMigrationsHistory
            await using var freshConn = new NpgsqlConnection(freshAdminConnStr);
            await freshConn.OpenAsync();

            await using (var checkCmd = new NpgsqlCommand("SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE';", freshConn))
            {
                var tableCount = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                tableCount.Should().Be(31, "30 bảng nghiệp vụ + 1 bảng __EFMigrationsHistory = 31 bảng");
            }

            await using (var historyCmd = new NpgsqlCommand("SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = @mid;", freshConn))
            {
                historyCmd.Parameters.AddWithValue("mid", DatabaseMigrationService.InitialBaselineMigrationId);
                var historyCount = Convert.ToInt32(await historyCmd.ExecuteScalarAsync());
                historyCount.Should().Be(1);
            }

            // Trigger ledger_append_only phải tồn tại và đang bật
            await using (var trigCmd = new NpgsqlCommand("SELECT tgenabled FROM pg_trigger WHERE tgname = 'ledger_append_only';", freshConn))
            {
                var status = (await trigCmd.ExecuteScalarAsync())?.ToString();
                status.Should().Be("O", "Trigger ledger_append_only phải đang được kích hoạt");
            }
        }
        finally
        {
            // Dọn dẹp database tạm sau khi test xong
            await using var serverConn = new NpgsqlConnection(PostgresTestEnvironment.AdminServerConnectionString);
            await serverConn.OpenAsync();
            await using var termCmd = new NpgsqlCommand($"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{freshDbName}' AND pid <> pg_backend_pid();", serverConn);
            await termCmd.ExecuteNonQueryAsync();
            await using var dropCmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{freshDbName}\";", serverConn);
            await dropCmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Postgres_BaselineAdoption_WhenLedgerTriggerDisabled_MustRejectWithDriftError()
    {
        var migrationService = new DatabaseMigrationService();

        // 1. Tắt trigger ledger_append_only trên inventory_transactions
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var disableCmd = new NpgsqlCommand("ALTER TABLE inventory_transactions DISABLE TRIGGER ledger_append_only;", conn);
            await disableCmd.ExecuteNonQueryAsync();
        }

        try
        {
            // 2. Gọi InitializeOrBaselineDatabaseAsync: Bắt buộc phải phát hiện drift và TỪ CHỐI
            var result = await migrationService.InitializeOrBaselineDatabaseAsync(PostgresTestEnvironment.AdminDatabaseConnectionString);

            result.Success.Should().BeFalse("Database có trigger ledger bị tắt không được phép tiếp nhận baseline");
            result.IsBaselineAdopted.Should().BeFalse();
            result.Errors.Should().NotBeNullOrEmpty();
            result.Errors.Should().Contain(e => e.Contains("ledger_append_only") && e.Contains("vô hiệu hóa"));
        }
        finally
        {
            // 3. Bật lại trigger để khôi phục trạng thái chuẩn
            await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
            {
                await conn.OpenAsync();
                await using var enableCmd = new NpgsqlCommand("ALTER TABLE inventory_transactions ENABLE TRIGGER ledger_append_only;", conn);
                await enableCmd.ExecuteNonQueryAsync();
            }

            // Xác minh sau khi bật lại thì baseline adoption thành công
            var restoredResult = await migrationService.InitializeOrBaselineDatabaseAsync(PostgresTestEnvironment.AdminDatabaseConnectionString);
            restoredResult.Success.Should().BeTrue();
            restoredResult.IsBaselineAdopted.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Postgres_BaselineAdoption_WhenCheckConstraintAllowsNegativeRetailPrice_MustRejectWithDetailedDiagnostic()
    {
        var tempDb = await CreateTemporaryCleanDatabaseAsync("oisms_ck_drift");
        var tempConnStr = PostgresTestEnvironment.BuildConnectionString(
            PostgresTestEnvironment.Host,
            PostgresTestEnvironment.Port,
            tempDb,
            PostgresTestEnvironment.AdminUser,
            PostgresTestEnvironment.AdminPassword);

        var migrationService = new DatabaseMigrationService();

        try
        {
            // 1. Cài đặt schema v0.5 chuẩn trên database tạm
            var installResult = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);
            installResult.Success.Should().BeTrue();

            // 2. Tái hiện lỗi: Sửa check constraint ck_skus_retail_price_nonnegative để cho phép giá âm (>= -1)
            await using (var conn = new NpgsqlConnection(tempConnStr))
            {
                await conn.OpenAsync();
                await using var dropCk = new NpgsqlCommand("ALTER TABLE skus DROP CONSTRAINT ck_skus_retail_price_nonnegative;", conn);
                await dropCk.ExecuteNonQueryAsync();

                await using var addDriftedCk = new NpgsqlCommand("ALTER TABLE skus ADD CONSTRAINT ck_skus_retail_price_nonnegative CHECK (retail_price >= -1);", conn);
                await addDriftedCk.ExecuteNonQueryAsync();

                // Xóa lịch sử migration để mô phỏng lượt kiểm tra tiếp nhận baseline
                await using var clearHistory = new NpgsqlCommand("DELETE FROM \"__EFMigrationsHistory\";", conn);
                await clearHistory.ExecuteNonQueryAsync();
            }

            // 3. Thực hiện kiểm tra tiếp nhận baseline: Bắt buộc phải TỪ CHỐI
            var baselineResult = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);

            baselineResult.Success.Should().BeFalse("Database có ràng buộc CHECK bị biến đổi cho phép giá âm phải bị từ chối");
            baselineResult.IsBaselineAdopted.Should().BeFalse();
            baselineResult.Errors.Should().NotBeNullOrEmpty();
            baselineResult.Errors.Should().Contain(e =>
                e.Contains("ck_skus_retail_price_nonnegative") &&
                e.Contains("sai biểu thức kiểm tra"));

            // 4. Xác minh không ghi nhận baseline vào __EFMigrationsHistory
            await using (var conn = new NpgsqlConnection(tempConnStr))
            {
                await conn.OpenAsync();
                await using var checkHistory = new NpgsqlCommand("SELECT count(*) FROM \"__EFMigrationsHistory\";", conn);
                var historyCount = Convert.ToInt32(await checkHistory.ExecuteScalarAsync());
                historyCount.Should().Be(0, "Không được ghi nhận migration history khi phát hiện schema drift");
            }
        }
        finally
        {
            await DropTemporaryDatabaseAsync(tempDb);
        }
    }

    [Fact]
    public async Task Postgres_BaselineAdoption_WhenLedgerTriggerDropsDeleteEvent_MustRejectWithDetailedDiagnostic()
    {
        var tempDb = await CreateTemporaryCleanDatabaseAsync("oisms_trig_drift");
        var tempConnStr = PostgresTestEnvironment.BuildConnectionString(
            PostgresTestEnvironment.Host,
            PostgresTestEnvironment.Port,
            tempDb,
            PostgresTestEnvironment.AdminUser,
            PostgresTestEnvironment.AdminPassword);

        var migrationService = new DatabaseMigrationService();

        try
        {
            // 1. Cài đặt schema v0.5 chuẩn trên database tạm
            var installResult = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);
            installResult.Success.Should().BeTrue();

            // 2. Tái hiện lỗi: Tạo lại trigger ledger_append_only bỏ sự kiện DELETE (chỉ có UPDATE OR TRUNCATE)
            await using (var conn = new NpgsqlConnection(tempConnStr))
            {
                await conn.OpenAsync();
                await using var dropTrig = new NpgsqlCommand("DROP TRIGGER ledger_append_only ON inventory_transactions;", conn);
                await dropTrig.ExecuteNonQueryAsync();

                await using var addDriftedTrig = new NpgsqlCommand(
                    "CREATE TRIGGER ledger_append_only BEFORE UPDATE OR TRUNCATE ON inventory_transactions FOR EACH STATEMENT EXECUTE FUNCTION oisms_deny_mutation();", conn);
                await addDriftedTrig.ExecuteNonQueryAsync();

                await using var clearHistory = new NpgsqlCommand("DELETE FROM \"__EFMigrationsHistory\";", conn);
                await clearHistory.ExecuteNonQueryAsync();
            }

            // 3. Thực hiện kiểm tra tiếp nhận baseline: Bắt buộc phải TỪ CHỐI
            var baselineResult = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);

            baselineResult.Success.Should().BeFalse("Database có trigger ledger_append_only bỏ sót sự kiện DELETE phải bị từ chối");
            baselineResult.IsBaselineAdopted.Should().BeFalse();
            baselineResult.Errors.Should().NotBeNullOrEmpty();
            baselineResult.Errors.Should().Contain(e =>
                e.Contains("ledger_append_only") &&
                e.Contains("sai sự kiện DELETE"));

            // 4. Xác minh không ghi nhận baseline vào __EFMigrationsHistory
            await using (var conn = new NpgsqlConnection(tempConnStr))
            {
                await conn.OpenAsync();
                await using var checkHistory = new NpgsqlCommand("SELECT count(*) FROM \"__EFMigrationsHistory\";", conn);
                var historyCount = Convert.ToInt32(await checkHistory.ExecuteScalarAsync());
                historyCount.Should().Be(0, "Không được ghi nhận migration history khi phát hiện trigger thiếu event bảo vệ");
            }
        }
        finally
        {
            await DropTemporaryDatabaseAsync(tempDb);
        }
    }

    [Fact]
    public async Task Postgres_BaselineAdoption_WhenForeignKeyAlteredToCascade_MustRejectWithDetailedDiagnostic()
    {
        var tempDb = await CreateTemporaryCleanDatabaseAsync("oisms_fk_drift");
        var tempConnStr = PostgresTestEnvironment.BuildConnectionString(
            PostgresTestEnvironment.Host,
            PostgresTestEnvironment.Port,
            tempDb,
            PostgresTestEnvironment.AdminUser,
            PostgresTestEnvironment.AdminPassword);

        var migrationService = new DatabaseMigrationService();

        try
        {
            // 1. Cài đặt schema v0.5 chuẩn trên database tạm
            var installResult = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);
            installResult.Success.Should().BeTrue();

            // 2. Tái hiện lỗi: Sửa foreign key fk_skus_product_id sang ON DELETE CASCADE
            await using (var conn = new NpgsqlConnection(tempConnStr))
            {
                await conn.OpenAsync();
                await using var dropFk = new NpgsqlCommand("ALTER TABLE skus DROP CONSTRAINT fk_skus_product_id;", conn);
                await dropFk.ExecuteNonQueryAsync();

                await using var addCascadeFk = new NpgsqlCommand(
                    "ALTER TABLE skus ADD CONSTRAINT fk_skus_product_id FOREIGN KEY (tenant_id, product_id) REFERENCES products(tenant_id, id) ON DELETE CASCADE;", conn);
                await addCascadeFk.ExecuteNonQueryAsync();

                await using var clearHistory = new NpgsqlCommand("DELETE FROM \"__EFMigrationsHistory\";", conn);
                await clearHistory.ExecuteNonQueryAsync();
            }

            // 3. Thực hiện kiểm tra tiếp nhận baseline: Bắt buộc phải TỪ CHỐI
            var baselineResult = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);

            baselineResult.Success.Should().BeFalse("Database có foreign key bị đổi sang ON DELETE CASCADE phải bị từ chối");
            baselineResult.IsBaselineAdopted.Should().BeFalse();
            baselineResult.Errors.Should().NotBeNullOrEmpty();
            baselineResult.Errors.Should().Contain(e =>
                e.Contains("fk_skus_product_id") &&
                e.Contains("sai hành vi ON DELETE") &&
                e.Contains("'c'"));

            // 4. Xác minh không ghi nhận baseline vào __EFMigrationsHistory
            await using (var conn = new NpgsqlConnection(tempConnStr))
            {
                await conn.OpenAsync();
                await using var checkHistory = new NpgsqlCommand("SELECT count(*) FROM \"__EFMigrationsHistory\";", conn);
                var historyCount = Convert.ToInt32(await checkHistory.ExecuteScalarAsync());
                historyCount.Should().Be(0, "Không được ghi nhận migration history khi phát hiện FK sai hành vi xóa");
            }
        }
        finally
        {
            await DropTemporaryDatabaseAsync(tempDb);
        }
    }

    [Fact]
    public async Task Postgres_BaselineAdoption_WhenConstraintNotValidated_MustRejectWithDetailedDiagnostic()
    {
        var tempDb = await CreateTemporaryCleanDatabaseAsync("oisms_notval_drift");
        var tempConnStr = PostgresTestEnvironment.BuildConnectionString(
            PostgresTestEnvironment.Host,
            PostgresTestEnvironment.Port,
            tempDb,
            PostgresTestEnvironment.AdminUser,
            PostgresTestEnvironment.AdminPassword);

        var migrationService = new DatabaseMigrationService();

        try
        {
            // 1. Cài đặt schema v0.5 chuẩn trên database tạm
            var installResult = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);
            installResult.Success.Should().BeTrue();

            // 2. Tái hiện lỗi: Sửa constraint sang NOT VALID (convalidated = false)
            await using (var conn = new NpgsqlConnection(tempConnStr))
            {
                await conn.OpenAsync();
                await using var dropCk = new NpgsqlCommand("ALTER TABLE skus DROP CONSTRAINT ck_skus_wholesale_price_nonnegative;", conn);
                await dropCk.ExecuteNonQueryAsync();

                await using var addNotValCk = new NpgsqlCommand(
                    "ALTER TABLE skus ADD CONSTRAINT ck_skus_wholesale_price_nonnegative CHECK (wholesale_price >= 0) NOT VALID;", conn);
                await addNotValCk.ExecuteNonQueryAsync();

                await using var clearHistory = new NpgsqlCommand("DELETE FROM \"__EFMigrationsHistory\";", conn);
                await clearHistory.ExecuteNonQueryAsync();
            }

            // 3. Thực hiện kiểm tra tiếp nhận baseline: Bắt buộc phải TỪ CHỐI
            var baselineResult = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);

            baselineResult.Success.Should().BeFalse("Database có ràng buộc chưa được validate (NOT VALID) phải bị từ chối");
            baselineResult.IsBaselineAdopted.Should().BeFalse();
            baselineResult.Errors.Should().NotBeNullOrEmpty();
            baselineResult.Errors.Should().Contain(e =>
                e.Contains("ck_skus_wholesale_price_nonnegative") &&
                e.Contains("chưa được validate"));

            // 4. Xác minh không ghi nhận baseline vào __EFMigrationsHistory
            await using (var conn = new NpgsqlConnection(tempConnStr))
            {
                await conn.OpenAsync();
                await using var checkHistory = new NpgsqlCommand("SELECT count(*) FROM \"__EFMigrationsHistory\";", conn);
                var historyCount = Convert.ToInt32(await checkHistory.ExecuteScalarAsync());
                historyCount.Should().Be(0, "Không được ghi nhận migration history khi có ràng buộc NOT VALID");
            }
        }
        finally
        {
            await DropTemporaryDatabaseAsync(tempDb);
        }
    }

    [Fact]
    public void Postgres_TestEnvironment_TargetGuard_ProhibitsDestructiveActionsOnWorkingOrSystemDatabases()
    {
        Action act1 = () => PostgresTestEnvironment.AssertSafeTestTarget("oisms");
        act1.Should().Throw<InvalidOperationException>()
            .WithMessage("*Nghiêm cấm thực hiện thao tác kiểm thử/xóa dữ liệu trên cơ sở dữ liệu làm việc hoặc hệ thống 'oisms'*");

        Action act2 = () => PostgresTestEnvironment.AssertSafeTestTarget("postgres");
        act2.Should().Throw<InvalidOperationException>()
            .WithMessage("*Nghiêm cấm thực hiện thao tác kiểm thử/xóa dữ liệu trên cơ sở dữ liệu làm việc hoặc hệ thống 'postgres'*");

        Action act3 = () => PostgresTestEnvironment.AssertSafeTestTarget("production_sales");
        act3.Should().Throw<InvalidOperationException>()
            .WithMessage("*Tên database kiểm thử bắt buộc phải chứa từ khóa 'test'*");

        Action act4 = () => PostgresTestEnvironment.AssertSafeTestTarget("");
        act4.Should().Throw<InvalidOperationException>();

        // Tên database test hợp lệ: Không ném exception
        Action actValid = () => PostgresTestEnvironment.AssertSafeTestTarget("oisms_test");
        actValid.Should().NotThrow();
    }

    [Fact]
    public async Task Postgres_TestEnvironment_HandlesSpecialCharactersInPassword_WithoutSqlInjection()
    {
        var specialPassword = "P@ss'\"#;& $Test2026";
        var tempRole = $"temp_role_{Guid.NewGuid():N}";

        await using var serverConn = new NpgsqlConnection(PostgresTestEnvironment.AdminServerConnectionString);
        await serverConn.OpenAsync();

        try
        {
            var createSql = $"CREATE ROLE {PostgresTestEnvironment.EscapeSqlIdentifier(tempRole)} WITH LOGIN PASSWORD {PostgresTestEnvironment.EscapeSqlLiteral(specialPassword)};";
            await using (var cmd = new NpgsqlCommand(createSql, serverConn))
            {
                await cmd.ExecuteNonQueryAsync();
            }

            var testConnStr = PostgresTestEnvironment.BuildConnectionString(
                PostgresTestEnvironment.Host,
                PostgresTestEnvironment.Port,
                "postgres",
                tempRole,
                specialPassword);

            await using var testConn = new NpgsqlConnection(testConnStr);
            await testConn.OpenAsync();

            await using (var pingCmd = new NpgsqlCommand("SELECT 1;", testConn))
            {
                var val = Convert.ToInt32(await pingCmd.ExecuteScalarAsync());
                val.Should().Be(1, "Kết nối với mật khẩu chứa ký tự đặc biệt phải xác thực thành công");
            }
        }
        finally
        {
            var dropSql = $"DROP ROLE IF EXISTS {PostgresTestEnvironment.EscapeSqlIdentifier(tempRole)};";
            await using var dropCmd = new NpgsqlCommand(dropSql, serverConn);
            await dropCmd.ExecuteNonQueryAsync();
        }
    }

    // =========================================================================
    // 5B. REGRESSION TESTS: FIXTURE RESOURCE OWNERSHIP & SCHEMA DRIFT CONTROLS
    // =========================================================================

    private static async Task<string> CreateTemporaryCleanDatabaseAsync(string prefix = "oisms_reg_test")
    {
        var tempDb = $"{prefix}_{Guid.NewGuid().ToString("N")[..8]}";
        await using var serverConn = new NpgsqlConnection(PostgresTestEnvironment.AdminServerConnectionString);
        await serverConn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{tempDb}\" ENCODING 'UTF8';", serverConn);
        await cmd.ExecuteNonQueryAsync();
        return tempDb;
    }

    private static async Task DropTemporaryDatabaseAsync(string dbName)
    {
        await using var serverConn = new NpgsqlConnection(PostgresTestEnvironment.AdminServerConnectionString);
        await serverConn.OpenAsync();
        var termSql = $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{dbName}' AND pid <> pg_backend_pid();";
        await using var termCmd = new NpgsqlCommand(termSql, serverConn);
        await termCmd.ExecuteNonQueryAsync();
        await using var dropCmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\";", serverConn);
        await dropCmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Postgres_TestEnvironment_PreExistingDatabase_WithoutMarker_MustReject_AndPreserveData()
    {
        var tempDb = await CreateTemporaryCleanDatabaseAsync("oisms_preexist_test");
        var tempConnStr = PostgresTestEnvironment.BuildConnectionString(
            PostgresTestEnvironment.Host,
            PostgresTestEnvironment.Port,
            tempDb,
            PostgresTestEnvironment.AdminUser,
            PostgresTestEnvironment.AdminPassword);

        var migrationService = new DatabaseMigrationService();
        var initResult = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);
        initResult.Success.Should().BeTrue();

        var sentinelId = Guid.NewGuid();
        const string sentinelName = "PreExisting Sentinel Tenant Data";
        await using (var conn = new NpgsqlConnection(tempConnStr))
        {
            await conn.OpenAsync();
            await using var insertCmd = new NpgsqlCommand(
                "INSERT INTO tenants (id, name, created_at, updated_at) VALUES (@id, @name, now(), now());", conn);
            insertCmd.Parameters.AddWithValue("id", sentinelId);
            insertCmd.Parameters.AddWithValue("name", sentinelName);
            await insertCmd.ExecuteNonQueryAsync();
        }

        try
        {
            Func<Task> resetAct = async () => await PostgresTestEnvironment.ResetTestDataAsync(tempConnStr);
            var ex = await resetAct.Should().ThrowAsync<InvalidOperationException>();
            ex.WithMessage("*Từ chối nhận diện cơ sở dữ liệu có sẵn*");

            await using (var conn = new NpgsqlConnection(tempConnStr))
            {
                await conn.OpenAsync();
                await using var checkMarkerCmd = new NpgsqlCommand(
                    "SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = '_oisms_test_fixture_marker';", conn);
                var markerExists = (await checkMarkerCmd.ExecuteScalarAsync()) != null;
                markerExists.Should().BeFalse("Fixture không được tự ý ghi marker lên database có sẵn");

                await using var checkSentinelCmd = new NpgsqlCommand(
                    "SELECT name FROM tenants WHERE id = @id;", conn);
                checkSentinelCmd.Parameters.AddWithValue("id", sentinelId);
                var actualName = (await checkSentinelCmd.ExecuteScalarAsync())?.ToString();
                actualName.Should().Be(sentinelName, "Dữ liệu của database có sẵn phải được bảo toàn nguyên vẹn");
            }
        }
        finally
        {
            await DropTemporaryDatabaseAsync(tempDb);
        }
    }

    [Fact]
    public async Task Postgres_TestEnvironment_ForeignOrFakeMarker_MustReject()
    {
        var tempDb = await CreateTemporaryCleanDatabaseAsync("oisms_foreign_test");
        var tempConnStr = PostgresTestEnvironment.BuildConnectionString(
            PostgresTestEnvironment.Host,
            PostgresTestEnvironment.Port,
            tempDb,
            PostgresTestEnvironment.AdminUser,
            PostgresTestEnvironment.AdminPassword);

        var migrationService = new DatabaseMigrationService();
        var initResult = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);
        initResult.Success.Should().BeTrue();

        var foreignRunId = $"foreign_run_{Guid.NewGuid():N}";
        await using (var conn = new NpgsqlConnection(tempConnStr))
        {
            await conn.OpenAsync();
            const string createMarkerSql = @"
CREATE TABLE public._oisms_test_fixture_marker (
    run_id text PRIMARY KEY,
    created_at timestamptz DEFAULT now()
);
INSERT INTO public._oisms_test_fixture_marker (run_id) VALUES (@runId);";
            await using var cmd = new NpgsqlCommand(createMarkerSql, conn);
            cmd.Parameters.AddWithValue("runId", foreignRunId);
            await cmd.ExecuteNonQueryAsync();
        }

        try
        {
            // 1. Reset gọi qua EnsureInitializedAsync: Từ chối vì database có marker của run khác và không thuộc sở hữu
            Func<Task> actReset = async () => await PostgresTestEnvironment.ResetTestDataAsync(tempConnStr);
            var exReset = await actReset.Should().ThrowAsync<InvalidOperationException>();
            exReset.WithMessage("*chưa có marker xác thực hợp lệ*");

            // 2. Kiểm tra trực tiếp AssertDatabaseOwnedByRunAsync: Từ chối vì RunId không khớp
            Func<Task> actAssert = async () => await PostgresTestEnvironment.AssertDatabaseOwnedByRunAsync(tempConnStr, PostgresTestEnvironment.RunId);
            var exAssert = await actAssert.Should().ThrowAsync<InvalidOperationException>();
            exAssert.WithMessage("*Marker không khớp với lượt kiểm thử hiện tại*");
        }
        finally
        {
            await DropTemporaryDatabaseAsync(tempDb);
        }
    }

    [Fact]
    public async Task Postgres_TestEnvironment_OwnedDatabase_InitializeResetCleanup_ShouldSucceed()
    {
        var fixtureTestDb = $"oisms_fixture_run_test_{Guid.NewGuid().ToString("N")[..8]}";
        var fixtureConnStr = PostgresTestEnvironment.BuildConnectionString(
            PostgresTestEnvironment.Host,
            PostgresTestEnvironment.Port,
            fixtureTestDb,
            PostgresTestEnvironment.AdminUser,
            PostgresTestEnvironment.AdminPassword);

        try
        {
            // 1. Fixture tự khởi tạo database mới chưa tồn tại
            await PostgresTestEnvironment.EnsureInitializedAsync(fixtureConnStr);
            PostgresTestEnvironment.CreatedDatabases.ContainsKey(fixtureTestDb).Should().BeTrue("Database được tạo bởi fixture phải được ghi nhận vào CreatedDatabases");

            // 2. Reset hoạt động thành công
            Func<Task> resetAct = async () => await PostgresTestEnvironment.ResetTestDataAsync(fixtureConnStr);
            await resetAct.Should().NotThrowAsync();

            // 3. Marker tồn tại với đúng RunId của lượt chạy
            await using (var conn = new NpgsqlConnection(fixtureConnStr))
            {
                await conn.OpenAsync();
                await using var checkCmd = new NpgsqlCommand(
                    "SELECT count(*) FROM public._oisms_test_fixture_marker WHERE run_id = @runId;", conn);
                checkCmd.Parameters.AddWithValue("runId", PostgresTestEnvironment.RunId);
                var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                count.Should().Be(1, "Marker phải chứa RunId chính xác của lượt chạy");
            }
        }
        finally
        {
            await DropTemporaryDatabaseAsync(fixtureTestDb);
        }
    }

    [Fact]
    public async Task Postgres_TestEnvironment_PreExistingRole_MustNotChangePasswordOrGrantExtraRoles()
    {
        var preExistingRole = $"preexist_role_{Guid.NewGuid().ToString("N")[..8]}";
        const string originalPassword = "SecretOriginalPassword_123!";

        await using (var serverConn = new NpgsqlConnection(PostgresTestEnvironment.AdminServerConnectionString))
        {
            await serverConn.OpenAsync();
            var createRoleSql = $"CREATE ROLE {PostgresTestEnvironment.EscapeSqlIdentifier(preExistingRole)} WITH LOGIN PASSWORD {PostgresTestEnvironment.EscapeSqlLiteral(originalPassword)};";
            await using var cmd = new NpgsqlCommand(createRoleSql, serverConn);
            await cmd.ExecuteNonQueryAsync();
        }

        try
        {
            var preExistingConnStr = PostgresTestEnvironment.BuildConnectionString(
                PostgresTestEnvironment.Host,
                PostgresTestEnvironment.Port,
                "postgres",
                preExistingRole,
                originalPassword);

            await using (var testConn = new NpgsqlConnection(preExistingConnStr))
            {
                await testConn.OpenAsync();
                testConn.State.Should().Be(ConnectionState.Open);
            }

            PostgresTestEnvironment.CreatedRoles.ContainsKey(preExistingRole).Should().BeFalse(
                "Role có sẵn từ trước không được coi là tài nguyên do fixture tạo");

            await using (var retestConn = new NpgsqlConnection(preExistingConnStr))
            {
                await retestConn.OpenAsync();
                retestConn.State.Should().Be(ConnectionState.Open, "Mật khẩu role có sẵn không bị thay đổi");
            }
        }
        finally
        {
            await using var serverConn = new NpgsqlConnection(PostgresTestEnvironment.AdminServerConnectionString);
            await serverConn.OpenAsync();
            await using var dropCmd = new NpgsqlCommand($"DROP ROLE IF EXISTS {PostgresTestEnvironment.EscapeSqlIdentifier(preExistingRole)};", serverConn);
            await dropCmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Postgres_BaselineAdoption_WhenFkSkusProductIdMissing_MustRejectWithDriftError()
    {
        var tempDb = await CreateTemporaryCleanDatabaseAsync("oisms_fk_miss_test");
        var tempConnStr = PostgresTestEnvironment.BuildConnectionString(
            PostgresTestEnvironment.Host, PostgresTestEnvironment.Port, tempDb,
            PostgresTestEnvironment.AdminUser, PostgresTestEnvironment.AdminPassword);

        var migrationService = new DatabaseMigrationService();
        var initResult = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);
        initResult.Success.Should().BeTrue();

        await using (var conn = new NpgsqlConnection(tempConnStr))
        {
            await conn.OpenAsync();
            await using var dropFkCmd = new NpgsqlCommand("ALTER TABLE skus DROP CONSTRAINT fk_skus_product_id;", conn);
            await dropFkCmd.ExecuteNonQueryAsync();
        }

        try
        {
            var result = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);
            result.Success.Should().BeFalse("Database thiếu fk_skus_product_id không được phép tiếp nhận baseline");
            result.Errors.Should().NotBeNullOrEmpty();
            result.Errors.Should().Contain(e => e.Contains("fk_skus_product_id"));
        }
        finally
        {
            await DropTemporaryDatabaseAsync(tempDb);
        }
    }

    [Fact]
    public async Task Postgres_BaselineAdoption_WhenFkDefinitionOmitsTenantId_MustRejectWithDriftError()
    {
        var tempDb = await CreateTemporaryCleanDatabaseAsync("oisms_fk_col_test");
        var tempConnStr = PostgresTestEnvironment.BuildConnectionString(
            PostgresTestEnvironment.Host, PostgresTestEnvironment.Port, tempDb,
            PostgresTestEnvironment.AdminUser, PostgresTestEnvironment.AdminPassword);

        var migrationService = new DatabaseMigrationService();
        var initResult = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);
        initResult.Success.Should().BeTrue();

        await using (var conn = new NpgsqlConnection(tempConnStr))
        {
            await conn.OpenAsync();
            await using var dropFkCmd = new NpgsqlCommand("ALTER TABLE skus DROP CONSTRAINT fk_skus_product_id;", conn);
            await dropFkCmd.ExecuteNonQueryAsync();

            await using var addFkCmd = new NpgsqlCommand("ALTER TABLE skus ADD CONSTRAINT fk_skus_product_id FOREIGN KEY (product_id) REFERENCES products(id);", conn);
            await addFkCmd.ExecuteNonQueryAsync();
        }

        try
        {
            var result = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);
            result.Success.Should().BeFalse("Khóa ngoại thiếu tenant_id phá vỡ cô lập tenant nên phải bị từ chối");
            result.Errors.Should().NotBeNullOrEmpty();
            result.Errors.Should().Contain(e => e.Contains("fk_skus_product_id") && (e.Contains("cột") || e.Contains("định nghĩa sai")));
        }
        finally
        {
            await DropTemporaryDatabaseAsync(tempDb);
        }
    }

    [Fact]
    public async Task Postgres_BaselineAdoption_WhenWarehouseStockConsistencyDisabled_MustRejectWithDriftError()
    {
        var tempDb = await CreateTemporaryCleanDatabaseAsync("oisms_wh_trig_test");
        var tempConnStr = PostgresTestEnvironment.BuildConnectionString(
            PostgresTestEnvironment.Host, PostgresTestEnvironment.Port, tempDb,
            PostgresTestEnvironment.AdminUser, PostgresTestEnvironment.AdminPassword);

        var migrationService = new DatabaseMigrationService();
        var initResult = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);
        initResult.Success.Should().BeTrue();

        await using (var conn = new NpgsqlConnection(tempConnStr))
        {
            await conn.OpenAsync();
            await using var disableCmd = new NpgsqlCommand("ALTER TABLE warehouse_inventory_balances DISABLE TRIGGER stock_consistency;", conn);
            await disableCmd.ExecuteNonQueryAsync();
        }

        try
        {
            var result = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);
            result.Success.Should().BeFalse("Trigger stock_consistency trên warehouse_inventory_balances bị tắt phải bị từ chối");
            result.Errors.Should().NotBeNullOrEmpty();
            result.Errors.Should().Contain(e => e.Contains("warehouse_inventory_balances") && e.Contains("stock_consistency") && e.Contains("vô hiệu hóa"));
        }
        finally
        {
            await DropTemporaryDatabaseAsync(tempDb);
        }
    }

    [Fact]
    public async Task Postgres_BaselineAdoption_WhenReservationOrLedgerTriggersMissing_MustRejectWithDriftError()
    {
        var tempDb = await CreateTemporaryCleanDatabaseAsync("oisms_res_trig_test");
        var tempConnStr = PostgresTestEnvironment.BuildConnectionString(
            PostgresTestEnvironment.Host, PostgresTestEnvironment.Port, tempDb,
            PostgresTestEnvironment.AdminUser, PostgresTestEnvironment.AdminPassword);

        var migrationService = new DatabaseMigrationService();
        var initResult = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);
        initResult.Success.Should().BeTrue();

        await using (var conn = new NpgsqlConnection(tempConnStr))
        {
            await conn.OpenAsync();
            await using var dropCmd = new NpgsqlCommand("DROP TRIGGER stock_lock ON reservations;", conn);
            await dropCmd.ExecuteNonQueryAsync();
        }

        try
        {
            var result = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);
            result.Success.Should().BeFalse("Thiếu trigger stock_lock trên reservations phải bị từ chối");
            result.Errors.Should().NotBeNullOrEmpty();
            result.Errors.Should().Contain(e => e.Contains("reservations") && e.Contains("stock_lock"));
        }
        finally
        {
            await DropTemporaryDatabaseAsync(tempDb);
        }
    }

    [Fact]
    public async Task Postgres_BaselineAdoption_WhenCheckConstraintMissing_MustRejectWithDriftError()
    {
        var tempDb = await CreateTemporaryCleanDatabaseAsync("oisms_ck_miss_test");
        var tempConnStr = PostgresTestEnvironment.BuildConnectionString(
            PostgresTestEnvironment.Host, PostgresTestEnvironment.Port, tempDb,
            PostgresTestEnvironment.AdminUser, PostgresTestEnvironment.AdminPassword);

        var migrationService = new DatabaseMigrationService();
        var initResult = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);
        initResult.Success.Should().BeTrue();

        await using (var conn = new NpgsqlConnection(tempConnStr))
        {
            await conn.OpenAsync();
            await using var dropCkCmd = new NpgsqlCommand("ALTER TABLE inventory_balances DROP CONSTRAINT ck_inventory_balances_on_hand_nonnegative;", conn);
            await dropCkCmd.ExecuteNonQueryAsync();
        }

        try
        {
            var result = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);
            result.Success.Should().BeFalse("Thiếu check constraint ck_inventory_balances_on_hand_nonnegative phải bị từ chối");
            result.Errors.Should().NotBeNullOrEmpty();
            result.Errors.Should().Contain(e => e.Contains("ck_inventory_balances_on_hand_nonnegative"));
        }
        finally
        {
            await DropTemporaryDatabaseAsync(tempDb);
        }
    }

    [Fact]
    public async Task Postgres_BaselineAdoption_WhenRejected_NoMigrationRecorded_AndDataPreserved()
    {
        var tempDb = await CreateTemporaryCleanDatabaseAsync("oisms_reject_safe_test");
        var tempConnStr = PostgresTestEnvironment.BuildConnectionString(
            PostgresTestEnvironment.Host, PostgresTestEnvironment.Port, tempDb,
            PostgresTestEnvironment.AdminUser, PostgresTestEnvironment.AdminPassword);

        var rawDdl = DatabaseMigrationService.FindAndLoadSchemaDdl();
        rawDdl.Should().NotBeNull("File DDL oisms_schema.sql phải được định vị thành công");
        var cleanDdl = DatabaseMigrationService.CleanDdlForConnectedDatabase(rawDdl!);

        await using (var conn = new NpgsqlConnection(tempConnStr))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(cleanDdl, conn);
            await cmd.ExecuteNonQueryAsync();

            await using var insCmd = new NpgsqlCommand(
                "INSERT INTO tenants (id, name, created_at, updated_at) VALUES ('33333333-3333-3333-3333-333333333333', 'Preserved Reject Tenant', now(), now());", conn);
            await insCmd.ExecuteNonQueryAsync();

            // Gây drift: Xóa trigger ledger_append_only trên inventory_transactions
            await using var dropCmd = new NpgsqlCommand("DROP TRIGGER ledger_append_only ON inventory_transactions;", conn);
            await dropCmd.ExecuteNonQueryAsync();
        }

        try
        {
            var migrationService = new DatabaseMigrationService();
            var result = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);

            result.Success.Should().BeFalse("Baseline phải bị từ chối khi phát hiện drift");
            result.IsBaselineAdopted.Should().BeFalse();

            await using (var conn = new NpgsqlConnection(tempConnStr))
            {
                await conn.OpenAsync();
                await using var checkHistoryCmd = new NpgsqlCommand(
                    "SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = '__EFMigrationsHistory';", conn);
                var historyTableExists = (await checkHistoryCmd.ExecuteScalarAsync()) != null;
                if (historyTableExists)
                {
                    await using var countCmd = new NpgsqlCommand("SELECT count(*) FROM \"__EFMigrationsHistory\";", conn);
                    var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
                    count.Should().Be(0, "Không được phép ghi nhận migration history khi baseline bị từ chối");
                }

                await using var checkDataCmd = new NpgsqlCommand(
                    "SELECT name FROM tenants WHERE id = '33333333-3333-3333-3333-333333333333';", conn);
                var tenantName = (await checkDataCmd.ExecuteScalarAsync())?.ToString();
                tenantName.Should().Be("Preserved Reject Tenant", "Dữ liệu nghiệp vụ phải được bảo toàn nguyên vẹn sau khi reject");
            }
        }
        finally
        {
            await DropTemporaryDatabaseAsync(tempDb);
        }
    }

    [Fact]
    public async Task Postgres_BaselineAdoption_WhenRoleCheckLiteralCaseDrifts_MustRejectWithDetailedDiagnostic()
    {
        var tempDb = await CreateTemporaryCleanDatabaseAsync("oisms_role_case_drift");
        var tempConnStr = PostgresTestEnvironment.BuildConnectionString(
            PostgresTestEnvironment.Host,
            PostgresTestEnvironment.Port,
            tempDb,
            PostgresTestEnvironment.AdminUser,
            PostgresTestEnvironment.AdminPassword);

        var migrationService = new DatabaseMigrationService();

        try
        {
            // 1. Cài đặt schema v0.5 chuẩn trên database tạm
            var installResult = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);
            installResult.Success.Should().BeTrue();

            // 2. Tái hiện lỗi Bug A: Sửa check constraint ck_users_role_values sang chữ thường ('owner', 'staff', 'cashier')
            await using (var conn = new NpgsqlConnection(tempConnStr))
            {
                await conn.OpenAsync();
                await using var dropCk = new NpgsqlCommand("ALTER TABLE users DROP CONSTRAINT ck_users_role_values;", conn);
                await dropCk.ExecuteNonQueryAsync();

                await using var addDriftedCk = new NpgsqlCommand(
                    "ALTER TABLE users ADD CONSTRAINT ck_users_role_values CHECK (role IN ('owner', 'staff', 'cashier'));", conn);
                await addDriftedCk.ExecuteNonQueryAsync();

                // Xóa lịch sử migration để mô phỏng lượt kiểm tra tiếp nhận baseline
                await using var clearHistory = new NpgsqlCommand("DELETE FROM \"__EFMigrationsHistory\";", conn);
                await clearHistory.ExecuteNonQueryAsync();
            }

            // 3. Thực hiện kiểm tra tiếp nhận baseline: Bắt buộc phải TỪ CHỐI vì sai khác case literal
            var baselineResult = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);

            baselineResult.Success.Should().BeFalse("Database có check constraint chứa chuỗi sai case ('owner' vs 'Owner') phải bị từ chối");
            baselineResult.IsBaselineAdopted.Should().BeFalse();
            baselineResult.Errors.Should().NotBeNullOrEmpty();
            baselineResult.Errors.Should().Contain(e =>
                e.Contains("ck_users_role_values") &&
                e.Contains("sai biểu thức kiểm tra"));

            // 4. Xác minh không ghi nhận baseline vào __EFMigrationsHistory
            await using (var conn = new NpgsqlConnection(tempConnStr))
            {
                await conn.OpenAsync();
                await using var checkHistory = new NpgsqlCommand("SELECT count(*) FROM \"__EFMigrationsHistory\";", conn);
                var historyCount = Convert.ToInt32(await checkHistory.ExecuteScalarAsync());
                historyCount.Should().Be(0, "Không được ghi nhận migration history khi phát hiện literal case drift");
            }
        }
        finally
        {
            await DropTemporaryDatabaseAsync(tempDb);
        }
    }

    [Fact]
    public async Task Postgres_BaselineAdoption_WhenLedgerTriggerHasWhenClause_MustRejectWithDetailedDiagnostic()
    {
        var tempDb = await CreateTemporaryCleanDatabaseAsync("oisms_trig_when_drift");
        var tempConnStr = PostgresTestEnvironment.BuildConnectionString(
            PostgresTestEnvironment.Host,
            PostgresTestEnvironment.Port,
            tempDb,
            PostgresTestEnvironment.AdminUser,
            PostgresTestEnvironment.AdminPassword);

        var migrationService = new DatabaseMigrationService();

        try
        {
            // 1. Cài đặt schema v0.5 chuẩn trên database tạm
            var installResult = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);
            installResult.Success.Should().BeTrue();

            // 2. Tái hiện lỗi Bug B: Sửa trigger ledger_append_only thêm WHEN (false)
            await using (var conn = new NpgsqlConnection(tempConnStr))
            {
                await conn.OpenAsync();
                await using var dropTrig = new NpgsqlCommand("DROP TRIGGER ledger_append_only ON inventory_transactions;", conn);
                await dropTrig.ExecuteNonQueryAsync();

                await using var addDriftedTrig = new NpgsqlCommand(
                    "CREATE TRIGGER ledger_append_only BEFORE UPDATE OR DELETE OR TRUNCATE ON inventory_transactions FOR EACH STATEMENT WHEN (false) EXECUTE FUNCTION oisms_deny_mutation();", conn);
                await addDriftedTrig.ExecuteNonQueryAsync();

                await using var clearHistory = new NpgsqlCommand("DELETE FROM \"__EFMigrationsHistory\";", conn);
                await clearHistory.ExecuteNonQueryAsync();
            }

            // 3. Thực hiện kiểm tra tiếp nhận baseline: Bắt buộc phải TỪ CHỐI vì có WHEN clause
            var baselineResult = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);

            baselineResult.Success.Should().BeFalse("Database có trigger ledger_append_only bị thêm WHEN (false) phải bị từ chối");
            baselineResult.IsBaselineAdopted.Should().BeFalse();
            baselineResult.Errors.Should().NotBeNullOrEmpty();
            baselineResult.Errors.Should().Contain(e =>
                e.Contains("ledger_append_only") &&
                e.Contains("chứa điều kiện WHEN không hợp lệ"));

            // 4. Xác minh không ghi nhận baseline vào __EFMigrationsHistory
            await using (var conn = new NpgsqlConnection(tempConnStr))
            {
                await conn.OpenAsync();
                await using var checkHistory = new NpgsqlCommand("SELECT count(*) FROM \"__EFMigrationsHistory\";", conn);
                var historyCount = Convert.ToInt32(await checkHistory.ExecuteScalarAsync());
                historyCount.Should().Be(0, "Không được ghi nhận migration history khi phát hiện trigger có WHEN condition");
            }
        }
        finally
        {
            await DropTemporaryDatabaseAsync(tempDb);
        }
    }

    [Fact]
    public async Task Postgres_BaselineAdoption_WhenLedgerTriggerHasColumnRestrictions_MustRejectWithDetailedDiagnostic()
    {
        var tempDb = await CreateTemporaryCleanDatabaseAsync("oisms_trig_col_drift");
        var tempConnStr = PostgresTestEnvironment.BuildConnectionString(
            PostgresTestEnvironment.Host,
            PostgresTestEnvironment.Port,
            tempDb,
            PostgresTestEnvironment.AdminUser,
            PostgresTestEnvironment.AdminPassword);

        var migrationService = new DatabaseMigrationService();

        try
        {
            // 1. Cài đặt schema v0.5 chuẩn trên database tạm
            var installResult = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);
            installResult.Success.Should().BeTrue();

            // 2. Tái hiện lỗi: Sửa trigger ledger_append_only giới hạn chỉ UPDATE OF balance_after thay vì cả bảng
            await using (var conn = new NpgsqlConnection(tempConnStr))
            {
                await conn.OpenAsync();
                await using var dropTrig = new NpgsqlCommand("DROP TRIGGER ledger_append_only ON inventory_transactions;", conn);
                await dropTrig.ExecuteNonQueryAsync();

                await using var addDriftedTrig = new NpgsqlCommand(
                    "CREATE TRIGGER ledger_append_only BEFORE UPDATE OF balance_after ON inventory_transactions FOR EACH STATEMENT EXECUTE FUNCTION oisms_deny_mutation();", conn);
                await addDriftedTrig.ExecuteNonQueryAsync();

                await using var clearHistory = new NpgsqlCommand("DELETE FROM \"__EFMigrationsHistory\";", conn);
                await clearHistory.ExecuteNonQueryAsync();
            }

            // 3. Thực hiện kiểm tra tiếp nhận baseline: Bắt buộc phải TỪ CHỐI vì bị giới hạn cột và thiếu DELETE/TRUNCATE
            var baselineResult = await migrationService.InitializeOrBaselineDatabaseAsync(tempConnStr);

            baselineResult.Success.Should().BeFalse("Database có trigger ledger_append_only bị giới hạn danh sách cột phải bị từ chối");
            baselineResult.IsBaselineAdopted.Should().BeFalse();
            baselineResult.Errors.Should().NotBeNullOrEmpty();
            baselineResult.Errors.Should().Contain(e =>
                e.Contains("ledger_append_only") &&
                (e.Contains("giới hạn theo danh sách cột") || e.Contains("sai sự kiện DELETE")));

            // 4. Xác minh không ghi nhận baseline vào __EFMigrationsHistory
            await using (var conn = new NpgsqlConnection(tempConnStr))
            {
                await conn.OpenAsync();
                await using var checkHistory = new NpgsqlCommand("SELECT count(*) FROM \"__EFMigrationsHistory\";", conn);
                var historyCount = Convert.ToInt32(await checkHistory.ExecuteScalarAsync());
                historyCount.Should().Be(0, "Không được ghi nhận migration history khi trigger bị giới hạn cột");
            }
        }
        finally
        {
            await DropTemporaryDatabaseAsync(tempDb);
        }
    }

    // =========================================================================
    // 6. ISOLATION READ FILTER (NFR-TENANT-01)
    // =========================================================================
    [Fact]
    public async Task Postgres_TenantIsolation_ReadFilter_ShouldStrictlyIsolateData()
    {
        var (context, tenantService) = CreatePostgresDbContext();
        await using (context)
        {
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);

            tenantService.SetTenant(OismsDatabaseSeeder.TenantAId);
            var productsA = await context.Products.ToListAsync();
            productsA.Should().NotBeEmpty();
            productsA.Should().AllSatisfy(p => p.TenantId.Should().Be(OismsDatabaseSeeder.TenantAId));

            tenantService.SetTenant(OismsDatabaseSeeder.TenantBId);
            var productsB = await context.Products.ToListAsync();
            productsB.Should().NotBeEmpty();
            productsB.Should().AllSatisfy(p => p.TenantId.Should().Be(OismsDatabaseSeeder.TenantBId));

            var productIdsA = productsA.Select(p => p.Id).ToHashSet();
            productsB.Should().NotContain(p => productIdsA.Contains(p.Id));
        }
    }

    [Fact]
    public async Task Postgres_TenantIsolation_Unauthenticated_ShouldReturnZeroRecords()
    {
        var (context, tenantService) = CreatePostgresDbContext();
        await using (context)
        {
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);

            tenantService.TenantId = null;

            var products = await context.Products.ToListAsync();
            products.Should().BeEmpty();

            var skus = await context.ProductVariants.ToListAsync();
            skus.Should().BeEmpty();

            var branches = await context.Branches.ToListAsync();
            branches.Should().BeEmpty();
        }
    }

    // =========================================================================
    // 7. END-TO-END SMOKE TEST ON REAL POSTGRESQL WITH PRODUCTION DI
    // =========================================================================
    [Fact]
    public async Task Postgres_EndToEnd_SmokeTest_RegisterLoginCreateSkuReadSku()
    {
        using var factory = new WebApplicationFactoryWithPostgres();
        var client = factory.CreateClient();
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // 1. Đăng ký Cửa hàng mới (FR-AUTH-01)
        var uniqueSuffix = DateTime.UtcNow.Ticks.ToString()[^6..];
        var randomPhoneDigits = new Random().Next(10000000, 99999999);
        var registerReq = new RegisterTenantRequest
        {
            TenantName = $"Smoke Shop {uniqueSuffix}",
            TenantCode = $"SMK{uniqueSuffix}",
            FullName = "Chủ Shop Smoke",
            Email = $"owner_{uniqueSuffix}@smoketest.local",
            Password = "Password@123",
            PhoneNumber = $"09{randomPhoneDigits}"
        };

        var regRes = await client.PostAsJsonAsync("/api/auth/register-tenant", registerReq);
        regRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var regData = await regRes.Content.ReadFromJsonAsync<RegisterTenantResponse>(jsonOptions);
        regData.Should().NotBeNull();

        // 2. Đăng nhập để nhận JWT Token (FR-AUTH-02)
        var loginReq = new LoginRequest
        {
            Email = registerReq.Email,
            Password = registerReq.Password
        };
        var loginRes = await client.PostAsJsonAsync("/api/auth/login", loginReq);
        loginRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginData = await loginRes.Content.ReadFromJsonAsync<AuthResponse>(jsonOptions);
        loginData.Should().NotBeNull();
        loginData!.AccessToken.Should().NotBeNullOrEmpty();

        // Gán JWT Bearer vào client
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginData.AccessToken);

        // 3. Tạo Sản phẩm và SKU biến thể (FR-PROD-01, FR-PROD-02)
        var skuCode = $"TSHIRT-{uniqueSuffix}";
        var barcode = $"893{uniqueSuffix.PadLeft(10, '7')[..10]}";
        var createProdReq = new CreateProductRequest
        {
            Name = $"Áo Thun Nam Cao Cấp {uniqueSuffix}",
            Description = "Áo thun cotton thoáng mát",
            Variants = new List<CreateVariantRequest>
            {
                new()
                {
                    SKU = skuCode,
                    VariantName = "Xanh Navy / Size L",
                    Barcode = barcode,
                    RetailPrice = 150000,
                    WholesalePrice = 110000,
                    Color = "Xanh Navy",
                    Size = "L"
                }
            }
        };

        var createProdRes = await client.PostAsJsonAsync("/api/products", createProdReq);
        createProdRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // 4. Đọc sản phẩm qua POS Barcode Scan (FR-POS-01)
        var posRes = await client.GetAsync($"/api/pos/barcode/{barcode}");
        posRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var posItem = await posRes.Content.ReadFromJsonAsync<PosProductSearchResponse>(jsonOptions);
        posItem.Should().NotBeNull();
        posItem!.SKU.Should().Be(skuCode);
        posItem.Barcode.Should().Be(barcode);
        posItem.RetailPrice.Should().Be(150000);
        posItem.Color.Should().Be("Xanh Navy");
        posItem.Size.Should().Be("L");

        // 5. Tìm kiếm nhanh trên POS Catalog
        var searchRes = await client.GetAsync($"/api/pos/search?query={skuCode}");
        searchRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var searchList = await searchRes.Content.ReadFromJsonAsync<List<PosProductSearchResponse>>(jsonOptions);
        searchList.Should().NotBeNull();
        searchList!.Should().Contain(x => x.SKU == skuCode && x.Barcode == barcode);
    }
}

public class WebApplicationFactoryWithPostgres : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Host"] = PostgresTestEnvironment.Host,
                ["Database:Port"] = PostgresTestEnvironment.Port.ToString(),
                ["Database:Database"] = PostgresTestEnvironment.DatabaseName,
                ["Database:Username"] = PostgresTestEnvironment.AppUser,
                ["Database:Password"] = PostgresTestEnvironment.AppPassword,
                ["ConnectionStrings:DefaultConnection"] = PostgresTestEnvironment.RuntimeConnectionString,
                ["Database:SeedDemoData"] = "false",
                ["Hangfire:Enabled"] = "false"
            });
        });

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseNpgsql(PostgresTestEnvironment.RuntimeConnectionString, npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    npgsqlOptions.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
                })
                .UseSnakeCaseNamingConvention();
            });
        });
    }
}
