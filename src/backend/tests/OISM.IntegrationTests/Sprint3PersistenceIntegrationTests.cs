using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OISM.Application.Common.Interfaces;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Bộ kiểm thử tích hợp thực thi trực tiếp trên cơ sở dữ liệu PostgreSQL 16+ thật (database: oisms_test).
/// Kiểm chứng nền tảng Persistence Task 1 Sprint 3:
/// 1. Đọc/ghi hợp lệ và bảo toàn độ chính xác số học (precision) của 7 thực thể mới.
/// 2. Kiểm tra ràng buộc toàn vẹn cơ sở dữ liệu (Check constraints, Foreign keys).
/// 3. Kiểm chứng cơ chế cô lập Multi-tenant qua Global Query Filters trên PostgreSQL thật.
/// </summary>
public class Sprint3PersistenceIntegrationTests : IAsyncLifetime
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

    [Fact]
    public async Task Postgres_Payment_Persistence_And_ConstraintValidation()
    {
        var (context, tenantService) = CreatePostgresDbContext();
        await using (context)
        {
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);
            tenantService.SetTenant(OismsDatabaseSeeder.TenantAId);

            var branch = await context.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await context.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await context.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            // 1. Tạo đơn hàng Order
            var orderId = Guid.NewGuid();
            var order = new Order
            {
                Id = orderId,
                TenantId = OismsDatabaseSeeder.TenantAId,
                Number = $"ORD-TEST-{Guid.NewGuid():N}"[..20],
                BranchId = branch.Id,
                Channel = "POS",
                Status = "Draft",
                TotalAmount = 150000.50m,
                RequestKey = Guid.NewGuid(),
                CreatedBy = user.Id,
                CreatedAt = DateTimeOffset.UtcNow,
                Items = new List<OrderItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        TenantId = OismsDatabaseSeeder.TenantAId,
                        OrderId = orderId,
                        SkuId = sku.Id,
                        SkuCodeSnapshot = sku.SKU,
                        SkuNameSnapshot = "Sản phẩm test",
                        Quantity = 1,
                        UnitPrice = 150000.50m,
                        CreatedAt = DateTimeOffset.UtcNow
                    }
                }
            };
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            // 2. Tạo Payment Tiền mặt hợp lệ (cash_received >= amount)
            var paymentId = Guid.NewGuid();
            var payment = new Payment
            {
                Id = paymentId,
                TenantId = OismsDatabaseSeeder.TenantAId,
                OrderId = orderId,
                Method = "Cash",
                Status = "Succeeded",
                Amount = 150000.50m,
                CashReceived = 200000.00m,
                PaidAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            };
            context.Payments.Add(payment);
            await context.SaveChangesAsync();

            // Đọc lại từ PostgreSQL
            var savedPayment = await context.Payments.FirstOrDefaultAsync(p => p.Id == paymentId);
            savedPayment.Should().NotBeNull();
            savedPayment!.Amount.Should().Be(150000.50m);
            savedPayment.CashReceived.Should().Be(200000.00m);
            savedPayment.Status.Should().Be("Succeeded");

            // 3. Kiểm chứng check constraint: ck_payments_cash_received
            // Nếu method = Cash và status = Succeeded nhưng cash_received < amount -> DB phải từ chối
            var order2Id = Guid.NewGuid();
            var order2 = new Order
            {
                Id = order2Id,
                TenantId = OismsDatabaseSeeder.TenantAId,
                Number = $"ORD-TEST-{Guid.NewGuid():N}"[..20],
                BranchId = branch.Id,
                Channel = "POS",
                Status = "Draft",
                TotalAmount = 200000m,
                RequestKey = Guid.NewGuid(),
                CreatedBy = user.Id,
                CreatedAt = DateTimeOffset.UtcNow
            };
            context.Orders.Add(order2);
            await context.SaveChangesAsync();

            var invalidPayment = new Payment
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                OrderId = order2Id,
                Method = "Cash",
                Status = "Succeeded",
                Amount = 200000m,
                CashReceived = 100000m, // Nhỏ hơn amount -> vi phạm ck_payments_cash_received
                PaidAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            };
            context.Payments.Add(invalidPayment);

            var act = async () => await context.SaveChangesAsync();
            await act.Should().ThrowAsync<DbUpdateException>()
                .Where(ex => ex.InnerException is PostgresException);
        }
    }

    [Fact]
    public async Task Postgres_StockTransfer_Persistence_And_Workflow()
    {
        var (context, tenantService) = CreatePostgresDbContext();
        await using (context)
        {
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);
            tenantService.SetTenant(OismsDatabaseSeeder.TenantAId);

            var branch = await context.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse1 = await context.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);

            var warehouse2 = new Warehouse
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                BranchId = branch.Id,
                Name = "Kho phụ kiểm thử chuyển kho",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            context.Warehouses.Add(warehouse2);
            await context.SaveChangesAsync();

            var user = await context.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await context.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            // 1. Tạo phiếu chuyển kho trạng thái Draft
            var transferId = Guid.NewGuid();
            var transfer = new StockTransfer
            {
                Id = transferId,
                TenantId = OismsDatabaseSeeder.TenantAId,
                Number = $"TRF-TEST-{Guid.NewGuid():N}"[..20],
                FromBranchId = branch.Id,
                ToBranchId = branch.Id,
                FromWarehouseId = warehouse1.Id,
                ToWarehouseId = warehouse2.Id,
                Status = "Draft",
                CreatedBy = user.Id,
                Note = "Chuyển nội bộ test",
                CreatedAt = DateTimeOffset.UtcNow,
                Items = new List<StockTransferItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        TenantId = OismsDatabaseSeeder.TenantAId,
                        TransferId = transferId,
                        SkuId = sku.Id,
                        Quantity = 25.500m,
                        UnitCost = 45000.123456m,
                        CreatedAt = DateTimeOffset.UtcNow
                    }
                }
            };

            context.StockTransfers.Add(transfer);
            await context.SaveChangesAsync();

            // Đọc lại từ PostgreSQL và kiểm tra độ chính xác số học numeric(18,3) và numeric(20,6)
            var savedTransfer = await context.StockTransfers
                .Include(t => t.Items)
                .FirstOrDefaultAsync(t => t.Id == transferId);

            savedTransfer.Should().NotBeNull();
            savedTransfer!.Status.Should().Be("Draft");
            savedTransfer.Items.Should().HaveCount(1);
            var item = savedTransfer.Items.First();
            item.Quantity.Should().Be(25.500m);
            item.UnitCost.Should().Be(45000.123456m);

            // 2. Chuyển sang InTransit (có dispatched_at)
            savedTransfer.Status = "InTransit";
            savedTransfer.DispatchedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync();

            var transitTransfer = await context.StockTransfers.FirstOrDefaultAsync(t => t.Id == transferId);
            transitTransfer!.Status.Should().Be("InTransit");
            transitTransfer.DispatchedAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Postgres_StockCount_Persistence_And_Confirmation()
    {
        var (context, tenantService) = CreatePostgresDbContext();
        await using (context)
        {
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);
            tenantService.SetTenant(OismsDatabaseSeeder.TenantAId);

            var branch = await context.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await context.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var user = await context.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await context.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            // 1. Tạo phiên kiểm kê
            var countId = Guid.NewGuid();
            var count = new StockCount
            {
                Id = countId,
                TenantId = OismsDatabaseSeeder.TenantAId,
                Number = $"CNT-TEST-{Guid.NewGuid():N}"[..20],
                BranchId = branch.Id,
                WarehouseId = warehouse.Id,
                Status = "Draft",
                CreatedBy = user.Id,
                Note = "Kiểm kê tháng 10 test",
                CreatedAt = DateTimeOffset.UtcNow,
                Items = new List<StockCountItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        TenantId = OismsDatabaseSeeder.TenantAId,
                        StockCountId = countId,
                        SkuId = sku.Id,
                        ExpectedQuantity = 100.000m,
                        CountedQuantity = 105.000m,
                        BalanceSequenceAtSnapshot = 12345L,
                        AdjustmentUnitCost = 72000.500000m,
                        CreatedAt = DateTimeOffset.UtcNow
                    }
                }
            };

            context.StockCounts.Add(count);
            await context.SaveChangesAsync();

            // Đọc lại từ PostgreSQL
            var savedCount = await context.StockCounts
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.Id == countId);

            savedCount.Should().NotBeNull();
            savedCount!.Items.Should().HaveCount(1);
            var countItem = savedCount.Items.First();
            countItem.ExpectedQuantity.Should().Be(100.000m);
            countItem.CountedQuantity.Should().Be(105.000m);
            countItem.BalanceSequenceAtSnapshot.Should().Be(12345L);
            countItem.AdjustmentUnitCost.Should().Be(72000.500000m);

            // 2. Chốt kiểm kê (Confirmed) thỏa mãn check constraint ck_stock_counts_confirmed
            savedCount.Status = "Confirmed";
            savedCount.ConfirmedAt = DateTimeOffset.UtcNow;
            savedCount.ConfirmedBy = user.Id;
            await context.SaveChangesAsync();

            var confirmedCount = await context.StockCounts.FirstOrDefaultAsync(c => c.Id == countId);
            confirmedCount!.Status.Should().Be("Confirmed");
            confirmedCount.ConfirmedAt.Should().NotBeNull();
            confirmedCount.ConfirmedBy.Should().Be(user.Id);
        }
    }

    [Fact]
    public async Task Postgres_InventoryAdjustment_Persistence_And_Query()
    {
        var (context, tenantService) = CreatePostgresDbContext();
        await using (context)
        {
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);
            tenantService.SetTenant(OismsDatabaseSeeder.TenantAId);

            var branch = await context.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await context.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var user = await context.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await context.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            var adjId = Guid.NewGuid();
            var adjustment = new InventoryAdjustment
            {
                Id = adjId,
                TenantId = OismsDatabaseSeeder.TenantAId,
                Number = $"ADJ-TEST-{Guid.NewGuid():N}"[..20],
                BranchId = branch.Id,
                WarehouseId = warehouse.Id,
                Reason = "Bút toán điều chỉnh thừa kiểm kê định kỳ",
                Status = "Draft",
                CreatedBy = user.Id,
                CreatedAt = DateTimeOffset.UtcNow,
                Items = new List<InventoryAdjustmentItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        TenantId = OismsDatabaseSeeder.TenantAId,
                        AdjustmentId = adjId,
                        SkuId = sku.Id,
                        QuantityDelta = 5.000m,
                        UnitCost = 72000.500000m,
                        CreatedAt = DateTimeOffset.UtcNow
                    }
                }
            };

            context.InventoryAdjustments.Add(adjustment);
            await context.SaveChangesAsync();

            var savedAdj = await context.InventoryAdjustments
                .Include(a => a.Items)
                .FirstOrDefaultAsync(a => a.Id == adjId);

            savedAdj.Should().NotBeNull();
            savedAdj!.Reason.Should().Be("Bút toán điều chỉnh thừa kiểm kê định kỳ");
            savedAdj.Items.Should().HaveCount(1);
            var adjItem = savedAdj.Items.First();
            adjItem.QuantityDelta.Should().Be(5.000m);
            adjItem.UnitCost.Should().Be(72000.500000m);
        }
    }

    [Fact]
    public async Task Postgres_Sprint3_AllNewEntities_MustEnforceTenantIsolation_ViaGlobalQueryFilters()
    {
        var (context, tenantService) = CreatePostgresDbContext();
        await using (context)
        {
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);

            // BƯỚC 1: Đăng nhập Tenant A và tạo dữ liệu ở cả 4 phân hệ mới
            tenantService.SetTenant(OismsDatabaseSeeder.TenantAId);

            var branchA = await context.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouseA1 = await context.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branchA.Id);

            var warehouseA2 = new Warehouse
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                BranchId = branchA.Id,
                Name = "Kho phụ Tenant A",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            context.Warehouses.Add(warehouseA2);
            await context.SaveChangesAsync();

            var userA = await context.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var skuA = await context.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            // Order & Payment
            var orderA = new Order
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                Number = $"ORD-ISO-A-{Guid.NewGuid():N}"[..20],
                BranchId = branchA.Id,
                Channel = "POS",
                Status = "Draft",
                TotalAmount = 100000m,
                RequestKey = Guid.NewGuid(),
                CreatedBy = userA.Id,
                CreatedAt = DateTimeOffset.UtcNow
            };
            context.Orders.Add(orderA);

            var paymentA = new Payment
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                OrderId = orderA.Id,
                Method = "BankTransfer",
                Status = "Succeeded",
                Amount = 100000m,
                PaidAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            };
            context.Payments.Add(paymentA);

            // Transfer
            var transferA = new StockTransfer
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                Number = $"TRF-ISO-A-{Guid.NewGuid():N}"[..20],
                FromBranchId = branchA.Id,
                ToBranchId = branchA.Id,
                FromWarehouseId = warehouseA1.Id,
                ToWarehouseId = warehouseA2.Id,
                Status = "Draft",
                CreatedBy = userA.Id,
                CreatedAt = DateTimeOffset.UtcNow
            };
            context.StockTransfers.Add(transferA);

            // Stock Count
            var countA = new StockCount
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                Number = $"CNT-ISO-A-{Guid.NewGuid():N}"[..20],
                BranchId = branchA.Id,
                WarehouseId = warehouseA1.Id,
                Status = "Draft",
                CreatedBy = userA.Id,
                CreatedAt = DateTimeOffset.UtcNow
            };
            context.StockCounts.Add(countA);

            // Adjustment
            var adjA = new InventoryAdjustment
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                Number = $"ADJ-ISO-A-{Guid.NewGuid():N}"[..20],
                BranchId = branchA.Id,
                WarehouseId = warehouseA1.Id,
                Reason = "Cô lập tenant kiểm thử",
                Status = "Draft",
                CreatedBy = userA.Id,
                CreatedAt = DateTimeOffset.UtcNow
            };
            context.InventoryAdjustments.Add(adjA);

            await context.SaveChangesAsync();

            // BƯỚC 2: Kiểm tra Tenant A nhìn thấy đúng dữ liệu của mình
            (await context.Payments.CountAsync()).Should().BeGreaterThanOrEqualTo(1);
            (await context.StockTransfers.CountAsync()).Should().BeGreaterThanOrEqualTo(1);
            (await context.StockCounts.CountAsync()).Should().BeGreaterThanOrEqualTo(1);
            (await context.InventoryAdjustments.CountAsync()).Should().BeGreaterThanOrEqualTo(1);

            // BƯỚC 3: Chuyển sang phiên Tenant B -> Toàn bộ Global Query Filters phải lọc sạch, trả về 0 bản ghi của Tenant A
            tenantService.SetTenant(OismsDatabaseSeeder.TenantBId);

            var paymentsUnderB = await context.Payments.Where(p => p.Id == paymentA.Id).ToListAsync();
            var transfersUnderB = await context.StockTransfers.Where(t => t.Id == transferA.Id).ToListAsync();
            var countsUnderB = await context.StockCounts.Where(c => c.Id == countA.Id).ToListAsync();
            var adjustmentsUnderB = await context.InventoryAdjustments.Where(a => a.Id == adjA.Id).ToListAsync();

            paymentsUnderB.Should().BeEmpty("Global Query Filter phải bảo vệ bảng payments khỏi rò rỉ dữ liệu đa tenant");
            transfersUnderB.Should().BeEmpty("Global Query Filter phải bảo vệ bảng stock_transfers khỏi rò rỉ dữ liệu đa tenant");
            countsUnderB.Should().BeEmpty("Global Query Filter phải bảo vệ bảng stock_counts khỏi rò rỉ dữ liệu đa tenant");
            adjustmentsUnderB.Should().BeEmpty("Global Query Filter phải bảo vệ bảng inventory_adjustments khỏi rò rỉ dữ liệu đa tenant");
        }
    }
}
