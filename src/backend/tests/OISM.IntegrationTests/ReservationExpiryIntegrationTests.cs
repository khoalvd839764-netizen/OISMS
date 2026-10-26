using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Inventory.Services;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Orders.Services;
using OISM.Application.Features.Realtime;
using OISM.Application.Features.Realtime.Dtos;
using OISM.Domain.Entities;
using OISM.Infrastructure.Jobs;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Bộ kiểm thử tích hợp toàn diện cho Task 6: Tự động hủy đơn giữ chỗ quá hạn TTL 15 phút (Hangfire Worker).
/// Kiểm chứng đầy đủ 12 Tiêu chí Nghiệm thu (AC-01 .. AC-12) trên PostgreSQL thật.
/// </summary>
public class ReservationExpiryIntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ReservationExpiryIntegrationTests(WebApplicationFactoryWithPostgres factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
        await PostgresTestEnvironment.EnsureHangfireSchemaAsync();
        OrderService.ResetCursors();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private class TestTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private (AppDbContext Context, TestTenantService TenantService) CreatePostgresDbContext(Guid? tenantId = null, params IInterceptor[] interceptors)
    {
        var tenantService = new TestTenantService { TenantId = tenantId };
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
        _client.DefaultRequestHeaders.Authorization = null;
        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = password
        });

        var body = await loginRes.Content.ReadAsStringAsync();
        loginRes.StatusCode.Should().Be(HttpStatusCode.OK, $"Đăng nhập thất bại cho user {email}: {body}");

        var authData = await loginRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        authData.Should().NotBeNull();
        authData!.AccessToken.Should().NotBeNullOrEmpty();
        return authData.AccessToken;
    }

    private HubConnection CreateHubConnection(string? accessToken = null)
    {
        var hubUrl = accessToken != null
            ? new Uri(_factory.Server.BaseAddress, $"/hubs/notifications?access_token={accessToken}")
            : new Uri(_factory.Server.BaseAddress, "/hubs/notifications");

        return new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
    }

    private static async Task<(InventoryBalance BranchBal, WarehouseInventoryBalance WhBal)> EnsureBalancesExistAsync(
        AppDbContext db, Guid tenantId, Guid branchId, Guid warehouseId, Guid skuId, decimal onHand = 100m, decimal reserved = 0m)
    {
        var bBal = await db.InventoryBalances.FirstOrDefaultAsync(b => b.TenantId == tenantId && b.BranchId == branchId && b.SkuId == skuId);
        if (bBal == null)
        {
            bBal = new InventoryBalance
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BranchId = branchId,
                SkuId = skuId,
                OnHand = onHand,
                Reserved = reserved,
                AverageCost = 50000m,
                LastSequence = 0,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.InventoryBalances.Add(bBal);
        }
        else
        {
            bBal.OnHand = Math.Max(bBal.OnHand, onHand);
            bBal.Reserved = reserved;
        }

        var whBal = await db.WarehouseInventoryBalances.FirstOrDefaultAsync(wb => wb.TenantId == tenantId && wb.BranchId == branchId && wb.WarehouseId == warehouseId && wb.SkuId == skuId);
        if (whBal == null)
        {
            whBal = new WarehouseInventoryBalance
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BranchId = branchId,
                WarehouseId = warehouseId,
                SkuId = skuId,
                OnHand = onHand,
                Reserved = reserved,
                LastSequence = 0,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.WarehouseInventoryBalances.Add(whBal);
        }
        else
        {
            whBal.OnHand = Math.Max(whBal.OnHand, onHand);
            whBal.Reserved = reserved;
        }

        return (bBal, whBal);
    }

    private async Task<(InventoryBalance BranchBal, WarehouseInventoryBalance WhBal)> SeedStockAsync(
        Guid tenantId, Guid branchId, Guid warehouseId, Guid skuId, decimal quantity = 50m, decimal unitPrice = 50000m)
    {
        var (context, tenantService) = CreatePostgresDbContext(tenantId);
        await using (context)
        {
            var supplier = await context.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            var user = await context.Users.FirstAsync(u => u.TenantId == tenantId);
            var wacCalc = new WacCalculator();
            var service = new GoodsReceiptService(context, tenantService, wacCalc);

            var receiptDto = new CreateGoodsReceiptDto
            {
                BranchId = branchId,
                WarehouseId = warehouseId,
                SupplierId = supplier.Id,
                Note = "Seed stock for test",
                Items = new List<CreateGoodsReceiptItemDto>
                {
                    new() { SkuId = skuId, Quantity = quantity, UnitPrice = unitPrice }
                }
            };

            var receipt = await service.CreateReceiptAsync(receiptDto, user.Id);
            await service.ConfirmReceiptAsync(receipt.Id, user.Id);

            var bBal = await context.InventoryBalances.FirstAsync(b => b.TenantId == tenantId && b.BranchId == branchId && b.SkuId == skuId);
            var whBal = await context.WarehouseInventoryBalances.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId && w.WarehouseId == warehouseId && w.SkuId == skuId);
            return (bBal, whBal);
        }
    }

    private class FaultInjectingInterceptor : DbCommandInterceptor
    {
        public Guid TargetOrderId { get; set; }
        public bool ShouldFail { get; set; } = true;
        public int FailureCount { get; private set; }
        public bool HasSeenWrites { get; private set; }
        private Guid _currentOrderId;

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            InspectCommand(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            InspectCommand(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            InspectCommand(command);

            if (ShouldFail && _currentOrderId == TargetOrderId &&
                command.CommandText.StartsWith("SET CONSTRAINTS ALL IMMEDIATE", StringComparison.Ordinal))
            {
                FailureCount++;
                throw new PostgresException("Simulated SQL fault after SaveChanges before commit", "FATAL", "XX000", "Simulated SQL fault");
            }

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void InspectCommand(DbCommand command)
        {
            var text = command.CommandText;

            // Nhận diện order ID đang được xử lý qua advisory lock hoặc FOR UPDATE
            if (text.Contains("/order/' ||", StringComparison.Ordinal) || text.Contains("orders WHERE tenant_id = ", StringComparison.Ordinal))
            {
                foreach (DbParameter p in command.Parameters)
                {
                    if (p.Value is Guid g && g != Guid.Empty && g != OismsDatabaseSeeder.TenantAId && g != OismsDatabaseSeeder.TenantBId)
                    {
                        _currentOrderId = g;
                        break;
                    }
                    if (p.Value is string s && Guid.TryParse(s, out var parsedGuid) && parsedGuid != Guid.Empty && parsedGuid != OismsDatabaseSeeder.TenantAId && parsedGuid != OismsDatabaseSeeder.TenantBId)
                    {
                        _currentOrderId = parsedGuid;
                        break;
                    }
                }
            }

            if (text.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase) &&
                (text.Contains("orders", StringComparison.OrdinalIgnoreCase) || text.Contains("reservations", StringComparison.OrdinalIgnoreCase)))
            {
                if (_currentOrderId == TargetOrderId)
                {
                    HasSeenWrites = true;
                }
            }
        }
    }

    #region AC-01: Worker & Recurring Registration Thật

    [Fact]
    public async Task AC01_HangfireRegistration_EnabledFalseAndTrue_ServerExecution()
    {
        // 1. Khi Hangfire:Enabled = false: Không có storage / recurring job
        using (var scope = _factory.Services.CreateScope())
        {
            var recurringManager = scope.ServiceProvider.GetService<IRecurringJobManager>();
            recurringManager.Should().BeNull("Khi Hangfire:Enabled = false, không được đăng ký Hangfire services.");
        }

        // 2. Khi Hangfire:Enabled = true: Khởi động Hangfire với PostgreSQL storage schema 'hangfire'
        using var hangfireFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var config = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Hangfire:Enabled"] = "true",
                        ["ConnectionStrings:DefaultConnection"] = PostgresTestEnvironment.RuntimeConnectionString,
                        ["Hangfire:WorkerCount"] = "2"
                    })
                    .Build();
                services.AddOismHangfire(config);
            });
        });

        // Kích hoạt host để chạy Program middleware bao gồm UseOismHangfire
        var hangfireClient = hangfireFactory.CreateClient();

        using (var scope = hangfireFactory.Services.CreateScope())
        {
            var recurringManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
            recurringManager.Should().NotBeNull();

            // Kiểm tra recurring job ID và metadata đăng ký
            var storage = scope.ServiceProvider.GetRequiredService<JobStorage>();
            using var conn = storage.GetConnection();
            var recurringJob = conn.GetRecurringJobs().FirstOrDefault(j => j.Id == HangfireConfigurationExtensions.RecurringJobId);
            recurringJob.Should().NotBeNull("Recurring job 'oisms-reservation-expiry' phải được đăng ký trong Hangfire.");
            recurringJob!.Cron.Should().Be("*/1 * * * *");
            recurringJob.TimeZoneId.Should().Be(TimeZoneInfo.Utc.Id);

            // 3. Chuẩn bị đơn hàng quá hạn trong PostgreSQL và kích hoạt job thông qua IReservationExpiryJob
            await SeedDatabaseAsync();
            var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
            hangfireClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

            var (db, _) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
            await using (db)
            {
                var branch = await db.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
                var wh = await db.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId);
                var sku = await db.Skus.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
                await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branch.Id, wh.Id, sku.Id, 50m, sku.RetailPrice);

                // Tạo đơn hàng qua API
                var orderRes = await hangfireClient.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
                {
                    RequestKey = Guid.NewGuid(),
                    BranchId = branch.Id,
                    Channel = "Admin",
                    Items = new List<OrderItemRequestDto>
                    {
                        new() { SkuId = sku.Id, SkuCode = sku.SKU, Quantity = 2m, UnitPrice = sku.RetailPrice }
                    }
                });
                orderRes.StatusCode.Should().Be(HttpStatusCode.Created);
                var createdOrder = await orderRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);

                // Chỉnh sửa thời gian phiếu giữ chỗ quá hạn 15 phút (về quá khứ)
                var now = DateTimeOffset.UtcNow;
                await db.Database.ExecuteSqlRawAsync(
                    "UPDATE reservations SET created_at = {0}, expires_at = {1} WHERE order_id = {2};",
                    now.AddMinutes(-30),
                    now.AddMinutes(-5),
                    createdOrder!.Id);

                // Kích hoạt tác vụ nền Hangfire thật qua IBackgroundJobClient
                var jobClient = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
                var jobId = jobClient.Enqueue<IReservationExpiryJob>(j => j.ExecuteAsync(now, CancellationToken.None));

                // Chờ Hangfire server thực thi và hoàn tất job
                var deadline = DateTime.UtcNow.AddSeconds(30);
                string? jobState = null;
                while (DateTime.UtcNow < deadline)
                {
                    using var c = storage.GetConnection();
                    jobState = c.GetStateData(jobId)?.Name;
                    if (jobState is "Succeeded" or "Failed") break;
                    await Task.Delay(250);
                }
                jobState.Should().Be("Succeeded", "Job thực thi qua Hangfire server thật phải hoàn tất ở trạng thái Succeeded.");

                // Kiểm tra trạng thái trong CSDL
                db.ChangeTracker.Clear();
                var updatedOrder = await db.Orders.Include(o => o.Reservations).FirstAsync(o => o.Id == createdOrder!.Id);
                updatedOrder.Status.Should().Be("Cancelled");
                updatedOrder.CancelledAt.Should().NotBeNull();
                updatedOrder.Reservations.Should().AllSatisfy(r => r.Status.Should().Be("Expired"));
            }
        }
    }

    #endregion

    #region AC-02: Storage & Khởi Động An Toàn

    [Fact]
    public async Task AC02_StorageSafety_SchemaHangfireIsolated_NoFallbackOnConfigError()
    {
        // 1. Kiểm tra schema 'hangfire' hoàn toàn tách biệt khỏi 'public'
        using var hangfireFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var config = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Hangfire:Enabled"] = "true",
                        ["ConnectionStrings:DefaultConnection"] = PostgresTestEnvironment.RuntimeConnectionString
                    })
                    .Build();
                services.AddOismHangfire(config);
            });
        });

        using var client = hangfireFactory.CreateClient();
        _ = hangfireFactory.Services.GetRequiredService<JobStorage>();

        // Kiểm tra bảng trong schema 'hangfire' trên PostgreSQL
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.RuntimeConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT count(*) FROM information_schema.tables WHERE table_schema = 'hangfire';", conn);
            var hangfireTableCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            hangfireTableCount.Should().BeGreaterThan(0, "Schema 'hangfire' phải chứa các bảng quản trị Hangfire.");

            // Kiểm tra schema 'public' không bị chèn thêm bảng Hangfire nào
            await using var cmdPublic = new NpgsqlCommand("SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name LIKE 'hangfire%';", conn);
            var publicPollutionCount = Convert.ToInt32(await cmdPublic.ExecuteScalarAsync());
            publicPollutionCount.Should().Be(0, "Schema 'public' tuyệt đối không được chứa bảng của Hangfire.");
        }

        // 2. Readiness probe và validator của hệ thống vẫn hoạt động hoàn hảo
        var readinessRes = await client.GetAsync("/health/ready");
        readinessRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Đăng ký lại / restart không nhân đôi recurring ID
        using (var scope = hangfireFactory.Services.CreateScope())
        {
            var recurringManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
            recurringManager.AddOrUpdate<IReservationExpiryJob>(
                HangfireConfigurationExtensions.RecurringJobId,
                job => job.ExecuteAsync(null, CancellationToken.None),
                "*/1 * * * *",
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

            var storage = scope.ServiceProvider.GetRequiredService<JobStorage>();
            using var conn = storage.GetConnection();
            var count = conn.GetRecurringJobs().Count(j => j.Id == HangfireConfigurationExtensions.RecurringJobId);
            count.Should().Be(1, "Idempotent AddOrUpdate không được tạo trùng recurring job ID.");
        }

        // 4. Cấu hình lỗi kết nối -> Báo lỗi rõ, KHÔNG fallback sang In-Memory
        var actFaulty = () =>
        {
            using var faultyFactory = _factory.WithWebHostBuilder(b =>
            {
                b.ConfigureServices(services =>
                {
                    var config = new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["Hangfire:Enabled"] = "true",
                            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=non_existent_db_12345;Username=invalid;Password=invalid;Timeout=2;Command Timeout=2;"
                        })
                        .Build();
                    services.AddOismHangfire(config);
                });
            });
            var storage = faultyFactory.Services.GetRequiredService<JobStorage>();
            using var conn = storage.GetConnection();
        };

        actFaulty.Should().Throw<Exception>("Khởi động Hangfire với kết nối sai phải ném ngoại lệ, cấm fallback sang In-Memory.");

        // 5. Thiếu connection string hoàn toàn -> ném InvalidOperationException rõ ràng
        var actMissingConn = () =>
        {
            var s = new ServiceCollection();
            var emptyConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Hangfire:Enabled"] = "true" })
                .Build();
            s.AddOismHangfire(emptyConfig);
        };
        actMissingConn.Should().Throw<InvalidOperationException>("Thiếu connection string phải ném ngoại lệ cấu hình, cấm fallback.");
    }

    #endregion

    #region AC-03: Expire Nguyên Tử Đúng Dữ Liệu

    [Fact]
    public async Task AC03_AtomicBasketExpiry_MultiSkuMultiWarehouse_ExpiredAndReleasedStatuses()
    {
        await SeedDatabaseAsync();
        var (context, tenantService) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);

        await using (context)
        {
            var branch = await context.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var wh1 = await context.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku1 = await context.Skus.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);

            // Tạo thêm Warehouse 2 và SKU 2 để kiểm thử phân bổ đa kho, đa SKU và tài nguyên inactive
            var wh2 = await context.Warehouses.FirstOrDefaultAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.Id != wh1.Id);
            if (wh2 == null)
            {
                wh2 = new Warehouse
                {
                    Id = Guid.NewGuid(),
                    TenantId = OismsDatabaseSeeder.TenantAId,
                    BranchId = branch.Id,
                    Name = "Kho phụ 2",
                    IsActive = false // Tài nguyên Inactive: Kho phụ 2 bị inactive nhưng vẫn phải giải phóng tồn an toàn
                };
                context.Warehouses.Add(wh2);
            }
            else
            {
                wh2.IsActive = false;
            }

            var product = await context.Products.FirstAsync(p => p.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku2 = new ProductVariant
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                ProductId = product.Id,
                SKU = $"POLO-TRANG-L-{Guid.NewGuid():N}"[..20],
                RetailPrice = 250000m,
                IsActive = true
            };
            context.ProductVariants.Add(sku2);
            await context.SaveChangesAsync();

            // Đảm bảo số dư tồn ban đầu qua nhập kho thật:
            // Phân bổ SKU1 vào cả Kho 1 (hoạt động) và Kho 2 (inactive) để kiểm thử phân bổ cùng SKU qua nhiều kho
            await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branch.Id, wh1.Id, sku1.Id, 50m, sku1.RetailPrice);
            await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branch.Id, wh2.Id, sku1.Id, 50m, sku1.RetailPrice);
            await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branch.Id, wh2.Id, sku2.Id, 50m, sku2.RetailPrice);

            var bBal1 = await context.InventoryBalances.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId && b.BranchId == branch.Id && b.SkuId == sku1.Id);
            var whBal1_sku1 = await context.WarehouseInventoryBalances.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id && w.WarehouseId == wh1.Id && w.SkuId == sku1.Id);
            var whBal2_sku1 = await context.WarehouseInventoryBalances.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id && w.WarehouseId == wh2.Id && w.SkuId == sku1.Id);
            var bBal2 = await context.InventoryBalances.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId && b.BranchId == branch.Id && b.SkuId == sku2.Id);
            var whBal2_sku2 = await context.WarehouseInventoryBalances.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id && w.WarehouseId == wh2.Id && w.SkuId == sku2.Id);

            var initialOnHand1 = bBal1.OnHand;
            var initialOnHand2 = bBal2.OnHand;
            var initialWac1 = bBal1.AverageCost;
            var initialWac2 = bBal2.AverageCost;
            var initialSeq1 = bBal1.LastSequence;
            var initialSeq2 = bBal2.LastSequence;
            var initialTxCount = await context.InventoryTransactions.CountAsync(t => t.TenantId == OismsDatabaseSeeder.TenantAId);

            // 1. Tạo đơn hàng Admin gồm 2 mặt hàng:
            // - SKU1 (3m) được chia phân bổ qua 2 kho: 2m tại kho1 (quá hạn) + 1m tại kho2 inactive (chưa quá hạn)
            // - SKU2 (2m) tại kho2 inactive (chưa quá hạn)
            var now = DateTimeOffset.UtcNow;
            var item1 = new OrderItem
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                SkuId = sku1.Id,
                SkuCodeSnapshot = sku1.SKU,
                SkuNameSnapshot = sku1.SKU,
                Quantity = 3m,
                UnitPrice = sku1.RetailPrice,
                CostPrice = 30000m
            };
            var item2 = new OrderItem
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                SkuId = sku2.Id,
                SkuCodeSnapshot = sku2.SKU,
                SkuNameSnapshot = sku2.SKU,
                Quantity = 2m,
                UnitPrice = sku2.RetailPrice,
                CostPrice = 40000m
            };

            var order = new Order
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                BranchId = branch.Id,
                Number = $"ORD-AC03-{Guid.NewGuid():N}"[..20],
                Channel = "Admin",
                Status = "Reserved",
                RequestKey = Guid.NewGuid(),
                TotalAmount = 500000m,
                CreatedAt = now,
                Items = new List<OrderItem> { item1, item2 }
            };
            context.Orders.Add(order);

            // Phiếu 1a: SKU1 tại Kho 1 (2m), đã quá hạn (ExpiresAt <= cutoff) -> Chuyển sang Expired
            var res1a = new Reservation
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                OrderId = order.Id,
                OrderItemId = item1.Id,
                BranchId = branch.Id,
                WarehouseId = wh1.Id,
                SkuId = sku1.Id,
                Quantity = 2m,
                Status = "Active",
                ExpiresAt = now.AddMinutes(-5), // Đã quá hạn
                CreatedAt = now.AddMinutes(-20)
            };
            // Phiếu 1b: CÙNG SKU1 tại Kho 2 inactive (1m), chưa quá hạn (ExpiresAt > cutoff) -> Chuyển sang Released
            var res1b = new Reservation
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                OrderId = order.Id,
                OrderItemId = item1.Id,
                BranchId = branch.Id,
                WarehouseId = wh2.Id,
                SkuId = sku1.Id,
                Quantity = 1m,
                Status = "Active",
                ExpiresAt = now.AddMinutes(10), // Chưa quá hạn trong cùng giỏ
                CreatedAt = now
            };
            // Phiếu 2: SKU2 tại Kho 2 (kho inactive, 2m), chưa quá hạn (ExpiresAt > cutoff) -> Chuyển sang Released
            var res2 = new Reservation
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                OrderId = order.Id,
                OrderItemId = item2.Id,
                BranchId = branch.Id,
                WarehouseId = wh2.Id,
                SkuId = sku2.Id,
                Quantity = 2m,
                Status = "Active",
                ExpiresAt = now.AddMinutes(10), // Chưa quá hạn trong cùng giỏ
                CreatedAt = now
            };
            context.Reservations.AddRange(res1a, res1b, res2);

            // Cập nhật Reserved balances cho đơn Admin
            bBal1.Reserved += 3m;
            whBal1_sku1.Reserved += 2m;
            whBal2_sku1.Reserved += 1m;
            bBal2.Reserved += 2m;
            whBal2_sku2.Reserved += 2m;

            // 2. Tạo đơn Legacy unpaid Draft do ReservationService tạo
            var legacyItem = new OrderItem
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                SkuId = sku1.Id,
                SkuCodeSnapshot = sku1.SKU,
                SkuNameSnapshot = sku1.SKU,
                Quantity = 1m,
                UnitPrice = sku1.RetailPrice,
                CostPrice = 30000m
            };
            var legacyOrder = new Order
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                BranchId = branch.Id,
                Number = $"ORD-LEGACY-{Guid.NewGuid():N}"[..20],
                Channel = "POS",
                Status = "Draft",
                RequestKey = Guid.NewGuid(),
                TotalAmount = 100000m,
                CreatedAt = now.AddMinutes(-30),
                Items = new List<OrderItem> { legacyItem }
            };
            var legacyRes = new Reservation
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                OrderId = legacyOrder.Id,
                OrderItemId = legacyItem.Id,
                BranchId = branch.Id,
                WarehouseId = wh1.Id,
                SkuId = sku1.Id,
                Quantity = 1m,
                Status = "Active",
                ExpiresAt = now.AddMinutes(-10),
                CreatedAt = now.AddMinutes(-30)
            };
            context.Orders.Add(legacyOrder);
            context.Reservations.Add(legacyRes);
            bBal1.Reserved += 1m;
            whBal1_sku1.Reserved += 1m;

            // 3. Tạo đơn Marketplace Shopee quá hạn có WebhookPayloadSignature, ExternalShopId, ExternalOrderId
            var mktItem = new OrderItem
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                SkuId = sku1.Id,
                SkuCodeSnapshot = sku1.SKU,
                SkuNameSnapshot = sku1.SKU,
                Quantity = 2m,
                UnitPrice = sku1.RetailPrice,
                CostPrice = 32000m
            };
            var mktOrder = new Order
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                BranchId = branch.Id,
                Number = $"ORD-MKT-{Guid.NewGuid():N}"[..20],
                Channel = "Shopee",
                ExternalShopId = "SHOP-TEST-01",
                ExternalOrderId = "SHOPEE-AC03-99999",
                WebhookPayloadSignature = "signature-test-shopee-ac03",
                Status = "Reserved",
                RequestKey = Guid.NewGuid(),
                TotalAmount = sku1.RetailPrice * 2m,
                CreatedAt = now.AddMinutes(-25),
                Items = new List<OrderItem> { mktItem }
            };
            var mktRes = new Reservation
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                OrderId = mktOrder.Id,
                OrderItemId = mktItem.Id,
                BranchId = branch.Id,
                WarehouseId = wh1.Id,
                SkuId = sku1.Id,
                Quantity = 2m,
                Status = "Active",
                ExpiresAt = now.AddMinutes(-12),
                CreatedAt = now.AddMinutes(-25)
            };
            context.Orders.Add(mktOrder);
            context.Reservations.Add(mktRes);
            bBal1.Reserved += 2m;
            whBal1_sku1.Reserved += 2m;

            await context.SaveChangesAsync();

            // 4. Thực thi quét hết hạn đơn hàng với notifier ghi nhận sự kiện
            var recordingNotifier = new RecordingRealtimeNotifier();
            var orderService = new OrderService(context, tenantService, new Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>(), recordingNotifier);
            var cancelledCount = await orderService.ExpireOverdueReservationsAsync(now);
            cancelledCount.Should().Be(3, "Phải hủy thành công cả 3 đơn hàng quá hạn (đơn Admin multi-SKU multi-warehouse, đơn Legacy Draft, và đơn Marketplace Shopee).");

            // 5. Kiểm tra dữ liệu sau khi giải phóng:
            context.ChangeTracker.Clear();
            var checkOrder = await context.Orders.Include(o => o.Reservations).FirstAsync(o => o.Id == order.Id);
            checkOrder.Status.Should().Be("Cancelled");
            checkOrder.CancelledAt.Should().NotBeNull();

            // Kiểm tra trạng thái phân bổ mixed Expired/Released trên CÙNG SKU1:
            var checkRes1a = checkOrder.Reservations.First(r => r.Id == res1a.Id);
            checkRes1a.Status.Should().Be("Expired", "Phiếu Active hết hạn tại kho 1 phải chuyển sang Expired.");
            checkRes1a.WarehouseId.Should().Be(wh1.Id);

            var checkRes1b = checkOrder.Reservations.First(r => r.Id == res1b.Id);
            checkRes1b.Status.Should().Be("Released", "Phiếu Active còn hạn của CÙNG SKU1 tại kho 2 inactive phải chuyển sang Released.");
            checkRes1b.WarehouseId.Should().Be(wh2.Id);

            var checkRes2 = checkOrder.Reservations.First(r => r.Id == res2.Id);
            checkRes2.Status.Should().Be("Released", "Phiếu Active còn hạn của SKU2 tại kho 2 inactive phải chuyển sang Released.");

            var checkLegacyOrder = await context.Orders.Include(o => o.Reservations).FirstAsync(o => o.Id == legacyOrder.Id);
            checkLegacyOrder.Status.Should().Be("Cancelled");
            checkLegacyOrder.Reservations.First().Status.Should().Be("Expired");

            var checkMktOrder = await context.Orders.Include(o => o.Reservations).Include(o => o.Items).FirstAsync(o => o.Id == mktOrder.Id);
            checkMktOrder.Status.Should().Be("Cancelled");
            checkMktOrder.CancelledAt.Should().NotBeNull();
            checkMktOrder.ExternalOrderId.Should().Be("SHOPEE-AC03-99999");
            checkMktOrder.WebhookPayloadSignature.Should().Be("signature-test-shopee-ac03");
            checkMktOrder.ExternalShopId.Should().Be("SHOP-TEST-01");
            checkMktOrder.Items.First().CostPrice.Should().Be(32000m);
            checkMktOrder.Reservations.First().Status.Should().Be("Expired");

            // Kiểm tra số dư tồn từng kho và chi nhánh:
            var finalBBal1 = await context.InventoryBalances.FirstAsync(b => b.BranchId == branch.Id && b.SkuId == sku1.Id);
            var finalWhBal1_sku1 = await context.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == wh1.Id && w.SkuId == sku1.Id);
            var finalWhBal2_sku1 = await context.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == wh2.Id && w.SkuId == sku1.Id);
            var finalBBal2 = await context.InventoryBalances.FirstAsync(b => b.BranchId == branch.Id && b.SkuId == sku2.Id);
            var finalWhBal2_sku2 = await context.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == wh2.Id && w.SkuId == sku2.Id);

            finalBBal1.OnHand.Should().Be(initialOnHand1);
            finalBBal2.OnHand.Should().Be(initialOnHand2);
            finalBBal1.AverageCost.Should().Be(initialWac1);
            finalBBal2.AverageCost.Should().Be(initialWac2);
            finalBBal1.LastSequence.Should().Be(initialSeq1);
            finalBBal2.LastSequence.Should().Be(initialSeq2);

            // Kiểm tra Reserved balances được khấu trừ đúng cho từng kho:
            finalWhBal1_sku1.Reserved.Should().Be(0m, "Số dư giữ chỗ SKU1 tại kho 1 phải được giải phóng về 0.");
            finalWhBal2_sku1.Reserved.Should().Be(0m, "Số dư giữ chỗ SKU1 tại kho 2 inactive phải được giải phóng về 0.");
            finalWhBal2_sku2.Reserved.Should().Be(0m, "Số dư giữ chỗ SKU2 tại kho 2 inactive phải được giải phóng về 0.");

            // Kiểm tra khớp nối deferred trigger: sum(Active) == Reserved
            var activeResSku1 = await context.Reservations.Where(r => r.TenantId == OismsDatabaseSeeder.TenantAId && r.BranchId == branch.Id && r.SkuId == sku1.Id && r.Status == "Active").SumAsync(r => r.Quantity);
            finalBBal1.Reserved.Should().Be(activeResSku1);
            finalBBal1.Reserved.Should().Be(0m);

            var activeResSku2 = await context.Reservations.Where(r => r.TenantId == OismsDatabaseSeeder.TenantAId && r.BranchId == branch.Id && r.SkuId == sku2.Id && r.Status == "Active").SumAsync(r => r.Quantity);
            finalBBal2.Reserved.Should().Be(activeResSku2);
            finalBBal2.Reserved.Should().Be(0m);

            // Kiểm tra sự kiện realtime: SKU1 dù được phân bổ ở cả 2 kho (kho 1 và kho 2) nhưng chỉ phát đúng 1 sự kiện gộp theo SKU trên mỗi order
            var sku1EventsForOrder = recordingNotifier.StockChangedCalls.Where(c => c.SkuId == sku1.Id).ToList();
            sku1EventsForOrder.Count.Should().Be(3, "Mỗi order dọn SKU1 phát đúng 1 sự kiện gộp theo SKU (Admin, Legacy, Marketplace).");

            var sku2EventsForOrder = recordingNotifier.StockChangedCalls.Where(c => c.SkuId == sku2.Id).ToList();
            sku2EventsForOrder.Count.Should().Be(1, "Order Admin dọn SKU2 phát đúng 1 sự kiện.");

            // Kiểm tra sổ kho / ledger: không được sinh bất kỳ bản ghi InventoryTransaction nào do expiry
            var finalTxCount = await context.InventoryTransactions.CountAsync(t => t.TenantId == OismsDatabaseSeeder.TenantAId);
            finalTxCount.Should().Be(initialTxCount, "Expiry giải phóng giữ chỗ tuyệt đối không được ghi sổ kho / tạo inventory transaction.");
        }
    }

    #endregion

    #region AC-04: Boundary Precision & No-Op

    [Fact]
    public async Task AC04_BoundaryPrecisionAndNoOp()
    {
        await SeedDatabaseAsync();
        var (context, tenantService) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);

        await using (context)
        {
            var branch = await context.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var wh = await context.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await context.Skus.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branch.Id, wh.Id, sku.Id, 50m, sku.RetailPrice);

            var bBal = await context.InventoryBalances.FirstAsync(b => b.BranchId == branch.Id && b.SkuId == sku.Id);
            var whBal = await context.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == wh.Id && w.SkuId == sku.Id);

            var cutoff = DateTimeOffset.UtcNow;

            // 1. Đơn A: ExpiresAt == cutoff (Chính xác tại mốc biên -> Phải Hết Hạn)
            var orderA = CreateTestOrderWithReservation(branch.Id, wh.Id, sku.Id, sku.RetailPrice, 1m, cutoff, "Reserved");

            // 2. Đơn B: ExpiresAt == cutoff + 10ms (Sau mốc biên -> CHƯA Hết Hạn)
            var orderB = CreateTestOrderWithReservation(branch.Id, wh.Id, sku.Id, sku.RetailPrice, 1m, cutoff.AddMilliseconds(10), "Reserved");

            // 3. Đơn C: Đã Confirmed -> Bỏ qua, không được giải phóng
            var orderC = CreateTestOrderWithReservation(branch.Id, wh.Id, sku.Id, sku.RetailPrice, 1m, cutoff.AddMinutes(-5), "Confirmed");
            orderC.ConfirmedAt = cutoff.AddMinutes(-6);

            // 4. Đơn D: POS Paid -> Bỏ qua, không được hủy
            var orderD = CreateTestOrderWithReservation(branch.Id, wh.Id, sku.Id, sku.RetailPrice, 1m, cutoff.AddMinutes(-5), "Reserved");
            orderD.Channel = "POS";
            orderD.Payment = new Payment
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                OrderId = orderD.Id,
                Amount = sku.RetailPrice,
                CashReceived = sku.RetailPrice,
                Method = "Cash",
                Status = "Succeeded",
                PaidAt = cutoff.AddMinutes(-6),
                CreatedAt = cutoff.AddMinutes(-6)
            };

            context.Orders.AddRange(orderA, orderB, orderC, orderD);
            bBal.Reserved += 3m; // cho orderA, orderB và orderD (Active reservations)
            whBal.Reserved += 3m;

            await context.SaveChangesAsync();

            var orderService = new OrderService(context, tenantService, new Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>(), null);

            // Lượt 1: Quét với cutoff chính xác
            var cancelledCount = await orderService.ExpireOverdueReservationsAsync(cutoff);
            cancelledCount.Should().Be(1, "Chỉ duy nhất Đơn A (ExpiresAt <= cutoff) được hủy. Đơn B chưa đến hạn, Đơn C đã Confirm, Đơn D đã Paid.");

            context.ChangeTracker.Clear();
            var checkA = await context.Orders.Include(o => o.Reservations).FirstAsync(o => o.Id == orderA.Id);
            checkA.Status.Should().Be("Cancelled");
            checkA.Reservations.First().Status.Should().Be("Expired");

            var checkB = await context.Orders.Include(o => o.Reservations).FirstAsync(o => o.Id == orderB.Id);
            checkB.Status.Should().Be("Reserved", "Đơn B có ExpiresAt > cutoff không được đổi trạng thái.");
            checkB.Reservations.First().Status.Should().Be("Active");

            var checkC = await context.Orders.FirstAsync(o => o.Id == orderC.Id);
            checkC.Status.Should().Be("Confirmed");

            // Snapshot các mốc thời gian và balances trước khi gọi lại
            var snapOrderBUpdated = checkB.UpdatedAt;
            var snapOrderCUpdated = checkC.UpdatedAt;
            var snapBal = await context.InventoryBalances.FirstAsync(b => b.BranchId == branch.Id && b.SkuId == sku.Id);
            var snapBalUpdated = snapBal.UpdatedAt;
            var snapOnHand = snapBal.OnHand;
            var snapWac = snapBal.AverageCost;
            var snapSeq = snapBal.LastSequence;

            // Lượt 2: Gọi lại với cùng cutoff -> Phải no-op (trả về 0)
            var repeatCount = await orderService.ExpireOverdueReservationsAsync(cutoff);
            repeatCount.Should().Be(0, "Lượt gọi lại không có đơn mới quá hạn phải trả về 0.");

            context.ChangeTracker.Clear();
            var checkB_after = await context.Orders.FirstAsync(o => o.Id == orderB.Id);
            var checkC_after = await context.Orders.FirstAsync(o => o.Id == orderC.Id);
            var checkBal_after = await context.InventoryBalances.FirstAsync(b => b.BranchId == branch.Id && b.SkuId == sku.Id);

            checkB_after.UpdatedAt.Should().Be(snapOrderBUpdated, "Lượt gọi no-op tuyệt đối không được cập nhật UpdatedAt của đơn hàng.");
            checkC_after.UpdatedAt.Should().Be(snapOrderCUpdated, "Lượt gọi no-op tuyệt đối không được cập nhật UpdatedAt của đơn Confirmed.");
            checkBal_after.UpdatedAt.Should().Be(snapBalUpdated, "Lượt gọi no-op tuyệt đối không được cập nhật UpdatedAt của số dư tồn kho.");
            checkBal_after.OnHand.Should().Be(snapOnHand);
            checkBal_after.AverageCost.Should().Be(snapWac);
            checkBal_after.LastSequence.Should().Be(snapSeq);
        }
    }

    private static Order CreateTestOrderWithReservation(Guid branchId, Guid warehouseId, Guid skuId, decimal price, decimal qty, DateTimeOffset expiresAt, string status)
    {
        var createdAt = expiresAt.AddMinutes(-15);
        var item = new OrderItem
        {
            Id = Guid.NewGuid(),
            TenantId = OismsDatabaseSeeder.TenantAId,
            SkuId = skuId,
            SkuCodeSnapshot = "SKU-BND",
            SkuNameSnapshot = "SKU Boundary",
            Quantity = qty,
            UnitPrice = price,
            CostPrice = 30000m
        };

        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = OismsDatabaseSeeder.TenantAId,
            BranchId = branchId,
            Number = $"ORD-BND-{Guid.NewGuid():N}"[..20],
            Channel = "Admin",
            Status = status,
            RequestKey = Guid.NewGuid(),
            TotalAmount = price * qty,
            CreatedAt = createdAt,
            Items = new List<OrderItem> { item }
        };

        var res = new Reservation
        {
            Id = Guid.NewGuid(),
            TenantId = OismsDatabaseSeeder.TenantAId,
            OrderId = order.Id,
            OrderItemId = item.Id,
            BranchId = branchId,
            WarehouseId = warehouseId,
            SkuId = skuId,
            Quantity = qty,
            Status = status == "Confirmed" ? "Consumed" : "Active",
            ExpiresAt = expiresAt,
            CreatedAt = createdAt
        };
        order.Reservations = new List<Reservation> { res };
        return order;
    }

    #endregion

    #region AC-05: Tenant Background Isolation

    [Fact]
    public async Task AC05_TenantBackgroundIsolation()
    {
        await SeedDatabaseAsync();

        // 1. Gọi trực tiếp IOrderService.ExpireOverdueReservationsAsync khi TenantId = null -> Phải ném UnauthorizedAccessException
        var (contextNoTenant, tenantServiceNoTenant) = CreatePostgresDbContext(null);
        await using (contextNoTenant)
        {
            var orderServiceNoTenant = new OrderService(contextNoTenant, tenantServiceNoTenant, new Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>(), null);
            var actNullTenant = async () => await orderServiceNoTenant.ExpireOverdueReservationsAsync();
            await actNullTenant.Should().ThrowAsync<UnauthorizedAccessException>("Cấm quét hết hạn mà không xác định TenantId.");
        }

        // 2. Tạo đơn quá hạn tại Tenant A và Tenant B
        var now = DateTimeOffset.UtcNow;
        Guid orderAId, orderBId;

        var (dbA, _) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
        await using (dbA)
        {
            var branchA = await dbA.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var whA = await dbA.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId);
            var skuA = await dbA.Skus.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branchA.Id, whA.Id, skuA.Id, 50m, skuA.RetailPrice);
            var balA = await dbA.InventoryBalances.FirstAsync(b => b.BranchId == branchA.Id && b.SkuId == skuA.Id);
            var whBalA = await dbA.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == whA.Id && w.SkuId == skuA.Id);

            var ordA = CreateTestOrderWithReservation(branchA.Id, whA.Id, skuA.Id, skuA.RetailPrice, 1m, now.AddMinutes(-5), "Reserved");
            ordA.TenantId = OismsDatabaseSeeder.TenantAId;
            ordA.Items.First().TenantId = OismsDatabaseSeeder.TenantAId;
            ordA.Reservations.First().TenantId = OismsDatabaseSeeder.TenantAId;
            dbA.Orders.Add(ordA);
            balA.Reserved += 1m;
            whBalA.Reserved += 1m;
            await dbA.SaveChangesAsync();
            orderAId = ordA.Id;
        }

        var (dbB, _) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantBId);
        await using (dbB)
        {
            var branchB = await dbB.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantBId);
            var whB = await dbB.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantBId);
            var skuB = await dbB.Skus.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantBId);
            await SeedStockAsync(OismsDatabaseSeeder.TenantBId, branchB.Id, whB.Id, skuB.Id, 50m, skuB.RetailPrice);
            var balB = await dbB.InventoryBalances.FirstAsync(b => b.BranchId == branchB.Id && b.SkuId == skuB.Id);
            var whBalB = await dbB.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == whB.Id && w.SkuId == skuB.Id);

            var ordB = CreateTestOrderWithReservation(branchB.Id, whB.Id, skuB.Id, skuB.RetailPrice, 1m, now.AddMinutes(-5), "Reserved");
            ordB.TenantId = OismsDatabaseSeeder.TenantBId;
            ordB.Items.First().TenantId = OismsDatabaseSeeder.TenantBId;
            ordB.Reservations.First().TenantId = OismsDatabaseSeeder.TenantBId;
            dbB.Orders.Add(ordB);
            balB.Reserved += 1m;
            whBalB.Reserved += 1m;
            await dbB.SaveChangesAsync();
            orderBId = ordB.Id;
        }

        // 3. Tác vụ ReservationExpiryJob quét cross-tenant trong clean scope riêng biệt cho từng tenant
        using (var scope = _factory.Services.CreateScope())
        {
            var expiryJob = scope.ServiceProvider.GetRequiredService<IReservationExpiryJob>();
            var totalCancelled = await expiryJob.ExecuteAsync(now);
            totalCancelled.Should().Be(2, "Job nền phải xử lý và hủy thành công cả đơn của Tenant A và Tenant B.");
        }

        // Xác minh cả hai đơn hàng đều đã chuyển sang Cancelled tại từng tenant
        var (verifyDbA, _) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
        await using (verifyDbA)
        {
            var ordA = await verifyDbA.Orders.FirstAsync(o => o.Id == orderAId);
            ordA.Status.Should().Be("Cancelled");
            ordA.TenantId.Should().Be(OismsDatabaseSeeder.TenantAId);

            // Xác minh Tenant A không truy cập được đơn của Tenant B
            var hasCrossTenant = await verifyDbA.Orders.AnyAsync(o => o.Id == orderBId);
            hasCrossTenant.Should().BeFalse("Global query filter phải ngăn cách rò rỉ đơn hàng sang Tenant A.");
        }

        var (verifyDbB, _) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantBId);
        await using (verifyDbB)
        {
            var ordB = await verifyDbB.Orders.FirstAsync(o => o.Id == orderBId);
            ordB.Status.Should().Be("Cancelled");
            ordB.TenantId.Should().Be(OismsDatabaseSeeder.TenantBId);
        }
    }

    #endregion

    #region AC-06: Idempotent Retry & Overlap

    [Fact]
    public async Task AC06_IdempotentRetryAndOverlap()
    {
        await SeedDatabaseAsync();
        var (context, tenantService) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);

        await using (context)
        {
            var branch = await context.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var wh = await context.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await context.Skus.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branch.Id, wh.Id, sku.Id, 50m, sku.RetailPrice);

            var bBal = await context.InventoryBalances.FirstAsync(b => b.BranchId == branch.Id && b.SkuId == sku.Id);
            var whBal = await context.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == wh.Id && w.SkuId == sku.Id);

            var now = DateTimeOffset.UtcNow;
            var order = CreateTestOrderWithReservation(branch.Id, wh.Id, sku.Id, sku.RetailPrice, 2m, now.AddMinutes(-10), "Reserved");
            context.Orders.Add(order);
            bBal.Reserved += 2m;
            whBal.Reserved += 2m;
            await context.SaveChangesAsync();

            // 1. Chạy song song tối thiểu 2 worker / context trên cùng 1 đơn hàng quá hạn với notifier ghi nhận sự kiện
            var recordingNotifier = new RecordingRealtimeNotifier();
            var barrier = new Barrier(2);
            var task1 = Task.Run(async () =>
            {
                var (ctx1, ts1) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
                await using (ctx1)
                {
                    var svc1 = new OrderService(ctx1, ts1, new Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>(), recordingNotifier);
                    barrier.SignalAndWait();
                    return await svc1.ExpireOverdueReservationsAsync(now);
                }
            });

            var task2 = Task.Run(async () =>
            {
                var (ctx2, ts2) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
                await using (ctx2)
                {
                    var svc2 = new OrderService(ctx2, ts2, new Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>(), recordingNotifier);
                    barrier.SignalAndWait();
                    return await svc2.ExpireOverdueReservationsAsync(now);
                }
            });

            var results = await Task.WhenAll(task1, task2);
            var totalCommits = results.Sum();

            totalCommits.Should().Be(1, "Chỉ duy nhất 1 worker được phép commit hủy đơn hàng. Worker còn lại phải no-op.");
            recordingNotifier.StockChangedCalls.Count.Should().Be(1, "Chỉ duy nhất 1 logical release event được phát, không có event lặp.");

            // 2. Chạy lại tuần tự lần thứ 3 -> Kết quả = 0, không đổi timestamps hoặc balances lần 2
            var (ctx3, ts3) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
            await using (ctx3)
            {
                var checkOrderBefore = await ctx3.Orders.FirstAsync(o => o.Id == order.Id);
                var snapCancelledAt = checkOrderBefore.CancelledAt;
                var snapOrderUpdated = checkOrderBefore.UpdatedAt;

                var initialBBal = await ctx3.InventoryBalances.FirstAsync(b => b.BranchId == branch.Id && b.SkuId == sku.Id);
                var snapBalUpdated = initialBBal.UpdatedAt;

                var svc3 = new OrderService(ctx3, ts3, new Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>(), recordingNotifier);
                var rerunCount = await svc3.ExpireOverdueReservationsAsync(now);
                rerunCount.Should().Be(0, "Lần chạy tuần tự tiếp theo phải trả về 0.");
                recordingNotifier.StockChangedCalls.Count.Should().Be(1, "Rerun no-op tuyệt đối không phát thêm sự kiện mới.");

                ctx3.ChangeTracker.Clear();
                var checkOrderAfter = await ctx3.Orders.FirstAsync(o => o.Id == order.Id);
                checkOrderAfter.CancelledAt.Should().Be(snapCancelledAt, "Rerun không được thay đổi CancelledAt.");
                checkOrderAfter.UpdatedAt.Should().Be(snapOrderUpdated, "Rerun không được thay đổi UpdatedAt của đơn hàng.");

                // Kiểm tra Reserved balance không bị trừ 2 lần ở cả hai cấp kho và chi nhánh
                var finalBBal = await ctx3.InventoryBalances.FirstAsync(b => b.BranchId == branch.Id && b.SkuId == sku.Id);
                var finalWhBal = await ctx3.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == wh.Id && w.SkuId == sku.Id);
                var activeSum = await ctx3.Reservations.Where(r => r.BranchId == branch.Id && r.SkuId == sku.Id && r.Status == "Active").SumAsync(r => r.Quantity);

                finalBBal.Reserved.Should().Be(activeSum, "Reserved balance chi nhánh phải khớp hoàn hảo với tổng Active reservations, không bị underflow.");
                finalBBal.Reserved.Should().Be(0m);
                finalWhBal.Reserved.Should().Be(0m);
                finalBBal.OnHand.Should().Be(50m);
                finalWhBal.OnHand.Should().Be(50m);
                finalBBal.UpdatedAt.Should().Be(snapBalUpdated, "Rerun no-op không được thay đổi UpdatedAt của số dư tồn.");
            }
        }
    }

    #endregion

    #region AC-07: Expire vs Confirm / Cancel Đúng Lịch

    [Fact]
    public async Task AC07_ExpiryVsConfirmCancelConcurrency()
    {
        await SeedDatabaseAsync();

        async Task RunGatedRaceAsync(string scenario, string winnerName, string loserName)
        {
            var now = DateTimeOffset.UtcNow;
            var expires = winnerName == "confirm" ? now.AddMinutes(10) : now.AddMinutes(-5);
            var cutoff = winnerName == "confirm" ? now.AddMinutes(20) : now;

            var (setupCtx, ts) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
            Guid user;
            Guid branchId;
            Guid whId;
            Guid skuId;
            Order order;
            await using (setupCtx)
            {
                user = await setupCtx.Users.Select(u => u.Id).FirstAsync();
                var branch = await setupCtx.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
                var wh = await setupCtx.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId);
                var sku = await setupCtx.Skus.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
                branchId = branch.Id;
                whId = wh.Id;
                skuId = sku.Id;
                await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branchId, whId, skuId, 50m, sku.RetailPrice);

                order = CreateTestOrderWithReservation(branchId, whId, skuId, sku.RetailPrice, 1m, expires, "Reserved");
                setupCtx.Orders.Add(order);
                (await setupCtx.InventoryBalances.SingleAsync(b => b.BranchId == branchId && b.SkuId == skuId)).Reserved += 1m;
                (await setupCtx.WarehouseInventoryBalances.SingleAsync(b => b.WarehouseId == whId && b.SkuId == skuId)).Reserved += 1m;
                await setupCtx.SaveChangesAsync();
            }

            var gate = new BeforeCommitGate();
            var attempt = new FirstSkuAttempt();
            var notifier = new RecordingRealtimeNotifier();

            var (winCtx, winTs) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId, gate);
            var (loseCtx, loseTs) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId, attempt);
            await using (winCtx)
            await using (loseCtx)
            {
                var winSvc = new OrderService(winCtx, winTs, new Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>(), notifier);
                var loseSvc = new OrderService(loseCtx, loseTs, new Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>(), notifier);

                async Task<string> ExecuteAction(OrderService svc, string actionName)
                {
                    try
                    {
                        return actionName switch
                        {
                            "confirm" => (await svc.ConfirmOrderAsync(order.Id, new ConfirmOrderRequestDto(), user)).Status,
                            "cancel" => (await svc.CancelOrderAsync(order.Id, new CancelOrderRequestDto { Reason = "Race test" }, user)).Status,
                            _ => "expiry:" + await svc.ExpireOverdueReservationsAsync(cutoff)
                        };
                    }
                    catch (OismException ex) { return "business-error:" + ex.ErrorCode; }
                }

                var winTask = ExecuteAction(winSvc, winnerName);
                await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));

                var loseTask = ExecuteAction(loseSvc, loserName);
                await attempt.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));

                // Quan sát PostgreSQL cluster xác nhận loser thực sự bị block trên wait_event = 'advisory'
                var observedWaiting = false;
                var deadline = DateTime.UtcNow.AddSeconds(5);
                await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.RuntimeConnectionString))
                {
                    await conn.OpenAsync();
                    while (DateTime.UtcNow < deadline)
                    {
                        await using var cmd = new NpgsqlCommand("SELECT wait_event FROM pg_stat_activity WHERE pid = @pid", conn);
                        cmd.Parameters.AddWithValue("pid", attempt.Pid);
                        var waitEvent = (await cmd.ExecuteScalarAsync()) as string;
                        if (waitEvent == "advisory")
                        {
                            observedWaiting = true;
                            break;
                        }
                        await Task.Delay(50);
                    }
                }
                observedWaiting.Should().BeTrue("Thao tác thua bắt buộc phải quan sát được bị chặn trên wait_event = 'advisory' của PostgreSQL cluster.");

                gate.Release.TrySetResult();
                var outcomes = await Task.WhenAll(winTask, loseTask);

                // Đối soát tính nhất quán
                var (verifyCtx, _) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
                await using (verifyCtx)
                {
                    var finalOrder = await verifyCtx.Orders.SingleAsync(o => o.Id == order.Id);
                    var bBal = await verifyCtx.InventoryBalances.SingleAsync(b => b.BranchId == branchId && b.SkuId == skuId);
                    var whBal = await verifyCtx.WarehouseInventoryBalances.SingleAsync(b => b.WarehouseId == whId && b.SkuId == skuId);

                    bBal.Reserved.Should().Be(0m, "Số dư giữ chỗ chi nhánh phải về 0 sau khi hoàn tất.");
                    whBal.Reserved.Should().Be(0m, "Số dư giữ chỗ kho phải về 0 sau khi hoàn tất.");

                    if (winnerName == "confirm")
                    {
                        outcomes[0].Should().Be("Confirmed");
                        outcomes[1].Should().Be("expiry:0");
                        finalOrder.Status.Should().Be("Confirmed");
                    }
                    else if (winnerName == "cancel")
                    {
                        outcomes[0].Should().Be("Cancelled");
                        outcomes[1].Should().Be("expiry:0");
                        finalOrder.Status.Should().Be("Cancelled");
                    }
                    else // winner is expiry
                    {
                        outcomes[0].Should().Be("expiry:1");
                        outcomes[1].Should().Be("business-error:INVALID_STATE_TRANSITION");
                        finalOrder.Status.Should().Be("Cancelled");
                    }
                }
            }
        }

        await RunGatedRaceAsync("confirm_holds_lock_expiry_waits", "confirm", "expiry");
        await RunGatedRaceAsync("expiry_holds_lock_confirm_waits", "expiry", "confirm");
        await RunGatedRaceAsync("cancel_holds_lock_expiry_waits", "cancel", "expiry");
        await RunGatedRaceAsync("expiry_holds_lock_cancel_waits", "expiry", "cancel");
    }

    #endregion

    #region AC-08: Lock Hierarchy & Cross-Flow Concurrency (Observer & Positive Control)

    [Fact]
    public async Task AC08_LockHierarchyAndCrossFlow_NoDeadlock_SqlStateObserver()
    {
        await SeedDatabaseAsync();

        // 1. Nối observer vào đúng DbContext options qua interceptor
        var observer = new SqlStateDeadlockObserver();
        var (ctx, ts) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId, observer);

        await using (ctx)
        {
            // 2. Positive control: Chứng minh observer cùng wiring thật bắt được lỗi SQLSTATE 40P01 nhân tạo
            var actControl = async () => await ctx.Database.ExecuteSqlRawAsync("DO $$ BEGIN RAISE EXCEPTION 'deadlock test' USING ERRCODE = '40P01'; END $$;");
            await actControl.Should().ThrowAsync<PostgresException>().Where(ex => ex.SqlState == "40P01");
            observer.Deadlock40P01Count.Should().Be(1, "Positive control chứng minh observer bắt được 40P01 qua DbContext options.");

            // Reset observer sạch trước khi vào race
            observer.Reset();
            observer.Deadlock40P01Count.Should().Be(0, "Observer phải được reset sạch trước khi bắt đầu race.");

            // 3. Chuẩn bị dữ liệu race multi-SKU đảo thứ tự:
            // Order A (sẽ bị Expiry dọn): [SKU_1, SKU_2]
            // Order B (sẽ Checkout POS): [SKU_2, SKU_1]
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var wh = await ctx.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku1 = await ctx.Skus.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await ctx.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);

            var product = await ctx.Products.FirstAsync(p => p.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku2 = new ProductVariant
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                ProductId = product.Id,
                SKU = $"POLO-RACE-2-{Guid.NewGuid():N}"[..20],
                RetailPrice = 180000m,
                IsActive = true
            };
            ctx.ProductVariants.Add(sku2);
            await ctx.SaveChangesAsync();

            await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branch.Id, wh.Id, sku1.Id, 50m, sku1.RetailPrice);
            await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branch.Id, wh.Id, sku2.Id, 50m, sku2.RetailPrice);

            var bBal1 = await ctx.InventoryBalances.FirstAsync(b => b.BranchId == branch.Id && b.SkuId == sku1.Id);
            var bBal2 = await ctx.InventoryBalances.FirstAsync(b => b.BranchId == branch.Id && b.SkuId == sku2.Id);
            var whBal1 = await ctx.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == wh.Id && w.SkuId == sku1.Id);
            var whBal2 = await ctx.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == wh.Id && w.SkuId == sku2.Id);

            var now = DateTimeOffset.UtcNow;
            var itemA1 = new OrderItem { Id = Guid.NewGuid(), TenantId = OismsDatabaseSeeder.TenantAId, SkuId = sku1.Id, SkuCodeSnapshot = sku1.SKU, SkuNameSnapshot = sku1.SKU, Quantity = 1m, UnitPrice = sku1.RetailPrice };
            var itemA2 = new OrderItem { Id = Guid.NewGuid(), TenantId = OismsDatabaseSeeder.TenantAId, SkuId = sku2.Id, SkuCodeSnapshot = sku2.SKU, SkuNameSnapshot = sku2.SKU, Quantity = 1m, UnitPrice = sku2.RetailPrice };

            var orderA = new Order
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                BranchId = branch.Id,
                Number = $"ORD-RACE-A-{Guid.NewGuid():N}"[..20],
                Channel = "Admin",
                Status = "Reserved",
                RequestKey = Guid.NewGuid(),
                TotalAmount = sku1.RetailPrice + sku2.RetailPrice,
                CreatedAt = now.AddMinutes(-20),
                Items = new List<OrderItem> { itemA1, itemA2 }
            };
            orderA.Reservations = new List<Reservation>
            {
                new() { Id = Guid.NewGuid(), TenantId = OismsDatabaseSeeder.TenantAId, OrderId = orderA.Id, OrderItemId = itemA1.Id, BranchId = branch.Id, WarehouseId = wh.Id, SkuId = sku1.Id, Quantity = 1m, Status = "Active", ExpiresAt = now.AddMinutes(-5), CreatedAt = now.AddMinutes(-20) },
                new() { Id = Guid.NewGuid(), TenantId = OismsDatabaseSeeder.TenantAId, OrderId = orderA.Id, OrderItemId = itemA2.Id, BranchId = branch.Id, WarehouseId = wh.Id, SkuId = sku2.Id, Quantity = 1m, Status = "Active", ExpiresAt = now.AddMinutes(-5), CreatedAt = now.AddMinutes(-20) }
            };

            bBal1.Reserved += 1m;
            bBal2.Reserved += 1m;
            whBal1.Reserved += 1m;
            whBal2.Reserved += 1m;

            ctx.Orders.Add(orderA);
            await ctx.SaveChangesAsync();

            // 4. Chạy có cổng kiểm soát (Gated multi-SKU cross-flow race):
            // Expiry dọn Order A (chứa SKU_1, SKU_2) vs Checkout POS mua [SKU_2, SKU_1] (đảo thứ tự)
            // Expiry giữ khóa advisory của cả 2 SKU tại cổng SET CONSTRAINTS (BeforeCommitGate).
            // POS bắt đầu checkout: theo thuật toán khóa chuẩn, POS sắp xếp SKU tăng dần nên cố gắng lấy khóa SKU_1 trước.
            // POS bị chặn trên wait_event = 'advisory' của PostgreSQL cluster. Quan sát trực tiếp trên pg_stat_activity.
            var expiryGate = new BeforeCommitGate { TargetOrderId = orderA.Id };
            var posAttempt = new FirstSkuAttempt();

            var taskExpiry = Task.Run(async () =>
            {
                var (ctxExp, tsExp) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId, expiryGate, observer);
                await using (ctxExp)
                {
                    var svcExp = new OrderService(ctxExp, tsExp, new Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>(), null);
                    return await svcExp.ExpireOverdueReservationsAsync(now);
                }
            });

            // Chờ Expiry xử lý xong nghiệp vụ và dừng lại tại BeforeCommitGate (đang nắm giữ cả 2 advisory lock)
            await expiryGate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            expiryGate.HasSeenWrites.Should().BeTrue("Expiry phải đã ghi dữ liệu vào ChangeTracker/DB trước khi đến cổng SET CONSTRAINTS.");

            var taskPos = Task.Run(async () =>
            {
                var (ctxPos, tsPos) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId, posAttempt, observer);
                await using (ctxPos)
                {
                    var svcPos = new OrderService(ctxPos, tsPos, new Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>(), null);
                    // Đảo thứ tự danh sách sản phẩm: [sku2, sku1]
                    // Do thuật toán sắp xếp SKU tăng dần, POS sẽ cố gắng khóa SKU_1 trước.
                    return await svcPos.CheckoutPosAsync(new PosCheckoutRequestDto
                    {
                        RequestKey = Guid.NewGuid(),
                        BranchId = branch.Id,
                        PaymentMethod = "Cash",
                        AmountGiven = 500000m,
                        TotalAmount = sku1.RetailPrice + sku2.RetailPrice,
                        Items = new List<OrderItemRequestDto>
                        {
                            new() { SkuId = sku2.Id, SkuCode = sku2.SKU, Quantity = 1m, UnitPrice = sku2.RetailPrice },
                            new() { SkuId = sku1.Id, SkuCode = sku1.SKU, Quantity = 1m, UnitPrice = sku1.RetailPrice }
                        }
                    }, user.Id);
                }
            });

            // Chờ POS bắt đầu cố gắng lấy advisory lock của SKU đầu tiên
            await posAttempt.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));

            // Quan sát pg_stat_activity xác nhận kết nối POS thực sự bị block trên wait_event = 'advisory' vì Expiry đang giữ lock
            var observedWaiting = false;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.RuntimeConnectionString))
            {
                await conn.OpenAsync();
                while (DateTime.UtcNow < deadline)
                {
                    await using var cmd = new NpgsqlCommand("SELECT wait_event FROM pg_stat_activity WHERE pid = @pid", conn);
                    cmd.Parameters.AddWithValue("pid", posAttempt.Pid);
                    var waitEvent = (await cmd.ExecuteScalarAsync()) as string;
                    if (waitEvent == "advisory")
                    {
                        observedWaiting = true;
                        break;
                    }
                    await Task.Delay(50);
                }
            }
            observedWaiting.Should().BeTrue("POS checkout với danh sách SKU đảo thứ tự bắt buộc phải quan sát được bị chặn trên wait_event = 'advisory' của PostgreSQL cluster do Expiry đang giữ khóa.");

            // Mở gate để Expiry commit và nhả lock, POS unblock và hoàn tất
            expiryGate.Release.TrySetResult();

            var expiryCancelled = await taskExpiry;
            var posOrder = await taskPos;

            expiryCancelled.Should().Be(1, "Order A quá hạn phải được Expiry dọn dẹp thành công.");
            posOrder.Success.Should().BeTrue("POS checkout phải hoàn tất thành công.");
            posOrder.OrderId.Should().NotBeEmpty();

            // 5. Kiểm tra kết quả: Observer ghi nhận KHÔNG có deadlock (40P01 = 0)
            observer.Deadlock40P01Count.Should().Be(0, "Không được xảy ra deadlock 40P01 nhờ cơ chế khóa advisory sorted SKU ascending.");

            // Kiểm tra tính toàn vẹn tồn kho và đối soát đầy đủ mọi SKU / warehouse / branch
            var (verifyCtx, _) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
            await using (verifyCtx)
            {
                var checkPosOrder = await verifyCtx.Orders.Include(o => o.Payment).SingleAsync(o => o.Id == posOrder.OrderId);
                checkPosOrder.Status.Should().Be("Completed");
                checkPosOrder.Payment.Should().NotBeNull();
                checkPosOrder.Payment!.Status.Should().Be("Succeeded");
                var finalBBal1 = await verifyCtx.InventoryBalances.FirstAsync(b => b.BranchId == branch.Id && b.SkuId == sku1.Id);
                var finalBBal2 = await verifyCtx.InventoryBalances.FirstAsync(b => b.BranchId == branch.Id && b.SkuId == sku2.Id);
                var finalWhBal1 = await verifyCtx.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == wh.Id && w.SkuId == sku1.Id);
                var finalWhBal2 = await verifyCtx.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == wh.Id && w.SkuId == sku2.Id);

                // Đối soát Reserved và Active sum cho cả SKU 1 và SKU 2
                var activeSum1 = await verifyCtx.Reservations.Where(r => r.BranchId == branch.Id && r.SkuId == sku1.Id && r.Status == "Active").SumAsync(r => r.Quantity);
                var activeSum2 = await verifyCtx.Reservations.Where(r => r.BranchId == branch.Id && r.SkuId == sku2.Id && r.Status == "Active").SumAsync(r => r.Quantity);

                finalBBal1.Reserved.Should().Be(activeSum1);
                finalBBal2.Reserved.Should().Be(activeSum2);
                finalWhBal1.Reserved.Should().Be(activeSum1);
                finalWhBal2.Reserved.Should().Be(activeSum2);

                finalBBal1.Reserved.Should().Be(0m);
                finalBBal2.Reserved.Should().Be(0m);
                finalWhBal1.Reserved.Should().Be(0m);
                finalWhBal2.Reserved.Should().Be(0m);

                // OnHand: POS mua 1 đơn vị mỗi SKU (ban đầu 50m) -> OnHand còn 49m cho cả 2 SKU tại cả kho và chi nhánh
                finalBBal1.OnHand.Should().Be(49m);
                finalBBal2.OnHand.Should().Be(49m);
                finalWhBal1.OnHand.Should().Be(49m);
                finalWhBal2.OnHand.Should().Be(49m);

                // Order A bị Expiry dọn phải có trạng thái Cancelled và các phiếu là Expired
                var checkOrderA = await verifyCtx.Orders.Include(o => o.Reservations).FirstAsync(o => o.Id == orderA.Id);
                checkOrderA.Status.Should().Be("Cancelled");
                checkOrderA.Reservations.Should().AllSatisfy(r => r.Status.Should().Be("Expired"));
            }
        }
    }

    #endregion

    #region AC-09: Fault Rollback & Batch Isolation

    [Fact]
    public async Task AC09_FaultRollbackAndBatchIsolation()
    {
        await SeedDatabaseAsync();
        var now = DateTimeOffset.UtcNow;

        var recordingNotifier = new RecordingRealtimeNotifier();
        var faultInterceptor = new FaultInjectingInterceptor();
        var (ctx, ts) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId, faultInterceptor);

        await using (ctx)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var wh = await ctx.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId);
            var skuFault = await ctx.Skus.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);

            // Tạo thêm SKU riêng biệt cho đơn valid để tách hoàn toàn row balances của order lỗi và order commit
            var product = await ctx.Products.FirstAsync(p => p.TenantId == OismsDatabaseSeeder.TenantAId);
            var skuValid = new ProductVariant
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                ProductId = product.Id,
                SKU = $"POLO-VALID-AC09-{Guid.NewGuid():N}"[..20],
                RetailPrice = 120000m,
                IsActive = true
            };
            ctx.ProductVariants.Add(skuValid);
            await ctx.SaveChangesAsync();

            await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branch.Id, wh.Id, skuFault.Id, 50m, skuFault.RetailPrice);
            await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branch.Id, wh.Id, skuValid.Id, 50m, skuValid.RetailPrice);

            var bBalFault = await ctx.InventoryBalances.FirstAsync(b => b.BranchId == branch.Id && b.SkuId == skuFault.Id);
            var whBalFault = await ctx.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == wh.Id && w.SkuId == skuFault.Id);

            var bBalValid = await ctx.InventoryBalances.FirstAsync(b => b.BranchId == branch.Id && b.SkuId == skuValid.Id);
            var whBalValid = await ctx.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == wh.Id && w.SkuId == skuValid.Id);

            // 1. Tạo 2 đơn quá hạn độc lập về SKU/balances:
            // - Đơn 1 (orderFault): Dùng skuFault, có WebhookPayloadSignature, Item CostPrice/UnitPrice, Payment, bị inject fault sau SaveChanges trước commit
            // - Đơn 2 (orderValid): Dùng skuValid riêng biệt, hợp lệ commit thành công
            var itemFault = new OrderItem
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                SkuId = skuFault.Id,
                SkuCodeSnapshot = skuFault.SKU,
                SkuNameSnapshot = skuFault.SKU,
                Quantity = 1m,
                UnitPrice = skuFault.RetailPrice,
                CostPrice = 45000m
            };

            var orderFault = new Order
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                BranchId = branch.Id,
                Number = $"ORD-FAULT-{Guid.NewGuid():N}"[..20],
                Channel = "Shopee",
                ExternalShopId = "SHOP-FAULT-01",
                ExternalOrderId = $"EXT-FAULT-{Guid.NewGuid():N}"[..20],
                WebhookPayloadSignature = "signature-fault-snapshot-ac09",
                Status = "Reserved",
                RequestKey = Guid.NewGuid(),
                TotalAmount = skuFault.RetailPrice,
                CreatedAt = now.AddMinutes(-25),
                Items = new List<OrderItem> { itemFault }
            };

            var resFault = new Reservation
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                OrderId = orderFault.Id,
                OrderItemId = itemFault.Id,
                BranchId = branch.Id,
                WarehouseId = wh.Id,
                SkuId = skuFault.Id,
                Quantity = 1m,
                Status = "Active",
                ExpiresAt = now.AddMinutes(-5),
                CreatedAt = now.AddMinutes(-25)
            };
            orderFault.Reservations = new List<Reservation> { resFault };

            var paymentFault = new Payment
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                OrderId = orderFault.Id,
                Method = "Cash",
                Status = "Pending",
                Amount = skuFault.RetailPrice
            };
            orderFault.Payment = paymentFault;

            var orderValid = CreateTestOrderWithReservation(branch.Id, wh.Id, skuValid.Id, skuValid.RetailPrice, 1m, now.AddMinutes(-5), "Reserved");
            orderValid.CreatedAt = now.AddMinutes(-20);

            faultInterceptor.TargetOrderId = orderFault.Id;
            faultInterceptor.ShouldFail = true;

            ctx.Orders.AddRange(orderFault, orderValid);
            bBalFault.Reserved += 1m;
            whBalFault.Reserved += 1m;
            bBalValid.Reserved += 1m;
            whBalValid.Reserved += 1m;
            await ctx.SaveChangesAsync();

            // 2. Chụp snapshot đầy đủ toàn bộ fields của đơn lỗi và balances độc lập bằng một DbContext riêng biệt trước khi chạy Expiry
            Guid snapOrderId = orderFault.Id;
            string snapStatus;
            DateTimeOffset? snapCancelledAt, snapConfirmedAt, snapCompletedAt;
            DateTimeOffset snapCreatedAt;
            decimal snapTotalAmount;
            Guid snapRequestKey;
            string? snapSig, snapShopId, snapExtOrderId;
            decimal? snapItemCost;
            decimal snapItemPrice, snapItemQty;
            string snapResStatus;
            DateTimeOffset snapResExpiresAt, snapResCreatedAt;
            decimal snapResQty;
            decimal snapBOnHand, snapBReserved, snapBWac;
            long snapBSeq;
            DateTimeOffset? snapBUpdated;
            decimal snapWhOnHand, snapWhReserved;
            long snapWhSeq;
            DateTimeOffset? snapWhUpdated;
            string snapPayStatus;
            decimal snapPayAmount;
            int snapTxCount;

            var (snapCtx, _) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
            await using (snapCtx)
            {
                var o = await snapCtx.Orders.Include(x => x.Items).Include(x => x.Reservations).Include(x => x.Payment).AsNoTracking().SingleAsync(x => x.Id == snapOrderId);
                snapStatus = o.Status;
                snapCancelledAt = o.CancelledAt;
                snapConfirmedAt = o.ConfirmedAt;
                snapCompletedAt = o.CompletedAt;
                snapCreatedAt = o.CreatedAt;
                snapTotalAmount = o.TotalAmount;
                snapRequestKey = o.RequestKey;
                snapSig = o.WebhookPayloadSignature;
                snapShopId = o.ExternalShopId;
                snapExtOrderId = o.ExternalOrderId;

                var itm = o.Items.Single();
                snapItemCost = itm.CostPrice;
                snapItemPrice = itm.UnitPrice;
                snapItemQty = itm.Quantity;

                var r = o.Reservations.Single();
                snapResStatus = r.Status;
                snapResExpiresAt = r.ExpiresAt;
                snapResCreatedAt = r.CreatedAt;
                snapResQty = r.Quantity;

                var bb = await snapCtx.InventoryBalances.AsNoTracking().SingleAsync(b => b.BranchId == branch.Id && b.SkuId == skuFault.Id);
                snapBOnHand = bb.OnHand;
                snapBReserved = bb.Reserved;
                snapBWac = bb.AverageCost;
                snapBSeq = bb.LastSequence;
                snapBUpdated = bb.UpdatedAt;

                var wb = await snapCtx.WarehouseInventoryBalances.AsNoTracking().SingleAsync(w => w.WarehouseId == wh.Id && w.SkuId == skuFault.Id);
                snapWhOnHand = wb.OnHand;
                snapWhReserved = wb.Reserved;
                snapWhSeq = wb.LastSequence;
                snapWhUpdated = wb.UpdatedAt;

                snapPayStatus = o.Payment!.Status;
                snapPayAmount = o.Payment.Amount;

                snapTxCount = await snapCtx.InventoryTransactions.AsNoTracking().CountAsync(t => t.TenantId == OismsDatabaseSeeder.TenantAId);
            }

            // 3. Thực thi Expiry:
            // Đơn lỗi gặp sự cố sau SaveChanges trước commit -> Rollback độc lập
            // Đơn valid commit thành công (Batch Isolation)
            var svc = new OrderService(ctx, ts, new Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>(), recordingNotifier);
            var count = await svc.ExpireOverdueReservationsAsync(now);
            count.Should().Be(1, "Một đơn lỗi không được ngăn chặn đơn độc lập trong batch. Chỉ đơn hợp lệ được commit.");
            faultInterceptor.HasSeenWrites.Should().BeTrue("Lỗi bắt buộc phải được inject tại điểm SET CONSTRAINTS sau khi EF Core đã ghi dữ liệu.");
            faultInterceptor.FailureCount.Should().Be(1);

            // Kiểm tra notifier: Chỉ 1 event phát ra cho đơn valid (skuValid), 0 event phát ra cho đơn rollback (skuFault)
            recordingNotifier.StockChangedCalls.Count.Should().Be(1, "Rollback tuyệt đối KHÔNG được phát sự kiện SignalR.");
            recordingNotifier.StockChangedCalls.First().SkuId.Should().Be(skuValid.Id);

            // 4. Đối soát SNAPSHOT ĐẦY ĐỦ từ DbContext độc lập:
            // Mọi row, fields, status, timestamps, sequences, signatures, item cost, payment của orderFault và skuFault phải không đổi 100%!
            var (verifyCtx, _) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
            await using (verifyCtx)
            {
                var checkOrderFault = await verifyCtx.Orders.Include(o => o.Items).Include(o => o.Reservations).Include(o => o.Payment).SingleAsync(o => o.Id == orderFault.Id);
                checkOrderFault.Status.Should().Be(snapStatus, "Đơn hàng phải giữ nguyên trạng thái Reserved sau rollback.");
                checkOrderFault.CancelledAt.Should().Be(snapCancelledAt, "Đơn lỗi rollback tuyệt đối không được có CancelledAt.");
                checkOrderFault.ConfirmedAt.Should().Be(snapConfirmedAt);
                checkOrderFault.CompletedAt.Should().Be(snapCompletedAt);
                checkOrderFault.CreatedAt.Should().Be(snapCreatedAt);
                checkOrderFault.TotalAmount.Should().Be(snapTotalAmount);
                checkOrderFault.RequestKey.Should().Be(snapRequestKey);
                checkOrderFault.WebhookPayloadSignature.Should().Be(snapSig);
                checkOrderFault.ExternalShopId.Should().Be(snapShopId);
                checkOrderFault.ExternalOrderId.Should().Be(snapExtOrderId);

                // Đối soát item: số dòng, đơn giá, giá vốn cost_price, số lượng không đổi
                checkOrderFault.Items.Count.Should().Be(1);
                var checkItem = checkOrderFault.Items.Single();
                checkItem.CostPrice.Should().Be(snapItemCost);
                checkItem.UnitPrice.Should().Be(snapItemPrice);
                checkItem.Quantity.Should().Be(snapItemQty);

                // Đối soát reservation: status Active, expires_at, created_at, số lượng không đổi
                checkOrderFault.Reservations.Count.Should().Be(1);
                var checkRes = checkOrderFault.Reservations.Single();
                checkRes.Status.Should().Be(snapResStatus);
                checkRes.ExpiresAt.Should().Be(snapResExpiresAt);
                checkRes.CreatedAt.Should().Be(snapResCreatedAt);
                checkRes.Quantity.Should().Be(snapResQty);

                // Đối soát payment: status, amount không đổi
                checkOrderFault.Payment.Should().NotBeNull();
                checkOrderFault.Payment!.Status.Should().Be(snapPayStatus);
                checkOrderFault.Payment.Amount.Should().Be(snapPayAmount);

                // Đối soát số dư tồn kho chi nhánh skuFault: OnHand, Reserved, WAC, Sequence, Timestamps không đổi 100%
                var checkBBal = await verifyCtx.InventoryBalances.SingleAsync(b => b.BranchId == branch.Id && b.SkuId == skuFault.Id);
                checkBBal.OnHand.Should().Be(snapBOnHand);
                checkBBal.Reserved.Should().Be(snapBReserved);
                checkBBal.AverageCost.Should().Be(snapBWac);
                checkBBal.LastSequence.Should().Be(snapBSeq);
                checkBBal.UpdatedAt.Should().Be(snapBUpdated);

                // Đối soát số dư tồn kho kho skuFault: OnHand, Reserved, WAC, Sequence, Timestamps không đổi 100%
                var checkWhBal = await verifyCtx.WarehouseInventoryBalances.SingleAsync(w => w.WarehouseId == wh.Id && w.SkuId == skuFault.Id);
                checkWhBal.OnHand.Should().Be(snapWhOnHand);
                checkWhBal.Reserved.Should().Be(snapWhReserved);
                checkWhBal.LastSequence.Should().Be(snapWhSeq);
                checkWhBal.UpdatedAt.Should().Be(snapWhUpdated);

                // Đối soát sổ kho: 0 transaction mới được sinh ra
                var midTxCount = await verifyCtx.InventoryTransactions.CountAsync(t => t.TenantId == OismsDatabaseSeeder.TenantAId);
                midTxCount.Should().Be(snapTxCount, "Không có transaction ledger nào được ghi nhận cho cả đơn thành công lẫn đơn rollback.");

                // Đối soát đơn valid trong batch: commit thành công, Reserved giảm 1m
                var checkOrderValid = await verifyCtx.Orders.Include(o => o.Reservations).SingleAsync(o => o.Id == orderValid.Id);
                checkOrderValid.Status.Should().Be("Cancelled");
                checkOrderValid.CancelledAt.Should().NotBeNull();
                checkOrderValid.Reservations.Single().Status.Should().Be("Expired");

                var checkBBalValid = await verifyCtx.InventoryBalances.SingleAsync(b => b.BranchId == branch.Id && b.SkuId == skuValid.Id);
                var checkWhBalValid = await verifyCtx.WarehouseInventoryBalances.SingleAsync(w => w.WarehouseId == wh.Id && w.SkuId == skuValid.Id);
                checkBBalValid.Reserved.Should().Be(0m);
                checkWhBalValid.Reserved.Should().Be(0m);
            }

            // 5. Clean Retry: Sau khi gỡ fault, đơn lỗi được hủy sạch sẽ và commit thành công đúng 1 lần
            faultInterceptor.ShouldFail = false;
            var retryCount = await svc.ExpireOverdueReservationsAsync(now);
            retryCount.Should().Be(1, "Retry sạch phải hoàn tất hủy đơn đã từng lỗi.");

            var (verifyRetryCtx, _) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
            await using (verifyRetryCtx)
            {
                var finalOrderFault = await verifyRetryCtx.Orders.Include(o => o.Reservations).SingleAsync(o => o.Id == orderFault.Id);
                finalOrderFault.Status.Should().Be("Cancelled");
                finalOrderFault.CancelledAt.Should().NotBeNull();
                finalOrderFault.Reservations.Single().Status.Should().Be("Expired");

                var finalBBalFault = await verifyRetryCtx.InventoryBalances.SingleAsync(b => b.BranchId == branch.Id && b.SkuId == skuFault.Id);
                var finalWhBalFault = await verifyRetryCtx.WarehouseInventoryBalances.SingleAsync(w => w.WarehouseId == wh.Id && w.SkuId == skuFault.Id);
                finalBBalFault.Reserved.Should().Be(0m);
                finalWhBalFault.Reserved.Should().Be(0m);
            }

            recordingNotifier.StockChangedCalls.Count.Should().Be(2, "Sau clean retry phải phát thêm đúng 1 sự kiện SignalR cho skuFault.");
            recordingNotifier.StockChangedCalls.Should().Contain(c => c.SkuId == skuFault.Id, "Phải có sự kiện cho skuFault sau khi clean retry.");
            recordingNotifier.StockChangedCalls.Should().Contain(c => c.SkuId == skuValid.Id, "Phải có sự kiện cho skuValid khi batch hoàn tất.");
        }
    }

    #endregion

    #region AC-10: Commit-Before-Send & Realtime Hub Integration

    [Fact]
    public async Task AC10_CommitBeforeSendAndNotifierFailureTolerance()
    {
        await SeedDatabaseAsync();
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);

        // 1. Kết nối SignalR Hub thật
        await using var hub = CreateHubConnection(ownerToken);
        await hub.StartAsync();

        var stockChangesReceived = new List<StockChangedRealtimeDto>();
        hub.On<StockChangedRealtimeDto>("StockLevelChanged", dto => stockChangesReceived.Add(dto));

        var (ctx, ts) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
        await using (ctx)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var wh = await ctx.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await ctx.Skus.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branch.Id, wh.Id, sku.Id, 50m, sku.RetailPrice);

            var bBal = await ctx.InventoryBalances.FirstAsync(b => b.BranchId == branch.Id && b.SkuId == sku.Id);
            var whBal = await ctx.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == wh.Id && w.SkuId == sku.Id);

            await hub.InvokeAsync("JoinBranchGroup", branch.Id);

            var now = DateTimeOffset.UtcNow;
            var order = CreateTestOrderWithReservation(branch.Id, wh.Id, sku.Id, sku.RetailPrice, 2m, now.AddMinutes(-5), "Reserved");
            ctx.Orders.Add(order);
            bBal.Reserved += 2m;
            whBal.Reserved += 2m;
            await ctx.SaveChangesAsync();

            // 2. Chạy expiry với notifier thật nối tới SignalR Hub và cổng chặn precommit
            var precommitGate = new BeforeCommitGate();
            var (gateCtx, gateTs) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId, precommitGate);
            await using (gateCtx)
            {
                using var notifierScope = _factory.Services.CreateScope();
                var hubNotifier = notifierScope.ServiceProvider.GetRequiredService<IRealtimeNotifier>();
                var gateOrderSvc = new OrderService(gateCtx, gateTs, new Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>(), hubNotifier);

                var expiryTask = gateOrderSvc.ExpireOverdueReservationsAsync(now);
                await precommitGate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));

                // Tại điểm chặn trước commit: CSDL độc lập chưa nhìn thấy thay đổi, Hub chưa nhận bất kỳ event nào
                var (verifyCtx, _) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
                await using (verifyCtx)
                {
                    var preOrder = await verifyCtx.Orders.SingleAsync(o => o.Id == order.Id);
                    preOrder.Status.Should().Be("Reserved", "Trước commit CSDL độc lập phải chưa nhìn thấy đơn bị hủy.");
                }
                stockChangesReceived.Count.Should().Be(0, "Trước commit tuyệt đối không được phát sự kiện SignalR.");

                // Mở cổng commit
                precommitGate.Release.TrySetResult();
                var cancelled = await expiryTask;
                cancelled.Should().Be(1);

                // Chờ nhận sự kiện Realtime qua WebSocket/LongPolling
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < deadline && stockChangesReceived.Count == 0)
                {
                    await Task.Delay(100);
                }

                stockChangesReceived.Count.Should().BeGreaterThanOrEqualTo(1, "Client phải nhận được sự kiện StockLevelChanged sau khi commit hủy đơn.");
                var eventPayload = stockChangesReceived.Last();
                eventPayload.BranchId.Should().Be(branch.Id);
                eventPayload.SkuId.Should().Be(sku.Id);
                eventPayload.NewAvailableStock.Should().Be(bBal.OnHand - (bBal.Reserved - 2m));
            }

            // 3. Notifier failure tolerance: Nếu notifier throw exception sau commit, DB vẫn commit thành công
            var throwingNotifier = new ThrowingRealtimeNotifier();
            ctx.ChangeTracker.Clear();
            bBal = await ctx.InventoryBalances.FirstAsync(b => b.BranchId == branch.Id && b.SkuId == sku.Id);
            whBal = await ctx.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == wh.Id && w.SkuId == sku.Id);

            var ord2 = CreateTestOrderWithReservation(branch.Id, wh.Id, sku.Id, sku.RetailPrice, 1m, now.AddMinutes(-5), "Reserved");
            ctx.Orders.Add(ord2);
            bBal.Reserved += 1m;
            whBal.Reserved += 1m;
            await ctx.SaveChangesAsync();

            var svcWithThrowing = new OrderService(ctx, ts, new Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>(), throwingNotifier);
            var countThrowing = await svcWithThrowing.ExpireOverdueReservationsAsync(now);
            countThrowing.Should().Be(1, "Ngoại lệ từ Notifier sau commit không được làm rollback DB.");

            ctx.ChangeTracker.Clear();
            var checkOrd2 = await ctx.Orders.FirstAsync(o => o.Id == ord2.Id);
            checkOrd2.Status.Should().Be("Cancelled");

            // Chạy lặp lại trên đơn đã hủy -> no-op trả về 0, không phát thêm event
            var repeatThrowing = await svcWithThrowing.ExpireOverdueReservationsAsync(now);
            repeatThrowing.Should().Be(0, "Lặp lại trên đơn đã hủy phải trả về 0.");
        }
    }

    #endregion

    #region AC-11: Bounded Batch, Clock & Cancellation Token

    [Fact]
    public async Task AC11_BoundedBatchAndCancellation()
    {
        await SeedDatabaseAsync();
        var now = DateTimeOffset.UtcNow;

        // 1. Clock control: cutoff điều khiển chính xác mốc thời gian không cần sleep 15 phút
        var (ctxClock, tsClock) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
        await using (ctxClock)
        {
            var branch = await ctxClock.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var wh = await ctxClock.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await ctxClock.Skus.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branch.Id, wh.Id, sku.Id, 100m, sku.RetailPrice);

            var order = CreateTestOrderWithReservation(branch.Id, wh.Id, sku.Id, sku.RetailPrice, 1m, now.AddMinutes(-5), "Reserved");
            ctxClock.Orders.Add(order);
            (await ctxClock.InventoryBalances.FirstAsync(b => b.BranchId == branch.Id && b.SkuId == sku.Id)).Reserved += 1m;
            (await ctxClock.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == wh.Id && w.SkuId == sku.Id)).Reserved += 1m;
            await ctxClock.SaveChangesAsync();

            var svc = new OrderService(ctxClock, tsClock, new Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>(), null);
            var cancelledBeforeCutoff = await svc.ExpireOverdueReservationsAsync(now.AddMinutes(-10));
            cancelledBeforeCutoff.Should().Be(0, "Cutoff trước khi hết hạn không hủy đơn.");

            // Dọn sạch và kết thúc đơn phase clock bằng cutoff đến hạn để không để lại overdue order cho phase tiếp theo
            var cancelledClockOrder = await svc.ExpireOverdueReservationsAsync(now);
            cancelledClockOrder.Should().Be(1, "Cutoff đến hạn phải hủy đơn phase clock thành công.");
            ctxClock.ChangeTracker.Clear();
            var checkClockOrder = await ctxClock.Orders.Include(o => o.Reservations).SingleAsync(o => o.Id == order.Id);
            checkClockOrder.Status.Should().Be("Cancelled");
            checkClockOrder.Reservations.Should().AllSatisfy(r => r.Status.Should().Be("Expired"));
        }

        // 2. Cancellation trong Transaction (sau khi đã ghi dữ liệu nhưng trước commit): Transaction phải rollback sạch sẽ
        var gate = new BeforeCommitGate();
        var notifier = new RecordingRealtimeNotifier();
        var (ctxCancel, tsCancel) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId, gate);
        Guid cancelOrderId;
        decimal snapBBalReserved, snapWhBalReserved;
        await using (ctxCancel)
        {
            var branch = await ctxCancel.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var wh = await ctxCancel.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await ctxCancel.Skus.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);

            var order = CreateTestOrderWithReservation(branch.Id, wh.Id, sku.Id, sku.RetailPrice, 1m, now.AddMinutes(-5), "Reserved");
            cancelOrderId = order.Id;
            gate.TargetOrderId = cancelOrderId;

            ctxCancel.Orders.Add(order);
            var bBal = await ctxCancel.InventoryBalances.FirstAsync(b => b.BranchId == branch.Id && b.SkuId == sku.Id);
            var whBal = await ctxCancel.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == wh.Id && w.SkuId == sku.Id);
            bBal.Reserved += 1m;
            whBal.Reserved += 1m;
            await ctxCancel.SaveChangesAsync();

            snapBBalReserved = bBal.Reserved;
            snapWhBalReserved = whBal.Reserved;

            using var cts = new CancellationTokenSource();
            var svcCancelling = new OrderService(ctxCancel, tsCancel, new Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>(), notifier);
            var work = svcCancelling.ExpireOverdueReservationsAsync(now, cts.Token);
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            gate.HasSeenWrites.Should().BeTrue("Tác vụ phải đã ghi dữ liệu vào ChangeTracker/DB trước khi đến cổng SET CONSTRAINTS.");

            // Hủy token khi đang ở cổng SET CONSTRAINTS (sau SaveChanges, trước Commit)
            cts.Cancel();
            var cancelledCleanly = false;
            try
            {
                await work;
            }
            catch (OperationCanceledException)
            {
                cancelledCleanly = true;
            }
            cancelledCleanly.Should().BeTrue("CancellationToken phải dừng tác vụ cleanly và ném OperationCanceledException.");

            // Kiểm tra từ context độc lập: uncommitted writes phải được rollback 100%, 0 event
            var (verifyCtx, _) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
            await using (verifyCtx)
            {
                var checkOrder = await verifyCtx.Orders.Include(o => o.Reservations).SingleAsync(o => o.Id == cancelOrderId);
                checkOrder.Status.Should().Be("Reserved", "Đơn hàng phải giữ nguyên trạng thái Reserved sau khi hủy tác vụ.");
                checkOrder.CancelledAt.Should().BeNull("Đơn bị hủy giao dịch tuyệt đối không có CancelledAt.");
                checkOrder.Reservations.Should().AllSatisfy(r => r.Status.Should().Be("Active"));

                var checkBBal = await verifyCtx.InventoryBalances.SingleAsync(b => b.BranchId == branch.Id && b.SkuId == sku.Id);
                var checkWhBal = await verifyCtx.WarehouseInventoryBalances.SingleAsync(w => w.WarehouseId == wh.Id && w.SkuId == sku.Id);
                checkBBal.Reserved.Should().Be(snapBBalReserved, "Số dư giữ chỗ chi nhánh phải được rollback nguyên vẹn.");
                checkWhBal.Reserved.Should().Be(snapWhBalReserved, "Số dư giữ chỗ kho phải được rollback nguyên vẹn.");
            }
            notifier.StockChangedCalls.Count.Should().Be(0, "Không được phát sự kiện SignalR khi transaction bị hủy.");
        }

        // 3. Batch Fairness & Keyset Cursor: 50 đơn lỗi không được bỏ đói đơn hợp lệ thứ 51 (kiểm thử trên Tenant B hoàn toàn độc lập)
        OrderService.ResetCursors();
        var targetTenant = OismsDatabaseSeeder.TenantBId;

        var (prepB, _) = CreatePostgresDbContext(targetTenant);
        Guid branchBId, whBId, skuBId;
        decimal priceB;
        await using (prepB)
        {
            var branchB = await prepB.Branches.FirstAsync(b => b.TenantId == targetTenant);
            var whB = await prepB.Warehouses.FirstAsync(w => w.TenantId == targetTenant);
            var skuB = await prepB.Skus.FirstAsync(s => s.TenantId == targetTenant);
            branchBId = branchB.Id;
            whBId = whB.Id;
            skuBId = skuB.Id;
            priceB = skuB.RetailPrice;
            await SeedStockAsync(targetTenant, branchBId, whBId, skuBId, 100m, priceB);
        }

        var poison = new PoisonOrderInterceptor();
        Order MakeOrder(int position)
        {
            var ord = CreateTestOrderWithReservation(branchBId, whBId, skuBId, priceB, 1m, now.AddMinutes(-5), "Reserved");
            ord.TenantId = targetTenant;
            ord.Items.First().TenantId = targetTenant;
            ord.Reservations.First().TenantId = targetTenant;
            ord.CreatedAt = now.AddMinutes(-100).AddSeconds(position);
            return ord;
        }

        var orders = Enumerable.Range(1, 51).Select(MakeOrder).ToArray();
        foreach (var ord in orders.Take(50)) poison.Ids.Add(ord.Id);
        poison.Enabled = true;

        var (ctxPoison, tsPoison) = CreatePostgresDbContext(targetTenant, poison);
        await using (ctxPoison)
        {
            ctxPoison.Orders.AddRange(orders);
            (await ctxPoison.InventoryBalances.FirstAsync(b => b.BranchId == branchBId && b.SkuId == skuBId)).Reserved += 51m;
            (await ctxPoison.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == whBId && w.SkuId == skuBId)).Reserved += 51m;
            await ctxPoison.SaveChangesAsync();

            var service = new OrderService(ctxPoison, tsPoison, new Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>(), null);

            // Lượt 1: Quét batch 50 đơn đầu (cả 50 đơn đều bị poison lỗi) -> Trả về 0
            var firstRun = await service.ExpireOverdueReservationsAsync(now);
            firstRun.Should().Be(0);

            // Lượt 2: Keyset cursor tiến trang qua 50 đơn lỗi để xử lý đơn thứ 51 -> Trả về 1, đơn 51 Cancelled!
            var secondRun = await service.ExpireOverdueReservationsAsync(now);
            secondRun.Should().Be(1, "Lượt chạy tiếp theo phải tiến trang qua nhóm lỗi để hủy đơn thứ 51 hợp lệ.");

            var (verifyFairness, _) = CreatePostgresDbContext(targetTenant);
            await using (verifyFairness)
            {
                var healthyOrder = await verifyFairness.Orders.SingleAsync(o => o.Id == orders[50].Id);
                healthyOrder.Status.Should().Be("Cancelled", "Đơn hợp lệ thứ 51 phải được hủy thành công mà không bị bỏ đói.");
                var remainingActive = await verifyFairness.Reservations.CountAsync(r => r.Status == "Active");
                remainingActive.Should().Be(50, "Chỉ còn đúng 50 phiếu của 50 đơn lỗi.");
            }

            // Gỡ bỏ fault: Các lượt tiếp theo dọn sạch toàn bộ 50 đơn còn lại
            poison.Enabled = false;
            var recoveryOne = await service.ExpireOverdueReservationsAsync(now);
            recoveryOne.Should().Be(50, "Gỡ bỏ fault thì các đơn lỗi trước đó phải được thu hồi thành công.");

            var recoveryTwo = await service.ExpireOverdueReservationsAsync(now);
            recoveryTwo.Should().Be(0, "Không còn đơn quá hạn nào tồn đọng.");
        }
    }

    #endregion

    #region Helper Classes

    private sealed class SqlStateDeadlockObserver : DbCommandInterceptor
    {
        public ConcurrentBag<string> FailedSqlStates { get; } = new();
        public ConcurrentBag<Exception> CaughtExceptions { get; } = new();
        public int Deadlock40P01Count => FailedSqlStates.Count(s => s == "40P01");

        public void Reset()
        {
            while (FailedSqlStates.TryTake(out _)) { }
            while (CaughtExceptions.TryTake(out _)) { }
        }

        public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
        {
            Record(eventData.Exception);
            base.CommandFailed(command, eventData);
        }

        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            Record(eventData.Exception);
            return base.CommandFailedAsync(command, eventData, cancellationToken);
        }

        private void Record(Exception? ex)
        {
            if (ex == null) return;
            CaughtExceptions.Add(ex);
            var curr = ex;
            while (curr != null)
            {
                if (curr is PostgresException pgEx && !string.IsNullOrEmpty(pgEx.SqlState))
                {
                    FailedSqlStates.Add(pgEx.SqlState);
                    break;
                }
                curr = curr.InnerException;
            }
        }
    }

    private sealed class RecordingRealtimeNotifier : IRealtimeNotifier
    {
        public ConcurrentBag<(Guid TenantId, Guid BranchId, Guid SkuId, decimal Available)> StockChangedCalls { get; } = new();

        public Task NotifyStockChangedAsync(Guid tenantId, Guid branchId, Guid skuId, decimal newAvailableStock, CancellationToken ct = default)
        {
            StockChangedCalls.Add((tenantId, branchId, skuId, newAvailableStock));
            return Task.CompletedTask;
        }

        public Task NotifyNewOrderAsync(Guid tenantId, OrderSummaryRealtimeDto order, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyLowStockAlertAsync(Guid tenantId, Guid branchId, Guid skuId, string skuCode, decimal currentAvailable, decimal threshold, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class ThrowingRealtimeNotifier : IRealtimeNotifier
    {
        public Task NotifyStockChangedAsync(Guid tenantId, Guid branchId, Guid skuId, decimal newAvailableStock, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Mô phỏng lỗi mạng khi phát sự kiện SignalR.");
        }

        public Task NotifyNewOrderAsync(Guid tenantId, OrderSummaryRealtimeDto order, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyLowStockAlertAsync(Guid tenantId, Guid branchId, Guid skuId, string skuCode, decimal currentAvailable, decimal threshold, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class BeforeCommitGate : DbCommandInterceptor
    {
        public bool Enabled { get; set; } = true;
        public Guid? TargetOrderId { get; set; }
        public bool HasSeenWrites { get; private set; }
        public bool ThrowAfterRelease { get; set; }
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Guid _currentOrderId;

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            InspectCommand(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            InspectCommand(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            InspectCommand(command);
            if (Enabled && command.CommandText.StartsWith("SET CONSTRAINTS ALL IMMEDIATE", StringComparison.Ordinal))
            {
                if (!TargetOrderId.HasValue || _currentOrderId == TargetOrderId.Value)
                {
                    Reached.TrySetResult();
                    await Release.Task.WaitAsync(cancellationToken);
                    if (ThrowAfterRelease) throw new InvalidOperationException("Simulated fault after SaveChanges before commit");
                }
            }
            return result;
        }

        private void InspectCommand(DbCommand command)
        {
            var text = command.CommandText;
            if (text.Contains("/order/' ||", StringComparison.Ordinal) || text.Contains("orders WHERE tenant_id = ", StringComparison.Ordinal))
            {
                foreach (DbParameter p in command.Parameters)
                {
                    if (p.Value is Guid g && g != Guid.Empty && g != OismsDatabaseSeeder.TenantAId && g != OismsDatabaseSeeder.TenantBId)
                    {
                        _currentOrderId = g;
                        break;
                    }
                    if (p.Value is string s && Guid.TryParse(s, out var parsedGuid) && parsedGuid != Guid.Empty && parsedGuid != OismsDatabaseSeeder.TenantAId && parsedGuid != OismsDatabaseSeeder.TenantBId)
                    {
                        _currentOrderId = parsedGuid;
                        break;
                    }
                }
            }

            if (text.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase) &&
                (text.Contains("orders", StringComparison.OrdinalIgnoreCase) || text.Contains("reservations", StringComparison.OrdinalIgnoreCase)))
            {
                if (!TargetOrderId.HasValue || _currentOrderId == TargetOrderId.Value)
                {
                    HasSeenWrites = true;
                }
            }
        }
    }

    private sealed class FirstSkuAttempt : DbCommandInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Pid { get; private set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("pg_advisory_xact_lock", StringComparison.Ordinal) && command.Parameters.Count == 3)
            {
                Pid = ((NpgsqlConnection)command.Connection!).ProcessID;
                Reached.TrySetResult();
            }
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class PoisonOrderInterceptor : DbCommandInterceptor
    {
        public HashSet<Guid> Ids { get; } = new();
        public bool Enabled { get; set; } = true;
        public int Failures { get; set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("orders WHERE tenant_id = @p0 AND id = @p1 FOR UPDATE", StringComparison.Ordinal))
            {
                foreach (DbParameter param in command.Parameters)
                {
                    if (param.Value is Guid g && Ids.Contains(g))
                    {
                        Failures++;
                        throw new PostgresException("Poison order lock failure", "FATAL", "XX000", "Poison order lock failure");
                    }
                }
            }
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    #endregion
}
