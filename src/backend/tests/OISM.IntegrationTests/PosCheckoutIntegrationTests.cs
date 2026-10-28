using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Inventory.Services;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Orders.Services;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Bộ kiểm thử tích hợp thực thi trên cơ sở dữ liệu PostgreSQL 16+ thật (database: oisms_test).
/// Kiểm chứng toàn diện chức năng Thanh toán nhanh tại quầy POS (Task S3-BE1-T02-POS-CHECKOUT):
/// AC-01: Dual routes (POST /api/pos/checkout & alias POST /api/orders/checkout).
/// AC-02: Toàn giỏ nguyên tử (1 Order Completed, 1 Payment Succeeded, Reservations Consumed, Sổ cái OUT, Tồn 2 cấp & WAC).
/// AC-03: Rollback toàn diện khi không đủ tồn kho (409 INSUFFICIENT_STOCK, không để lại rác).
/// AC-04: Idempotency (replay cùng key/payload, từ chối khác payload, cô lập multi-tenant).
/// AC-05: Concurrency stress test (50 tasks đồng thời tranh 1 suất tồn kho, đúng 1 thắng, không deadlock).
/// AC-06: Bất biến WAC snapshot và bảo mật dữ liệu nhạy cảm (không lộ giá vốn cho thu ngân).
/// AC-07: Xác thực, phân quyền RBAC và kiểm tra hợp lệ DTO / tiền khách đưa.
/// </summary>
public class PosCheckoutIntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public PosCheckoutIntegrationTests(WebApplicationFactoryWithPostgres factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private class TestTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    public class MidstreamCheckoutFaultException : Exception
    {
        public MidstreamCheckoutFaultException(string message) : base(message) { }
    }

    public class MidstreamFailureInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public bool ShouldFail { get; set; } = true;
        public int SavedChangesCount { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (ShouldFail)
            {
                SavedChangesCount = result;
                throw new MidstreamCheckoutFaultException("Mô phỏng đứt kết nối mạng/crash giữa chừng sau khi đã ghi dữ liệu vào CSDL nhưng trước COMMIT.");
            }
            return base.SavedChangesAsync(eventData, result, cancellationToken);
        }
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
        // Khi gọi SeedAsync, TenantId phải là null (chế độ seeder hệ thống) để không vi phạm bảo vệ chéo Tenant A / Tenant B
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

    private async Task SeedStockAsync(Guid tenantId, Guid branchId, Guid warehouseId, Guid supplierId, Guid userId, Guid skuId, decimal quantity, decimal unitPrice)
    {
        var (context, tenantService) = CreatePostgresDbContext(tenantId);
        await using (context)
        {
            var wacCalc = new WacCalculator();
            var service = new GoodsReceiptService(context, tenantService, wacCalc);

            var receiptDto = new CreateGoodsReceiptDto
            {
                BranchId = branchId,
                WarehouseId = warehouseId,
                SupplierId = supplierId,
                Note = "Seed tồn kho cho integration test",
                Items = new List<CreateGoodsReceiptItemDto>
                {
                    new() { SkuId = skuId, Quantity = quantity, UnitPrice = unitPrice }
                }
            };

            var receipt = await service.CreateReceiptAsync(receiptDto, userId);
            await service.ConfirmReceiptAsync(receipt.Id, userId);
        }
    }

    // =========================================================================
    // AC-01: Dual Routes & Contract Compatibility
    // =========================================================================
    [Fact]
    public async Task AC01_DualRoutes_CanonicalAndAlias_ShouldBothSucceedWithIdenticalStructure()
    {
        await SeedDatabaseAsync();

        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);

        Guid branchId;
        Guid warehouseId;
        Guid supplierId;
        Guid userId;
        Guid skuId;
        string skuCode;
        decimal retailPrice;

        await using (var ctx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await ctx.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await ctx.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            branchId = branch.Id;
            warehouseId = warehouse.Id;
            supplierId = supplier.Id;
            userId = user.Id;
            skuId = sku.Id;
            skuCode = sku.SKU;
            retailPrice = sku.RetailPrice;
        }

        // Nhập kho 10 sản phẩm
        await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branchId, warehouseId, supplierId, userId, skuId, 10m, 150000m);

        // 1. Tuyến đường chính thức Canonical: POST /api/pos/checkout
        var requestCanonical = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPrice,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1, UnitPrice = retailPrice }
            }
        };

        var responseCanonical = await _client.PostAsJsonAsync("/api/pos/checkout", requestCanonical);
        responseCanonical.StatusCode.Should().Be(HttpStatusCode.OK);
        var resultCanonical = await responseCanonical.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);
        resultCanonical.Should().NotBeNull();
        resultCanonical!.Success.Should().BeTrue();
        resultCanonical.OrderNumber.Should().StartWith("POS-");
        resultCanonical.Receipt.Should().NotBeNull();
        resultCanonical.Receipt.Items.Should().HaveCount(1);

        // 2. Tuyến đường Alias: POST /api/orders/checkout
        var requestAlias = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPrice,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1, UnitPrice = retailPrice }
            }
        };

        var responseAlias = await _client.PostAsJsonAsync("/api/orders/checkout", requestAlias);
        responseAlias.StatusCode.Should().Be(HttpStatusCode.OK);
        var resultAlias = await responseAlias.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);
        resultAlias.Should().NotBeNull();
        resultAlias!.Success.Should().BeTrue();
        resultAlias.OrderNumber.Should().StartWith("POS-");
        resultAlias.Receipt.Should().NotBeNull();
        resultAlias.Receipt.Items.Should().HaveCount(1);
    }

    // =========================================================================
    // AC-02: Atomic POS Fast Checkout (Multi-SKU Cart)
    // =========================================================================
    [Fact]
    public async Task AC02_AtomicPosCheckout_MultiSku_ShouldCreateOrderPaymentReservationsAndLedgerOut_InSingleTransaction()
    {
        await SeedDatabaseAsync();

        Guid branchId;
        Guid warehouseId;
        Guid supplierId;
        Guid userId;
        Guid sku1Id;
        Guid sku2Id;
        string sku1Code;
        string sku2Code = "POLO-NAM-TRANG-L";
        decimal sku1Retail = 299000m;
        decimal sku2Retail = 350000m;
        decimal sku1Cost = 150000m;
        decimal sku2Cost = 200000m;

        await using (var ctx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await ctx.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await ctx.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var product = await ctx.Products.FirstAsync(p => p.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku1 = await ctx.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            var sku2 = new ProductVariant
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                ProductId = product.Id,
                Code = sku2Code,
                SKU = sku2Code,
                RetailPrice = sku2Retail,
                WholesalePrice = 250000m,
                Unit = "piece",
                IsActive = true
            };
            ctx.ProductVariants.Add(sku2);
            await ctx.SaveChangesAsync();

            branchId = branch.Id;
            warehouseId = warehouse.Id;
            supplierId = supplier.Id;
            userId = user.Id;
            sku1Id = sku1.Id;
            sku1Code = sku1.SKU;
            sku2Id = sku2.Id;
        }

        // Nhập kho 10 đơn vị cho mỗi SKU
        await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branchId, warehouseId, supplierId, userId, sku1Id, 10m, sku1Cost);
        await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branchId, warehouseId, supplierId, userId, sku2Id, 10m, sku2Cost);

        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);

        // Mua 2 SKU1 và 3 SKU2
        var expectedTotal = (2 * sku1Retail) + (3 * sku2Retail); // 2*299k + 3*350k = 598k + 1050k = 1,648,000đ
        var cashGiven = 2000000m;
        var expectedChange = cashGiven - expectedTotal; // 352,000đ
        var requestKey = Guid.NewGuid();

        var request = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            CustomerName = "Nguyễn Văn Khách",
            CustomerPhone = "0987654321",
            AmountGiven = cashGiven,
            RequestKey = requestKey,
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = sku1Id, SkuCode = sku1Code, Quantity = 2, UnitPrice = sku1Retail },
                new() { SkuId = sku2Id, SkuCode = sku2Code, Quantity = 3, UnitPrice = sku2Retail }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/pos/checkout", request);
        var responseContent = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Thanh toán thất bại: {responseContent}");

        var result = await response.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.TotalAmount.Should().Be(expectedTotal);
        result.AmountGiven.Should().Be(cashGiven);
        result.ChangeDue.Should().Be(expectedChange);
        result.Receipt.Should().NotBeNull();
        result.Receipt.Items.Should().HaveCount(2);

        // Kiểm chứng tính toàn vẹn trong PostgreSQL
        await using (var verifyCtx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            // 1. Order
            var order = await verifyCtx.Orders
                .Include(o => o.Items)
                .Include(o => o.Payment)
                .FirstOrDefaultAsync(o => o.Id == result.OrderId);

            order.Should().NotBeNull();
            order!.Status.Should().Be("Completed");
            order.ConfirmedAt.Should().NotBeNull();
            order.CompletedAt.Should().NotBeNull();
            order.Channel.Should().Be("POS");
            order.TotalAmount.Should().Be(expectedTotal);
            order.RequestKey.Should().Be(requestKey);

            // 2. Payment
            order.Payment.Should().NotBeNull();
            order.Payment!.Status.Should().Be("Succeeded");
            order.Payment.Method.Should().Be("Cash");
            order.Payment.Amount.Should().Be(expectedTotal);
            order.Payment.CashReceived.Should().Be(cashGiven);
            order.Payment.PaidAt.Should().NotBeNull();

            // 3. OrderItems
            order.Items.Should().HaveCount(2);
            var item1 = order.Items.First(i => i.SkuId == sku1Id);
            var item2 = order.Items.First(i => i.SkuId == sku2Id);

            item1.Quantity.Should().Be(2m);
            item1.UnitPrice.Should().Be(sku1Retail);
            item1.CostPrice.Should().Be(sku1Cost); // WAC snapshot

            item2.Quantity.Should().Be(3m);
            item2.UnitPrice.Should().Be(sku2Retail);
            item2.CostPrice.Should().Be(sku2Cost); // WAC snapshot

            // 4. Reservations (Consumed)
            var reservations = await verifyCtx.Reservations
                .Where(r => r.OrderId == order.Id)
                .ToListAsync();
            reservations.Should().HaveCount(2);
            reservations.Should().OnlyContain(r => r.Status == "Consumed");

            // 5. InventoryTransactions (OUT)
            var transactions = await verifyCtx.InventoryTransactions
                .Where(t => t.TenantId == OismsDatabaseSeeder.TenantAId && (t.SkuId == sku1Id || t.SkuId == sku2Id) && t.Type == "OUT")
                .ToListAsync();
            transactions.Should().HaveCount(2);
            var tx1 = transactions.First(t => t.SkuId == sku1Id);
            var tx2 = transactions.First(t => t.SkuId == sku2Id);
            tx1.Quantity.Should().Be(2m);
            tx1.BalanceAfter.Should().Be(8m); // 10 - 2 = 8
            tx2.Quantity.Should().Be(3m);
            tx2.BalanceAfter.Should().Be(7m); // 10 - 3 = 7

            // 6. Số dư 2 cấp
            var bal1 = await verifyCtx.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == sku1Id);
            bal1.OnHand.Should().Be(8m);
            bal1.Reserved.Should().Be(0m);

            var whBal1 = await verifyCtx.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == warehouseId && w.SkuId == sku1Id);
            whBal1.OnHand.Should().Be(8m);
            whBal1.Reserved.Should().Be(0m);

            var bal2 = await verifyCtx.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == sku2Id);
            bal2.OnHand.Should().Be(7m);
            bal2.Reserved.Should().Be(0m);

            var whBal2 = await verifyCtx.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == warehouseId && w.SkuId == sku2Id);
            whBal2.OnHand.Should().Be(7m);
            whBal2.Reserved.Should().Be(0m);
        }
    }

    // =========================================================================
    // AC-03: Insufficient Stock Handling & Complete Transaction Rollback
    // =========================================================================
    [Fact]
    public async Task AC03_InsufficientStock_ShouldReturn409_AndRollbackEntireTransactionWithoutOrphans()
    {
        await SeedDatabaseAsync();

        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);

        Guid branchId;
        Guid warehouseId;
        Guid supplierId;
        Guid userId;
        Guid skuId;
        string skuCode;
        decimal retailPrice;

        await using (var ctx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await ctx.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await ctx.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            branchId = branch.Id;
            warehouseId = warehouse.Id;
            supplierId = supplier.Id;
            userId = user.Id;
            skuId = sku.Id;
            skuCode = sku.SKU;
            retailPrice = sku.RetailPrice;
        }

        // Nhập kho chỉ 5 sản phẩm
        await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branchId, warehouseId, supplierId, userId, skuId, 5m, 100000m);

        // Yêu cầu mua 99 sản phẩm (vượt tồn khả dụng)
        var failRequestKey = Guid.NewGuid();
        var request = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPrice * 99,
            RequestKey = failRequestKey,
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 99, UnitPrice = retailPrice }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/pos/checkout", request);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var errorBody = await response.Content.ReadAsStringAsync();
        errorBody.Should().Contain("INSUFFICIENT_STOCK");

        // Kiểm chứng Rollback CSDL: Không có bản ghi rác nào được lưu lại
        await using (var verifyCtx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var orderExists = await verifyCtx.Orders.AnyAsync(o => o.RequestKey == failRequestKey);
            orderExists.Should().BeFalse("Giao dịch bị từ chối không được phép tạo bản ghi Order rác.");

            var reservationCount = await verifyCtx.Reservations.CountAsync(r => r.TenantId == OismsDatabaseSeeder.TenantAId);
            reservationCount.Should().Be(0, "Không được phép để lại reservation rác.");

            var txOutCount = await verifyCtx.InventoryTransactions.CountAsync(t => t.TenantId == OismsDatabaseSeeder.TenantAId && t.Type == "OUT");
            txOutCount.Should().Be(0, "Không được phép ghi nhận bút toán OUT rác.");

            var bal = await verifyCtx.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bal.OnHand.Should().Be(5m, "Tồn kho không được bị suy giảm khi giao dịch thất bại.");
            bal.Reserved.Should().Be(0m);
        }
    }

    [Fact]
    public async Task AC03_MultiItemCart_OneSkuOutOfStock_ShouldRollbackEntirelyWithoutPartialSaves()
    {
        await SeedDatabaseAsync();

        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);

        Guid branchId;
        Guid warehouseId;
        Guid supplierId;
        Guid userId;
        Guid sku1Id;
        string sku1Code;
        decimal retailPrice1;
        Guid sku2Id;
        string sku2Code;
        decimal retailPrice2;

        await using (var ctx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await ctx.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await ctx.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku1 = await ctx.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            branchId = branch.Id;
            warehouseId = warehouse.Id;
            supplierId = supplier.Id;
            userId = user.Id;
            sku1Id = sku1.Id;
            sku1Code = sku1.SKU;
            retailPrice1 = sku1.RetailPrice;

            sku2Id = Guid.NewGuid();
            sku2Code = "SKU-AO-2";
            retailPrice2 = 200000m;
            ctx.ProductVariants.Add(new ProductVariant
            {
                Id = sku2Id,
                TenantId = OismsDatabaseSeeder.TenantAId,
                ProductId = sku1.ProductId,
                SKU = sku2Code,
                VariantName = "Biến thể 2",
                RetailPrice = retailPrice2,
                CostPrice = 120000m,
                IsActive = true
            });
            await ctx.SaveChangesAsync();
        }

        // Nhập kho SKU 1: 10 sản phẩm (dư tồn)
        await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branchId, warehouseId, supplierId, userId, sku1Id, 10m, 100000m);
        // Nhập kho SKU 2: 1 sản phẩm (thiếu tồn nếu yêu cầu mua 5)
        await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branchId, warehouseId, supplierId, userId, sku2Id, 1m, 80000m);

        var failRequestKey = Guid.NewGuid();
        var request = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPrice1 * 2 + retailPrice2 * 5,
            RequestKey = failRequestKey,
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = sku1Id, SkuCode = sku1Code, Quantity = 2, UnitPrice = retailPrice1 },
                new() { SkuId = sku2Id, SkuCode = sku2Code, Quantity = 5, UnitPrice = retailPrice2 } // Thiếu tồn!
            }
        };

        var response = await _client.PostAsJsonAsync("/api/pos/checkout", request);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var errorBody = await response.Content.ReadAsStringAsync();
        errorBody.Should().Contain("INSUFFICIENT_STOCK");

        // Kiểm chứng Rollback sạch sẽ toàn diện: không lưu bất kỳ phần nào của đơn hàng hay reservation
        await using (var verifyCtx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var orderExists = await verifyCtx.Orders.AnyAsync(o => o.RequestKey == failRequestKey);
            orderExists.Should().BeFalse("Giao dịch giỏ nhiều dòng thất bại không được lưu Order.");

            var reservationCount = await verifyCtx.Reservations.CountAsync(r => r.TenantId == OismsDatabaseSeeder.TenantAId);
            reservationCount.Should().Be(0, "Không được lưu reservation cho SKU 1 hoặc SKU 2.");

            var txOutCount = await verifyCtx.InventoryTransactions.CountAsync(t => t.TenantId == OismsDatabaseSeeder.TenantAId && t.Type == "OUT");
            txOutCount.Should().Be(0, "Không được xuất sổ cái cho bất kỳ mặt hàng nào.");

            var bal1 = await verifyCtx.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == sku1Id);
            bal1.OnHand.Should().Be(10m, "Tồn kho SKU 1 phải giữ nguyên vẹn 10.");

            var bal2 = await verifyCtx.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == sku2Id);
            bal2.OnHand.Should().Be(1m, "Tồn kho SKU 2 phải giữ nguyên vẹn 1.");
        }
    }

    [Fact]
    public async Task AC03_FaultInjection_MidstreamFailure_ShouldRollbackAndAllowCleanRetry()
    {
        await SeedDatabaseAsync();

        Guid branchId;
        Guid warehouseId;
        Guid supplierId;
        Guid userId;
        Guid skuId;
        string skuCode;
        decimal retailPrice;

        await using (var ctx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await ctx.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await ctx.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            branchId = branch.Id;
            warehouseId = warehouse.Id;
            supplierId = supplier.Id;
            userId = user.Id;
            skuId = sku.Id;
            skuCode = sku.SKU;
            retailPrice = sku.RetailPrice;
        }

        // Nhập kho 10 sản phẩm
        await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branchId, warehouseId, supplierId, userId, skuId, 10m, 120000m);

        var requestKey = Guid.NewGuid();
        var request = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPrice * 2,
            RequestKey = requestKey,
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2, UnitPrice = retailPrice }
            }
        };

        // 1. Fault Injection: Interceptor ném lỗi ngay sau SaveChangesAsync trước khi COMMIT giao dịch
        var midstreamInterceptor = new MidstreamFailureInterceptor { ShouldFail = true };
        var (failingContext, failingTenantService) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId, midstreamInterceptor);
        await using (failingContext)
        {
            var failingOrderService = new OrderService(failingContext, failingTenantService, Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>.Instance);
            Func<Task> actFailing = async () => await failingOrderService.CheckoutPosAsync(request, userId);
            await actFailing.Should().ThrowAsync<MidstreamCheckoutFaultException>();
            midstreamInterceptor.SavedChangesCount.Should().BeGreaterThan(0, "Xác nhận các câu lệnh INSERT/UPDATE đã được gửi xuống PostgreSQL trước khi lỗi ném ra.");
        }

        // 2. Xác minh Rollback hoàn toàn: CSDL PostgreSQL không bị ô nhiễm dữ liệu dở dang
        await using (var verifyCtx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var orderExists = await verifyCtx.Orders.AnyAsync(o => o.RequestKey == requestKey);
            orderExists.Should().BeFalse("Giao dịch bị crash không được để lại Order.");

            var reservationCount = await verifyCtx.Reservations.CountAsync(r => r.TenantId == OismsDatabaseSeeder.TenantAId);
            reservationCount.Should().Be(0, "Không được để lại Reservation.");

            var txOutCount = await verifyCtx.InventoryTransactions.CountAsync(t => t.TenantId == OismsDatabaseSeeder.TenantAId && t.Type == "OUT");
            txOutCount.Should().Be(0, "Không được để lại Sổ cái OUT.");

            var bal = await verifyCtx.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bal.OnHand.Should().Be(10m, "Tồn kho phải được giữ nguyên 10.");
        }

        // 3. Retry với cùng RequestKey và cùng payload sau khi kết nối phục hồi -> Thành công đúng 1 lần
        var (retryContext, retryTenantService) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
        await using (retryContext)
        {
            var cleanOrderService = new OrderService(retryContext, retryTenantService, Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>.Instance);
            var retryResult = await cleanOrderService.CheckoutPosAsync(request, userId);
            retryResult.Should().NotBeNull();
            retryResult.Success.Should().BeTrue();
            retryResult.OrderNumber.Should().StartWith("POS-");
        }

        // 4. Xác minh CSDL sau Retry thành công: Đúng 1 Order, 1 Payment, tồn kho giảm chính xác 2
        await using (var finalCtx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var ordersCount = await finalCtx.Orders.CountAsync(o => o.RequestKey == requestKey);
            ordersCount.Should().Be(1, "Sau retry chỉ có đúng 1 Order được tạo.");

            var paymentsCount = await finalCtx.Payments.CountAsync(p => p.TenantId == OismsDatabaseSeeder.TenantAId);
            paymentsCount.Should().Be(1, "Chỉ có đúng 1 Payment được tạo.");

            var bal = await finalCtx.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bal.OnHand.Should().Be(8m, "Tồn kho bị trừ chính xác 2 đơn vị sau retry thành công.");
        }
    }

    // =========================================================================
    // AC-04: Comprehensive Idempotency Replay, Field Mismatches, and Alias Replay
    // =========================================================================
    [Fact]
    public async Task AC04_Idempotency_ComprehensiveFieldMismatch_And_ReplayWithCatalogAndStockChanges()
    {
        await SeedDatabaseAsync();

        var cashierTokenA = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierTokenA);

        Guid branchAId;
        Guid branchOtherId;
        Guid warehouseAId;
        Guid supplierAId;
        Guid userAId;
        Guid skuAId;
        string skuACode;
        decimal retailPriceA;

        await using (var ctx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await ctx.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await ctx.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            branchAId = branch.Id;
            warehouseAId = warehouse.Id;
            supplierAId = supplier.Id;
            userAId = user.Id;
            skuAId = sku.Id;
            skuACode = sku.SKU;
            retailPriceA = sku.RetailPrice;

            // Tạo thêm chi nhánh khác trong cùng Tenant A để test mismatch BranchId
            branchOtherId = Guid.NewGuid();
            ctx.Branches.Add(new Branch
            {
                Id = branchOtherId,
                TenantId = OismsDatabaseSeeder.TenantAId,
                Name = "Chi nhánh Quận 3",
                Address = "456 Nam Kỳ Khởi Nghĩa, Quận 3, TP.HCM",
                IsActive = true
            });
            await ctx.SaveChangesAsync();
        }

        await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branchAId, warehouseAId, supplierAId, userAId, skuAId, 20m, 100000m);

        var sharedRequestKey = Guid.NewGuid();

        var initialRequest = new PosCheckoutRequestDto
        {
            BranchId = branchAId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPriceA * 2,
            RequestKey = sharedRequestKey,
            CustomerName = "Nguyễn Văn A",
            CustomerPhone = "0901234567",
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuAId, SkuCode = skuACode, Quantity = 2, UnitPrice = retailPriceA }
            }
        };

        // 1. Gửi lần 1 -> Thành công
        var response1 = await _client.PostAsJsonAsync("/api/pos/checkout", initialRequest);
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        var result1 = await response1.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);
        result1.Should().NotBeNull();
        var orderNumber1 = result1!.OrderNumber;

        // 2. Replay cùng RequestKey & cùng Payload qua route Canonical -> Trả kết quả cũ, KHÔNG trừ kho lần 2
        var response2 = await _client.PostAsJsonAsync("/api/pos/checkout", initialRequest);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        var result2 = await response2.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);
        result2.Should().NotBeNull();
        result2!.OrderNumber.Should().Be(orderNumber1);
        result2.Message.Should().Contain("Idempotent replay");

        // 3. Replay cùng RequestKey & cùng Payload qua route Alias (/api/orders/checkout) -> Cũng trả về kết quả cũ
        var responseAlias = await _client.PostAsJsonAsync("/api/orders/checkout", initialRequest);
        responseAlias.StatusCode.Should().Be(HttpStatusCode.OK);
        var resultAlias = await responseAlias.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);
        resultAlias.Should().NotBeNull();
        resultAlias!.OrderNumber.Should().Be(orderNumber1);

        // Kiểm tra tồn kho chỉ bị trừ 2 đơn vị (20 - 2 = 18), không phải trừ 4 hay 6
        await using (var verifyCtx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var bal = await verifyCtx.InventoryBalances.FirstAsync(b => b.BranchId == branchAId && b.SkuId == skuAId);
            bal.OnHand.Should().Be(18m);

            var ordersCount = await verifyCtx.Orders.CountAsync(o => o.TenantId == OismsDatabaseSeeder.TenantAId && o.RequestKey == sharedRequestKey);
            ordersCount.Should().Be(1, "Chỉ tạo duy nhất 1 bản ghi đơn hàng khi gọi lặp.");
        }

        // 4. Test F-01: Đổi riêng từng trường dẫn đến 409 IDEMPOTENCY_PAYLOAD_MISMATCH mà không đổi DB

        // a) Đổi UnitPrice
        var diffUnitPriceReq = new PosCheckoutRequestDto
        {
            BranchId = branchAId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPriceA * 2,
            RequestKey = sharedRequestKey,
            CustomerName = "Nguyễn Văn A",
            CustomerPhone = "0901234567",
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuAId, SkuCode = skuACode, Quantity = 2, UnitPrice = retailPriceA + 1000m } // Đổi giá
            }
        };
        var diffUnitPriceRes = await _client.PostAsJsonAsync("/api/pos/checkout", diffUnitPriceReq);
        diffUnitPriceRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await diffUnitPriceRes.Content.ReadAsStringAsync()).Should().Contain("IDEMPOTENCY_PAYLOAD_MISMATCH");

        // b) Đổi AmountGiven
        var diffAmountGivenReq = new PosCheckoutRequestDto
        {
            BranchId = branchAId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = (retailPriceA * 2) + 50000m, // Đổi tiền khách đưa
            RequestKey = sharedRequestKey,
            CustomerName = "Nguyễn Văn A",
            CustomerPhone = "0901234567",
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuAId, SkuCode = skuACode, Quantity = 2, UnitPrice = retailPriceA }
            }
        };
        var diffAmountGivenRes = await _client.PostAsJsonAsync("/api/pos/checkout", diffAmountGivenReq);
        diffAmountGivenRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await diffAmountGivenRes.Content.ReadAsStringAsync()).Should().Contain("IDEMPOTENCY_PAYLOAD_MISMATCH");

        // c) Đổi CustomerName
        var diffCustomerNameReq = new PosCheckoutRequestDto
        {
            BranchId = branchAId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPriceA * 2,
            RequestKey = sharedRequestKey,
            CustomerName = "Trần Thị B", // Đổi tên khách
            CustomerPhone = "0901234567",
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuAId, SkuCode = skuACode, Quantity = 2, UnitPrice = retailPriceA }
            }
        };
        var diffCustomerNameRes = await _client.PostAsJsonAsync("/api/pos/checkout", diffCustomerNameReq);
        diffCustomerNameRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await diffCustomerNameRes.Content.ReadAsStringAsync()).Should().Contain("IDEMPOTENCY_PAYLOAD_MISMATCH");

        // d) Đổi CustomerPhone
        var diffCustomerPhoneReq = new PosCheckoutRequestDto
        {
            BranchId = branchAId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPriceA * 2,
            RequestKey = sharedRequestKey,
            CustomerName = "Nguyễn Văn A",
            CustomerPhone = "0999888777", // Đổi SĐT khách
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuAId, SkuCode = skuACode, Quantity = 2, UnitPrice = retailPriceA }
            }
        };
        var diffCustomerPhoneRes = await _client.PostAsJsonAsync("/api/pos/checkout", diffCustomerPhoneReq);
        diffCustomerPhoneRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await diffCustomerPhoneRes.Content.ReadAsStringAsync()).Should().Contain("IDEMPOTENCY_PAYLOAD_MISMATCH");

        // e) Đổi PaymentMethod
        var diffMethodReq = new PosCheckoutRequestDto
        {
            BranchId = branchAId,
            Channel = "POS",
            PaymentMethod = "BankTransfer", // Đổi method
            AmountGiven = null,
            RequestKey = sharedRequestKey,
            CustomerName = "Nguyễn Văn A",
            CustomerPhone = "0901234567",
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuAId, SkuCode = skuACode, Quantity = 2, UnitPrice = retailPriceA }
            }
        };
        var diffMethodRes = await _client.PostAsJsonAsync("/api/pos/checkout", diffMethodReq);
        diffMethodRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await diffMethodRes.Content.ReadAsStringAsync()).Should().Contain("IDEMPOTENCY_PAYLOAD_MISMATCH");

        // f) Đổi BranchId
        var diffBranchReq = new PosCheckoutRequestDto
        {
            BranchId = branchOtherId, // Đổi chi nhánh
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPriceA * 2,
            RequestKey = sharedRequestKey,
            CustomerName = "Nguyễn Văn A",
            CustomerPhone = "0901234567",
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuAId, SkuCode = skuACode, Quantity = 2, UnitPrice = retailPriceA }
            }
        };
        var diffBranchRes = await _client.PostAsJsonAsync("/api/pos/checkout", diffBranchReq);
        diffBranchRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await diffBranchRes.Content.ReadAsStringAsync()).Should().Contain("IDEMPOTENCY_PAYLOAD_MISMATCH");

        // g) Đổi Số lượng Quantity
        var diffQtyReq = new PosCheckoutRequestDto
        {
            BranchId = branchAId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPriceA * 5,
            RequestKey = sharedRequestKey,
            CustomerName = "Nguyễn Văn A",
            CustomerPhone = "0901234567",
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuAId, SkuCode = skuACode, Quantity = 5, UnitPrice = retailPriceA } // Đổi số lượng
            }
        };
        var diffQtyRes = await _client.PostAsJsonAsync("/api/pos/checkout", diffQtyReq);
        diffQtyRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await diffQtyRes.Content.ReadAsStringAsync()).Should().Contain("IDEMPOTENCY_PAYLOAD_MISMATCH");

        // 5. Test F-01: Replay sau khi Bán hết sạch tồn kho (tồn tụt về 0) và Giá niêm yết Catalog tăng gấp đôi
        var drainStockReq = new PosCheckoutRequestDto
        {
            BranchId = branchAId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPriceA * 18,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuAId, SkuCode = skuACode, Quantity = 18, UnitPrice = retailPriceA }
            }
        };
        var drainRes = await _client.PostAsJsonAsync("/api/pos/checkout", drainStockReq);
        drainRes.StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var modifyCtx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var skuToUpdate = await modifyCtx.ProductVariants.FirstAsync(v => v.Id == skuAId);
            skuToUpdate.RetailPrice = retailPriceA * 2; // Tăng giá gấp đôi trong Catalog
            await modifyCtx.SaveChangesAsync();

            var balNow = await modifyCtx.InventoryBalances.FirstAsync(b => b.BranchId == branchAId && b.SkuId == skuAId);
            balNow.OnHand.Should().Be(0m, "Tồn kho đã cạn sạch về 0.");
        }

        // Replay đơn ban đầu vẫn phải thành công rực rỡ và trả về đúng dữ liệu giao dịch đã ghi nhận
        var replayAfterChangeRes = await _client.PostAsJsonAsync("/api/pos/checkout", initialRequest);
        replayAfterChangeRes.StatusCode.Should().Be(HttpStatusCode.OK, "Replay đơn đã hoàn tất không được kiểm tra lại giá catalog mới hay tồn hiện tại!");
        var replayAfterResult = await replayAfterChangeRes.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);
        replayAfterResult!.OrderNumber.Should().Be(orderNumber1);
        replayAfterResult.TotalAmount.Should().Be(retailPriceA * 2);

        // 6. Kiểm chứng Multi-tenant: Tenant B dùng CHÍNH XÁC RequestKey này không bị xung đột với Tenant A
        var cashierTokenB = await LoginAsAsync("cashier@shopb.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierTokenB);

        Guid branchBId;
        Guid warehouseBId;
        Guid supplierBId;
        Guid userBId;
        Guid skuBId;
        string skuBCode;
        decimal retailPriceB;

        await using (var ctx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantBId).Context)
        {
            var branchB = await ctx.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantBId);
            var warehouseB = await ctx.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantBId && w.BranchId == branchB.Id);
            var supplierB = await ctx.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantBId);
            var userB = await ctx.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantBId);
            var skuB = await ctx.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantBId);

            branchBId = branchB.Id;
            warehouseBId = warehouseB.Id;
            supplierBId = supplierB.Id;
            userBId = userB.Id;
            skuBId = skuB.Id;
            skuBCode = skuB.SKU;
            retailPriceB = skuB.RetailPrice;
        }

        await SeedStockAsync(OismsDatabaseSeeder.TenantBId, branchBId, warehouseBId, supplierBId, userBId, skuBId, 10m, 50000m);

        var tenantBRequest = new PosCheckoutRequestDto
        {
            BranchId = branchBId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPriceB,
            RequestKey = sharedRequestKey, // Dùng cùng key với Tenant A
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuBId, SkuCode = skuBCode, Quantity = 1, UnitPrice = retailPriceB }
            }
        };

        var responseTenantB = await _client.PostAsJsonAsync("/api/pos/checkout", tenantBRequest);
        responseTenantB.StatusCode.Should().Be(HttpStatusCode.OK, "Khóa Idempotency giữa 2 Tenant hoàn toàn độc lập và được cô lập an toàn.");
    }

    [Fact]
    public async Task AC04_ConcurrentFirstSubmission_AcrossDualAliases_ShouldCreateExactlyOneOrder()
    {
        await SeedDatabaseAsync();

        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);

        Guid branchId;
        Guid warehouseId;
        Guid supplierId;
        Guid userId;
        Guid skuId;
        string skuCode;
        decimal retailPrice;

        await using (var ctx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await ctx.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await ctx.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            branchId = branch.Id;
            warehouseId = warehouse.Id;
            supplierId = supplier.Id;
            userId = user.Id;
            skuId = sku.Id;
            skuCode = sku.SKU;
            retailPrice = sku.RetailPrice;
        }

        await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branchId, warehouseId, supplierId, userId, skuId, 20m, 100000m);

        var sharedKey = Guid.NewGuid();
        var req = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPrice * 2,
            RequestKey = sharedKey,
            CustomerName = "Nguyễn Văn Concurrent",
            CustomerPhone = "0909888777",
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2, UnitPrice = retailPrice }
            }
        };

        // F-04: KHÔNG tạo đơn trước khi bắt đầu!
        // Dùng key chưa tồn tại trong CSDL, phát đồng thời 10 request (5 canonical + 5 alias) cùng lúc.
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            var url = i % 2 == 0 ? "/api/pos/checkout" : "/api/orders/checkout";
            var res = await _client.PostAsJsonAsync(url, req);
            var body = await res.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);
            return (StatusCode: res.StatusCode, OrderId: body?.OrderId, OrderNumber: body?.OrderNumber);
        }).ToList();

        var results = await Task.WhenAll(tasks);

        // Tất cả 10 request đồng thời phải trả về HTTP 200 OK
        foreach (var r in results)
        {
            r.StatusCode.Should().Be(HttpStatusCode.OK);
            r.OrderId.Should().NotBeNull();
            r.OrderId.Should().NotBe(Guid.Empty);
            r.OrderNumber.Should().NotBeNullOrWhiteSpace();
        }

        // Tất cả 10 request phải trả về cùng một OrderId và cùng một OrderNumber
        var firstOrderId = results[0].OrderId;
        var firstOrderNumber = results[0].OrderNumber;
        results.All(r => r.OrderId == firstOrderId).Should().BeTrue("Mọi request đồng thời cùng key phải trả về đúng OrderId của đơn duy nhất.");
        results.All(r => r.OrderNumber == firstOrderNumber).Should().BeTrue("Mọi request đồng thời cùng key phải trả về đúng OrderNumber của đơn duy nhất.");

        // Kiểm tra cơ sở dữ liệu: đúng 1 Order, 1 Payment, tồn kho và ledger chỉ bị tác động 1 lần
        await using (var verifyCtx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var ordersCount = await verifyCtx.Orders.CountAsync(o => o.RequestKey == sharedKey);
            ordersCount.Should().Be(1, "Chỉ được tạo đúng 1 Order trong CSDL cho RequestKey.");

            var paymentsCount = await verifyCtx.Payments.CountAsync(p => p.OrderId == firstOrderId!.Value);
            paymentsCount.Should().Be(1, "Chỉ được tạo đúng 1 Payment trong CSDL.");

            var bal = await verifyCtx.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bal.OnHand.Should().Be(18m, "Tồn kho chi nhánh chỉ được trừ 1 lần duy nhất (20 - 2 = 18).");

            var whBal = await verifyCtx.WarehouseInventoryBalances.FirstAsync(b => b.WarehouseId == warehouseId && b.SkuId == skuId);
            whBal.OnHand.Should().Be(18m, "Tồn kho cấp kho chỉ được trừ 1 lần duy nhất (20 - 2 = 18).");

            var ledgerEntries = await verifyCtx.InventoryTransactions.Where(t => t.OrderItem!.OrderId == firstOrderId!.Value).ToListAsync();
            ledgerEntries.Count.Should().Be(1, "Chỉ được ghi đúng 1 bản ghi sổ cái OUT.");
            ledgerEntries[0].Quantity.Should().Be(2m);
            ledgerEntries[0].BalanceAfter.Should().Be(18m);
        }
    }

    [Fact]
    public async Task AC04_ConcurrentReplay_AcrossDualAliases_ShouldAllReturnSameOrder()
    {
        await SeedDatabaseAsync();

        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);

        Guid branchId;
        Guid warehouseId;
        Guid supplierId;
        Guid userId;
        Guid skuId;
        string skuCode;
        decimal retailPrice;

        await using (var ctx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await ctx.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await ctx.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            branchId = branch.Id;
            warehouseId = warehouse.Id;
            supplierId = supplier.Id;
            userId = user.Id;
            skuId = sku.Id;
            skuCode = sku.SKU;
            retailPrice = sku.RetailPrice;
        }

        await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branchId, warehouseId, supplierId, userId, skuId, 20m, 100000m);

        var sharedKey = Guid.NewGuid();
        var req = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPrice * 2,
            RequestKey = sharedKey,
            CustomerName = "Nguyễn Văn A",
            CustomerPhone = "0909999999",
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2, UnitPrice = retailPrice }
            }
        };

        // Gửi đơn hàng ban đầu
        var initRes = await _client.PostAsJsonAsync("/api/pos/checkout", req);
        initRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var initResult = await initRes.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);
        var expectedOrderNumber = initResult!.OrderNumber;

        // Bắn đồng thời 10 request replay qua cả 2 alias routes (5 canonical + 5 alias)
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            var url = i % 2 == 0 ? "/api/pos/checkout" : "/api/orders/checkout";
            var res = await _client.PostAsJsonAsync(url, req);
            var body = await res.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);
            return (StatusCode: res.StatusCode, OrderNumber: body?.OrderNumber);
        }).ToList();

        var results = await Task.WhenAll(tasks);

        foreach (var r in results)
        {
            r.StatusCode.Should().Be(HttpStatusCode.OK);
            r.OrderNumber.Should().Be(expectedOrderNumber);
        }

        // Tồn kho chỉ bị trừ 1 lần duy nhất (20 - 2 = 18)
        await using (var verifyCtx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var bal = await verifyCtx.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bal.OnHand.Should().Be(18m);

            var ordersCount = await verifyCtx.Orders.CountAsync(o => o.RequestKey == sharedKey);
            ordersCount.Should().Be(1);
        }
    }

    // =========================================================================
    // AC-05: Concurrency Stress Test (50 Checkouts tranh 1 tồn kho)
    // =========================================================================
    [Fact]
    public async Task AC05_ConcurrencyStressTest_50ConcurrentCheckouts_CompetingForOneUnit_ExactlyOneWins()
    {
        await SeedDatabaseAsync();

        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);

        Guid branchId;
        Guid warehouseId;
        Guid supplierId;
        Guid userId;
        Guid skuId;
        string skuCode;
        decimal retailPrice;

        await using (var ctx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await ctx.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await ctx.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            branchId = branch.Id;
            warehouseId = warehouse.Id;
            supplierId = supplier.Id;
            userId = user.Id;
            skuId = sku.Id;
            skuCode = sku.SKU;
            retailPrice = sku.RetailPrice;
        }

        // Nhập kho ĐÚNG 1 sản phẩm duy nhất!
        await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branchId, warehouseId, supplierId, userId, skuId, 1m, 100000m);

        // Kích hoạt 50 yêu cầu checkout đồng thời tranh giành 1 sản phẩm cuối cùng
        const int concurrencyLevel = 50;
        var tasks = Enumerable.Range(0, concurrencyLevel).Select(async i =>
        {
            var localClient = _factory.CreateClient();
            localClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);

            var req = new PosCheckoutRequestDto
            {
                BranchId = branchId,
                Channel = "POS",
                PaymentMethod = "Cash",
                AmountGiven = retailPrice,
                RequestKey = Guid.NewGuid(), // Mỗi yêu cầu có một RequestKey riêng
                Items = new List<OrderItemRequestDto>
                {
                    new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1, UnitPrice = retailPrice }
                }
            };

            return await localClient.PostAsJsonAsync("/api/pos/checkout", req);
        });

        var responses = await Task.WhenAll(tasks);

        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        var otherCount = responses.Count(r => r.StatusCode != HttpStatusCode.OK && r.StatusCode != HttpStatusCode.Conflict);

        otherCount.Should().Be(0, "Không được có lỗi không mong muốn hoặc văng 500 khi chịu tải đồng thời.");
        successCount.Should().Be(1, "Trong 50 yêu cầu tranh chấp 1 sản phẩm, chỉ duy nhất 1 yêu cầu được phép thành công!");
        conflictCount.Should().Be(49, "49 yêu cầu còn lại phải bị từ chối bằng 409 INSUFFICIENT_STOCK.");

        // Kiểm tra tồn kho cuối cùng phải là 0, không được âm!
        await using (var verifyCtx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var bal = await verifyCtx.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bal.OnHand.Should().Be(0m);
            bal.Reserved.Should().Be(0m);

            var whBal = await verifyCtx.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == warehouseId && w.SkuId == skuId);
            whBal.OnHand.Should().Be(0m);
            whBal.Reserved.Should().Be(0m);

            var completedOrdersCount = await verifyCtx.Orders.CountAsync(o => o.TenantId == OismsDatabaseSeeder.TenantAId && o.Status == "Completed");
            completedOrdersCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task AC05_DeadlockPrevention_ReversedSkuArrays_ShouldCompleteWithoutDeadlock()
    {
        await SeedDatabaseAsync();

        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);

        Guid branchId;
        Guid warehouseId;
        Guid supplierId;
        Guid userId;
        Guid sku1Id;
        string sku1Code;
        decimal retailPrice1;
        Guid sku2Id;
        string sku2Code;
        decimal retailPrice2;

        await using (var ctx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await ctx.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await ctx.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku1 = await ctx.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            branchId = branch.Id;
            warehouseId = warehouse.Id;
            supplierId = supplier.Id;
            userId = user.Id;
            sku1Id = sku1.Id;
            sku1Code = sku1.SKU;
            retailPrice1 = sku1.RetailPrice;

            sku2Id = Guid.NewGuid();
            sku2Code = "SKU-AO-2";
            retailPrice2 = 200000m;
            ctx.ProductVariants.Add(new ProductVariant
            {
                Id = sku2Id,
                TenantId = OismsDatabaseSeeder.TenantAId,
                ProductId = sku1.ProductId,
                SKU = sku2Code,
                VariantName = "Biến thể 2",
                RetailPrice = retailPrice2,
                CostPrice = 120000m,
                IsActive = true
            });
            await ctx.SaveChangesAsync();
        }

        // Nhập kho dồi dào cho cả 2 SKU
        await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branchId, warehouseId, supplierId, userId, sku1Id, 50m, 100000m);
        await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branchId, warehouseId, supplierId, userId, sku2Id, 50m, 120000m);

        // Giỏ hàng 1: [SKU 1, SKU 2]
        var req1 = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = (retailPrice1 + retailPrice2) * 5,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = sku1Id, SkuCode = sku1Code, Quantity = 1, UnitPrice = retailPrice1 },
                new() { SkuId = sku2Id, SkuCode = sku2Code, Quantity = 1, UnitPrice = retailPrice2 }
            }
        };

        // Giỏ hàng 2: ĐẢO NGƯỢC THỨ TỰ [SKU 2, SKU 1]
        var req2 = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = (retailPrice1 + retailPrice2) * 5,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = sku2Id, SkuCode = sku2Code, Quantity = 1, UnitPrice = retailPrice2 },
                new() { SkuId = sku1Id, SkuCode = sku1Code, Quantity = 1, UnitPrice = retailPrice1 }
            }
        };

        // Chạy đồng thời 2 giao dịch có thứ tự mảng SKU nghịch đảo qua 2 URL khác nhau
        var task1 = _client.PostAsJsonAsync("/api/pos/checkout", req1);
        var task2 = _client.PostAsJsonAsync("/api/orders/checkout", req2);

        await Task.WhenAll(task1, task2);

        var res1 = await task1;
        var res2 = await task2;

        res1.StatusCode.Should().Be(HttpStatusCode.OK, "Giỏ 1 phải thành công không bị deadlock.");
        res2.StatusCode.Should().Be(HttpStatusCode.OK, "Giỏ 2 phải thành công không bị deadlock.");
    }

    [Fact]
    public async Task AC05_PosVsOnlineReservation_ShouldProtectReservedStockAndPreventOverselling()
    {
        await SeedDatabaseAsync();

        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);

        Guid branchId;
        Guid warehouseId;
        Guid supplierId;
        Guid userId;
        Guid skuId;
        string skuCode;
        decimal retailPrice;

        await using (var ctx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await ctx.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await ctx.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            branchId = branch.Id;
            warehouseId = warehouse.Id;
            supplierId = supplier.Id;
            userId = user.Id;
            skuId = sku.Id;
            skuCode = sku.SKU;
            retailPrice = sku.RetailPrice;
        }

        // 1. Nhập kho 10 sản phẩm
        await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branchId, warehouseId, supplierId, userId, skuId, 10m, 100000m);

        // 2. Kênh bán Online gọi ReservationService (Sprint 2) giữ chỗ 7 sản phẩm
        var (resCtx, resTenant) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
        await using (resCtx)
        {
            var resService = new ReservationService(resCtx, resTenant);
            var reserveResult = await resService.ReserveStockAsync(new ReserveStockRequestDto
            {
                BranchId = branchId,
                WarehouseId = warehouseId,
                SkuId = skuId,
                Quantity = 7m,
                DurationMinutes = 15
            }, userId);

            reserveResult.Status.Should().Be("Active");
            reserveResult.RemainingAvailableStock.Should().Be(3m); // 10 - 7 = 3 khả dụng
        }

        // 3. Khách mua tại quầy POS đòi mua 5 sản phẩm (5 > 3 khả dụng, dù OnHand = 10) -> Phải bị từ chối 409 INSUFFICIENT_STOCK
        var oversellReq = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPrice * 5,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 5, UnitPrice = retailPrice }
            }
        };

        var oversellRes = await _client.PostAsJsonAsync("/api/pos/checkout", oversellReq);
        oversellRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var oversellErr = await oversellRes.Content.ReadAsStringAsync();
        oversellErr.Should().Contain("INSUFFICIENT_STOCK");

        // 4. Khách POS đổi ý chỉ mua 3 sản phẩm (3 <= 3 khả dụng) -> Thành công
        var validReq = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPrice * 3,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 3, UnitPrice = retailPrice }
            }
        };

        var validRes = await _client.PostAsJsonAsync("/api/pos/checkout", validReq);
        validRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Kiểm tra CSDL: OnHand = 7, Reserved = 7, Available = 0. Giữ chỗ online 7 sản phẩm được bảo toàn tuyệt đối!
        await using (var verifyCtx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var bal = await verifyCtx.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bal.OnHand.Should().Be(7m);
            bal.Reserved.Should().Be(7m);
            (bal.OnHand - bal.Reserved).Should().Be(0m);
        }

        // 6. Request tiếp theo của POS dù chỉ mua 1 sản phẩm cũng bị từ chối vì Available = 0
        var rejectReq = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPrice,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1, UnitPrice = retailPrice }
            }
        };
        var rejectRes = await _client.PostAsJsonAsync("/api/pos/checkout", rejectReq);
        rejectRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AC05_ConcurrentPosVsOnlineReservation_ShouldPreventOversellAndMaintainConsistency()
    {
        await SeedDatabaseAsync();

        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);

        Guid branchId;
        Guid warehouseId;
        Guid supplierId;
        Guid userId;
        Guid skuId;
        string skuCode;
        decimal retailPrice;

        await using (var ctx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await ctx.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await ctx.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            branchId = branch.Id;
            warehouseId = warehouse.Id;
            supplierId = supplier.Id;
            userId = user.Id;
            skuId = sku.Id;
            skuCode = sku.SKU;
            retailPrice = sku.RetailPrice;
        }

        // Tồn kho ban đầu = 5 đơn vị
        await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branchId, warehouseId, supplierId, userId, skuId, 5m, 100000m);

        // POS checkout muốn mua 4 đơn vị
        var posReq = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPrice * 4,
            RequestKey = Guid.NewGuid(),
            CustomerName = "POS Buyer Concurrent",
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 4m, UnitPrice = retailPrice }
            }
        };

        // ReservationService muốn giữ chỗ 3 đơn vị (Tổng nhu cầu: 4 + 3 = 7 > 5 tồn kho khả dụng)
        var reserveReq = new ReserveStockRequestDto
        {
            BranchId = branchId,
            WarehouseId = warehouseId,
            SkuId = skuId,
            Quantity = 3m,
            DurationMinutes = 15,
            RequestKey = Guid.NewGuid()
        };

        var startBarrier = new ManualResetEventSlim(false);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));

        bool posSucceeded = false;
        bool posInsufficientStock = false;
        bool reserveSucceeded = false;
        bool reserveInsufficientStock = false;

        // Task 1: POS Checkout qua HTTP API
        var posTask = Task.Run(async () =>
        {
            startBarrier.Wait(cts.Token);
            var res = await _client.PostAsJsonAsync("/api/pos/checkout", posReq, cts.Token);
            if (res.StatusCode == HttpStatusCode.OK)
            {
                posSucceeded = true;
            }
            else if (res.StatusCode == HttpStatusCode.Conflict)
            {
                var body = await res.Content.ReadAsStringAsync(cts.Token);
                if (body.Contains("INSUFFICIENT_STOCK"))
                {
                    posInsufficientStock = true;
                }
            }
            else
            {
                var body = await res.Content.ReadAsStringAsync(cts.Token);
                throw new Exception($"POS Checkout trả mã trạng thái không mong đợi: {res.StatusCode}, body: {body}");
            }
        }, cts.Token);

        // Task 2: ReservationService trên scope / DbContext riêng biệt
        var reserveTask = Task.Run(async () =>
        {
            var (resCtx, resTenant) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
            await using (resCtx)
            {
                var reservationService = new ReservationService(resCtx, resTenant);
                startBarrier.Wait(cts.Token);
                try
                {
                    var result = await reservationService.ReserveStockAsync(reserveReq, userId, cts.Token);
                    if (result.Status == "Active")
                    {
                        reserveSucceeded = true;
                    }
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("không đủ"))
                {
                    reserveInsufficientStock = true;
                }
            }
        }, cts.Token);

        // Mở cổng đồng bộ để 2 tác vụ khởi chạy đồng thời
        startBarrier.Set();

        await Task.WhenAll(posTask, reserveTask);

        // Kiểm chứng kết quả cạnh tranh: Đúng 1 bên thành công, bên còn lại bị từ chối có kiểm soát
        (posSucceeded ^ reserveSucceeded).Should().BeTrue("Đúng một bên (POS hoặc Reservation) phải thành công, không thể cả hai cùng thành công vì tổng nhu cầu 7 > 5.");
        (posInsufficientStock ^ reserveInsufficientStock).Should().BeTrue("Bên thất bại phải nhận lỗi thiếu hàng có kiểm soát (INSUFFICIENT_STOCK hoặc InvalidOperationException).");

        // Kiểm chứng tính nhất quán CSDL: Không oversell, không âm, tồn 2 cấp và ledger nhất quán
        await using (var verifyCtx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var bal = await verifyCtx.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            var whBal = await verifyCtx.WarehouseInventoryBalances.FirstAsync(b => b.WarehouseId == warehouseId && b.SkuId == skuId);

            bal.OnHand.Should().BeGreaterThanOrEqualTo(0m);
            bal.Reserved.Should().BeGreaterThanOrEqualTo(0m);
            (bal.OnHand - bal.Reserved).Should().BeGreaterThanOrEqualTo(0m, "Tồn khả dụng tuyệt đối không được âm.");

            // Hai cấp tồn kho phải khớp nhau
            whBal.OnHand.Should().Be(bal.OnHand);
            whBal.Reserved.Should().Be(bal.Reserved);

            var activeReservations = await verifyCtx.Reservations.Where(r => r.TenantId == OismsDatabaseSeeder.TenantAId && r.SkuId == skuId && r.Status == "Active").ToListAsync();
            activeReservations.Sum(r => r.Quantity).Should().Be(bal.Reserved, "Tổng số lượng reservation Active phải khớp chính xác với bal.Reserved.");

            if (posSucceeded)
            {
                // POS mua 4 đơn vị -> OnHand = 1, Reserved = 0
                bal.OnHand.Should().Be(1m);
                bal.Reserved.Should().Be(0m);
                activeReservations.Should().BeEmpty();

                var ledgerOut = await verifyCtx.InventoryTransactions.Where(t => t.Type == "OUT" && t.SkuId == skuId).ToListAsync();
                ledgerOut.Count.Should().Be(1);
                ledgerOut[0].Quantity.Should().Be(4m);
                ledgerOut[0].BalanceAfter.Should().Be(1m);
            }
            else
            {
                // Reservation giữ chỗ 3 đơn vị -> OnHand = 5, Reserved = 3, Available = 2
                bal.OnHand.Should().Be(5m);
                bal.Reserved.Should().Be(3m);
                activeReservations.Count.Should().Be(1);
                activeReservations[0].Quantity.Should().Be(3m);

                var ledgerOut = await verifyCtx.InventoryTransactions.Where(t => t.Type == "OUT" && t.SkuId == skuId).ToListAsync();
                ledgerOut.Should().BeEmpty("POS không thành công nên không được ghi ledger OUT.");
            }
        }
    }

    [Fact]
    public async Task AC06_SplitWarehouseAllocation_ShouldUseActiveWarehousesSequentially_AndIgnoreInactiveAndCrossTenant()
    {
        await SeedDatabaseAsync();

        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);

        Guid branchId;
        Guid wh1Id;
        Guid wh2Id;
        Guid wh3InactiveId;
        Guid supplierId;
        Guid userId;
        Guid skuId;
        string skuCode;
        decimal retailPrice;

        // Tạo thêm kho WH2 (active) và WH3 (inactive) trong cùng Branch của Tenant A
        await using (var ctx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var wh1 = await ctx.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await ctx.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            branchId = branch.Id;
            wh1Id = wh1.Id;
            supplierId = supplier.Id;
            userId = user.Id;
            skuId = sku.Id;
            skuCode = sku.SKU;
            retailPrice = sku.RetailPrice;

            wh2Id = Guid.NewGuid();
            ctx.Warehouses.Add(new Warehouse
            {
                Id = wh2Id,
                TenantId = OismsDatabaseSeeder.TenantAId,
                BranchId = branchId,
                Name = "Kho phụ hoạt động",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(1)
            });

            wh3InactiveId = Guid.NewGuid();
            ctx.Warehouses.Add(new Warehouse
            {
                Id = wh3InactiveId,
                TenantId = OismsDatabaseSeeder.TenantAId,
                BranchId = branchId,
                Name = "Kho tạm dừng hoạt động",
                IsActive = false, // INACTIVE!
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(2)
            });

            await ctx.SaveChangesAsync();
        }

        // Nhập kho:
        // WH 1 (active): 4 sản phẩm
        // WH 2 (active): 6 sản phẩm
        // WH 3 (inactive): 10 sản phẩm
        await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branchId, wh1Id, supplierId, userId, skuId, 4m, 100000m);
        await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branchId, wh2Id, supplierId, userId, skuId, 6m, 100000m);
        await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branchId, wh3InactiveId, supplierId, userId, skuId, 10m, 100000m);

        // Nhập kho cho Tenant B: 10 sản phẩm
        Guid tenantBWhId;
        Guid tenantBSkuId;
        await using (var ctxB = CreatePostgresDbContext(OismsDatabaseSeeder.TenantBId).Context)
        {
            var branchB = await ctxB.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantBId);
            var whB = await ctxB.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantBId && w.BranchId == branchB.Id);
            var supB = await ctxB.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantBId);
            var userB = await ctxB.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantBId);
            var skuB = await ctxB.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantBId);
            tenantBWhId = whB.Id;
            tenantBSkuId = skuB.Id;

            await SeedStockAsync(OismsDatabaseSeeder.TenantBId, branchB.Id, whB.Id, supB.Id, userB.Id, skuB.Id, 10m, 100000m);
        }

        // POS mua 7 sản phẩm (WH 1 có 4, WH 2 có 6 -> phải tự phân bổ qua cả 2 kho active để đủ 7)
        var splitReq = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPrice * 7,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 7, UnitPrice = retailPrice }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/pos/checkout", splitReq);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Kiểm tra phân bổ chi tiết trong DB:
        await using (var verifyCtx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            // 1. Kho WH3 inactive hoàn toàn không bị đụng tới
            var wh3Bal = await verifyCtx.WarehouseInventoryBalances.FirstAsync(wb => wb.WarehouseId == wh3InactiveId && wb.SkuId == skuId);
            wh3Bal.OnHand.Should().Be(10m, "Kho ngừng hoạt động tuyệt đối không được phân bổ hàng.");

            // 2. Hai kho hoạt động WH1 và WH2 đã phân bổ tổng cộng 7 sản phẩm
            var wh1Bal = await verifyCtx.WarehouseInventoryBalances.FirstAsync(wb => wb.WarehouseId == wh1Id && wb.SkuId == skuId);
            var wh2Bal = await verifyCtx.WarehouseInventoryBalances.FirstAsync(wb => wb.WarehouseId == wh2Id && wb.SkuId == skuId);
            var totalDeductedFromActive = (4m - wh1Bal.OnHand) + (6m - wh2Bal.OnHand);
            totalDeductedFromActive.Should().Be(7m, "Tổng số lượng phân bổ qua các kho hoạt động phải đúng bằng 7.");

            // 3. Đúng 2 Reservation Consumed được tạo
            var reservations = await verifyCtx.Reservations.Where(r => r.TenantId == OismsDatabaseSeeder.TenantAId && r.SkuId == skuId).ToListAsync();
            reservations.Should().HaveCount(2);
            reservations.All(r => r.Status == "Consumed").Should().BeTrue();
            reservations.Sum(r => r.Quantity).Should().Be(7m);

            // 4. Đúng 2 bút toán Sổ cái OUT được ghi nhận với SequenceNo tăng dần và BalanceAfter hợp lệ
            var ledgers = await verifyCtx.InventoryTransactions.Where(t => t.TenantId == OismsDatabaseSeeder.TenantAId && t.Type == "OUT").OrderBy(t => t.SequenceNo).ToListAsync();
            ledgers.Should().HaveCount(2);
            ledgers[1].SequenceNo.Should().BeGreaterThan(ledgers[0].SequenceNo);
            ledgers.Sum(t => t.Quantity).Should().Be(7m);
        }

        // 5. Kiểm tra kho của Tenant B không bị ảnh hưởng
        await using (var verifyCtxB = CreatePostgresDbContext(OismsDatabaseSeeder.TenantBId).Context)
        {
            var tenantBBal = await verifyCtxB.WarehouseInventoryBalances.FirstAsync(wb => wb.WarehouseId == tenantBWhId && wb.SkuId == tenantBSkuId);
            tenantBBal.OnHand.Should().Be(10m, "Kho của Tenant B hoàn toàn không bị ảnh hưởng.");
        }
    }

    // =========================================================================
    // AC-06: WAC Snapshot Immutability & Zero Sensitive Data Leakage
    // =========================================================================
    [Fact]
    public async Task AC06_WacSnapshotImmutability_And_ZeroSensitiveDataLeakage()
    {
        await SeedDatabaseAsync();

        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);

        Guid branchId;
        Guid warehouseId;
        Guid supplierId;
        Guid userId;
        Guid skuId;
        string skuCode;
        decimal retailPrice;
        decimal initialCost = 100000m;

        await using (var ctx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await ctx.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await ctx.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            branchId = branch.Id;
            warehouseId = warehouse.Id;
            supplierId = supplier.Id;
            userId = user.Id;
            skuId = sku.Id;
            skuCode = sku.SKU;
            retailPrice = sku.RetailPrice;
        }

        // 1. Nhập kho lần 1: 10 sản phẩm @ 100.000đ -> WAC ban đầu = 100.000đ
        await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branchId, warehouseId, supplierId, userId, skuId, 10m, initialCost);

        // 2. Bán 2 sản phẩm tại quầy POS
        var checkoutReq = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPrice * 2,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2, UnitPrice = retailPrice }
            }
        };

        var checkoutRes = await _client.PostAsJsonAsync("/api/pos/checkout", checkoutReq);
        checkoutRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Kiểm tra Zero Sensitive Data Leakage: Response JSON tuyệt đối KHÔNG chứa CostPrice, TotalCost, TotalCogs
        var responseJson = await checkoutRes.Content.ReadAsStringAsync();
        responseJson.Should().NotContain("costPrice", "Response POS không được chứa trường nhạy cảm CostPrice");
        responseJson.Should().NotContain("totalCost", "Response POS không được chứa trường nhạy cảm TotalCost");
        responseJson.Should().NotContain("totalCogs", "Response POS không được chứa trường nhạy cảm TotalCogs");

        var checkoutResult = await checkoutRes.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);
        var orderId = checkoutResult!.OrderId;

        // Kiểm tra Replay response cũng tuyệt đối không để lộ giá vốn:
        var replayRes = await _client.PostAsJsonAsync("/api/pos/checkout", checkoutReq);
        replayRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayJson = await replayRes.Content.ReadAsStringAsync();
        replayJson.Should().NotContain("costPrice", "Replay response không được chứa trường nhạy cảm CostPrice");
        replayJson.Should().NotContain("totalCost", "Replay response không được chứa trường nhạy cảm TotalCost");
        replayJson.Should().NotContain("totalCogs", "Replay response không được chứa trường nhạy cảm TotalCogs");

        // 4. Nhập kho lần 2 với giá cao hơn: 10 sản phẩm @ 200.000đ -> WAC kho mới tăng lên
        await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branchId, warehouseId, supplierId, userId, skuId, 10m, 200000m);

        // 5. Kiểm tra tính BẤT BIẾN của WAC Snapshot: OrderItem của đơn đã bán trước đó vẫn giữ nguyên 100.000đ
        await using (var verifyCtx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var orderItem = await verifyCtx.OrderItems.FirstAsync(i => i.OrderId == orderId && i.SkuId == skuId);
            orderItem.CostPrice.Should().Be(initialCost, "CostPrice snapshot của đơn hàng đã bán không được thay đổi khi có đợt nhập hàng mới!");

            // Kiểm tra WAC hiện tại trong kho đã tăng lên
            var currentBal = await verifyCtx.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            // Còn 8 cái @ 100k (800k) + 10 cái @ 200k (2000k) = 2800k / 18 = 155,555.555556
            currentBal.AverageCost.Should().BeGreaterThan(initialCost);
        }
    }

    // =========================================================================
    // AC-07: RBAC, Security & Validation Checks
    // =========================================================================
    [Fact]
    public async Task AC07_SecurityRbac_And_Validation()
    {
        await SeedDatabaseAsync();

        Guid branchId;
        Guid warehouseId;
        Guid supplierId;
        Guid userId;
        Guid skuId;
        string skuCode;
        decimal retailPrice;
        Guid branchBId;
        Guid skuBId;

        await using (var ctx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await ctx.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await ctx.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            branchId = branch.Id;
            warehouseId = warehouse.Id;
            supplierId = supplier.Id;
            userId = user.Id;
            skuId = sku.Id;
            skuCode = sku.SKU;
            retailPrice = sku.RetailPrice;
        }

        // Lấy thông tin Branch và SKU của Tenant B
        await using (var ctxB = CreatePostgresDbContext(OismsDatabaseSeeder.TenantBId).Context)
        {
            var branchB = await ctxB.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantBId);
            var skuB = await ctxB.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantBId);
            branchBId = branchB.Id;
            skuBId = skuB.Id;
        }

        await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branchId, warehouseId, supplierId, userId, skuId, 20m, 100000m);

        // 1. Unauthenticated (Thiếu Bearer Token) -> 401 Unauthorized
        _client.DefaultRequestHeaders.Authorization = null;
        var unauthReq = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPrice,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1, UnitPrice = retailPrice } }
        };
        var unauthRes = await _client.PostAsJsonAsync("/api/pos/checkout", unauthReq);
        unauthRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 2. 403 Forbidden: Bearer Token có vai trò không thuộc Owner, Staff, Cashier (ví dụ Auditor/Guest)
        using (var scope = _factory.Services.CreateScope())
        {
            var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
            var nonAuthorizedUser = new User
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                Email = "auditor@shopa.local",
                DisplayName = "Kiểm toán viên",
                Role = "Staff"
            };
            var nonAuthorizedToken = jwtService.GenerateAccessToken(nonAuthorizedUser, new[] { "Auditor" });
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", nonAuthorizedToken);
        }
        var forbiddenRes = await _client.PostAsJsonAsync("/api/pos/checkout", unauthReq);
        forbiddenRes.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Vai trò Auditor không có quyền truy cập endpoint checkout POS.");

        // Đăng nhập tài khoản Cashier hợp lệ cho các test tiếp theo
        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);

        // 3. F-03 & AC-01: Kiểm tra lỗi xác thực HTTP trả về RFC 7807 với errorCode = "VALIDATION_ERROR" trên cả 2 tuyến đường

        // a) RequestKey rỗng (Guid.Empty) qua canonical /api/pos/checkout
        var emptyKeyReq = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPrice,
            RequestKey = Guid.Empty, // Invalid!
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1, UnitPrice = retailPrice } }
        };
        var emptyKeyResPos = await _client.PostAsJsonAsync("/api/pos/checkout", emptyKeyReq);
        emptyKeyResPos.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var emptyKeyBodyPos = await emptyKeyResPos.Content.ReadAsStringAsync();
        emptyKeyBodyPos.Should().Contain("VALIDATION_ERROR");

        // b) RequestKey rỗng (Guid.Empty) qua alias /api/orders/checkout
        var emptyKeyResOrd = await _client.PostAsJsonAsync("/api/orders/checkout", emptyKeyReq);
        emptyKeyResOrd.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var emptyKeyBodyOrd = await emptyKeyResOrd.Content.ReadAsStringAsync();
        emptyKeyBodyOrd.Should().Contain("VALIDATION_ERROR");

        // c) Giỏ hàng rỗng (Items count = 0) trên cả 2 URL
        var emptyCartReq = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPrice,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>()
        };
        var emptyCartResPos = await _client.PostAsJsonAsync("/api/pos/checkout", emptyCartReq);
        emptyCartResPos.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await emptyCartResPos.Content.ReadAsStringAsync()).Should().Contain("VALIDATION_ERROR");

        var emptyCartResOrd = await _client.PostAsJsonAsync("/api/orders/checkout", emptyCartReq);
        emptyCartResOrd.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await emptyCartResOrd.Content.ReadAsStringAsync()).Should().Contain("VALIDATION_ERROR");

        // d) Số lượng âm (Quantity <= 0) trên cả 2 URL
        var negQtyReq = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPrice,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = -1, UnitPrice = retailPrice } }
        };
        var negQtyResPos = await _client.PostAsJsonAsync("/api/pos/checkout", negQtyReq);
        negQtyResPos.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await negQtyResPos.Content.ReadAsStringAsync()).Should().Contain("VALIDATION_ERROR");

        var negQtyResOrd = await _client.PostAsJsonAsync("/api/orders/checkout", negQtyReq);
        negQtyResOrd.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await negQtyResOrd.Content.ReadAsStringAsync()).Should().Contain("VALIDATION_ERROR");

        // e) Mismatched header Idempotency-Key và DTO RequestKey
        var headerMismatchMsg = new HttpRequestMessage(HttpMethod.Post, "/api/pos/checkout")
        {
            Content = JsonContent.Create(new PosCheckoutRequestDto
            {
                BranchId = branchId,
                Channel = "POS",
                PaymentMethod = "Cash",
                AmountGiven = retailPrice,
                RequestKey = Guid.NewGuid(),
                Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1, UnitPrice = retailPrice } }
            })
        };
        headerMismatchMsg.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); // Khác với body RequestKey!
        var headerMismatchRes = await _client.SendAsync(headerMismatchMsg);
        headerMismatchRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await headerMismatchRes.Content.ReadAsStringAsync()).Should().Contain("VALIDATION_ERROR");

        // 4. F-02: Kiểm tra precision/scale (Quantity > 3 decimals, UnitPrice > 2 decimals, AmountGiven > 2 decimals)

        // a) Quantity có 4 chữ số thập phân (1.0004m) -> 400 VALIDATION_ERROR
        var badScaleQtyReq = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPrice * 2,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1.0004m, UnitPrice = retailPrice } }
        };
        var badScaleQtyRes = await _client.PostAsJsonAsync("/api/pos/checkout", badScaleQtyReq);
        badScaleQtyRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await badScaleQtyRes.Content.ReadAsStringAsync()).Should().Contain("VALIDATION_ERROR");

        // b) UnitPrice có 3 chữ số thập phân (100.555m) -> 400 VALIDATION_ERROR
        var badScalePriceReq = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPrice * 2,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1, UnitPrice = 100.555m } }
        };
        var badScalePriceRes = await _client.PostAsJsonAsync("/api/pos/checkout", badScalePriceReq);
        badScalePriceRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await badScalePriceRes.Content.ReadAsStringAsync()).Should().Contain("VALIDATION_ERROR");

        // c) AmountGiven có 3 chữ số thập phân (500.123m) -> 400 VALIDATION_ERROR
        var badScaleAmountReq = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = 500.123m,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1, UnitPrice = retailPrice } }
        };
        var badScaleAmountRes = await _client.PostAsJsonAsync("/api/pos/checkout", badScaleAmountReq);
        badScaleAmountRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await badScaleAmountRes.Content.ReadAsStringAsync()).Should().Contain("VALIDATION_ERROR");

        // d) Hợp lệ scale 3 chữ số thập phân (Quantity = 1.125m) -> Thành công 200 OK và replay sạch sẽ
        var validScaleReq = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPrice * 2,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1.125m, UnitPrice = retailPrice } }
        };
        var validScaleRes = await _client.PostAsJsonAsync("/api/pos/checkout", validScaleReq);
        validScaleRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var validScaleResult = await validScaleRes.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);
        validScaleResult.Should().NotBeNull();
        validScaleResult!.Receipt.Items[0].Quantity.Should().Be(1.125m);

        // Replay chính xác đơn hàng có số lượng 3 chữ số thập phân
        var replayScaleRes = await _client.PostAsJsonAsync("/api/pos/checkout", validScaleReq);
        replayScaleRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayScaleResult = await replayScaleRes.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);
        replayScaleResult!.OrderNumber.Should().Be(validScaleResult.OrderNumber);

        // 5. Tham chiếu Chi nhánh khác Tenant (Branch của Tenant B) -> 404 BRANCH_INACTIVE_OR_INVALID
        var crossBranchReq = new PosCheckoutRequestDto
        {
            BranchId = branchBId, // Chi nhánh của Tenant B!
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPrice,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1, UnitPrice = retailPrice } }
        };
        var crossBranchRes = await _client.PostAsJsonAsync("/api/pos/checkout", crossBranchReq);
        crossBranchRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await crossBranchRes.Content.ReadAsStringAsync()).Should().Contain("BRANCH_INACTIVE_OR_INVALID");

        // 6. Tham chiếu SKU khác Tenant (SKU của Tenant B) -> 404 SKU_NOT_FOUND
        var crossSkuReq = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPrice,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuBId, SkuCode = "SKU-B", Quantity = 1, UnitPrice = retailPrice } }
        };
        var crossSkuRes = await _client.PostAsJsonAsync("/api/pos/checkout", crossSkuReq);
        crossSkuRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await crossSkuRes.Content.ReadAsStringAsync()).Should().Contain("SKU_NOT_FOUND");

        // 7. SKU ngừng hoạt động (IsActive = false) -> 422 SKU_INACTIVE
        Guid inactiveSkuId;
        string inactiveSkuCode;
        decimal inactiveRetailPrice;
        await using (var ctx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId && v.Id == skuId);
            sku.IsActive = false; // Tạm dừng SKU
            await ctx.SaveChangesAsync();
            inactiveSkuId = sku.Id;
            inactiveSkuCode = sku.SKU;
            inactiveRetailPrice = sku.RetailPrice;
        }

        var inactiveSkuReq = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = inactiveRetailPrice,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = inactiveSkuId, SkuCode = inactiveSkuCode, Quantity = 1, UnitPrice = inactiveRetailPrice } }
        };
        var inactiveSkuRes = await _client.PostAsJsonAsync("/api/pos/checkout", inactiveSkuReq);
        inactiveSkuRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await inactiveSkuRes.Content.ReadAsStringAsync()).Should().Contain("SKU_INACTIVE");

        // Phục hồi lại SKU hoạt động cho các test tiếp
        await using (var ctx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId && v.Id == skuId);
            sku.IsActive = true;
            await ctx.SaveChangesAsync();
        }

        // 8. Tiền khách đưa không đủ (AmountGiven < TotalAmount) -> 400 Bad Request
        var shortCashReq = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = 1000m, // Bé hơn rất nhiều so với retailPrice
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1, UnitPrice = retailPrice } }
        };
        var shortCashRes = await _client.PostAsJsonAsync("/api/pos/checkout", shortCashReq);
        shortCashRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var shortCashBody = await shortCashRes.Content.ReadAsStringAsync();
        shortCashBody.Should().Contain("INSUFFICIENT_CASH_RECEIVED");

        // 9. Sai lệch đơn giá niêm yết (Client gửi UnitPrice khác Catalog RetailPrice) -> 400 Bad Request
        var priceMismatchReq = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = 999999m,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1, UnitPrice = 10000m } } // Sai giá!
        };
        var priceMismatchRes = await _client.PostAsJsonAsync("/api/pos/checkout", priceMismatchReq);
        priceMismatchRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var priceMismatchBody = await priceMismatchRes.Content.ReadAsStringAsync();
        priceMismatchBody.Should().Contain("PRICE_MISMATCH");

        // 10. Thanh toán chuyển khoản BankTransfer / BANK_TRANSFER_QR -> Thành công, AmountGiven = null, ChangeDue = null
        var bankReq = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "BANK_TRANSFER_QR",
            AmountGiven = null,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1, UnitPrice = retailPrice } }
        };
        var bankRes = await _client.PostAsJsonAsync("/api/pos/checkout", bankReq);
        bankRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var bankResult = await bankRes.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);
        bankResult.Should().NotBeNull();
        bankResult!.PaymentMethod.Should().Be("BankTransfer");
        bankResult.AmountGiven.Should().BeNull();
        bankResult.ChangeDue.Should().BeNull();
    }

    [Fact]
    public async Task AC07_NumericOverflow_And_BoundaryLimits_OnBothRoutes()
    {
        await SeedDatabaseAsync();

        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);

        Guid branchId;
        Guid warehouseId;
        Guid supplierId;
        Guid userId;
        Guid skuId;
        string skuCode;
        decimal retailPrice;

        await using (var ctx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);
            var warehouse = await ctx.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id);
            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId);
            var user = await ctx.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId);
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId);

            branchId = branch.Id;
            warehouseId = warehouse.Id;
            supplierId = supplier.Id;
            userId = user.Id;
            skuId = sku.Id;
            skuCode = sku.SKU;
            retailPrice = sku.RetailPrice;
        }

        await SeedStockAsync(OismsDatabaseSeeder.TenantAId, branchId, warehouseId, supplierId, userId, skuId, 20m, 100000m);

        // 1. AmountGiven vượt numeric(20,2) (10^18 = 1,000,000,000,000,000,000m) trên cả 2 routes -> 400 VALIDATION_ERROR
        var oversizedCashKey1 = Guid.NewGuid();
        var oversizedCashReq1 = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = 1000000000000000000m,
            RequestKey = oversizedCashKey1,
            CustomerName = "Oversized Cash 1",
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1m, UnitPrice = retailPrice } }
        };

        var overCashResPos = await _client.PostAsJsonAsync("/api/pos/checkout", oversizedCashReq1);
        overCashResPos.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var overCashBodyPos = await overCashResPos.Content.ReadAsStringAsync();
        overCashBodyPos.Should().Contain("VALIDATION_ERROR");

        var oversizedCashKey2 = Guid.NewGuid();
        var oversizedCashReq2 = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = 1000000000000000000m,
            RequestKey = oversizedCashKey2,
            CustomerName = "Oversized Cash 2",
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1m, UnitPrice = retailPrice } }
        };

        var overCashResOrd = await _client.PostAsJsonAsync("/api/orders/checkout", oversizedCashReq2);
        overCashResOrd.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var overCashBodyOrd = await overCashResOrd.Content.ReadAsStringAsync();
        overCashBodyOrd.Should().Contain("VALIDATION_ERROR");

        // Kiểm tra CSDL không bị thay đổi
        await using (var verifyCtx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var orderCount1 = await verifyCtx.Orders.CountAsync(o => o.RequestKey == oversizedCashKey1);
            var orderCount2 = await verifyCtx.Orders.CountAsync(o => o.RequestKey == oversizedCashKey2);
            orderCount1.Should().Be(0);
            orderCount2.Should().Be(0);

            var bal = await verifyCtx.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bal.OnHand.Should().Be(20m, "Tồn kho không được thay đổi khi request bị từ chối do overflow.");
        }

        // 2. Quantity vượt numeric(18,3) (10^15 = 1,000,000,000,000,000m) trên cả 2 routes -> 400 VALIDATION_ERROR
        var overQtyReq1 = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPrice,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1000000000000000m, UnitPrice = retailPrice } }
        };
        var overQtyResPos = await _client.PostAsJsonAsync("/api/pos/checkout", overQtyReq1);
        overQtyResPos.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await overQtyResPos.Content.ReadAsStringAsync()).Should().Contain("VALIDATION_ERROR");

        var overQtyReq2 = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPrice,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1000000000000000m, UnitPrice = retailPrice } }
        };
        var overQtyResOrd = await _client.PostAsJsonAsync("/api/orders/checkout", overQtyReq2);
        overQtyResOrd.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await overQtyResOrd.Content.ReadAsStringAsync()).Should().Contain("VALIDATION_ERROR");

        // 3. UnitPrice vượt numeric(20,2) (10^18 = 1,000,000,000,000,000,000m) trên cả 2 routes -> 400 VALIDATION_ERROR
        var overPriceReq1 = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = 1000000000000000000m,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1m, UnitPrice = 1000000000000000000m } }
        };
        var overPriceResPos = await _client.PostAsJsonAsync("/api/pos/checkout", overPriceReq1);
        overPriceResPos.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await overPriceResPos.Content.ReadAsStringAsync()).Should().Contain("VALIDATION_ERROR");

        var overPriceReq2 = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = 1000000000000000000m,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1m, UnitPrice = 1000000000000000000m } }
        };
        var overPriceResOrd = await _client.PostAsJsonAsync("/api/orders/checkout", overPriceReq2);
        overPriceResOrd.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await overPriceResOrd.Content.ReadAsStringAsync()).Should().Contain("VALIDATION_ERROR");

        // 4. Tổng tiền tính toán vượt quá numeric(20,2) -> 400 VALIDATION_ERROR có kiểm soát
        // Tạo fixture SKU với RetailPrice = 900,000,000,000,000,000m (hợp lệ trong numeric(20,2))
        // Khi mua 2 đơn vị: 2 * 9*10^17 = 1.8*10^18 > 999,999,999,999,999,999.99m
        Guid bigPriceSkuId = Guid.NewGuid();
        string bigPriceSkuCode = "BIG-PRICE-SKU";
        decimal bigRetailPrice = 900000000000000000m;

        await using (var seedCtx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var product = await seedCtx.Products.FirstAsync(p => p.TenantId == OismsDatabaseSeeder.TenantAId);
            var bigSku = new ProductVariant
            {
                Id = bigPriceSkuId,
                TenantId = OismsDatabaseSeeder.TenantAId,
                ProductId = product.Id,
                SKU = bigPriceSkuCode,
                RetailPrice = bigRetailPrice,
                WholesalePrice = bigRetailPrice,
                Unit = "Cái",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            seedCtx.ProductVariants.Add(bigSku);
            await seedCtx.SaveChangesAsync();
        }

        var overTotalReq = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = 999999999999999999.99m,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = bigPriceSkuId, SkuCode = bigPriceSkuCode, Quantity = 2m, UnitPrice = bigRetailPrice }
            }
        };
        var overTotalRes = await _client.PostAsJsonAsync("/api/pos/checkout", overTotalReq);
        overTotalRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var overTotalBody = await overTotalRes.Content.ReadAsStringAsync();
        overTotalBody.Should().Contain("VALIDATION_ERROR");

        // 5. Giá trị biên hợp lệ tối đa numeric(20,2) (999,999,999,999,999,999.99m) -> Thành công và replay đúng
        var boundaryKey = Guid.NewGuid();
        var maxBoundaryCash = 999999999999999999.99m;
        var boundaryReq = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = maxBoundaryCash,
            RequestKey = boundaryKey,
            CustomerName = "Boundary Customer",
            CustomerPhone = "0987654321",
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1m, UnitPrice = retailPrice }
            }
        };

        var boundaryRes = await _client.PostAsJsonAsync("/api/pos/checkout", boundaryReq);
        boundaryRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var boundaryResult = await boundaryRes.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);
        boundaryResult.Should().NotBeNull();
        boundaryResult!.AmountGiven.Should().Be(maxBoundaryCash);
        boundaryResult.ChangeDue.Should().Be(maxBoundaryCash - retailPrice);

        // Kiểm tra lưu DB đúng giá trị biên
        await using (var verifyCtx = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId).Context)
        {
            var savedPayment = await verifyCtx.Payments.FirstAsync(p => p.OrderId == boundaryResult.OrderId);
            savedPayment.CashReceived.Should().Be(maxBoundaryCash);
        }

        // Replay qua alias route
        var replayBoundaryRes = await _client.PostAsJsonAsync("/api/orders/checkout", boundaryReq);
        replayBoundaryRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayBoundaryResult = await replayBoundaryRes.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);
        replayBoundaryResult!.OrderNumber.Should().Be(boundaryResult.OrderNumber);
        replayBoundaryResult.AmountGiven.Should().Be(maxBoundaryCash);
        replayBoundaryResult.ChangeDue.Should().Be(maxBoundaryCash - retailPrice);
    }
}
