using System.Collections.Concurrent;
using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.RegisterTenant;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Inventory.Services;
using OISM.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

public class InventoryAndGoodsReceiptIntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private class TestTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    public InventoryAndGoodsReceiptIntegrationTests(WebApplicationFactoryWithPostgres factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private (AppDbContext Context, TestTenantService TenantService) CreatePostgresDbContext(params IInterceptor[] interceptors)
    {
        var tenantService = new TestTenantService();
        var context = PostgresTestEnvironment.CreateRuntimeDbContext(tenantService, interceptors);
        return (context, tenantService);
    }

    public class MidstreamConfirmationFaultException : Exception
    {
        public MidstreamConfirmationFaultException(string message) : base(message) { }
    }

    public class MidstreamFailureInterceptor : SaveChangesInterceptor
    {
        public bool ShouldFail { get; set; } = true;
        public int SavedChangesCount { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (ShouldFail)
            {
                SavedChangesCount = result;
                throw new MidstreamConfirmationFaultException("Mô phỏng đứt kết nối mạng/crash giữa chừng sau khi đã ghi dữ liệu vào CSDL nhưng trước COMMIT.");
            }
            return base.SavedChangesAsync(eventData, result, cancellationToken);
        }
    }

    public class DeferredStockConsistencyViolationInterceptor : SaveChangesInterceptor
    {
        public bool SaveChangesCompletedWithoutError { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context != null)
            {
                // Tìm bản ghi WarehouseInventoryBalance được tạo/cập nhật trong ChangeTracker
                var whBalance = eventData.Context.ChangeTracker
                    .Entries<WarehouseInventoryBalance>()
                    .FirstOrDefault();

                if (whBalance != null)
                {
                    // Cố tình làm lệch tồn kho giữa Warehouse và Branch (+999m)
                    // Vì trigger stock_consistency là DEFERRABLE INITIALLY DEFERRED,
                    // SaveChangesAsync sẽ ghi thành công vào PostgreSQL mà không vấp lỗi.
                    // Lỗi sẽ chỉ được kích hoạt khi SET CONSTRAINTS ALL IMMEDIATE được thực thi!
                    whBalance.Entity.OnHand += 999m;
                }
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            SaveChangesCompletedWithoutError = true;
            return base.SavedChangesAsync(eventData, result, cancellationToken);
        }
    }

    // =========================================================================
    // 1. BE1-04: TẠO PHIẾU NHẬP KHO NHIỀU DÒNG (DRAFT)
    // =========================================================================
    [Fact]
    public async Task Postgres_GoodsReceipt_Create_MultipleItems_ShouldSucceedAsDraft()
    {
        var (context, tenantService) = CreatePostgresDbContext();
        await using (context)
        {
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);
            tenantService.SetTenant(OismsDatabaseSeeder.TenantAId);

            var branch = await context.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await context.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var supplier = await context.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await context.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var productA = await context.Products.FirstAsync(p => p.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku2 = new ProductVariant
            {
                TenantId = OismsDatabaseSeeder.TenantAId,
                ProductId = productA.Id,
                SKU = "POLO-NAM-TRANG-L",
                RetailPrice = 199000m,
                WholesalePrice = 120000m,
                Unit = "piece"
            };
            context.ProductVariants.Add(sku2);
            await context.SaveChangesAsync();

            var skus = await context.ProductVariants.Where(v => v.TenantId == OismsDatabaseSeeder.TenantAId).OrderBy(v => v.SKU).Take(2).ToListAsync();

            var wacCalc = new WacCalculator();
            var service = new GoodsReceiptService(context, tenantService, wacCalc);

            var createDto = new CreateGoodsReceiptDto
            {
                BranchId = branch.Id,
                WarehouseId = warehouse.Id,
                SupplierId = supplier.Id,
                Note = "Phiếu nhập thử nghiệm 2 mặt hàng",
                Items = new List<CreateGoodsReceiptItemDto>
                {
                    new() { SkuId = skus[0].Id, Quantity = 10m, UnitPrice = 100000m },
                    new() { SkuId = skus[1].Id, Quantity = 5m, UnitPrice = 150000m }
                }
            };

            // Act
            var response = await service.CreateReceiptAsync(createDto, user.Id);

            // Assert
            response.Should().NotBeNull();
            response.Status.Should().Be("Draft");
            response.Items.Should().HaveCount(2);
            response.TotalAmount.Should().Be(10m * 100000m + 5m * 150000m);
            response.ConfirmedAt.Should().BeNull();

            // Kiểm tra trong PostgreSQL
            var savedReceipt = await context.GoodsReceipts
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.Id == response.Id);

            savedReceipt.Should().NotBeNull();
            savedReceipt!.Status.Should().Be("Draft");
            savedReceipt.Items.Should().HaveCount(2);
        }
    }

    // =========================================================================
    // 2. BE1-05 & BE1-07: XÁC NHẬN NHẬP NGUYÊN TỬ, TÍNH WAC VÀ GHI SỔ CÁI
    // Kiểm chứng ca bắt buộc: Tồn 10 x 100.000 + Nhập 20 x 130.000 => Tồn 30, WAC 120.000
    // =========================================================================
    [Fact]
    public async Task Postgres_GoodsReceipt_Confirm_Atomic_UpdatesBalances_WAC_AndLedger()
    {
        var (context, tenantService) = CreatePostgresDbContext();
        await using (context)
        {
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);
            tenantService.SetTenant(OismsDatabaseSeeder.TenantAId);

            var branch = await context.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await context.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var supplier = await context.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await context.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await context.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            var wacCalc = new WacCalculator();
            var service = new GoodsReceiptService(context, tenantService, wacCalc);

            // Đợt 1: Nhập 10 cái @ 100.000đ
            var receipt1Dto = new CreateGoodsReceiptDto
            {
                BranchId = branch.Id,
                WarehouseId = warehouse.Id,
                SupplierId = supplier.Id,
                Note = "Lô 1 - 10 cái x 100k",
                Items = new List<CreateGoodsReceiptItemDto>
                {
                    new() { SkuId = sku.Id, Quantity = 10m, UnitPrice = 100000m }
                }
            };
            var receipt1 = await service.CreateReceiptAsync(receipt1Dto, user.Id);
            var confirmedReceipt1 = await service.ConfirmReceiptAsync(receipt1.Id, user.Id);

            confirmedReceipt1.Status.Should().Be("Confirmed");
            confirmedReceipt1.ConfirmedAt.Should().NotBeNull();

            // Đợt 2: Nhập 20 cái @ 130.000đ (Ca kiểm thử bắt buộc BE1-07)
            var receipt2Dto = new CreateGoodsReceiptDto
            {
                BranchId = branch.Id,
                WarehouseId = warehouse.Id,
                SupplierId = supplier.Id,
                Note = "Lô 2 - 20 cái x 130k",
                Items = new List<CreateGoodsReceiptItemDto>
                {
                    new() { SkuId = sku.Id, Quantity = 20m, UnitPrice = 130000m }
                }
            };
            var receipt2 = await service.CreateReceiptAsync(receipt2Dto, user.Id);
            var confirmedReceipt2 = await service.ConfirmReceiptAsync(receipt2.Id, user.Id);

            confirmedReceipt2.Status.Should().Be("Confirmed");

            // Assert: Kiểm tra số dư chi nhánh (inventory_balances)
            var branchBalance = await context.InventoryBalances
                .FirstOrDefaultAsync(b => b.BranchId == branch.Id && b.SkuId == sku.Id);

            branchBalance.Should().NotBeNull();
            branchBalance!.OnHand.Should().Be(30m);
            branchBalance.AverageCost.Should().Be(120000m); // (10*100k + 20*130k) / 30 = 120k chuẩn xác!
            branchBalance.LastSequence.Should().Be(2);

            // Assert: Kiểm tra số dư kho (warehouse_inventory_balances)
            var warehouseBalance = await context.WarehouseInventoryBalances
                .FirstOrDefaultAsync(w => w.WarehouseId == warehouse.Id && w.SkuId == sku.Id);

            warehouseBalance.Should().NotBeNull();
            warehouseBalance!.OnHand.Should().Be(30m);
            warehouseBalance.LastSequence.Should().Be(2);

            // Assert: Kiểm tra Sổ cái kho (inventory_transactions)
            var ledgerRows = await context.InventoryTransactions
                .Where(t => t.BranchId == branch.Id && t.SkuId == sku.Id)
                .OrderBy(t => t.SequenceNo)
                .ToListAsync();

            ledgerRows.Should().HaveCount(2);

            // Dòng 1: Nhập 10
            ledgerRows[0].SequenceNo.Should().Be(1);
            ledgerRows[0].Type.Should().Be("IN");
            ledgerRows[0].Quantity.Should().Be(10m);
            ledgerRows[0].UnitCost.Should().Be(100000m);
            ledgerRows[0].BalanceAfter.Should().Be(10m);

            // Dòng 2: Nhập 20
            ledgerRows[1].SequenceNo.Should().Be(2);
            ledgerRows[1].Type.Should().Be("IN");
            ledgerRows[1].Quantity.Should().Be(20m);
            ledgerRows[1].UnitCost.Should().Be(130000m);
            ledgerRows[1].BalanceAfter.Should().Be(30m);
        }
    }

    // =========================================================================
    // 3. BE1-05: XÁC NHẬN LẶP (IDEMPOTENCY / CONFLICT)
    // =========================================================================
    [Fact]
    public async Task Postgres_GoodsReceipt_Confirm_Duplicate_ShouldThrowInvalidOperationException()
    {
        var (context, tenantService) = CreatePostgresDbContext();
        await using (context)
        {
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);
            tenantService.SetTenant(OismsDatabaseSeeder.TenantAId);

            var branch = await context.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await context.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var supplier = await context.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await context.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await context.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            var wacCalc = new WacCalculator();
            var service = new GoodsReceiptService(context, tenantService, wacCalc);

            var receiptDto = new CreateGoodsReceiptDto
            {
                BranchId = branch.Id,
                WarehouseId = warehouse.Id,
                SupplierId = supplier.Id,
                Items = new List<CreateGoodsReceiptItemDto>
                {
                    new() { SkuId = sku.Id, Quantity = 5m, UnitPrice = 50000m }
                }
            };
            var receipt = await service.CreateReceiptAsync(receiptDto, user.Id);
            await service.ConfirmReceiptAsync(receipt.Id, user.Id);

            // Act & Assert: Xác nhận lại lần 2 phải quăng InvalidOperationException
            Func<Task> act = async () => await service.ConfirmReceiptAsync(receipt.Id, user.Id);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*đã được xác nhận trước đó*");
        }
    }

    // =========================================================================
    // 4. BE1-02 & BE1-04: CÔ LẬP ĐA NGƯỜI THUÊ (CROSS-TENANT ISOLATION)
    // =========================================================================
    [Fact]
    public async Task Postgres_GoodsReceipt_Confirm_CrossTenant_ShouldBeBlocked()
    {
        var (context, tenantService) = CreatePostgresDbContext();
        await using (context)
        {
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);

            // Tạo phiếu tại Tenant A
            tenantService.SetTenant(OismsDatabaseSeeder.TenantAId);
            var branchA = await context.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouseA = await context.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branchA.Id);
            var supplierA = await context.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var userA = await context.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var skuA = await context.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            var wacCalc = new WacCalculator();
            var serviceA = new GoodsReceiptService(context, tenantService, wacCalc);

            var receiptA = await serviceA.CreateReceiptAsync(new CreateGoodsReceiptDto
            {
                BranchId = branchA.Id,
                WarehouseId = warehouseA.Id,
                SupplierId = supplierA.Id,
                Items = new List<CreateGoodsReceiptItemDto> { new() { SkuId = skuA.Id, Quantity = 5m, UnitPrice = 50000m } }
            }, userA.Id);

            // Chuyển sang phiên của Tenant B
            tenantService.SetTenant(OismsDatabaseSeeder.TenantBId);
            var userB = await context.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantBId);
            var serviceB = new GoodsReceiptService(context, tenantService, wacCalc);

            // Act & Assert: Tenant B không thể xác nhận phiếu của Tenant A
            Func<Task> act = async () => await serviceB.ConfirmReceiptAsync(receiptA.Id, userB.Id);
            await act.Should().ThrowAsync<KeyNotFoundException>();
        }
    }

    // =========================================================================
    // 5. BE1-06: SỔ CÁI BẤT BIẾN (APPEND-ONLY LEDGER) - TRIGGER DENIAL
    // =========================================================================
    [Fact]
    public async Task Postgres_InventoryLedger_AppendOnly_RuntimeCannotUpdateOrDelete()
    {
        var (context, tenantService) = CreatePostgresDbContext();
        await using (context)
        {
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);
            tenantService.SetTenant(OismsDatabaseSeeder.TenantAId);

            var branch = await context.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await context.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var supplier = await context.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await context.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await context.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            var service = new GoodsReceiptService(context, tenantService, new WacCalculator());
            var receipt = await service.CreateReceiptAsync(new CreateGoodsReceiptDto
            {
                BranchId = branch.Id,
                WarehouseId = warehouse.Id,
                SupplierId = supplier.Id,
                Items = new List<CreateGoodsReceiptItemDto> { new() { SkuId = sku.Id, Quantity = 8m, UnitPrice = 60000m } }
            }, user.Id);
            await service.ConfirmReceiptAsync(receipt.Id, user.Id);

            // Thử thực thi UPDATE trực tiếp trên inventory_transactions bằng runtime account
            await using var conn = new NpgsqlConnection(PostgresTestEnvironment.RuntimeConnectionString);
            await conn.OpenAsync();

            await using var cmdUpdate = new NpgsqlCommand("UPDATE inventory_transactions SET quantity = 999 WHERE tenant_id = @tid", conn);
            cmdUpdate.Parameters.AddWithValue("tid", OismsDatabaseSeeder.TenantAId);

            Func<Task> actUpdate = async () => await cmdUpdate.ExecuteNonQueryAsync();
            await actUpdate.Should().ThrowAsync<PostgresException>()
                .Where(ex => ex.SqlState == "23514" || ex.SqlState == "42501" || ex.Message.Contains("Mutation denied") || ex.Message.Contains("permission denied") || ex.Message.Contains("oisms_deny_mutation"));

            // Thử thực thi DELETE
            await using var cmdDelete = new NpgsqlCommand("DELETE FROM inventory_transactions WHERE tenant_id = @tid", conn);
            cmdDelete.Parameters.AddWithValue("tid", OismsDatabaseSeeder.TenantAId);

            Func<Task> actDelete = async () => await cmdDelete.ExecuteNonQueryAsync();
            await actDelete.Should().ThrowAsync<PostgresException>()
                .Where(ex => ex.SqlState == "23514" || ex.SqlState == "42501" || ex.Message.Contains("Mutation denied") || ex.Message.Contains("permission denied") || ex.Message.Contains("oisms_deny_mutation"));
        }
    }

    // =========================================================================
    // 6. BE1-09: RESERVATION TỒN KHẢ DỤNG VÀ GIỮ CHỖ HỢP LỆ
    // =========================================================================
    [Fact]
    public async Task Postgres_Reservation_Single_ReducesAvailable_IncreasesReserved()
    {
        var (context, tenantService) = CreatePostgresDbContext();
        await using (context)
        {
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);
            tenantService.SetTenant(OismsDatabaseSeeder.TenantAId);

            var branch = await context.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await context.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var supplier = await context.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await context.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await context.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            // Nhập 10 cái vào kho
            var grService = new GoodsReceiptService(context, tenantService, new WacCalculator());
            var receipt = await grService.CreateReceiptAsync(new CreateGoodsReceiptDto
            {
                BranchId = branch.Id,
                WarehouseId = warehouse.Id,
                SupplierId = supplier.Id,
                Items = new List<CreateGoodsReceiptItemDto> { new() { SkuId = sku.Id, Quantity = 10m, UnitPrice = 50000m } }
            }, user.Id);
            await grService.ConfirmReceiptAsync(receipt.Id, user.Id);

            // Giữ chỗ 4 cái
            var resService = new ReservationService(context, tenantService);
            var reserveResult = await resService.ReserveStockAsync(new ReserveStockRequestDto
            {
                BranchId = branch.Id,
                WarehouseId = warehouse.Id,
                SkuId = sku.Id,
                Quantity = 4m,
                DurationMinutes = 15
            }, user.Id);

            reserveResult.Status.Should().Be("Active");
            reserveResult.RemainingAvailableStock.Should().Be(6m); // 10 - 4 = 6

            // Kiểm tra DB
            var branchBalance = await context.InventoryBalances.FirstAsync(b => b.BranchId == branch.Id && b.SkuId == sku.Id);
            branchBalance.OnHand.Should().Be(10m);
            branchBalance.Reserved.Should().Be(4m);

            var warehouseBalance = await context.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == warehouse.Id && w.SkuId == sku.Id);
            warehouseBalance.OnHand.Should().Be(10m);
            warehouseBalance.Reserved.Should().Be(4m);
        }
    }

    // =========================================================================
    // 7. BE1-09: KIỂM THỬ ĐỒNG THỜI 50 YÊU CẦU TRANH 1 ĐƠN VỊ TỒN CUỐI (FLASH SALE)
    // Tiêu chí: Đúng 1 thành công, 49 bị từ chối hợp lệ, Available = 0, không âm tồn.
    // =========================================================================
    [Fact]
    public async Task Postgres_Reservation_Concurrency_50Requests_1Stock_OnlyOneSucceeds()
    {
        // 1. Chuẩn bị tồn kho ban đầu đúng bằng 1 đơn vị
        var (initContext, initTenantService) = CreatePostgresDbContext();
        Guid branchId, warehouseId, skuId, userId;
        await using (initContext)
        {
            await OismsDatabaseSeeder.SeedAsync(initContext, PostgresTestEnvironment.AppPassword);
            initTenantService.SetTenant(OismsDatabaseSeeder.TenantAId);

            var branch = await initContext.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await initContext.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var supplier = await initContext.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await initContext.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await initContext.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            branchId = branch.Id;
            warehouseId = warehouse.Id;
            skuId = sku.Id;
            userId = user.Id;

            // Nhập đúng 1 sản phẩm
            var grService = new GoodsReceiptService(initContext, initTenantService, new WacCalculator());
            var receipt = await grService.CreateReceiptAsync(new CreateGoodsReceiptDto
            {
                BranchId = branchId,
                WarehouseId = warehouseId,
                SupplierId = supplier.Id,
                Items = new List<CreateGoodsReceiptItemDto> { new() { SkuId = skuId, Quantity = 1m, UnitPrice = 99000m } }
            }, userId);
            await grService.ConfirmReceiptAsync(receipt.Id, userId);
        }

        // 2. Kích hoạt 50 request độc lập đồng thời tranh nhau giữ đúng 1 sản phẩm đó
        const int concurrentRequests = 50;
        var successCount = 0;
        var rejectedCount = 0;
        var unexpectedExceptions = new ConcurrentBag<Exception>();
        var barrier = new TaskCompletionSource();

        var tasks = Enumerable.Range(0, concurrentRequests).Select(async _ =>
        {
            var (taskContext, taskTenantService) = CreatePostgresDbContext();
            await using (taskContext)
            {
                taskTenantService.SetTenant(OismsDatabaseSeeder.TenantAId);
                var reservationService = new ReservationService(taskContext, taskTenantService);

                // Chờ barrier để bảo đảm cả 50 request cùng xuất phát tại 1 thời điểm
                await barrier.Task;

                try
                {
                    var result = await reservationService.ReserveStockAsync(new ReserveStockRequestDto
                    {
                        BranchId = branchId,
                        WarehouseId = warehouseId,
                        SkuId = skuId,
                        Quantity = 1m,
                        DurationMinutes = 15
                    }, userId);

                    if (result.Status == "Active")
                    {
                        Interlocked.Increment(ref successCount);
                    }
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("không đủ") || ex.Message.Contains("Tồn kho khả dụng"))
                {
                    // Chỉ xem là từ chối hợp lệ khi là lỗi nghiệp vụ hết tồn kho khả dụng
                    Interlocked.Increment(ref rejectedCount);
                }
                catch (Exception ex)
                {
                    // Mọi lỗi hệ thống, timeout, connection drop hay SQL không mong muốn đều ghi nhận để fail test
                    unexpectedExceptions.Add(ex);
                }
            }
        }).ToList();

        // Đồng loạt giải phóng barrier
        barrier.SetResult();
        await Task.WhenAll(tasks);

        // 3. Assert kết quả tranh chấp:
        unexpectedExceptions.Should().BeEmpty("Không được có bất kỳ lỗi hệ thống, kết nối DB, timeout hay lỗi SQL ngoài dự kiến");
        successCount.Should().Be(1, "Chỉ duy nhất 1 yêu cầu được phép thành công khi chỉ còn 1 tồn");
        rejectedCount.Should().Be(49, "49 yêu cầu còn lại phải bị từ chối nghiệp vụ hợp lệ vì hết tồn khả dụng");

        // 4. Kiểm tra trạng thái cuối trong CSDL: on_hand = 1, reserved = 1, available = 0
        var (finalContext, finalTenantService) = CreatePostgresDbContext();
        await using (finalContext)
        {
            finalTenantService.SetTenant(OismsDatabaseSeeder.TenantAId);
            var branchBalance = await finalContext.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            branchBalance.OnHand.Should().Be(1m);
            branchBalance.Reserved.Should().Be(1m);
            (branchBalance.OnHand - branchBalance.Reserved).Should().Be(0m);

            var warehouseBalance = await finalContext.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == warehouseId && w.SkuId == skuId);
            warehouseBalance.OnHand.Should().Be(1m);
            warehouseBalance.Reserved.Should().Be(1m);
            (warehouseBalance.OnHand - warehouseBalance.Reserved).Should().Be(0m);

            // Xác minh số reservation Active trong CSDL đúng bằng 1
            var reservations = await finalContext.Reservations
                .Where(r => r.TenantId == OismsDatabaseSeeder.TenantAId && r.SkuId == skuId)
                .ToListAsync();
            reservations.Should().HaveCount(1, "Chỉ được tạo duy nhất 1 bản ghi reservation");
            reservations[0].Status.Should().Be("Active");
            reservations[0].Quantity.Should().Be(1m);

            // Xác minh số order_items đúng bằng 1
            var orderItems = await finalContext.OrderItems
                .Where(oi => oi.TenantId == OismsDatabaseSeeder.TenantAId && oi.SkuId == skuId)
                .ToListAsync();
            orderItems.Should().HaveCount(1, "Chỉ được tạo duy nhất 1 bản ghi order item");
            orderItems[0].Quantity.Should().Be(1m);
        }
    }

    // =========================================================================
    // 8. BE1-03, BE1-08, BE1-10: E2E HTTP CLIENT & PHÂN QUYỀN RBAC (CASHIER / OWNER)
    // =========================================================================
    [Fact]
    public async Task Postgres_WebApi_E2E_FullFlow_CashierBlocked_AdminReadRealBalances()
    {
        // 1. Đăng ký & Đăng nhập Chủ shop (Owner)
        var regOwner = new RegisterTenantRequest
        {
            TenantName = "Shop Thời Trang E2E",
            TenantCode = "E2E_SHOP",
            FullName = "Chủ Cửa Hàng E2E",
            Email = "owner_e2e@shop.com",
            Password = "Password@123"
        };
        await _client.PostAsJsonAsync("/api/auth/register-tenant", regOwner);

        var loginOwnerRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "owner_e2e@shop.com",
            Password = "Password@123"
        });
        var ownerAuth = await loginOwnerRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        var ownerToken = ownerAuth!.AccessToken;

        // 2. Tạo Chi nhánh, Kho hàng, Nhà cung cấp và Sản phẩm/SKU
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var branchRes = await _client.PostAsJsonAsync("/api/branches", new { Name = "Chi nhánh Quận 1" });
        var branch = await branchRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var branchId = branch.GetProperty("id").GetGuid();

        var warehouseRes = await _client.PostAsJsonAsync("/api/warehouses", new { BranchId = branchId, Name = "Kho Tổng Quận 1" });
        var warehouse = await warehouseRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var warehouseId = warehouse.GetProperty("id").GetGuid();

        var supplierRes = await _client.PostAsJsonAsync("/api/suppliers", new { Name = "Tổng kho May Mặc SG", Phone = "0901234567" });
        var supplier = await supplierRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var supplierId = supplier.GetProperty("id").GetGuid();

        var prodRes = await _client.PostAsJsonAsync("/api/products", new
        {
            Name = "Áo Thun Polo E2E",
            Variants = new[]
            {
                new { Sku = "POLO-E2E-DEN", VariantName = "Màu đen", RetailPrice = 250000m, WholesalePrice = 180000m }
            }
        });
        prodRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var prod = await prodRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var productId = prod.GetProperty("productId").GetGuid();

        var getProdRes = await _client.GetAsync($"/api/products/{productId}");
        var prodDetail = await getProdRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var skuId = prodDetail.GetProperty("variants")[0].GetProperty("id").GetGuid();

        // 3. Kiểm thử phân quyền RBAC: Tạo tài khoản Thu ngân (Cashier)
        // Tạo người dùng Cashier qua DbContext của Factory
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using (db.OpenPrivilegedScope())
            {
                var cashierUser = new User
                {
                    TenantId = ownerAuth.User.TenantId,
                    Email = "cashier_e2e@shop.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password@123"),
                    FullName = "Thu ngân E2E",
                    Role = "Cashier"
                };
                db.Users.Add(cashierUser);
                await db.SaveChangesAsync();
            }
        }

        var loginCashierRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "cashier_e2e@shop.com",
            Password = "Password@123"
        });
        var cashierAuth = await loginCashierRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        var cashierToken = cashierAuth!.AccessToken;

        // 4. Xác minh Cashier bị 403 Forbidden khi lập hoặc duyệt phiếu nhập kho
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        var cashierCreateGrRes = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            BranchId = branchId,
            WarehouseId = warehouseId,
            SupplierId = supplierId,
            Items = new List<CreateGoodsReceiptItemDto> { new() { SkuId = skuId, Quantity = 10m, UnitPrice = 100000m } }
        });
        cashierCreateGrRes.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Thu ngân (Cashier) không được phép tạo phiếu nhập kho");

        // Xác minh Cashier bị 403 khi đọc Sổ cái kho (Ledger)
        var cashierLedgerRes = await _client.GetAsync("/api/inventory/ledger");
        cashierLedgerRes.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Thu ngân (Cashier) không được phép xem sổ cái kho");

        // 5. Chủ shop (Owner) lập phiếu nhập và xác nhận nhập kho
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var ownerCreateGrRes = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            BranchId = branchId,
            WarehouseId = warehouseId,
            SupplierId = supplierId,
            Note = "Phiếu nhập E2E lô đầu",
            Items = new List<CreateGoodsReceiptItemDto>
            {
                new() { SkuId = skuId, Quantity = 50m, UnitPrice = 120000m }
            }
        });
        ownerCreateGrRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdGr = await ownerCreateGrRes.Content.ReadFromJsonAsync<GoodsReceiptResponseDto>(_jsonOptions);

        var confirmRes = await _client.PostAsync($"/api/goods-receipts/{createdGr!.Id}/confirm", null);
        confirmRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. Cả Chủ shop và Thu ngân đều có thể tra cứu Tồn kho thật (BE1-08)
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        var balancesRes = await _client.GetAsync($"/api/inventory/balances?branchId={branchId}");
        balancesRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var pagedBalances = await balancesRes.Content.ReadFromJsonAsync<PagedResult<InventoryBalanceDto>>(_jsonOptions);

        pagedBalances.Should().NotBeNull();
        pagedBalances!.Items.Should().Contain(b => b.SkuId == skuId && b.OnHand == 50m && b.Available == 50m && b.AverageCost == 120000m);

        // 7. Chủ shop xem Sổ cái kho (BE1-06)
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var ledgerRes = await _client.GetAsync($"/api/inventory/ledger?branchId={branchId}&skuId={skuId}");
        ledgerRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var pagedLedger = await ledgerRes.Content.ReadFromJsonAsync<PagedResult<InventoryLedgerDto>>(_jsonOptions);

        pagedLedger.Should().NotBeNull();
        pagedLedger!.Items.Should().Contain(l => l.SkuId == skuId && l.Type == "IN" && l.Quantity == 50m && l.SequenceNo == 1);

        // 8. Tạo Tenant 2 và Owner 2 để xác minh cô lập đa Tenant
        var tenant2Id = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using (db.OpenPrivilegedScope())
            {
                var tenant2 = new Tenant
                {
                    Id = tenant2Id,
                    Name = "Shop Khác E2E",
                    IsActive = true
                };
                var owner2User = new User
                {
                    TenantId = tenant2Id,
                    Email = "owner2_e2e@shop.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password@123"),
                    FullName = "Chủ Shop 2",
                    Role = "Owner",
                    IsActive = true
                };
                db.Tenants.Add(tenant2);
                db.Users.Add(owner2User);
                await db.SaveChangesAsync();
            }
        }

        // Xóa header Authorization của Tenant 1 trước khi đăng nhập tài khoản Tenant 2
        _client.DefaultRequestHeaders.Authorization = null;
        var loginOwner2Res = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "owner2_e2e@shop.com",
            Password = "Password@123"
        });
        loginOwner2Res.StatusCode.Should().Be(HttpStatusCode.OK);
        var owner2Auth = await loginOwner2Res.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        var otherToken = owner2Auth!.AccessToken;

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);
        var otherReadRes = await _client.GetAsync($"/api/goods-receipts/{createdGr!.Id}");
        otherReadRes.StatusCode.Should().Be(HttpStatusCode.NotFound, "Tenant khác không được phép đọc chứng từ của tenant hiện tại");

        var otherConfirmRes = await _client.PostAsync($"/api/goods-receipts/{createdGr!.Id}/confirm", null);
        otherConfirmRes.StatusCode.Should().Be(HttpStatusCode.NotFound, "Tenant khác không được phép xác nhận chứng từ của tenant hiện tại");
    }

    // =========================================================================
    // 9. TRUY VẤN VÀ BỘ LỌC TỒN KHO, PHIẾU NHẬP & VALIDATION BIÊN
    // =========================================================================
    [Fact]
    public async Task Postgres_InventoryAndGoodsReceipt_QueryFiltersAndValidations_ShouldSucceed()
    {
        var (context, tenantService) = CreatePostgresDbContext();
        await using (context)
        {
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);
            tenantService.SetTenant(OismsDatabaseSeeder.TenantAId);

            var branch = await context.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await context.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var supplier = await context.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await context.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await context.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            var wacCalc = new WacCalculator();
            var grService = new GoodsReceiptService(context, tenantService, wacCalc);
            var invService = new InventoryService(context, tenantService);
            var resService = new ReservationService(context, tenantService);

            // 1. Test validation khi tạo phiếu nhập:
            // a. Số lượng <= 0
            var invQtyDto = new CreateGoodsReceiptDto
            {
                BranchId = branch.Id,
                WarehouseId = warehouse.Id,
                SupplierId = supplier.Id,
                Items = new List<CreateGoodsReceiptItemDto> { new() { SkuId = sku.Id, Quantity = 0m, UnitPrice = 100000m } }
            };
            Func<Task> actZeroQty = async () => await grService.CreateReceiptAsync(invQtyDto, user.Id);
            await actZeroQty.Should().ThrowAsync<ArgumentException>().WithMessage("*phải lớn hơn 0*");

            // b. Đơn giá < 0
            var negPriceDto = new CreateGoodsReceiptDto
            {
                BranchId = branch.Id,
                WarehouseId = warehouse.Id,
                SupplierId = supplier.Id,
                Items = new List<CreateGoodsReceiptItemDto> { new() { SkuId = sku.Id, Quantity = 10m, UnitPrice = -500m } }
            };
            Func<Task> actNegPrice = async () => await grService.CreateReceiptAsync(negPriceDto, user.Id);
            await actNegPrice.Should().ThrowAsync<ArgumentException>().WithMessage("*không được âm*");

            // c. Kho không thuộc chi nhánh
            var branch2 = new Branch { TenantId = OismsDatabaseSeeder.TenantAId, Name = "Chi nhánh phụ" };
            context.Branches.Add(branch2);
            await context.SaveChangesAsync();
            var badBranchDto = new CreateGoodsReceiptDto
            {
                BranchId = branch2.Id,
                WarehouseId = warehouse.Id, // warehouse thuộc branch 1
                SupplierId = supplier.Id,
                Items = new List<CreateGoodsReceiptItemDto> { new() { SkuId = sku.Id, Quantity = 10m, UnitPrice = 50000m } }
            };
            Func<Task> actMismatch = async () => await grService.CreateReceiptAsync(badBranchDto, user.Id);
            await actMismatch.Should().ThrowAsync<InvalidOperationException>().WithMessage("*không thuộc chi nhánh*");

            // d. Không có items
            var emptyItemsDto = new CreateGoodsReceiptDto
            {
                BranchId = branch.Id,
                WarehouseId = warehouse.Id,
                SupplierId = supplier.Id,
                Items = new List<CreateGoodsReceiptItemDto>()
            };
            Func<Task> actEmpty = async () => await grService.CreateReceiptAsync(emptyItemsDto, user.Id);
            await actEmpty.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ít nhất 1 dòng*");

            // e. SKU không tồn tại
            var nonExistentSkuDto = new CreateGoodsReceiptDto
            {
                BranchId = branch.Id,
                WarehouseId = warehouse.Id,
                SupplierId = supplier.Id,
                Items = new List<CreateGoodsReceiptItemDto> { new() { SkuId = Guid.NewGuid(), Quantity = 5m, UnitPrice = 50000m } }
            };
            Func<Task> actBadSku = async () => await grService.CreateReceiptAsync(nonExistentSkuDto, user.Id);
            await actBadSku.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*không tồn tại*");

            // 2. Tạo và xác nhận phiếu nhập hợp lệ để kiểm tra query
            var validDto = new CreateGoodsReceiptDto
            {
                BranchId = branch.Id,
                WarehouseId = warehouse.Id,
                SupplierId = supplier.Id,
                Note = "Phiếu kiểm thử tra cứu",
                Items = new List<CreateGoodsReceiptItemDto> { new() { SkuId = sku.Id, Quantity = 25m, UnitPrice = 80000m } }
            };
            var receipt = await grService.CreateReceiptAsync(validDto, user.Id);
            await grService.ConfirmReceiptAsync(receipt.Id, user.Id);

            // 3. Test GetReceiptByIdAsync
            var found = await grService.GetReceiptByIdAsync(receipt.Id);
            found.Should().NotBeNull();
            found!.Status.Should().Be("Confirmed");
            Func<Task> actNotFound = async () => await grService.GetReceiptByIdAsync(Guid.NewGuid());
            await actNotFound.Should().ThrowAsync<KeyNotFoundException>();

            // 4. Test GetReceiptsAsync với filter
            var list = await grService.GetReceiptsAsync(new GoodsReceiptFilterDto
            {
                BranchId = branch.Id,
                WarehouseId = warehouse.Id,
                SupplierId = supplier.Id,
                Status = "Confirmed",
                PageIndex = 1,
                PageSize = 10
            });
            list.Should().NotBeNull();
            list.Items.Should().Contain(r => r.Id == receipt.Id);

            // 5. Test GetBalancesAsync theo Kho cụ thể
            var whBalances = await invService.GetBalancesAsync(new InventoryBalanceFilterDto
            {
                BranchId = branch.Id,
                WarehouseId = warehouse.Id,
                SkuId = sku.Id
            });
            whBalances.Should().NotBeNull();
            whBalances.Items.Should().Contain(b => b.WarehouseId == warehouse.Id && b.OnHand == 25m);

            // 6. Test GetSkuBalanceAtBranchAsync
            var skuBalance = await invService.GetSkuBalanceAtBranchAsync(branch.Id, sku.Id);
            skuBalance.Should().NotBeNull();
            skuBalance!.OnHand.Should().Be(25m);
            skuBalance.AverageCost.Should().Be(80000m);

            var nullBalance = await invService.GetSkuBalanceAtBranchAsync(branch.Id, Guid.NewGuid());
            nullBalance.Should().BeNull();

            // 7. Test Reservation validation
            Func<Task> actBadQty = async () => await resService.ReserveStockAsync(new ReserveStockRequestDto
            {
                BranchId = branch.Id,
                WarehouseId = warehouse.Id,
                SkuId = sku.Id,
                Quantity = 0m
            }, user.Id);
            await actBadQty.Should().ThrowAsync<ArgumentOutOfRangeException>().WithMessage("*lớn hơn 0*");

            Func<Task> actBadSkuRes = async () => await resService.ReserveStockAsync(new ReserveStockRequestDto
            {
                BranchId = branch.Id,
                WarehouseId = warehouse.Id,
                SkuId = Guid.NewGuid(),
                Quantity = 5m
            }, user.Id);
            await actBadSkuRes.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Không tìm thấy tồn kho*");
        }
    }

    // =========================================================================
    // 10. REGRESSION TEST: TRUY VẤN TỒN KHO RỖNG KHI LỌC THEO WAREHOUSE_ID
    // (Kiểm chứng không còn lỗi "Sequence contains no elements" và metadata đúng)
    // =========================================================================
    [Fact]
    public async Task Postgres_InventoryBalances_WhenWarehouseHasNoStock_OrFilterNoMatch_ShouldReturnEmptyListWithoutException()
    {
        var (context, tenantService) = CreatePostgresDbContext();
        await using (context)
        {
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);
            tenantService.SetTenant(OismsDatabaseSeeder.TenantAId);

            var branch = await context.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await context.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var supplier = await context.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await context.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await context.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            var invService = new InventoryService(context, tenantService);

            // Ca A: Kho chưa có bất kỳ dữ liệu tồn nào, không truyền BranchId
            var emptyResult = await invService.GetBalancesAsync(new InventoryBalanceFilterDto
            {
                WarehouseId = warehouse.Id,
                BranchId = null,
                PageIndex = 1,
                PageSize = 20
            });

            emptyResult.Should().NotBeNull();
            emptyResult.Items.Should().BeEmpty();
            emptyResult.TotalCount.Should().Be(0);
            emptyResult.PageIndex.Should().Be(1);
            emptyResult.PageSize.Should().Be(20);
            emptyResult.TotalPages.Should().Be(0);

            // Ca B: Nhập hàng vào kho, sau đó lọc theo từ khóa không khớp
            var grService = new GoodsReceiptService(context, tenantService, new WacCalculator());
            var receipt = await grService.CreateReceiptAsync(new CreateGoodsReceiptDto
            {
                BranchId = branch.Id,
                WarehouseId = warehouse.Id,
                SupplierId = supplier.Id,
                Items = new List<CreateGoodsReceiptItemDto> { new() { SkuId = sku.Id, Quantity = 15m, UnitPrice = 75000m } }
            }, user.Id);
            await grService.ConfirmReceiptAsync(receipt.Id, user.Id);

            var noMatchResult = await invService.GetBalancesAsync(new InventoryBalanceFilterDto
            {
                WarehouseId = warehouse.Id,
                BranchId = null,
                SearchTerm = "MA_KHONG_TON_TAI_XYZ_999",
                PageIndex = 1,
                PageSize = 10
            });

            noMatchResult.Should().NotBeNull();
            noMatchResult.Items.Should().BeEmpty();
            noMatchResult.TotalCount.Should().Be(0);

            // Ca C: Đối chứng trường hợp có dữ liệu - lấy đúng WAC từ chi nhánh mà không cần truyền BranchId
            var positiveResult = await invService.GetBalancesAsync(new InventoryBalanceFilterDto
            {
                WarehouseId = warehouse.Id,
                BranchId = null,
                PageIndex = 1,
                PageSize = 10
            });

            positiveResult.Should().NotBeNull();
            positiveResult.TotalCount.Should().Be(1);
            positiveResult.Items.Should().HaveCount(1);
            positiveResult.Items[0].SkuId.Should().Be(sku.Id);
            positiveResult.Items[0].OnHand.Should().Be(15m);
            positiveResult.Items[0].AverageCost.Should().Be(75000m);
        }
    }

    // =========================================================================
    // 11. ĐỒNG THỜI: 2 REQUEST XÁC NHẬN CÙNG 1 PHIẾU NHẬP (CONCURRENT SAME RECEIPT)
    // (Đúng 1 thành công, 1 nhận 409 Conflict / InvalidOperationException kiểm soát được,
    //  không phát sinh 23505 uq_inventory_transactions_3 hay 500)
    // =========================================================================
    [Fact]
    public async Task Postgres_GoodsReceipt_Confirm_SimultaneousSameReceipt_OneSucceeds_OneReturnsConflict()
    {
        Guid receiptId;
        Guid tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId;
        Guid warehouseId;
        Guid skuId;
        Guid userId;

        // 1. Chuẩn bị phiếu Draft có 2 mặt hàng
        var (initContext, initTenantService) = CreatePostgresDbContext();
        await using (initContext)
        {
            await OismsDatabaseSeeder.SeedAsync(initContext, PostgresTestEnvironment.AppPassword);
            initTenantService.SetTenant(tenantId);

            var branch = await initContext.Branches.FirstAsync(b => b.TenantId == tenantId);
            var warehouse = await initContext.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branch.Id);
            var supplier = await initContext.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            var sku1 = await initContext.ProductVariants.FirstAsync(v => v.TenantId == tenantId);
            var product = await initContext.Products.FirstAsync(p => p.TenantId == tenantId);
            var sku2 = new ProductVariant
            {
                TenantId = tenantId,
                ProductId = product.Id,
                SKU = "POLO-NAM-TRANG-XL",
                RetailPrice = 220000m,
                WholesalePrice = 140000m,
                Unit = "piece"
            };
            initContext.ProductVariants.Add(sku2);
            await initContext.SaveChangesAsync();

            branchId = branch.Id;
            warehouseId = warehouse.Id;
            skuId = sku1.Id;
            var user = await initContext.Users.FirstAsync(u => u.TenantId == tenantId);
            userId = user.Id;

            var grService = new GoodsReceiptService(initContext, initTenantService, new WacCalculator());
            var receipt = await grService.CreateReceiptAsync(new CreateGoodsReceiptDto
            {
                BranchId = branchId,
                WarehouseId = warehouseId,
                SupplierId = supplier.Id,
                Items = new List<CreateGoodsReceiptItemDto>
                {
                    new() { SkuId = sku1.Id, Quantity = 10m, UnitPrice = 100000m },
                    new() { SkuId = sku2.Id, Quantity = 20m, UnitPrice = 150000m }
                }
            }, userId);

            receiptId = receipt.Id;
        }

        // 2. Chạy 2 request độc lập đồng thời xác nhận cùng 1 phiếu, đồng bộ bằng TaskCompletionSource
        var barrier = new TaskCompletionSource();
        var successCount = 0;
        var conflictCount = 0;
        var unexpectedExceptions = new ConcurrentBag<Exception>();

        var task1 = Task.Run(async () =>
        {
            var (ctx, ts) = CreatePostgresDbContext();
            await using (ctx)
            {
                ts.SetTenant(tenantId);
                var svc = new GoodsReceiptService(ctx, ts, new WacCalculator());
                await barrier.Task;
                try
                {
                    var res = await svc.ConfirmReceiptAsync(receiptId, userId);
                    if (res.Status == "Confirmed") Interlocked.Increment(ref successCount);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("đã được xác nhận trước đó"))
                {
                    Interlocked.Increment(ref conflictCount);
                }
                catch (Exception ex)
                {
                    unexpectedExceptions.Add(ex);
                }
            }
        });

        var task2 = Task.Run(async () =>
        {
            var (ctx, ts) = CreatePostgresDbContext();
            await using (ctx)
            {
                ts.SetTenant(tenantId);
                var svc = new GoodsReceiptService(ctx, ts, new WacCalculator());
                await barrier.Task;
                try
                {
                    var res = await svc.ConfirmReceiptAsync(receiptId, userId);
                    if (res.Status == "Confirmed") Interlocked.Increment(ref successCount);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("đã được xác nhận trước đó"))
                {
                    Interlocked.Increment(ref conflictCount);
                }
                catch (Exception ex)
                {
                    unexpectedExceptions.Add(ex);
                }
            }
        });

        barrier.SetResult();
        await Task.WhenAll(task1, task2);

        // 3. Assert kết quả:
        unexpectedExceptions.Should().BeEmpty("Không được phát sinh lỗi 23505 uq_inventory_transactions_3 hay lỗi hệ thống 500");
        successCount.Should().Be(1, "Đúng 1 request được phép xác nhận thành công");
        conflictCount.Should().Be(1, "Request thứ 2 phải nhận kết quả xung đột 409 có kiểm soát");

        // 4. Kiểm tra CSDL thực tế: Tồn kho chỉ tăng đúng 1 lần, sổ cái chỉ ghi 2 dòng cho 2 mặt hàng
        var (finalContext, finalTenantService) = CreatePostgresDbContext();
        await using (finalContext)
        {
            finalTenantService.SetTenant(tenantId);
            var finalReceipt = await finalContext.GoodsReceipts.FirstAsync(r => r.Id == receiptId);
            finalReceipt.Status.Should().Be("Confirmed");

            var ledgerEntries = await finalContext.InventoryTransactions
                .Where(t => t.TenantId == tenantId && t.ReceiptItem!.ReceiptId == receiptId)
                .ToListAsync();
            ledgerEntries.Should().HaveCount(2, "Sổ cái chỉ được ghi đúng 2 dòng, không bị trùng lặp");

            var branchBal = await finalContext.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            branchBal.OnHand.Should().Be(10m, "Tồn kho chi nhánh chỉ được cộng đúng 1 lần");
        }
    }

    // =========================================================================
    // 12. ĐỒNG THỜI: 2 PHIẾU KHÁC NHAU NHẬP CÙNG SKU (CONCURRENT DIFFERENT RECEIPTS)
    // (Cả 2 thành công, không deadlock, sequence tăng tuần tự 1 và 2, WAC tính đúng)
    // =========================================================================
    [Fact]
    public async Task Postgres_GoodsReceipt_Confirm_TwoDifferentReceipts_ConcurrentSameSku_BothSucceed_CorrectSequenceAndWAC()
    {
        Guid tenantId = OismsDatabaseSeeder.TenantAId;
        Guid receiptId1;
        Guid receiptId2;
        Guid branchId;
        Guid warehouseId;
        Guid skuId;
        Guid userId;

        // 1. Chuẩn bị 2 phiếu Draft cùng nhập 1 SKU:
        // Phiếu 1: Nhập 10 cái x 100.000
        // Phiếu 2: Nhập 20 cái x 130.000
        var (initContext, initTenantService) = CreatePostgresDbContext();
        await using (initContext)
        {
            await OismsDatabaseSeeder.SeedAsync(initContext, PostgresTestEnvironment.AppPassword);
            initTenantService.SetTenant(tenantId);

            var branch = await initContext.Branches.FirstAsync(b => b.TenantId == tenantId);
            var warehouse = await initContext.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branch.Id);
            var supplier = await initContext.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            var user = await initContext.Users.FirstAsync(u => u.TenantId == tenantId);
            var sku = await initContext.ProductVariants.FirstAsync(v => v.TenantId == tenantId);

            branchId = branch.Id;
            warehouseId = warehouse.Id;
            skuId = sku.Id;
            userId = user.Id;

            var grService = new GoodsReceiptService(initContext, initTenantService, new WacCalculator());

            var r1 = await grService.CreateReceiptAsync(new CreateGoodsReceiptDto
            {
                BranchId = branchId,
                WarehouseId = warehouseId,
                SupplierId = supplier.Id,
                Items = new List<CreateGoodsReceiptItemDto> { new() { SkuId = skuId, Quantity = 10m, UnitPrice = 100000m } }
            }, userId);

            var r2 = await grService.CreateReceiptAsync(new CreateGoodsReceiptDto
            {
                BranchId = branchId,
                WarehouseId = warehouseId,
                SupplierId = supplier.Id,
                Items = new List<CreateGoodsReceiptItemDto> { new() { SkuId = skuId, Quantity = 20m, UnitPrice = 130000m } }
            }, userId);

            receiptId1 = r1.Id;
            receiptId2 = r2.Id;
        }

        // 2. Kích hoạt đồng thời 2 task xác nhận 2 phiếu khác nhau
        var barrier = new TaskCompletionSource();
        var task1 = Task.Run(async () =>
        {
            var (ctx, ts) = CreatePostgresDbContext();
            await using (ctx)
            {
                ts.SetTenant(tenantId);
                var svc = new GoodsReceiptService(ctx, ts, new WacCalculator());
                await barrier.Task;
                return await svc.ConfirmReceiptAsync(receiptId1, userId);
            }
        });

        var task2 = Task.Run(async () =>
        {
            var (ctx, ts) = CreatePostgresDbContext();
            await using (ctx)
            {
                ts.SetTenant(tenantId);
                var svc = new GoodsReceiptService(ctx, ts, new WacCalculator());
                await barrier.Task;
                return await svc.ConfirmReceiptAsync(receiptId2, userId);
            }
        });

        barrier.SetResult();
        var results = await Task.WhenAll(task1, task2);

        // 3. Assert kết quả: cả 2 đều thành công Confirmed
        results[0].Status.Should().Be("Confirmed");
        results[1].Status.Should().Be("Confirmed");

        // 4. Kiểm tra CSDL thực tế:
        // Tổng tồn: 30
        // WAC: (10 * 100k + 20 * 130k) / 30 = 120.000
        // Sequence: 1 và 2 liên tục
        var (finalContext, finalTenantService) = CreatePostgresDbContext();
        await using (finalContext)
        {
            finalTenantService.SetTenant(tenantId);

            var branchBalance = await finalContext.InventoryBalances
                .FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            branchBalance.OnHand.Should().Be(30m);
            branchBalance.AverageCost.Should().Be(120000m);
            branchBalance.LastSequence.Should().Be(2);

            var warehouseBalance = await finalContext.WarehouseInventoryBalances
                .FirstAsync(w => w.WarehouseId == warehouseId && w.SkuId == skuId);
            warehouseBalance.OnHand.Should().Be(30m);
            warehouseBalance.LastSequence.Should().Be(2);

            var ledgerEntries = await finalContext.InventoryTransactions
                .Where(t => t.TenantId == tenantId && t.BranchId == branchId && t.SkuId == skuId)
                .OrderBy(t => t.SequenceNo)
                .ToListAsync();

            ledgerEntries.Should().HaveCount(2);
            ledgerEntries[0].SequenceNo.Should().Be(1);
            ledgerEntries[1].SequenceNo.Should().Be(2);
            ledgerEntries[1].BalanceAfter.Should().Be(30m);
        }
    }

    // =========================================================================
    // 13. BE1-10C: ROLLBACK TRONG CHÍNH LUỒNG XÁC NHẬN (CONFIRMRECEIPTASYNC) & RETRY
    // =========================================================================
    [Fact]
    public async Task Postgres_GoodsReceipt_Confirm_MidstreamFailure_RollsBackCompletely_AndCanRetry()
    {
        var (setupContext, setupTenantService) = CreatePostgresDbContext();
        await using (setupContext)
        {
            await OismsDatabaseSeeder.SeedAsync(setupContext, PostgresTestEnvironment.AppPassword);
            setupTenantService.SetTenant(OismsDatabaseSeeder.TenantAId);

            var branch = await setupContext.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await setupContext.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var supplier = await setupContext.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await setupContext.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku1 = await setupContext.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);
            var product = await setupContext.Products.FirstAsync(p => p.TenantId == OismsDatabaseSeeder.TenantAId);
            
            // 1. Tạo tồn ban đầu cho SKU 1 (10 cái @ 100.000 VNĐ) để kiểm thử dữ liệu hiện hữu không bị ảnh hưởng khi rollback
            var setupGrService = new GoodsReceiptService(setupContext, setupTenantService, new WacCalculator());
            var initReceipt = await setupGrService.CreateReceiptAsync(new CreateGoodsReceiptDto
            {
                BranchId = branch.Id,
                WarehouseId = warehouse.Id,
                SupplierId = supplier.Id,
                Items = new List<CreateGoodsReceiptItemDto>
                {
                    new() { SkuId = sku1.Id, Quantity = 10m, UnitPrice = 100000m }
                }
            }, user.Id);
            await setupGrService.ConfirmReceiptAsync(initReceipt.Id, user.Id);

            // Kiểm tra trạng thái ban đầu của SKU 1
            var initialBranchBal = await setupContext.InventoryBalances.FirstAsync(b => b.BranchId == branch.Id && b.SkuId == sku1.Id);
            initialBranchBal.OnHand.Should().Be(10m);
            initialBranchBal.AverageCost.Should().Be(100000m);
            initialBranchBal.LastSequence.Should().Be(1);

            // Tạo thêm SKU 2 (chưa có tồn)
            var sku2 = new ProductVariant
            {
                TenantId = OismsDatabaseSeeder.TenantAId,
                ProductId = product.Id,
                SKU = "POLO-NAM-ROLLBACK-L",
                RetailPrice = 250000m,
                WholesalePrice = 160000m,
                Unit = "piece"
            };
            setupContext.ProductVariants.Add(sku2);
            await setupContext.SaveChangesAsync();

            // 2. Tạo phiếu nhập nhiều dòng mới (SKU 1 nhập 20 cái @ 130.000 VNĐ, SKU 2 nhập 15 cái @ 80.000 VNĐ)
            var receiptToConfirm = await setupGrService.CreateReceiptAsync(new CreateGoodsReceiptDto
            {
                BranchId = branch.Id,
                WarehouseId = warehouse.Id,
                SupplierId = supplier.Id,
                Note = "Phiếu nhập nhiều dòng kiểm thử rollback",
                Items = new List<CreateGoodsReceiptItemDto>
                {
                    new() { SkuId = sku1.Id, Quantity = 20m, UnitPrice = 130000m },
                    new() { SkuId = sku2.Id, Quantity = 15m, UnitPrice = 80000m }
                }
            }, user.Id);
            receiptToConfirm.Status.Should().Be("Draft");

            // 3. Khởi tạo GoodsReceiptService với Interceptor gây lỗi có kiểm soát ngay sau SaveChangesAsync nhưng trước COMMIT
            var midstreamInterceptor = new MidstreamFailureInterceptor();
            var (failingContext, failingTenantService) = CreatePostgresDbContext(midstreamInterceptor);
            await using (failingContext)
            {
                failingTenantService.SetTenant(OismsDatabaseSeeder.TenantAId);
                var failingGrService = new GoodsReceiptService(failingContext, failingTenantService, new WacCalculator());

                // Gọi trực tiếp GoodsReceiptService.ConfirmReceiptAsync trên PostgreSQL thật
                Func<Task> actFailing = async () => await failingGrService.ConfirmReceiptAsync(receiptToConfirm.Id, user.Id);

                // Xác minh ném đúng exception do Interceptor kích hoạt
                await actFailing.Should().ThrowAsync<MidstreamConfirmationFaultException>()
                    .WithMessage("*crash giữa chừng sau khi đã ghi dữ liệu vào CSDL*");

                // Xác minh điểm gây lỗi: Dữ liệu đã thực sự được SaveChangesAsync gửi xuống PostgreSQL trong transaction
                midstreamInterceptor.SavedChangesCount.Should().BeGreaterThan(0, "Xác minh SaveChangesAsync đã thực thi các câu lệnh SQL vào CSDL trước khi lỗi xảy ra");
            }

            // 4. Dùng Connection / DbContext ĐỘC LẬP để kiểm tra CSDL sau rollback
            var (verifyContext, verifyTenantService) = CreatePostgresDbContext();
            await using (verifyContext)
            {
                verifyTenantService.SetTenant(OismsDatabaseSeeder.TenantAId);

                // a. Phiếu vẫn Draft, chưa có thông tin xác nhận
                var dbReceipt = await verifyContext.GoodsReceipts.FirstAsync(x => x.Id == receiptToConfirm.Id);
                dbReceipt.Status.Should().Be("Draft", "Phiếu nhập phải vẫn ở trạng thái Draft sau rollback");
                dbReceipt.ConfirmedAt.Should().BeNull("Không được có thời điểm xác nhận khi rollback");
                dbReceipt.ConfirmedBy.Should().BeNull("Không được có người xác nhận khi rollback");

                // b. Tồn hai cấp, WAC và sequence của SKU 1 hiện hữu được giữ nguyên vẹn
                var sku1BranchBal = await verifyContext.InventoryBalances.FirstAsync(b => b.BranchId == branch.Id && b.SkuId == sku1.Id);
                sku1BranchBal.OnHand.Should().Be(10m, "Tồn kho chi nhánh hiện hữu của SKU 1 phải giữ nguyên 10");
                sku1BranchBal.AverageCost.Should().Be(100000m, "WAC hiện hữu của SKU 1 phải giữ nguyên 100.000");
                sku1BranchBal.LastSequence.Should().Be(1, "Sequence hiện hữu của SKU 1 phải giữ nguyên 1");

                var sku1WhBal = await verifyContext.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == warehouse.Id && w.SkuId == sku1.Id);
                sku1WhBal.OnHand.Should().Be(10m, "Tồn kho cấp Kho của SKU 1 phải giữ nguyên 10");
                sku1WhBal.LastSequence.Should().Be(1, "Sequence cấp Kho của SKU 1 phải giữ nguyên 1");

                // c. SKU 2 không có bản ghi tồn dở dang
                var sku2BranchBal = await verifyContext.InventoryBalances.FirstOrDefaultAsync(b => b.BranchId == branch.Id && b.SkuId == sku2.Id);
                sku2BranchBal.Should().BeNull("SKU 2 chưa từng có tồn thì không được để lại bản ghi sau rollback");

                // d. Không có dòng sổ cái dở dang nào của phiếu nhập này
                var ledgerCount = await verifyContext.InventoryTransactions
                    .CountAsync(t => t.ReceiptItem!.ReceiptId == receiptToConfirm.Id);
                ledgerCount.Should().Be(0, "Không được có bất kỳ dòng sổ cái nào của phiếu nhập được lưu lại");

                var totalSku1Ledger = await verifyContext.InventoryTransactions.CountAsync(t => t.SkuId == sku1.Id);
                totalSku1Ledger.Should().Be(1, "Chỉ duy nhất dòng sổ cái ban đầu của SKU 1 được giữ lại");
            }

            // 5. Thử xác nhận lại (Retry) sau lỗi bằng service thật trên DbContext sạch
            var (retryContext, retryTenantService) = CreatePostgresDbContext();
            await using (retryContext)
            {
                retryTenantService.SetTenant(OismsDatabaseSeeder.TenantAId);
                var retryGrService = new GoodsReceiptService(retryContext, retryTenantService, new WacCalculator());

                var retryResult = await retryGrService.ConfirmReceiptAsync(receiptToConfirm.Id, user.Id);
                retryResult.Status.Should().Be("Confirmed", "Lần xác nhận lại phải thành công hoàn toàn");
                retryResult.ConfirmedAt.Should().NotBeNull();
                retryResult.ConfirmedBy.Should().Be(user.Id);
            }

            // 6. Dùng DbContext độc lập kiểm tra kết quả sau Retry: Tồn, WAC, Ledger chỉ được cập nhật đúng một lần
            var (finalContext, finalTenantService) = CreatePostgresDbContext();
            await using (finalContext)
            {
                finalTenantService.SetTenant(OismsDatabaseSeeder.TenantAId);

                var finalReceipt = await finalContext.GoodsReceipts.FirstAsync(x => x.Id == receiptToConfirm.Id);
                finalReceipt.Status.Should().Be("Confirmed");

                // SKU 1: 10 cái @ 100.000 + 20 cái @ 130.000 -> Tồn 30, WAC 120.000, Sequence 2
                var finalSku1Branch = await finalContext.InventoryBalances.FirstAsync(b => b.BranchId == branch.Id && b.SkuId == sku1.Id);
                finalSku1Branch.OnHand.Should().Be(30m);
                finalSku1Branch.AverageCost.Should().Be(120000m);
                finalSku1Branch.LastSequence.Should().Be(2);

                var finalSku1Wh = await finalContext.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == warehouse.Id && w.SkuId == sku1.Id);
                finalSku1Wh.OnHand.Should().Be(30m);
                finalSku1Wh.LastSequence.Should().Be(2);

                // SKU 2: 15 cái @ 80.000 -> Tồn 15, WAC 80.000, Sequence 1
                var finalSku2Branch = await finalContext.InventoryBalances.FirstAsync(b => b.BranchId == branch.Id && b.SkuId == sku2.Id);
                finalSku2Branch.OnHand.Should().Be(15m);
                finalSku2Branch.AverageCost.Should().Be(80000m);
                finalSku2Branch.LastSequence.Should().Be(1);

                // Sổ cái: Đúng 2 dòng cho phiếu nhập này
                var receiptLedgerCount = await finalContext.InventoryTransactions
                    .CountAsync(t => t.ReceiptItem!.ReceiptId == receiptToConfirm.Id);
                receiptLedgerCount.Should().Be(2, "Đúng 2 dòng sổ cái tương ứng với 2 mặt hàng");

                var sku1Ledger = await finalContext.InventoryTransactions
                    .Where(t => t.SkuId == sku1.Id)
                    .OrderBy(t => t.SequenceNo)
                    .ToListAsync();
                sku1Ledger.Should().HaveCount(2);
                sku1Ledger[0].SequenceNo.Should().Be(1);
                sku1Ledger[1].SequenceNo.Should().Be(2);
                sku1Ledger[1].BalanceAfter.Should().Be(30m);
            }
        }
    }

    // =========================================================================
    // 14. BE1-10C: VI PHẠM DEFERRED CONSTRAINT (STOCK_CONSISTENCY) ROLLBACK TẠI SET CONSTRAINTS IMMEDIATE
    // =========================================================================
    [Fact]
    public async Task Postgres_GoodsReceipt_Confirm_DeferredStockConsistencyConstraint_RollsBackAtSetConstraintsImmediate()
    {
        var (setupContext, setupTenantService) = CreatePostgresDbContext();
        await using (setupContext)
        {
            await OismsDatabaseSeeder.SeedAsync(setupContext, PostgresTestEnvironment.AppPassword);
            setupTenantService.SetTenant(OismsDatabaseSeeder.TenantAId);

            var branch = await setupContext.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await setupContext.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var supplier = await setupContext.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await setupContext.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await setupContext.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            var setupGrService = new GoodsReceiptService(setupContext, setupTenantService, new WacCalculator());
            var receipt = await setupGrService.CreateReceiptAsync(new CreateGoodsReceiptDto
            {
                BranchId = branch.Id,
                WarehouseId = warehouse.Id,
                SupplierId = supplier.Id,
                Items = new List<CreateGoodsReceiptItemDto>
                {
                    new() { SkuId = sku.Id, Quantity = 10m, UnitPrice = 120000m }
                }
            }, user.Id);

            // Gắn Interceptor cố tình làm sai lệch tồn kho Warehouse (+999m) so với Branch
            var violationInterceptor = new DeferredStockConsistencyViolationInterceptor();
            var (failingContext, failingTenantService) = CreatePostgresDbContext(violationInterceptor);
            await using (failingContext)
            {
                failingTenantService.SetTenant(OismsDatabaseSeeder.TenantAId);
                var failingGrService = new GoodsReceiptService(failingContext, failingTenantService, new WacCalculator());

                // Chạy chính luồng ConfirmReceiptAsync
                Func<Task> actViolation = async () => await failingGrService.ConfirmReceiptAsync(receipt.Id, user.Id);

                var thrownEx = await actViolation.Should().ThrowAsync<Exception>();

                // 1. Phân biệt rõ điểm phát sinh lỗi:
                // SaveChangesAsync phải hoàn tất thành công vì ràng buộc stock_consistency là DEFERRABLE INITIALLY DEFERRED
                violationInterceptor.SaveChangesCompletedWithoutError.Should().BeTrue(
                    "SaveChanges phải hoàn tất thành công vì ràng buộc stock_consistency là DEFERRABLE INITIALLY DEFERRED; lỗi chỉ được kích hoạt tại SET CONSTRAINTS ALL IMMEDIATE");

                // 2. Xác minh lỗi cụ thể từ PostgreSQL: Mã SQLSTATE 23514 và chẩn đoán vi phạm
                var pgEx = (thrownEx.Which as PostgresException)
                    ?? (thrownEx.Which.InnerException as PostgresException)
                    ?? (thrownEx.Which.GetBaseException() as PostgresException);

                pgEx.Should().NotBeNull("Phải nhận được ngoại lệ PostgresException từ PostgreSQL");
                pgEx!.SqlState.Should().Be("23514", "Lỗi vi phạm trigger deferred stock_consistency phải có mã SQLSTATE 23514 (check_violation)");
                pgEx.MessageText.Should().Contain("Branch balance differs from warehouse totals or ledger sequence",
                    "Thông điệp lỗi phải đúng với chẩn đoán từ hàm oisms_check_stock()");
            }

            // 3. Dùng DbContext độc lập xác minh toàn bộ dữ liệu bị rollback hoàn toàn
            var (verifyContext, verifyTenantService) = CreatePostgresDbContext();
            await using (verifyContext)
            {
                verifyTenantService.SetTenant(OismsDatabaseSeeder.TenantAId);

                // Phiếu vẫn ở trạng thái Draft
                var dbReceipt = await verifyContext.GoodsReceipts.FirstAsync(x => x.Id == receipt.Id);
                dbReceipt.Status.Should().Be("Draft", "Phiếu nhập phải vẫn ở trạng thái Draft sau khi deferred constraint bị vi phạm");
                dbReceipt.ConfirmedAt.Should().BeNull();

                // Tồn kho không bị cập nhật sai lệch, giữ nguyên giá trị ban đầu (0)
                var branchBal = await verifyContext.InventoryBalances
                    .FirstOrDefaultAsync(b => b.BranchId == branch.Id && b.SkuId == sku.Id);
                if (branchBal != null)
                {
                    branchBal.OnHand.Should().Be(0m, "Tồn chi nhánh phải giữ nguyên 0 ban đầu khi bị rollback, không bị tăng thành 10");
                    branchBal.AverageCost.Should().Be(0m, "WAC phải giữ nguyên 0");
                    branchBal.LastSequence.Should().Be(0, "Sequence phải giữ nguyên 0");
                }

                var whBal = await verifyContext.WarehouseInventoryBalances
                    .FirstOrDefaultAsync(w => w.WarehouseId == warehouse.Id && w.SkuId == sku.Id);
                whBal.Should().BeNull("Tồn kho (kể cả giá trị sai lệch +999) phải bị rollback hoàn toàn");

                // Sổ cái không được ghi
                var ledgerCount = await verifyContext.InventoryTransactions
                    .CountAsync(t => t.ReceiptItem!.ReceiptId == receipt.Id);
                ledgerCount.Should().Be(0, "Sổ cái không được ghi bất kỳ dòng nào khi vi phạm deferred constraint");
            }
        }
    }

    // =========================================================================
    // 15. BỔ SUNG: KIỂM CHỨNG CẤP DATABASE VỀ DEFERRED CONSTRAINT KHI SET CONSTRAINTS
    // =========================================================================
    [Fact]
    public async Task Postgres_Inventory_DatabaseLevel_DeferredStockConsistencyConstraint_RollsBackOnViolation()
    {
        var (context, tenantService) = CreatePostgresDbContext();
        await using (context)
        {
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);
            tenantService.SetTenant(OismsDatabaseSeeder.TenantAId);

            var branch = await context.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await context.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var sku = await context.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            var strategy = context.Database.CreateExecutionStrategy();

            Func<Task> actViolation = async () =>
            {
                await strategy.ExecuteAsync(async () =>
                {
                    await using var tx = await context.Database.BeginTransactionAsync();

                    var whBalance = new WarehouseInventoryBalance
                    {
                        TenantId = OismsDatabaseSeeder.TenantAId,
                        BranchId = branch.Id,
                        WarehouseId = warehouse.Id,
                        SkuId = sku.Id,
                        OnHand = 100m,
                        Reserved = 0m,
                        LastSequence = 1,
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                    context.WarehouseInventoryBalances.Add(whBalance);
                    await context.SaveChangesAsync();

                    // Ép trigger DEFERRED kiểm tra ngay lập tức tại lệnh SET CONSTRAINTS
                    await context.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE;");
                    await tx.CommitAsync();
                });
            };

            var ex = await actViolation.Should().ThrowAsync<Exception>();
            var pgEx = (ex.Which as PostgresException)
                ?? (ex.Which.InnerException as PostgresException)
                ?? (ex.Which.GetBaseException() as PostgresException);

            pgEx.Should().NotBeNull();
            pgEx!.SqlState.Should().Be("23514");

            var (verifyContext, verifyTenantService) = CreatePostgresDbContext();
            await using (verifyContext)
            {
                verifyTenantService.SetTenant(OismsDatabaseSeeder.TenantAId);

                var whRecord = await verifyContext.WarehouseInventoryBalances
                    .FirstOrDefaultAsync(w => w.WarehouseId == warehouse.Id && w.SkuId == sku.Id);

                whRecord.Should().BeNull("Dữ liệu vi phạm deferred constraint cấp DB phải bị rollback 100%");
            }
        }
    }

    /// <summary>
    /// Regression Test BE1: Sửa lỗi gửi lặp reservation gây HTTP 500 do unique constraint uq_reservations_2.
    /// Kiểm chứng:
    /// - Gửi reservation lần đầu thành công (200 OK).
    /// - Gửi lặp cùng yêu cầu (OrderId, OrderItemId, WarehouseId, SkuId, Quantity) trả lại kết quả cũ (200 OK), KHÔNG cộng dồn Reserved.
    /// - Gửi lặp cùng dòng đơn nhưng khác số lượng trả 409 Conflict rõ ràng (không ném 500).
    /// - Kiểm tra cặp OrderId / OrderItemId bắt buộc đi cùng nhau (thiếu 1 trong 2 -> 400 Bad Request).
    /// - Kiểm tra dòng đơn thuộc đúng Order, SKU và phạm vi chi nhánh/kho (sai quan hệ -> 400 Bad Request).
    /// - Gửi đồng thời 2 yêu cầu giữ chỗ cho cùng dòng đơn: bảo vệ bằng uq_reservations_2 không văng 500, rollback sạch sẽ khi thất bại.
    /// </summary>
    [Fact]
    public async Task Postgres_Reservation_DuplicateAndConcurrentOrderItem_ShouldBeIdempotentOrConflict_AndNeverThrow500()
    {
        var runCode = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        // 1. Đăng ký & Đăng nhập Chủ shop
        var regOwner = new RegisterTenantRequest
        {
            TenantName = $"Shop Res Idempotency {runCode}",
            TenantCode = $"RES_{runCode}",
            FullName = "Chủ Shop Res",
            Email = $"res_{runCode}@shop.com",
            Password = "Password@123"
        };
        await _client.PostAsJsonAsync("/api/auth/register-tenant", regOwner);

        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = regOwner.Email,
            Password = regOwner.Password
        });
        var auth = await loginRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        var token = auth!.AccessToken;
        var tenantId = auth.User.TenantId;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 2. Tạo Branch1, Warehouse1, Supplier, Product/SKU
        var branchRes1 = await _client.PostAsJsonAsync("/api/branches", new { Name = $"Chi nhánh 1 {runCode}" });
        var branch1 = await branchRes1.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var branch1Id = branch1.GetProperty("id").GetGuid();

        var whRes1 = await _client.PostAsJsonAsync("/api/warehouses", new { BranchId = branch1Id, Name = $"Kho 1 {runCode}" });
        var wh1 = await whRes1.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var wh1Id = wh1.GetProperty("id").GetGuid();

        // Tạo Branch2 & Warehouse2 để kiểm tra validation sai chi nhánh
        var branchRes2 = await _client.PostAsJsonAsync("/api/branches", new { Name = $"Chi nhánh 2 {runCode}" });
        var branch2 = await branchRes2.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var branch2Id = branch2.GetProperty("id").GetGuid();

        var whRes2 = await _client.PostAsJsonAsync("/api/warehouses", new { BranchId = branch2Id, Name = $"Kho 2 {runCode}" });
        var wh2 = await whRes2.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var wh2Id = wh2.GetProperty("id").GetGuid();

        var supplierRes = await _client.PostAsJsonAsync("/api/suppliers", new { Name = $"NCC {runCode}", Phone = "0901234567" });
        var supplier = await supplierRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var supplierId = supplier.GetProperty("id").GetGuid();

        var prodRes = await _client.PostAsJsonAsync("/api/products", new
        {
            Name = $"Áo Test Idempotency {runCode}",
            Variants = new[]
            {
                new { Sku = $"SKU-RES-{runCode}", VariantName = "Màu Cam", RetailPrice = 200000m, WholesalePrice = 140000m }
            }
        });
        var prod = await prodRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var productId = prod.GetProperty("productId").GetGuid();

        var getProdRes = await _client.GetAsync($"/api/products/{productId}");
        var prodDoc = await getProdRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var skuId = prodDoc.GetProperty("variants")[0].GetProperty("id").GetGuid();

        // Nhập kho 10 sản phẩm vào Kho 1
        var grRes = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            BranchId = branch1Id,
            WarehouseId = wh1Id,
            SupplierId = supplierId,
            Items = new List<CreateGoodsReceiptItemDto>
            {
                new() { SkuId = skuId, Quantity = 10m, UnitPrice = 100000m }
            }
        });
        grRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var gr = await grRes.Content.ReadFromJsonAsync<GoodsReceiptResponseDto>(_jsonOptions);
        var cfRes = await _client.PostAsync($"/api/goods-receipts/{gr!.Id}/confirm", null);
        cfRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Tạo Order và 2 OrderItems trong Database
        Guid orderId;
        Guid orderId2;
        Guid orderItemId1;
        Guid orderItemId2;
        Guid orderIdBranch2;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using (db.OpenPrivilegedScope())
            {
                var order = new Order
                {
                    TenantId = tenantId,
                    Number = $"ORD-{runCode}-1",
                    BranchId = branch1Id,
                    Channel = "POS",
                    Status = "Draft",
                    TotalAmount = 1000000m,
                    RequestKey = Guid.NewGuid(),
                    CreatedAt = DateTimeOffset.UtcNow
                };
                db.Orders.Add(order);

                var item1 = new OrderItem
                {
                    TenantId = tenantId,
                    Order = order,
                    SkuId = skuId,
                    SkuCodeSnapshot = $"SKU-RES-{runCode}",
                    SkuNameSnapshot = $"Áo Test Idempotency {runCode}",
                    Quantity = 5m,
                    UnitPrice = 200000m,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                db.OrderItems.Add(item1);

                // Order 2 thuộc cùng chi nhánh 1 cho OrderItem2
                var order2 = new Order
                {
                    TenantId = tenantId,
                    Number = $"ORD-{runCode}-2",
                    BranchId = branch1Id,
                    Channel = "POS",
                    Status = "Draft",
                    TotalAmount = 600000m,
                    RequestKey = Guid.NewGuid(),
                    CreatedAt = DateTimeOffset.UtcNow
                };
                db.Orders.Add(order2);

                var item2 = new OrderItem
                {
                    TenantId = tenantId,
                    Order = order2,
                    SkuId = skuId,
                    SkuCodeSnapshot = $"SKU-RES-{runCode}",
                    SkuNameSnapshot = $"Áo Test Idempotency {runCode}",
                    Quantity = 3m,
                    UnitPrice = 200000m,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                db.OrderItems.Add(item2);

                // Tạo một Order 3 thuộc Branch 2 để test kiểm tra chi nhánh
                var order3 = new Order
                {
                    TenantId = tenantId,
                    Number = $"ORD-{runCode}-3",
                    BranchId = branch2Id,
                    Channel = "POS",
                    Status = "Draft",
                    TotalAmount = 400000m,
                    RequestKey = Guid.NewGuid(),
                    CreatedAt = DateTimeOffset.UtcNow
                };
                db.Orders.Add(order3);

                await db.SaveChangesAsync();

                orderId = order.Id;
                orderId2 = order2.Id;
                orderItemId1 = item1.Id;
                orderItemId2 = item2.Id;
                orderIdBranch2 = order3.Id;
            }
        }

        // 4. Test Case: Validation bắt cặp OrderId & OrderItemId
        var reqOnlyOrderId = new ReserveStockRequestDto
        {
            BranchId = branch1Id,
            WarehouseId = wh1Id,
            SkuId = skuId,
            Quantity = 2m,
            OrderId = orderId,
            OrderItemId = null
        };
        var resOnlyOrder = await _client.PostAsJsonAsync("/api/inventory/reserve", reqOnlyOrderId);
        resOnlyOrder.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Chỉ gửi OrderId mà thiếu OrderItemId phải trả về 400 Bad Request");

        var reqOnlyOrderItem = new ReserveStockRequestDto
        {
            BranchId = branch1Id,
            WarehouseId = wh1Id,
            SkuId = skuId,
            Quantity = 2m,
            OrderId = null,
            OrderItemId = orderItemId1
        };
        var resOnlyItem = await _client.PostAsJsonAsync("/api/inventory/reserve", reqOnlyOrderItem);
        resOnlyItem.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Chỉ gửi OrderItemId mà thiếu OrderId phải trả về 400 Bad Request");

        // 5. Test Case: Validation quan hệ dòng đơn, kho và chi nhánh
        var reqWrongWarehouseBranch = new ReserveStockRequestDto
        {
            BranchId = branch1Id,
            WarehouseId = wh2Id, // Kho thuộc branch 2 nhưng branch gửi là branch 1
            SkuId = skuId,
            Quantity = 2m,
            OrderId = orderId,
            OrderItemId = orderItemId1
        };
        var resWrongWh = await _client.PostAsJsonAsync("/api/inventory/reserve", reqWrongWarehouseBranch);
        resWrongWh.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Kho không thuộc chi nhánh yêu cầu giữ chỗ phải trả về 400");

        var reqWrongOrderBranch = new ReserveStockRequestDto
        {
            BranchId = branch1Id,
            WarehouseId = wh1Id,
            SkuId = skuId,
            Quantity = 2m,
            OrderId = orderIdBranch2, // Đơn thuộc branch 2
            OrderItemId = orderItemId1
        };
        var resWrongOrderBranch = await _client.PostAsJsonAsync("/api/inventory/reserve", reqWrongOrderBranch);
        resWrongOrderBranch.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Đơn hàng không thuộc chi nhánh yêu cầu phải trả về 400");

        var reqWrongOrderMatch = new ReserveStockRequestDto
        {
            BranchId = branch1Id,
            WarehouseId = wh1Id,
            SkuId = skuId,
            Quantity = 2m,
            OrderId = orderId, // Đơn 1
            OrderItemId = orderItemId2 // Dòng đơn thuộc Đơn 2
        };
        var resWrongOrderMatch = await _client.PostAsJsonAsync("/api/inventory/reserve", reqWrongOrderMatch);
        resWrongOrderMatch.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Dòng đơn không thuộc đơn hàng phải trả về 400 Bad Request");

        // 6. Test Case: Giữ chỗ thành công lần đầu cho OrderItem1
        var validReqItem1 = new ReserveStockRequestDto
        {
            BranchId = branch1Id,
            WarehouseId = wh1Id,
            SkuId = skuId,
            Quantity = 2m,
            OrderId = orderId,
            OrderItemId = orderItemId1
        };

        var firstRes = await _client.PostAsJsonAsync("/api/inventory/reserve", validReqItem1);
        firstRes.StatusCode.Should().Be(HttpStatusCode.OK, "Lần giữ chỗ đầu tiên phải thành công 200 OK");
        var firstResult = await firstRes.Content.ReadFromJsonAsync<ReserveStockResponseDto>(_jsonOptions);
        firstResult!.Quantity.Should().Be(2m);
        firstResult.RemainingAvailableStock.Should().Be(8m);

        // Kiểm tra tồn kho trong DB sau lần 1: Reserved = 2, Available = 8
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using (db.OpenPrivilegedScope())
            {
                var branchBal = await db.InventoryBalances.IgnoreQueryFilters().FirstAsync(b => b.BranchId == branch1Id && b.SkuId == skuId);
                branchBal.Reserved.Should().Be(2m);
                (branchBal.OnHand - branchBal.Reserved).Should().Be(8m);

                var whBal = await db.WarehouseInventoryBalances.IgnoreQueryFilters().FirstAsync(w => w.WarehouseId == wh1Id && w.SkuId == skuId);
                whBal.Reserved.Should().Be(2m);
                (whBal.OnHand - whBal.Reserved).Should().Be(8m);

                var countRes = await db.Reservations.IgnoreQueryFilters().CountAsync(r => r.OrderItemId == orderItemId1 && r.WarehouseId == wh1Id);
                countRes.Should().Be(1, "Chỉ có đúng 1 bản ghi reservation được tạo");
            }
        }

        // 7. Test Case: Gửi lặp tuần tự cùng yêu cầu (Idempotent Retry)
        // Gửi tiếp POST /api/inventory/reserve với đúng OrderId/OrderItemId đang có reservation Active
        var dupRes = await _client.PostAsJsonAsync("/api/inventory/reserve", validReqItem1);
        dupRes.StatusCode.Should().Be(HttpStatusCode.OK, "Gửi lặp cùng yêu cầu giữ chỗ phải trả về 200 OK (idempotent)");
        var dupResult = await dupRes.Content.ReadFromJsonAsync<ReserveStockResponseDto>(_jsonOptions);
        dupResult!.ReservationId.Should().Be(firstResult.ReservationId, "Phải trả về mã giữ chỗ cũ");
        dupResult.RemainingAvailableStock.Should().Be(8m, "Tồn khả dụng không được bị trừ lần nữa");

        // Xác minh trong DB: Reserved TUYỆT ĐỐI KHÔNG BỊ CỘNG DỒN THÀNH 4!
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using (db.OpenPrivilegedScope())
            {
                var branchBal = await db.InventoryBalances.IgnoreQueryFilters().FirstAsync(b => b.BranchId == branch1Id && b.SkuId == skuId);
                branchBal.Reserved.Should().Be(2m, "Reserved chi nhánh không được cộng dồn khi gửi lặp");

                var whBal = await db.WarehouseInventoryBalances.IgnoreQueryFilters().FirstAsync(w => w.WarehouseId == wh1Id && w.SkuId == skuId);
                whBal.Reserved.Should().Be(2m, "Reserved kho không được cộng dồn khi gửi lặp");

                var countRes = await db.Reservations.IgnoreQueryFilters().CountAsync(r => r.OrderItemId == orderItemId1 && r.WarehouseId == wh1Id);
                countRes.Should().Be(1, "Vẫn chỉ có duy nhất 1 bản ghi reservation trong cơ sở dữ liệu");
            }
        }

        // 8. Test Case: Gửi lặp cùng OrderItemId nhưng KHÁC số lượng -> Trả về 409 Conflict rõ ràng, không tăng Reserved
        var conflictQtyReq = new ReserveStockRequestDto
        {
            BranchId = branch1Id,
            WarehouseId = wh1Id,
            SkuId = skuId,
            Quantity = 3m, // Khác số lượng 2m ban đầu
            OrderId = orderId,
            OrderItemId = orderItemId1
        };
        var conflictQtyRes = await _client.PostAsJsonAsync("/api/inventory/reserve", conflictQtyReq);
        conflictQtyRes.StatusCode.Should().Be(HttpStatusCode.Conflict, "Gửi lặp dòng đơn với số lượng khác phải bị từ chối 409 Conflict");

        // Xác minh Reserved vẫn giữ nguyên 2m
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using (db.OpenPrivilegedScope())
            {
                var branchBal = await db.InventoryBalances.IgnoreQueryFilters().FirstAsync(b => b.BranchId == branch1Id && b.SkuId == skuId);
                branchBal.Reserved.Should().Be(2m);
            }
        }

        // 9. Test Case: Gửi đồng thời 2 yêu cầu giữ chỗ cho OrderItem2 (Concurrent duplicate requests)
        var concReqItem2 = new ReserveStockRequestDto
        {
            BranchId = branch1Id,
            WarehouseId = wh1Id,
            SkuId = skuId,
            Quantity = 1.5m,
            OrderId = orderId2,
            OrderItemId = orderItemId2
        };

        var task1 = _client.PostAsJsonAsync("/api/inventory/reserve", concReqItem2);
        var task2 = _client.PostAsJsonAsync("/api/inventory/reserve", concReqItem2);
        var concResults = await Task.WhenAll(task1, task2);

        // Cả 2 yêu cầu KHÔNG ĐƯỢC PHÉP trả về HTTP 500 do uq_reservations_2
        foreach (var r in concResults)
        {
            r.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, "Không được văng 500 khi giữ chỗ đồng thời cùng dòng đơn");
            (r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Conflict).Should().BeTrue();
        }

        // Kiểm tra trong DB: OrderItem2 chỉ có đúng 1 reservation Active và Reserved đúng bằng 2 + 1.5 = 3.5m
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using (db.OpenPrivilegedScope())
            {
                var countResItem2 = await db.Reservations.IgnoreQueryFilters().CountAsync(r => r.OrderItemId == orderItemId2 && r.WarehouseId == wh1Id);
                countResItem2.Should().Be(1, "Chỉ duy nhất 1 bản ghi reservation được tạo cho OrderItem2");

                var branchBal = await db.InventoryBalances.IgnoreQueryFilters().FirstAsync(b => b.BranchId == branch1Id && b.SkuId == skuId);
                branchBal.Reserved.Should().Be(3.5m, "Tổng Reserved chi nhánh phải bằng 2 + 1.5 = 3.5");

                var whBal = await db.WarehouseInventoryBalances.IgnoreQueryFilters().FirstAsync(w => w.WarehouseId == wh1Id && w.SkuId == skuId);
                whBal.Reserved.Should().Be(3.5m, "Tổng Reserved kho phải bằng 2 + 1.5 = 3.5");
            }
        }
    }

    // =========================================================================
    // 17. BE1-09 / A10: BỔ SUNG VALIDATION TRẠNG THÁI HOẠT ĐỘNG (ISACTIVE)
    // - Product inactive, SKU active -> 409 Conflict ("Sản phẩm '...' đã ngừng hoạt động.").
    // - Product active, SKU inactive -> 409 Conflict ("Biến thể SKU '...' đã ngừng hoạt động.").
    // - Branch inactive -> 409 Conflict ("Chi nhánh '...' đã ngừng hoạt động.").
    // - Warehouse inactive -> 409 Conflict ("Kho hàng '...' đã ngừng hoạt động.").
    // - Các tài nguyên hợp lệ và active vẫn giữ chỗ thành công (200 OK).
    // - Request bị từ chối không để lại dữ liệu dở dang (Order, OrderItem, Reservation) và không đổi số dư Reserved.
    // - Gửi lặp reservation cũ sau khi Product/SKU bị tắt: Idempotent 200 OK, không mất reservation, không tăng Reserved.
    // - Non-OrderId/OrderItemId request với SKU/Product inactive: Từ chối 409 Conflict, không tạo Order nháp dở dang.
    // =========================================================================
    [Fact]
    public async Task Postgres_Reservation_A10_IsActiveValidation_ResourcesDeactivation_AndIdempotentResend_Regressions()
    {
        var runCode = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        // 1. Đăng ký & Đăng nhập Chủ shop
        var regOwner = new RegisterTenantRequest
        {
            TenantName = $"Shop A10 Res {runCode}",
            TenantCode = $"A10_{runCode}",
            FullName = "Chủ Shop A10",
            Email = $"a10_{runCode}@shop.com",
            Password = "Password@123"
        };
        await _client.PostAsJsonAsync("/api/auth/register-tenant", regOwner);

        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = regOwner.Email,
            Password = regOwner.Password
        });
        var auth = await loginRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        var token = auth!.AccessToken;
        var tenantId = auth.User.TenantId;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 2. Tạo Branch1, Warehouse1, Supplier
        var branchRes1 = await _client.PostAsJsonAsync("/api/branches", new { Name = $"Chi nhánh A10 {runCode}" });
        var branch1 = await branchRes1.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var branch1Id = branch1.GetProperty("id").GetGuid();

        var whRes1 = await _client.PostAsJsonAsync("/api/warehouses", new { BranchId = branch1Id, Name = $"Kho A10 {runCode}" });
        var wh1 = await whRes1.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var wh1Id = wh1.GetProperty("id").GetGuid();

        var supplierRes = await _client.PostAsJsonAsync("/api/suppliers", new { Name = $"NCC A10 {runCode}", Phone = "0901234567" });
        var supplier = await supplierRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var supplierId = supplier.GetProperty("id").GetGuid();

        // 3. Tạo Product 1 (Active) với 2 SKU: SKU 1 (Active) và SKU 2 (sẽ deactivated)
        var prodRes1 = await _client.PostAsJsonAsync("/api/products", new
        {
            Name = $"Áo A10 Active {runCode}",
            Variants = new[]
            {
                new { Sku = $"SKU-ACT-{runCode}", VariantName = "Màu Xanh", RetailPrice = 150000m, WholesalePrice = 100000m },
                new { Sku = $"SKU-INACT-{runCode}", VariantName = "Màu Đỏ", RetailPrice = 160000m, WholesalePrice = 110000m }
            }
        });
        var prod1 = await prodRes1.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var product1Id = prod1.GetProperty("productId").GetGuid();

        var getProd1 = await _client.GetAsync($"/api/products/{product1Id}");
        var prod1Doc = await getProd1.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var sku1Id = prod1Doc.GetProperty("variants")[0].GetProperty("id").GetGuid();
        var sku2Id = prod1Doc.GetProperty("variants")[1].GetProperty("id").GetGuid();

        // 4. Tạo Product 2 (sẽ deactivated) với SKU 3 (Active)
        var prodRes2 = await _client.PostAsJsonAsync("/api/products", new
        {
            Name = $"Quần A10 Discontinued {runCode}",
            Variants = new[]
            {
                new { Sku = $"SKU-DISC-{runCode}", VariantName = "Size L", RetailPrice = 250000m, WholesalePrice = 180000m }
            }
        });
        var prod2 = await prodRes2.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var product2Id = prod2.GetProperty("productId").GetGuid();

        var getProd2 = await _client.GetAsync($"/api/products/{product2Id}");
        var prod2Doc = await getProd2.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var sku3Id = prod2Doc.GetProperty("variants")[0].GetProperty("id").GetGuid();

        // 5. Nhập kho 10 chiếc cho mỗi SKU vào Warehouse1
        var grRes = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            BranchId = branch1Id,
            WarehouseId = wh1Id,
            SupplierId = supplierId,
            Items = new List<CreateGoodsReceiptItemDto>
            {
                new() { SkuId = sku1Id, Quantity = 10m, UnitPrice = 80000m },
                new() { SkuId = sku2Id, Quantity = 10m, UnitPrice = 90000m },
                new() { SkuId = sku3Id, Quantity = 10m, UnitPrice = 120000m }
            }
        });
        var gr = await grRes.Content.ReadFromJsonAsync<GoodsReceiptResponseDto>(_jsonOptions);
        await _client.PostAsync($"/api/goods-receipts/{gr!.Id}/confirm", null);

        // 6. Tạo Order và OrderItems trong DB
        Guid orderId;
        Guid orderItemId1;
        Guid orderItemId2;
        Guid orderItemId3;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using (db.OpenPrivilegedScope())
            {
                var order = new Order
                {
                    TenantId = tenantId,
                    Number = $"ORD-A10-{runCode}",
                    BranchId = branch1Id,
                    Channel = "POS",
                    Status = "Draft",
                    TotalAmount = 1000000m,
                    RequestKey = Guid.NewGuid(),
                    CreatedAt = DateTimeOffset.UtcNow
                };
                db.Orders.Add(order);

                var item1 = new OrderItem
                {
                    TenantId = tenantId,
                    Order = order,
                    SkuId = sku1Id,
                    SkuCodeSnapshot = $"SKU-ACT-{runCode}",
                    SkuNameSnapshot = "Màu Xanh",
                    Quantity = 2m,
                    UnitPrice = 150000m,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                db.OrderItems.Add(item1);

                var item2 = new OrderItem
                {
                    TenantId = tenantId,
                    Order = order,
                    SkuId = sku2Id,
                    SkuCodeSnapshot = $"SKU-INACT-{runCode}",
                    SkuNameSnapshot = "Màu Đỏ",
                    Quantity = 2m,
                    UnitPrice = 160000m,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                db.OrderItems.Add(item2);

                var item3 = new OrderItem
                {
                    TenantId = tenantId,
                    Order = order,
                    SkuId = sku3Id,
                    SkuCodeSnapshot = $"SKU-DISC-{runCode}",
                    SkuNameSnapshot = "Size L",
                    Quantity = 2m,
                    UnitPrice = 250000m,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                db.OrderItems.Add(item3);

                await db.SaveChangesAsync();
                orderId = order.Id;
                orderItemId1 = item1.Id;
                orderItemId2 = item2.Id;
                orderItemId3 = item3.Id;
            }
        }

        // CA 1: Các tài nguyên hợp lệ và active -> Giữ chỗ thành công (200 OK)
        var validReq = new ReserveStockRequestDto
        {
            BranchId = branch1Id,
            WarehouseId = wh1Id,
            SkuId = sku1Id,
            Quantity = 2m,
            OrderId = orderId,
            OrderItemId = orderItemId1
        };
        var validRes = await _client.PostAsJsonAsync("/api/inventory/reserve", validReq);
        validRes.StatusCode.Should().Be(HttpStatusCode.OK, "Tài nguyên hợp lệ và active phải giữ chỗ thành công 200 OK");
        var validResult = await validRes.Content.ReadFromJsonAsync<ReserveStockResponseDto>(_jsonOptions);
        validResult!.Status.Should().Be("Active");
        validResult.RemainingAvailableStock.Should().Be(8m);

        // CA 2: Product active nhưng SKU inactive -> 409 Conflict
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using (db.OpenPrivilegedScope())
            {
                var sku2 = await db.ProductVariants.IgnoreQueryFilters().FirstAsync(v => v.Id == sku2Id);
                sku2.IsActive = false;
                await db.SaveChangesAsync();
            }
        }

        var inactiveSkuReq = new ReserveStockRequestDto
        {
            BranchId = branch1Id,
            WarehouseId = wh1Id,
            SkuId = sku2Id,
            Quantity = 2m,
            OrderId = orderId,
            OrderItemId = orderItemId2
        };
        var inactiveSkuRes = await _client.PostAsJsonAsync("/api/inventory/reserve", inactiveSkuReq);
        inactiveSkuRes.StatusCode.Should().Be(HttpStatusCode.Conflict, "SKU bị tắt phải trả về 409 Conflict");
        var inactiveSkuErr = await inactiveSkuRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        inactiveSkuErr.GetProperty("message").GetString().Should().Contain("Biến thể SKU").And.Contain("đã ngừng hoạt động");

        // Kiểm tra tính toàn vẹn: Không sinh reservation, không thay đổi Reserved của SKU 2
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using (db.OpenPrivilegedScope())
            {
                var countRes = await db.Reservations.IgnoreQueryFilters().CountAsync(r => r.OrderItemId == orderItemId2);
                countRes.Should().Be(0, "Không được tạo bản ghi Reservation dở dang khi bị từ chối");

                var whBal = await db.WarehouseInventoryBalances.IgnoreQueryFilters().FirstAsync(w => w.WarehouseId == wh1Id && w.SkuId == sku2Id);
                whBal.Reserved.Should().Be(0m, "Reserved kho của SKU 2 không được tăng");

                var branchBal = await db.InventoryBalances.IgnoreQueryFilters().FirstAsync(b => b.BranchId == branch1Id && b.SkuId == sku2Id);
                branchBal.Reserved.Should().Be(0m, "Reserved chi nhánh của SKU 2 không được tăng");
            }
        }

        // CA 3: Product inactive, SKU active -> 409 Conflict
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using (db.OpenPrivilegedScope())
            {
                var prod2Entity = await db.Products.IgnoreQueryFilters().FirstAsync(p => p.Id == product2Id);
                prod2Entity.IsActive = false;
                await db.SaveChangesAsync();
            }
        }

        var inactiveProdReq = new ReserveStockRequestDto
        {
            BranchId = branch1Id,
            WarehouseId = wh1Id,
            SkuId = sku3Id,
            Quantity = 2m,
            OrderId = orderId,
            OrderItemId = orderItemId3
        };
        var inactiveProdRes = await _client.PostAsJsonAsync("/api/inventory/reserve", inactiveProdReq);
        inactiveProdRes.StatusCode.Should().Be(HttpStatusCode.Conflict, "Product bị tắt phải trả về 409 Conflict");
        var inactiveProdErr = await inactiveProdRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        inactiveProdErr.GetProperty("message").GetString().Should().Contain("Sản phẩm").And.Contain("đã ngừng hoạt động");

        // Kiểm tra tính toàn vẹn: Không sinh reservation, không thay đổi Reserved của SKU 3
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using (db.OpenPrivilegedScope())
            {
                var countRes = await db.Reservations.IgnoreQueryFilters().CountAsync(r => r.OrderItemId == orderItemId3);
                countRes.Should().Be(0, "Không được tạo bản ghi Reservation khi Product inactive");

                var whBal = await db.WarehouseInventoryBalances.IgnoreQueryFilters().FirstAsync(w => w.WarehouseId == wh1Id && w.SkuId == sku3Id);
                whBal.Reserved.Should().Be(0m);

                var branchBal = await db.InventoryBalances.IgnoreQueryFilters().FirstAsync(b => b.BranchId == branch1Id && b.SkuId == sku3Id);
                branchBal.Reserved.Should().Be(0m);
            }
        }

        // CA 4: Chi nhánh inactive -> 409 Conflict
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using (db.OpenPrivilegedScope())
            {
                var branchEntity = await db.Branches.IgnoreQueryFilters().FirstAsync(b => b.Id == branch1Id);
                branchEntity.IsActive = false;
                await db.SaveChangesAsync();
            }
        }

        Guid orderIdB;
        Guid orderItemIdB;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using (db.OpenPrivilegedScope())
            {
                var ord = new Order
                {
                    TenantId = tenantId,
                    Number = $"ORD-B-{runCode}",
                    BranchId = branch1Id,
                    Channel = "POS",
                    Status = "Draft",
                    TotalAmount = 300000m,
                    RequestKey = Guid.NewGuid(),
                    CreatedAt = DateTimeOffset.UtcNow
                };
                db.Orders.Add(ord);

                var itm = new OrderItem
                {
                    TenantId = tenantId,
                    Order = ord,
                    SkuId = sku1Id,
                    SkuCodeSnapshot = $"SKU-ACT-{runCode}",
                    SkuNameSnapshot = "Màu Xanh",
                    Quantity = 1m,
                    UnitPrice = 150000m,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                db.OrderItems.Add(itm);
                await db.SaveChangesAsync();
                orderIdB = ord.Id;
                orderItemIdB = itm.Id;
            }
        }

        var inactiveBranchReq = new ReserveStockRequestDto
        {
            BranchId = branch1Id,
            WarehouseId = wh1Id,
            SkuId = sku1Id,
            Quantity = 1m,
            OrderId = orderIdB,
            OrderItemId = orderItemIdB
        };
        var inactiveBranchRes = await _client.PostAsJsonAsync("/api/inventory/reserve", inactiveBranchReq);
        inactiveBranchRes.StatusCode.Should().Be(HttpStatusCode.Conflict, "Chi nhánh inactive phải trả về 409 Conflict");
        var inactiveBranchErr = await inactiveBranchRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        inactiveBranchErr.GetProperty("message").GetString().Should().Contain("Chi nhánh").And.Contain("đã ngừng hoạt động");

        // Phục hồi Branch1 hoạt động
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using (db.OpenPrivilegedScope())
            {
                var branchEntity = await db.Branches.IgnoreQueryFilters().FirstAsync(b => b.Id == branch1Id);
                branchEntity.IsActive = true;
                await db.SaveChangesAsync();
            }
        }

        // CA 5: Kho hàng inactive -> 409 Conflict
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using (db.OpenPrivilegedScope())
            {
                var whEntity = await db.Warehouses.IgnoreQueryFilters().FirstAsync(w => w.Id == wh1Id);
                whEntity.IsActive = false;
                await db.SaveChangesAsync();
            }
        }

        var inactiveWhReq = new ReserveStockRequestDto
        {
            BranchId = branch1Id,
            WarehouseId = wh1Id,
            SkuId = sku1Id,
            Quantity = 1m,
            OrderId = orderIdB,
            OrderItemId = orderItemIdB
        };
        var inactiveWhRes = await _client.PostAsJsonAsync("/api/inventory/reserve", inactiveWhReq);
        inactiveWhRes.StatusCode.Should().Be(HttpStatusCode.Conflict, "Kho hàng inactive phải trả về 409 Conflict");
        var inactiveWhErr = await inactiveWhRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        inactiveWhErr.GetProperty("message").GetString().Should().Contain("Kho hàng").And.Contain("đã ngừng hoạt động");

        // Phục hồi Warehouse1 hoạt động
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using (db.OpenPrivilegedScope())
            {
                var whEntity = await db.Warehouses.IgnoreQueryFilters().FirstAsync(w => w.Id == wh1Id);
                whEntity.IsActive = true;
                await db.SaveChangesAsync();
            }
        }

        // CA 6: Gửi lặp reservation cũ sau khi Product/SKU bị tắt -> Idempotent thành công 200 OK, không mất reservation, không tăng Reserved
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using (db.OpenPrivilegedScope())
            {
                var prod1Entity = await db.Products.IgnoreQueryFilters().FirstAsync(p => p.Id == product1Id);
                prod1Entity.IsActive = false;

                var sku1Entity = await db.ProductVariants.IgnoreQueryFilters().FirstAsync(v => v.Id == sku1Id);
                sku1Entity.IsActive = false;

                await db.SaveChangesAsync();
            }
        }

        // Gửi lặp lại đúng reservation CA 1 (validReq cho OrderItem 1)
        var dupAfterDeactRes = await _client.PostAsJsonAsync("/api/inventory/reserve", validReq);
        dupAfterDeactRes.StatusCode.Should().Be(HttpStatusCode.OK, "Gửi lặp reservation cũ đã Active vẫn phải thành công 200 OK dù Product/SKU bị tắt sau đó");
        var dupAfterDeactResult = await dupAfterDeactRes.Content.ReadFromJsonAsync<ReserveStockResponseDto>(_jsonOptions);
        dupAfterDeactResult!.ReservationId.Should().Be(validResult!.ReservationId, "Phải trả về đúng ID của reservation cũ");
        dupAfterDeactResult.Quantity.Should().Be(2m);

        // Xác minh trong DB: Reserved vẫn là 2m (không bị cộng dồn thành 4m), reservation vẫn Active
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using (db.OpenPrivilegedScope())
            {
                var branchBal = await db.InventoryBalances.IgnoreQueryFilters().FirstAsync(b => b.BranchId == branch1Id && b.SkuId == sku1Id);
                branchBal.Reserved.Should().Be(2m, "Reserved chi nhánh không được cộng dồn khi gửi lặp");

                var whBal = await db.WarehouseInventoryBalances.IgnoreQueryFilters().FirstAsync(w => w.WarehouseId == wh1Id && w.SkuId == sku1Id);
                whBal.Reserved.Should().Be(2m, "Reserved kho không được cộng dồn khi gửi lặp");

                var resEntity = await db.Reservations.IgnoreQueryFilters().FirstAsync(r => r.Id == validResult.ReservationId);
                resEntity.Status.Should().Be("Active", "Reservation cũ không được phép bị hủy hay mất trạng thái Active");
            }
        }

        // CA 7: Tạo reservation mới không truyền OrderId/OrderItemId (luồng POS) cho SKU bị tắt -> 409 Conflict, KHÔNG tạo Order dở dang
        var posInactiveReq = new ReserveStockRequestDto
        {
            BranchId = branch1Id,
            WarehouseId = wh1Id,
            SkuId = sku1Id, // Đang inactive
            Quantity = 1m
        };
        var posInactiveRes = await _client.PostAsJsonAsync("/api/inventory/reserve", posInactiveReq);
        posInactiveRes.StatusCode.Should().Be(HttpStatusCode.Conflict, "POS giữ chỗ cho SKU inactive phải bị từ chối 409 Conflict");

        // Kiểm tra không có Order/OrderItem hay Reservation rác phát sinh trong DB
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using (db.OpenPrivilegedScope())
            {
                var ordersCount = await db.Orders.IgnoreQueryFilters().CountAsync(o => o.TenantId == tenantId && o.Status == "Draft" && o.Number.StartsWith("ORD-RES-"));
                ordersCount.Should().Be(0, "Không được tự sinh Order nháp dở dang khi bị từ chối giữ chỗ");

                var resCount = await db.Reservations.IgnoreQueryFilters().CountAsync(r => r.TenantId == tenantId && r.SkuId == sku1Id);
                resCount.Should().Be(1, "Chỉ có duy nhất 1 bản ghi reservation ban đầu của OrderItem 1");
            }
        }
    }
}
