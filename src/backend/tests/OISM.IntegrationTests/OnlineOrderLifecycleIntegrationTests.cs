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
/// Kiểm chứng toàn diện chức năng Vòng đời Đơn hàng Trực tuyến ngoài POS (Task S3-BE1-T03-ONLINE-ORDER-LIFECYCLE):
/// AC-01: Tạo đơn hàng Admin + giữ chỗ tồn kho multi-SKU (POST /api/orders, 201 Created).
/// AC-02: Idempotency trên CreateOrder (replay 200 OK, từ chối payload mismatch 409, concurrent first submission).
/// AC-03: Duyệt đơn hàng (POST /api/orders/{id}/confirm: trừ OnHand, giảm Reserved, ghi Ledger OUT, chốt WAC snapshot).
/// AC-04: Hoàn tất đơn hàng (POST /api/orders/{id}/complete: 200 OK, idempotent replay, chặn chuyển trạng thái sai).
/// AC-05: Hủy đơn hàng (POST /api/orders/{id}/cancel: giải phóng Active reservations, OnHand không đổi, chặn hủy đơn sai).
/// AC-06: Concurrency & Stress: 50 đơn tranh 1 suất tồn kho, Confirm vs Cancel, Online vs POS không deadlock.
/// AC-07: Atomic Rollback & Resilience: Cart thiếu tồn rollback toàn bộ, Phiếu giữ chỗ hết hạn chặn Confirm nhưng cho phép Cancel.
/// AC-08: Phân quyền RBAC (401 unauthenticated, 403 Cashier forbidden) & Giới hạn biên số học / scale.
/// </summary>
public class OnlineOrderLifecycleIntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public OnlineOrderLifecycleIntegrationTests(WebApplicationFactoryWithPostgres factory)
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

    public class MidstreamOrderFaultException : Exception
    {
        public MidstreamOrderFaultException(string message) : base(message) { }
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
                throw new MidstreamOrderFaultException("Mô phỏng đứt kết nối mạng/crash giữa chừng sau khi đã ghi dữ liệu vào CSDL nhưng trước COMMIT.");
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
    // AC-01: Tạo đơn hàng Admin + giữ chỗ tồn kho multi-SKU (POST /api/orders)
    // =========================================================================
    [Fact]
    public async Task AC01_CreateOrder_MultiSku_ShouldReserveStock_AndReturn201Created()
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId;
        Guid warehouseId;
        Guid supplierId;
        Guid userId;
        Guid sku1Id, sku2Id;
        string sku1Code, sku2Code = "POLO-NAM-TRANG-L";
        decimal retailPrice1, retailPrice2 = 350000m;

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            var warehouse = await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId);
            warehouseId = warehouse.Id;
            var supplier = await db.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            supplierId = supplier.Id;
            var user = await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local");
            userId = user.Id;

            var sku1 = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            sku1Id = sku1.Id;
            sku1Code = sku1.SKU;
            retailPrice1 = sku1.RetailPrice;

            sku2Id = Guid.NewGuid();
            var sku2 = new ProductVariant
            {
                Id = sku2Id,
                TenantId = tenantId,
                ProductId = sku1.ProductId,
                Code = sku2Code,
                SKU = sku2Code,
                RetailPrice = retailPrice2,
                WholesalePrice = 250000m,
                Unit = "piece",
                IsActive = true
            };
            db.ProductVariants.Add(sku2);
            await db.SaveChangesAsync();
        }

        // Nhập kho 10 đơn vị cho mỗi SKU
        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, sku1Id, 10, 100000);
        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, sku2Id, 10, 150000);

        var requestKey = Guid.NewGuid();
        var createRequest = new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            CustomerName = "Nguyễn Văn Admin",
            CustomerPhone = "0901234567",
            ShippingAddress = "123 Đường Lê Lợi, Q1, TP.HCM",
            RequestKey = requestKey,
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = sku1Id, SkuCode = sku1Code, Quantity = 2, UnitPrice = retailPrice1 },
                new() { SkuId = sku2Id, SkuCode = sku2Code, Quantity = 3, UnitPrice = retailPrice2 }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/orders", createRequest);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, $"Tạo đơn thất bại: {body}");

        var orderDto = await response.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
        orderDto.Should().NotBeNull();
        orderDto!.Status.Should().Be("Reserved");
        orderDto.TotalAmount.Should().Be(2 * retailPrice1 + 3 * retailPrice2);
        orderDto.TotalCogs.Should().BeNull("Đơn Reserved chưa chốt WAC nên TotalCogs phải là null");
        orderDto.ConfirmedAt.Should().BeNull();
        orderDto.CompletedAt.Should().BeNull();
        orderDto.CancelledAt.Should().BeNull();
        orderDto.Items.Should().HaveCount(2);
        orderDto.Items.All(i => i.CostPrice == null).Should().BeTrue();
        orderDto.Payment.Should().BeNull();

        // Kiểm tra CSDL
        var (verifyDb, _) = CreatePostgresDbContext(tenantId);
        await using (verifyDb)
        {
            // Tồn kho: OnHand không đổi, Reserved tăng
            var bBal1 = await verifyDb.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == sku1Id);
            bBal1.OnHand.Should().Be(10);
            bBal1.Reserved.Should().Be(2);

            var bBal2 = await verifyDb.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == sku2Id);
            bBal2.OnHand.Should().Be(10);
            bBal2.Reserved.Should().Be(3);

            var whBal1 = await verifyDb.WarehouseInventoryBalances.FirstAsync(wb => wb.WarehouseId == warehouseId && wb.SkuId == sku1Id);
            whBal1.OnHand.Should().Be(10);
            whBal1.Reserved.Should().Be(2);

            var whBal2 = await verifyDb.WarehouseInventoryBalances.FirstAsync(wb => wb.WarehouseId == warehouseId && wb.SkuId == sku2Id);
            whBal2.OnHand.Should().Be(10);
            whBal2.Reserved.Should().Be(3);

            // Phiếu giữ chỗ Active
            var reservations = await verifyDb.Reservations.Where(r => r.OrderId == orderDto.Id).ToListAsync();
            reservations.Should().HaveCount(2);
            reservations.All(r => r.Status == "Active").Should().BeTrue();
            reservations.All(r => r.ExpiresAt > DateTimeOffset.UtcNow).Should().BeTrue();

            // Sổ cái không ghi bút toán OUT
            var ledgerOut = await verifyDb.InventoryTransactions.Where(t => t.Type == "OUT").ToListAsync();
            ledgerOut.Should().BeEmpty();
        }
    }

    // =========================================================================
    // AC-02: Idempotency trên CreateOrder (Replay 200 OK & Reject Mismatch 409)
    // =========================================================================
    [Fact]
    public async Task AC02_CreateOrder_Idempotency_SamePayloadReturns200_DifferentPayloadReturns409()
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
        string skuCode;
        decimal retailPrice;

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            warehouseId = (await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId)).Id;
            supplierId = (await db.Suppliers.FirstAsync(s => s.TenantId == tenantId)).Id;
            userId = (await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local")).Id;

            var variant = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = variant.Id;
            skuCode = variant.SKU;
            retailPrice = variant.RetailPrice;
        }

        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 10, 100000);

        var requestKey = Guid.NewGuid();
        var originalRequest = new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            CustomerName = "Khách Hàng Idempotency",
            CustomerPhone = "0988776655",
            ShippingAddress = "Quận 1, TP.HCM",
            RequestKey = requestKey,
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2, UnitPrice = retailPrice }
            }
        };

        // 1. Gửi lần 1 -> 201 Created
        var res1 = await _client.PostAsJsonAsync("/api/orders", originalRequest);
        res1.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdOrder = await res1.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
        createdOrder.Should().NotBeNull();

        // 2. Gửi lần 2 cùng key, cùng payload -> 200 OK và trả về cùng thông tin
        var res2 = await _client.PostAsJsonAsync("/api/orders", originalRequest);
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayedOrder = await res2.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
        replayedOrder.Should().NotBeNull();
        replayedOrder!.Id.Should().Be(createdOrder!.Id);
        replayedOrder.Number.Should().Be(createdOrder.Number);

        // 3. Gửi lần 3 cùng key nhưng đổi CustomerName -> 409 IDEMPOTENCY_PAYLOAD_MISMATCH
        var mismatchedNameRequest = new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            CustomerName = "Tên Khách Bị Thay Đổi",
            CustomerPhone = "0988776655",
            ShippingAddress = "Quận 1, TP.HCM",
            RequestKey = requestKey,
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2, UnitPrice = retailPrice }
            }
        };
        var resMismatchName = await _client.PostAsJsonAsync("/api/orders", mismatchedNameRequest);
        resMismatchName.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var bodyMismatch = await resMismatchName.Content.ReadAsStringAsync();
        bodyMismatch.Should().Contain("IDEMPOTENCY_PAYLOAD_MISMATCH");

        // 4. Gửi lần 4 cùng key nhưng đổi số lượng Quantity -> 409 IDEMPOTENCY_PAYLOAD_MISMATCH
        var mismatchedQtyRequest = new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            CustomerName = "Khách Hàng Idempotency",
            CustomerPhone = "0988776655",
            ShippingAddress = "Quận 1, TP.HCM",
            RequestKey = requestKey,
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 5, UnitPrice = retailPrice }
            }
        };
        var resMismatchQty = await _client.PostAsJsonAsync("/api/orders", mismatchedQtyRequest);
        resMismatchQty.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Kiểm tra tồn giữ chỗ không bị cộng dồn
        var (verifyDb, _) = CreatePostgresDbContext(tenantId);
        await using (verifyDb)
        {
            var bBal = await verifyDb.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bBal.Reserved.Should().Be(2, "Tồn giữ chỗ phải giữ nguyên là 2 đơn vị");
        }
    }

    [Fact]
    public async Task AC02_CreateOrder_ConcurrentFirstSubmission_ShouldCreateExactlyOneOrder()
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
        string skuCode;
        decimal retailPrice;

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            warehouseId = (await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId)).Id;
            supplierId = (await db.Suppliers.FirstAsync(s => s.TenantId == tenantId)).Id;
            userId = (await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local")).Id;

            var variant = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = variant.Id;
            skuCode = variant.SKU;
            retailPrice = variant.RetailPrice;
        }

        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 20, 100000);

        var sharedKey = Guid.NewGuid();
        var request = new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            CustomerName = "Khách Hàng Đua Concurrent",
            CustomerPhone = "0911223344",
            RequestKey = sharedKey,
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2, UnitPrice = retailPrice }
            }
        };

        const int concurrency = 10;
        var tasks = Enumerable.Range(0, concurrency).Select(async _ =>
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
            return await client.PostAsJsonAsync("/api/orders", request);
        }).ToList();

        var responses = await Task.WhenAll(tasks);

        // Tất cả request phải thành công (1 request 201 Created, các request còn lại 200 OK replay)
        responses.All(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.OK).Should().BeTrue();
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);

        var orderIds = new List<Guid>();
        foreach (var r in responses)
        {
            var dto = await r.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
            orderIds.Add(dto!.Id);
        }

        orderIds.Distinct().Should().HaveCount(1, "Toàn bộ các request gửi đồng thời phải trả về cùng một mã đơn hàng duy nhất");

        // Kiểm tra tồn kho chỉ bị trừ giữ chỗ 1 lần duy nhất (2 đơn vị)
        var (verifyDb, _) = CreatePostgresDbContext(tenantId);
        await using (verifyDb)
        {
            var orders = await verifyDb.Orders.Where(o => o.RequestKey == sharedKey).ToListAsync();
            orders.Should().HaveCount(1);

            var bBal = await verifyDb.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bBal.Reserved.Should().Be(2);
        }
    }

    // =========================================================================
    // AC-03: Duyệt đơn hàng (POST /api/orders/{id}/confirm)
    // =========================================================================
    [Fact]
    public async Task AC03_ConfirmOrder_ShouldDeductStock_ConsumeReservation_AndSnapshotWac()
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
        string skuCode;
        decimal retailPrice;

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            warehouseId = (await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId)).Id;
            supplierId = (await db.Suppliers.FirstAsync(s => s.TenantId == tenantId)).Id;
            userId = (await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local")).Id;

            var variant = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = variant.Id;
            skuCode = variant.SKU;
            retailPrice = variant.RetailPrice;
        }

        // Nhập kho 10 chiếc với giá 100,000đ -> WAC = 100,000đ
        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 10, 100000);

        // Tạo đơn online (Reserved)
        var createRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2, UnitPrice = retailPrice }
            }
        });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdOrder = await createRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);

        // Duyệt đơn hàng
        var confirmRes = await _client.PostAsJsonAsync($"/api/orders/{createdOrder!.Id}/confirm", new ConfirmOrderRequestDto
        {
            Note = "Duyệt đơn online đóng gói xuất kho"
        });
        var confirmBody = await confirmRes.Content.ReadAsStringAsync();
        confirmRes.StatusCode.Should().Be(HttpStatusCode.OK, $"Duyệt đơn thất bại: {confirmBody}");

        var confirmedOrder = await confirmRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
        confirmedOrder.Should().NotBeNull();
        confirmedOrder!.Status.Should().Be("Confirmed");
        confirmedOrder.ConfirmedAt.Should().NotBeNull();
        confirmedOrder.Items.First().CostPrice.Should().Be(100000);
        confirmedOrder.TotalCogs.Should().Be(2 * 100000);

        // Kiểm tra CSDL
        var (verifyDb, _) = CreatePostgresDbContext(tenantId);
        await using (verifyDb)
        {
            // Tồn kho: OnHand giảm 2 (10 -> 8), Reserved giảm 2 (2 -> 0)
            var bBal = await verifyDb.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bBal.OnHand.Should().Be(8);
            bBal.Reserved.Should().Be(0);

            var whBal = await verifyDb.WarehouseInventoryBalances.FirstAsync(wb => wb.WarehouseId == warehouseId && wb.SkuId == skuId);
            whBal.OnHand.Should().Be(8);
            whBal.Reserved.Should().Be(0);

            // Phiếu giữ chỗ chuyển sang Consumed
            var reservations = await verifyDb.Reservations.Where(r => r.OrderId == createdOrder.Id).ToListAsync();
            reservations.Should().HaveCount(1);
            reservations[0].Status.Should().Be("Consumed");

            // Sổ cái ghi bút toán OUT đúng WAC
            var ledgerOut = await verifyDb.InventoryTransactions.FirstAsync(t => t.Type == "OUT" && t.SkuId == skuId);
            ledgerOut.Quantity.Should().Be(2);
            ledgerOut.UnitCost.Should().Be(100000);
            ledgerOut.BalanceAfter.Should().Be(8);
        }
    }

    [Fact]
    public async Task AC03_ConfirmOrder_WACReflectsInterimGoodsReceipt_SellingPriceUnchanged()
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
        string skuCode;
        decimal retailPrice;

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            warehouseId = (await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId)).Id;
            supplierId = (await db.Suppliers.FirstAsync(s => s.TenantId == tenantId)).Id;
            userId = (await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local")).Id;

            var variant = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = variant.Id;
            skuCode = variant.SKU;
            retailPrice = variant.RetailPrice;
        }

        // Lô 1: Nhập 10 chiếc @ 100,000đ -> WAC = 100,000đ
        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 10, 100000);

        // Tạo đơn online 2 chiếc
        var createRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2, UnitPrice = retailPrice }
            }
        });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdOrder = await createRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);

        // Giữa chừng: Nhập thêm Lô 2: 10 chiếc @ 200,000đ
        // WAC mới = (10 * 100,000 + 10 * 200,000) / 20 = 150,000đ
        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 10, 200000);

        // Duyệt đơn hàng
        var confirmRes = await _client.PostAsJsonAsync($"/api/orders/{createdOrder!.Id}/confirm", new ConfirmOrderRequestDto());
        confirmRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmedOrder = await confirmRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);

        // Giá bán niêm yết lúc đặt không đổi
        confirmedOrder!.Items.First().UnitPrice.Should().Be(retailPrice);

        // Giá vốn snapshot tại thời điểm Confirm phải phản ánh WAC mới (150,000đ)
        confirmedOrder.Items.First().CostPrice.Should().Be(150000);
        confirmedOrder.TotalCogs.Should().Be(2 * 150000);
    }

    // =========================================================================
    // AC-04: Hoàn tất đơn hàng (POST /api/orders/{id}/complete)
    // =========================================================================
    [Fact]
    public async Task AC04_CompleteOrder_FromConfirmed_ShouldSucceed_AndReplayIdempotently()
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
        string skuCode;
        decimal retailPrice;

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            warehouseId = (await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId)).Id;
            supplierId = (await db.Suppliers.FirstAsync(s => s.TenantId == tenantId)).Id;
            userId = (await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local")).Id;

            var variant = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = variant.Id;
            skuCode = variant.SKU;
            retailPrice = variant.RetailPrice;
        }

        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 10, 100000);

        // Tạo đơn & duyệt đơn
        var createRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2, UnitPrice = retailPrice } }
        });
        var order = await createRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
        await _client.PostAsJsonAsync($"/api/orders/{order!.Id}/confirm", new ConfirmOrderRequestDto());

        // Gửi hoàn tất đơn hàng lần 1
        var completeRes1 = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/complete", new CompleteOrderRequestDto());
        completeRes1.StatusCode.Should().Be(HttpStatusCode.OK);
        var completedOrder1 = await completeRes1.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
        completedOrder1!.Status.Should().Be("Completed");
        completedOrder1.CompletedAt.Should().NotBeNull();

        // Gửi hoàn tất lặp lại lần 2 (Idempotent replay)
        var completeRes2 = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/complete", new CompleteOrderRequestDto());
        completeRes2.StatusCode.Should().Be(HttpStatusCode.OK);
        var completedOrder2 = await completeRes2.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
        completedOrder2!.Status.Should().Be("Completed");
        completedOrder2.CompletedAt.Should().BeCloseTo(completedOrder1.CompletedAt!.Value, TimeSpan.FromMilliseconds(50), "CompletedAt phải được giữ nguyên khi replay");

        // Kiểm tra tồn kho không bị trừ thêm lần nữa
        var (verifyDb, _) = CreatePostgresDbContext(tenantId);
        await using (verifyDb)
        {
            var bBal = await verifyDb.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bBal.OnHand.Should().Be(8, "OnHand chỉ trừ đúng 1 lần khi Confirm");

            var ledgerEntries = await verifyDb.InventoryTransactions.Where(t => t.Type == "OUT").ToListAsync();
            ledgerEntries.Should().HaveCount(1, "Sổ cái OUT không được ghi lần 2 khi Complete");
        }
    }

    [Fact]
    public async Task AC04_CompleteOrder_FromNonConfirmed_ShouldReturn409()
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
        string skuCode;
        decimal retailPrice;

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            warehouseId = (await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId)).Id;
            supplierId = (await db.Suppliers.FirstAsync(s => s.TenantId == tenantId)).Id;
            userId = (await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local")).Id;

            var variant = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = variant.Id;
            skuCode = variant.SKU;
            retailPrice = variant.RetailPrice;
        }

        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 10, 100000);

        // Đơn Reserved chưa Confirm mà gọi Complete -> 409 INVALID_STATE_TRANSITION
        var createRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2, UnitPrice = retailPrice } }
        });
        var order = await createRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);

        var completeRes = await _client.PostAsJsonAsync($"/api/orders/{order!.Id}/complete", new CompleteOrderRequestDto());
        completeRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await completeRes.Content.ReadAsStringAsync();
        body.Should().Contain("INVALID_STATE_TRANSITION");
    }

    // =========================================================================
    // AC-05: Hủy đơn hàng (POST /api/orders/{id}/cancel)
    // =========================================================================
    [Fact]
    public async Task AC05_CancelOrder_FromReserved_ShouldReleaseReservations_WithoutModifyingOnHand()
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
        string skuCode;
        decimal retailPrice;

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            warehouseId = (await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId)).Id;
            supplierId = (await db.Suppliers.FirstAsync(s => s.TenantId == tenantId)).Id;
            userId = (await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local")).Id;

            var variant = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = variant.Id;
            skuCode = variant.SKU;
            retailPrice = variant.RetailPrice;
        }

        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 10, 100000);

        // Tạo đơn (Reserved) -> Reserved = 3
        var createRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 3, UnitPrice = retailPrice } }
        });
        var order = await createRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);

        // Hủy đơn hàng
        var cancelRes = await _client.PostAsJsonAsync($"/api/orders/{order!.Id}/cancel", new CancelOrderRequestDto
        {
            Reason = "Khách hàng đổi ý không mua nữa"
        });
        cancelRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelledOrder = await cancelRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
        cancelledOrder!.Status.Should().Be("Cancelled");
        cancelledOrder.CancelledAt.Should().NotBeNull();

        // Kiểm tra CSDL
        var (verifyDb, _) = CreatePostgresDbContext(tenantId);
        await using (verifyDb)
        {
            // Reserved được giải phóng về 0, OnHand không đổi (10)
            var bBal = await verifyDb.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bBal.OnHand.Should().Be(10);
            bBal.Reserved.Should().Be(0);

            var whBal = await verifyDb.WarehouseInventoryBalances.FirstAsync(wb => wb.WarehouseId == warehouseId && wb.SkuId == skuId);
            whBal.OnHand.Should().Be(10);
            whBal.Reserved.Should().Be(0);

            // Phiếu giữ chỗ chuyển sang Released
            var reservations = await verifyDb.Reservations.Where(r => r.OrderId == order.Id).ToListAsync();
            reservations.Should().HaveCount(1);
            reservations[0].Status.Should().Be("Released");

            // Không ghi bút toán nào vào Sổ cái
            var ledgerOut = await verifyDb.InventoryTransactions.Where(t => t.Type == "OUT").ToListAsync();
            ledgerOut.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task AC05_CancelOrder_FromConfirmedOrCompleted_ShouldReturn409()
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
        string skuCode;
        decimal retailPrice;

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            warehouseId = (await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId)).Id;
            supplierId = (await db.Suppliers.FirstAsync(s => s.TenantId == tenantId)).Id;
            userId = (await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local")).Id;

            var variant = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = variant.Id;
            skuCode = variant.SKU;
            retailPrice = variant.RetailPrice;
        }

        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 10, 100000);

        // Tạo & Duyệt đơn (Confirmed)
        var createRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2, UnitPrice = retailPrice } }
        });
        var order = await createRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
        await _client.PostAsJsonAsync($"/api/orders/{order!.Id}/confirm", new ConfirmOrderRequestDto());

        // Hủy đơn Confirmed -> 409 INVALID_STATE_TRANSITION
        var cancelRes = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", new CancelOrderRequestDto
        {
            Reason = "Thử hủy đơn đã Confirmed"
        });
        cancelRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await cancelRes.Content.ReadAsStringAsync();
        body.Should().Contain("INVALID_STATE_TRANSITION");
    }

    [Fact]
    public async Task AC05_CancelOrder_WithEmptyReason_ShouldReturn400()
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
        string skuCode;
        decimal retailPrice;

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            warehouseId = (await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId)).Id;
            supplierId = (await db.Suppliers.FirstAsync(s => s.TenantId == tenantId)).Id;
            userId = (await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local")).Id;

            var variant = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = variant.Id;
            skuCode = variant.SKU;
            retailPrice = variant.RetailPrice;
        }

        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 10, 100000);

        var createRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2, UnitPrice = retailPrice } }
        });
        var order = await createRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);

        // Hủy với Reason rỗng -> 400 VALIDATION_ERROR
        var cancelRes = await _client.PostAsJsonAsync($"/api/orders/{order!.Id}/cancel", new CancelOrderRequestDto
        {
            Reason = "   "
        });
        cancelRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await cancelRes.Content.ReadAsStringAsync();
        body.Should().Contain("VALIDATION_ERROR");
    }

    // =========================================================================
    // AC-06: Concurrency & Stress Tests
    // =========================================================================
    [Fact]
    public async Task AC06_ConcurrencyStressTest_50CreateOrders_CompetingForOneUnit_ExactlyOneWins()
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
        string skuCode;
        decimal retailPrice;

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            warehouseId = (await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId)).Id;
            supplierId = (await db.Suppliers.FirstAsync(s => s.TenantId == tenantId)).Id;
            userId = (await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local")).Id;

            var variant = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = variant.Id;
            skuCode = variant.SKU;
            retailPrice = variant.RetailPrice;
        }

        // Nhập kho ĐÚNG 1 ĐƠN VỊ DUY NHẤT
        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 1, 100000);

        const int concurrency = 50;
        var tasks = Enumerable.Range(0, concurrency).Select(async i =>
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

            var req = new CreateOrderRequestDto
            {
                BranchId = branchId,
                Channel = "Admin",
                CustomerName = $"Khách Concurrent {i}",
                RequestKey = Guid.NewGuid(), // Mỗi request một key độc lập
                Items = new List<OrderItemRequestDto>
                {
                    new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1, UnitPrice = retailPrice }
                }
            };

            return await client.PostAsJsonAsync("/api/orders", req);
        }).ToList();

        var responses = await Task.WhenAll(tasks);

        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var failureCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        successCount.Should().Be(1, "Chỉ có đúng 1 yêu cầu thành công tạo đơn và giữ chỗ");
        failureCount.Should().Be(concurrency - 1, "49 yêu cầu còn lại phải nhận 409 Conflict do hết tồn khả dụng");

        // Kiểm tra tồn kho không bị âm
        var (verifyDb, _) = CreatePostgresDbContext(tenantId);
        await using (verifyDb)
        {
            var bBal = await verifyDb.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bBal.OnHand.Should().Be(1);
            bBal.Reserved.Should().Be(1);

            var orders = await verifyDb.Orders.ToListAsync();
            orders.Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task AC06_ConcurrentConfirmAndCancel_ShouldHaveExactlyOneWinner_AndControlledError()
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
        string skuCode;
        decimal retailPrice;

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            warehouseId = (await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId)).Id;
            supplierId = (await db.Suppliers.FirstAsync(s => s.TenantId == tenantId)).Id;
            userId = (await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local")).Id;

            var variant = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = variant.Id;
            skuCode = variant.SKU;
            retailPrice = variant.RetailPrice;
        }

        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 10, 100000);

        var createRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2, UnitPrice = retailPrice } }
        });
        var order = await createRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);

        // Bắn đồng thời Confirm và Cancel vào cùng 1 đơn hàng
        var client1 = _factory.CreateClient();
        client1.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var taskConfirm = client1.PostAsJsonAsync($"/api/orders/{order!.Id}/confirm", new ConfirmOrderRequestDto());

        var client2 = _factory.CreateClient();
        client2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var taskCancel = client2.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", new CancelOrderRequestDto { Reason = "Đua hủy đơn" });

        var results = await Task.WhenAll(taskConfirm, taskCancel);

        var okCount = results.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflictCount = results.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        okCount.Should().Be(1, "Chỉ một trong hai tác vụ Confirm hoặc Cancel được thành công");
        conflictCount.Should().Be(1, "Tác vụ còn lại phải bị từ chối 409 INVALID_STATE_TRANSITION");

        var conflictRes = results.First(r => r.StatusCode == HttpStatusCode.Conflict);
        var conflictBody = await conflictRes.Content.ReadAsStringAsync();
        conflictBody.Should().Contain("INVALID_STATE_TRANSITION");
    }

    [Fact]
    public async Task AC06_ConcurrentCreateOnline_Vs_PosCheckout_ShouldPreventOversell_WithoutDeadlock()
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
        string skuCode;
        decimal retailPrice;

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            warehouseId = (await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId)).Id;
            supplierId = (await db.Suppliers.FirstAsync(s => s.TenantId == tenantId)).Id;
            userId = (await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local")).Id;

            var variant = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = variant.Id;
            skuCode = variant.SKU;
            retailPrice = variant.RetailPrice;
        }

        // Nhập kho đúng 1 đơn vị
        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 1, 100000);

        var client1 = _factory.CreateClient();
        client1.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var taskOnline = client1.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1, UnitPrice = retailPrice } }
        });

        var client2 = _factory.CreateClient();
        client2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var taskPos = client2.PostAsJsonAsync("/api/pos/checkout", new PosCheckoutRequestDto
        {
            BranchId = branchId,
            RequestKey = Guid.NewGuid(),
            PaymentMethod = "Cash",
            AmountGiven = retailPrice,
            TotalAmount = retailPrice,
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1, UnitPrice = retailPrice } }
        });

        var responses = await Task.WhenAll(taskOnline, taskPos);

        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.OK);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        successCount.Should().Be(1, "Chỉ 1 trong 2 kênh Online hoặc POS được lấy đơn vị tồn kho duy nhất");
        conflictCount.Should().Be(1, "Kênh còn lại phải nhận 409 INSUFFICIENT_STOCK");
    }

    // =========================================================================
    // AC-07: Atomic Rollback & Resilience
    // =========================================================================
    [Fact]
    public async Task AC07_AtomicRollback_OneItemOutOfStock_RollsBackEntireCart()
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, sku1Id, sku2Id;
        string sku1Code, sku2Code = "POLO-NAM-TRANG-L";
        decimal retailPrice1, retailPrice2 = 350000m;

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            warehouseId = (await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId)).Id;
            supplierId = (await db.Suppliers.FirstAsync(s => s.TenantId == tenantId)).Id;
            userId = (await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local")).Id;

            var sku1 = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            sku1Id = sku1.Id;
            sku1Code = sku1.SKU;
            retailPrice1 = sku1.RetailPrice;

            sku2Id = Guid.NewGuid();
            var sku2 = new ProductVariant
            {
                Id = sku2Id,
                TenantId = tenantId,
                ProductId = sku1.ProductId,
                Code = sku2Code,
                SKU = sku2Code,
                RetailPrice = retailPrice2,
                WholesalePrice = 250000m,
                Unit = "piece",
                IsActive = true
            };
            db.ProductVariants.Add(sku2);
            await db.SaveChangesAsync();
        }

        // SKU 1 có 10 chiếc, SKU 2 có 0 chiếc
        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, sku1Id, 10, 100000);

        var request = new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = sku1Id, SkuCode = sku1Code, Quantity = 2, UnitPrice = retailPrice1 },
                new() { SkuId = sku2Id, SkuCode = sku2Code, Quantity = 1, UnitPrice = retailPrice2 }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/orders", request);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("INSUFFICIENT_STOCK");

        // Kiểm tra toàn bộ đơn bị rollback sạch, SKU 1 không bị giữ chỗ dở dang
        var (verifyDb, _) = CreatePostgresDbContext(tenantId);
        await using (verifyDb)
        {
            var bBal1 = await verifyDb.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == sku1Id);
            bBal1.Reserved.Should().Be(0);

            var orders = await verifyDb.Orders.ToListAsync();
            orders.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task AC07_ExpiredReservation_CannotBeConfirmed_Returns409_CancelReleasesExpiredReservation()
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
        string skuCode;
        decimal retailPrice;

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            warehouseId = (await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId)).Id;
            supplierId = (await db.Suppliers.FirstAsync(s => s.TenantId == tenantId)).Id;
            userId = (await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local")).Id;

            var variant = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = variant.Id;
            skuCode = variant.SKU;
            retailPrice = variant.RetailPrice;
        }

        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 10, 100000);

        var createRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2, UnitPrice = retailPrice } }
        });
        var order = await createRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);

        // Giả lập phiếu giữ chỗ hết hạn (set expires_at về quá khứ trong CSDL, bảo đảm ck_reservations_expiry: expires_at > created_at)
        var (expireDb, _) = CreatePostgresDbContext(tenantId);
        await using (expireDb)
        {
            await expireDb.Database.ExecuteSqlRawAsync(
                "UPDATE reservations SET created_at = {0}, expires_at = {1} WHERE order_id = {2};",
                DateTimeOffset.UtcNow.AddMinutes(-30),
                DateTimeOffset.UtcNow.AddMinutes(-15),
                order!.Id);
        }

        // 1. Thử duyệt đơn khi phiếu giữ chỗ đã hết hạn -> 409 RESERVATION_EXPIRED
        var confirmRes = await _client.PostAsJsonAsync($"/api/orders/{order!.Id}/confirm", new ConfirmOrderRequestDto());
        confirmRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await confirmRes.Content.ReadAsStringAsync();
        body.Should().Contain("RESERVATION_EXPIRED");

        // 2. Hủy đơn khi phiếu giữ chỗ đã hết hạn -> Cho phép hủy để giải phóng tồn giữ chỗ sạch sẽ
        var cancelRes = await _client.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", new CancelOrderRequestDto
        {
            Reason = "Hủy đơn có giữ chỗ đã quá hạn"
        });
        cancelRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Kiểm tra tồn giữ chỗ được hoàn lại sạch sẽ
        var (verifyDb, _) = CreatePostgresDbContext(tenantId);
        await using (verifyDb)
        {
            var bBal = await verifyDb.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bBal.Reserved.Should().Be(0);

            var res = await verifyDb.Reservations.FirstAsync(r => r.OrderId == order.Id);
            res.Status.Should().Be("Released");
        }
    }

    // =========================================================================
    // AC-08: Phân quyền RBAC & Validation biên số học
    // =========================================================================
    [Fact]
    public async Task AC08_Security_401Unauthenticated_And_403CashierForbidden_OnAllFourEndpoints()
    {
        await SeedDatabaseAsync();

        var dummyGuid = Guid.NewGuid();

        // 1. Chưa xác thực (No token) -> 401 Unauthorized
        _client.DefaultRequestHeaders.Authorization = null;

        var resCreate401 = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto());
        resCreate401.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var resConfirm401 = await _client.PostAsJsonAsync($"/api/orders/{dummyGuid}/confirm", new ConfirmOrderRequestDto());
        resConfirm401.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var resComplete401 = await _client.PostAsJsonAsync($"/api/orders/{dummyGuid}/complete", new CompleteOrderRequestDto());
        resComplete401.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var resCancel401 = await _client.PostAsJsonAsync($"/api/orders/{dummyGuid}/cancel", new CancelOrderRequestDto { Reason = "test" });
        resCancel401.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 2. Vai trò Cashier -> 403 Forbidden trên cả 4 endpoints của Order
        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);

        var resCreate403 = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto());
        resCreate403.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var resConfirm403 = await _client.PostAsJsonAsync($"/api/orders/{dummyGuid}/confirm", new ConfirmOrderRequestDto());
        resConfirm403.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var resComplete403 = await _client.PostAsJsonAsync($"/api/orders/{dummyGuid}/complete", new CompleteOrderRequestDto());
        resComplete403.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var resCancel403 = await _client.PostAsJsonAsync($"/api/orders/{dummyGuid}/cancel", new CancelOrderRequestDto { Reason = "test" });
        resCancel403.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC08_NumericOverflow_And_MagnitudeLimits_ShouldReturn400ValidationError()
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, skuId;
        string skuCode;

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            var variant = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = variant.Id;
            skuCode = variant.SKU;
        }

        // 1. Đơn giá vượt giới hạn numeric(20,2)
        var overflowPriceRequest = new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1, UnitPrice = 1_000_000_000_000_000_000m }
            }
        };
        var resOverflowPrice = await _client.PostAsJsonAsync("/api/orders", overflowPriceRequest);
        resOverflowPrice.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body1 = await resOverflowPrice.Content.ReadAsStringAsync();
        body1.Should().Contain("VALIDATION_ERROR");

        // 2. Số lượng vượt quá 3 chữ số thập phân
        var scaleQuantityRequest = new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1.1234m, UnitPrice = 100000 }
            }
        };
        var resScaleQty = await _client.PostAsJsonAsync("/api/orders", scaleQuantityRequest);
        resScaleQty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body2 = await resScaleQty.Content.ReadAsStringAsync();
        body2.Should().Contain("VALIDATION_ERROR");
    }

    // =========================================================================
    // T3-F01: Giới hạn endpoint tạo đơn Admin (POST /api/orders chỉ nhận Admin)
    // =========================================================================
    [Theory]
    [InlineData("POS")]
    [InlineData("Shopee")]
    [InlineData("TikTok")]
    [InlineData("Lazada")]
    [InlineData("Ecommerce")]
    [InlineData("InvalidChannel")]
    public async Task T3_F01_CreateOrder_NonAdminChannel_ShouldReturn400ValidationError_AndZeroDbSideEffects(string nonAdminChannel)
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
        string skuCode;
        decimal retailPrice;

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            var warehouse = await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId);
            warehouseId = warehouse.Id;
            var supplier = await db.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            supplierId = supplier.Id;
            var user = await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local");
            userId = user.Id;
            var sku = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = sku.Id;
            skuCode = sku.SKU;
            retailPrice = sku.RetailPrice;
        }

        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 10m, 100000m);

        var requestKey = Guid.NewGuid();
        var request = new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = nonAdminChannel,
            RequestKey = requestKey,
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2, UnitPrice = retailPrice }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/orders", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("VALIDATION_ERROR");
        body.Should().Contain("Admin");

        // Đối soát CSDL: Tuyệt đối không phát sinh đơn hàng, không tạo reservation, không đổi số dư tồn kho
        var (verifyDb, _) = CreatePostgresDbContext(tenantId);
        await using (verifyDb)
        {
            var orderExists = await verifyDb.Orders.AnyAsync(o => o.RequestKey == requestKey);
            orderExists.Should().BeFalse("Kênh không hợp lệ bị từ chối không được phát sinh Order.");

            var resCount = await verifyDb.Reservations.CountAsync(r => r.TenantId == tenantId);
            resCount.Should().Be(0, "Không được phát sinh Reservation.");

            var bBal = await verifyDb.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bBal.OnHand.Should().Be(10m);
            bBal.Reserved.Should().Be(0m);
        }
    }

    // =========================================================================
    // T3-F02: Giữ đúng precision WAC numeric(20,6) trên CostPrice, Sổ cái & DTO
    // =========================================================================
    [Fact]
    public async Task T3_F02_ConfirmOrder_PrecisionWac_6Decimals_CostPriceAndLedgerUnitCost_MustMatchExactly()
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
        string skuCode;
        decimal retailPrice;

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            var warehouse = await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId);
            warehouseId = warehouse.Id;
            var supplier = await db.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            supplierId = supplier.Id;
            var user = await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local");
            userId = user.Id;
            var sku = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = sku.Id;
            skuCode = sku.SKU;
            retailPrice = sku.RetailPrice;
        }

        // Nhập lô 1: 1 sản phẩm đơn giá 1đ
        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 1m, 1m);
        // Nhập lô 2: 2 sản phẩm đơn giá 2đ
        // => Tổng tồn = 3, Tổng trị giá = 5 => WAC = 5 / 3 = 1.66666666... Làm tròn numeric(20,6) là 1.666667
        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 2m, 2m);

        decimal expectedWac = 1.666667m;

        // Tạo đơn hàng Admin mua 1 sản phẩm
        var createRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1m, UnitPrice = retailPrice }
            }
        });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdOrder = await createRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
        createdOrder.Should().NotBeNull();

        // Chốt duyệt đơn (POST /api/orders/{id}/confirm)
        var confirmRes = await _client.PostAsJsonAsync($"/api/orders/{createdOrder!.Id}/confirm", new ConfirmOrderRequestDto());
        confirmRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var confirmedDto = await confirmRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
        confirmedDto.Should().NotBeNull();

        // Kiểm tra DTO trả về khớp đúng 6 chữ số thập phân
        confirmedDto!.Items[0].CostPrice.Should().Be(expectedWac);
        confirmedDto.TotalCogs.Should().Be(expectedWac);

        // Kiểm tra trực tiếp trong PostgreSQL: order_items.cost_price và inventory_transactions.unit_cost
        var (verifyDb, _) = CreatePostgresDbContext(tenantId);
        await using (verifyDb)
        {
            var dbItem = await verifyDb.OrderItems.FirstAsync(i => i.OrderId == createdOrder.Id);
            dbItem.CostPrice.Should().Be(expectedWac, "order_items.cost_price phải lưu đúng 1.666667, không bị làm tròn thành 1.666700.");

            var dbLedger = await verifyDb.InventoryTransactions.FirstAsync(t => t.OrderItemId == dbItem.Id && t.Type == "OUT");
            dbLedger.UnitCost.Should().Be(expectedWac, "inventory_transactions.unit_cost phải khớp chính xác với order_items.cost_price.");
        }

        // Nhập thêm hàng mới với đơn giá cao (10 sản phẩm @ 10đ) => WAC chi nhánh thay đổi
        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 10m, 10m);

        // Snapshot giá vốn và Sổ cái của đơn đã chốt phải hoàn toàn bất biến (immutable)
        var (verifyDb2, _) = CreatePostgresDbContext(tenantId);
        await using (verifyDb2)
        {
            var dbItemAfter = await verifyDb2.OrderItems.FirstAsync(i => i.OrderId == createdOrder.Id);
            dbItemAfter.CostPrice.Should().Be(expectedWac, "Snapshot giá vốn của đơn hàng đã confirm phải bất biến trước các giao dịch nhập kho sau đó.");

            var dbLedgerAfter = await verifyDb2.InventoryTransactions.FirstAsync(t => t.OrderItemId == dbItemAfter.Id && t.Type == "OUT");
            dbLedgerAfter.UnitCost.Should().Be(expectedWac, "UnitCost của bút toán OUT đã ghi sổ phải bất biến.");
        }
    }

    // =========================================================================
    // T3-F03: Kiểm tra đủ phân bổ giữ chỗ trước khi duyệt (ConfirmOrder)
    // =========================================================================
    [Fact]
    public async Task T3_F03_ConfirmOrder_UnderAllocatedReservation_ShouldReturn409InvalidStateTransition_AndZeroMutation()
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
        string skuCode;
        decimal retailPrice;

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            var warehouse = await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId);
            warehouseId = warehouse.Id;
            var supplier = await db.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            supplierId = supplier.Id;
            var user = await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local");
            userId = user.Id;
            var sku = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = sku.Id;
            skuCode = sku.SKU;
            retailPrice = sku.RetailPrice;
        }

        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 10m, 100000m);

        // Tạo đơn hàng mua 2 sản phẩm
        var createRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2m, UnitPrice = retailPrice }
            }
        });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = await createRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
        order.Should().NotBeNull();

        // Mô phỏng under-allocation fixture: giảm giữ chỗ từ 2 xuống 1 và giảm balance tương ứng
        var (setupDb, _) = CreatePostgresDbContext(tenantId);
        await using (setupDb)
        {
            var strategy = setupDb.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await setupDb.Database.BeginTransactionAsync();
                var res = await setupDb.Reservations.FirstAsync(r => r.OrderId == order!.Id);
                res.Quantity -= 1m;
                var bBal = await setupDb.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
                bBal.Reserved -= 1m;
                var whBal = await setupDb.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == warehouseId && w.SkuId == skuId);
                whBal.Reserved -= 1m;
                await setupDb.SaveChangesAsync();
                await setupDb.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE;");
                await tx.CommitAsync();
            });
        }

        // Gọi ConfirmOrder -> Phải bị từ chối 409 INVALID_STATE_TRANSITION
        var confirmRes = await _client.PostAsJsonAsync($"/api/orders/{order!.Id}/confirm", new ConfirmOrderRequestDto());
        confirmRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await confirmRes.Content.ReadAsStringAsync();
        body.Should().Contain("INVALID_STATE_TRANSITION");

        // Đối soát CSDL: Đơn hàng vẫn ở trạng thái Reserved, không có ledger OUT, OnHand giữ nguyên
        var (verifyDb, _) = CreatePostgresDbContext(tenantId);
        await using (verifyDb)
        {
            var dbOrder = await verifyDb.Orders.FirstAsync(o => o.Id == order.Id);
            dbOrder.Status.Should().Be("Reserved", "Đơn hàng under-allocated không được duyệt sang Confirmed.");

            var orderItemIds = await verifyDb.OrderItems.Where(i => i.OrderId == order.Id).Select(i => i.Id).ToListAsync();
            var outLedgerCount = await verifyDb.InventoryTransactions.CountAsync(t => (t.OrderItemId.HasValue && orderItemIds.Contains(t.OrderItemId.Value)) || t.Type == "OUT");
            outLedgerCount.Should().Be(0, "Tuyệt đối không được ghi sổ OUT khi không đủ phân bổ.");

            var bBal = await verifyDb.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bBal.OnHand.Should().Be(10m, "OnHand không bị trừ.");
        }
    }

    [Fact]
    public async Task T3_F03_ConfirmOrder_MultiWarehouseAllocation_ShouldSucceed()
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, wh1Id, wh2Id, supplierId, userId, skuId;
        string skuCode;
        decimal retailPrice;

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            var wh1 = await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId);
            wh1Id = wh1.Id;

            wh2Id = Guid.NewGuid();
            db.Warehouses.Add(new Warehouse
            {
                Id = wh2Id,
                TenantId = tenantId,
                BranchId = branchId,
                Name = "Kho phụ 2",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(1)
            });
            await db.SaveChangesAsync();

            var supplier = await db.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            supplierId = supplier.Id;
            var user = await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local");
            userId = user.Id;
            var sku = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = sku.Id;
            skuCode = sku.SKU;
            retailPrice = sku.RetailPrice;
        }

        // Nhập 1 sản phẩm vào Kho 1, 2 sản phẩm vào Kho 2
        await SeedStockAsync(tenantId, branchId, wh1Id, supplierId, userId, skuId, 1m, 100000m);
        await SeedStockAsync(tenantId, branchId, wh2Id, supplierId, userId, skuId, 2m, 100000m);

        // Tạo đơn hàng mua 3 sản phẩm -> Hệ thống tự động phân bổ: 1 sp tại Kho 1, 2 sp tại Kho 2
        var createRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 3m, UnitPrice = retailPrice }
            }
        });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = await createRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
        order.Should().NotBeNull();

        // Chốt duyệt đơn hàng
        var confirmRes = await _client.PostAsJsonAsync($"/api/orders/{order!.Id}/confirm", new ConfirmOrderRequestDto());
        confirmRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Đối soát CSDL: Cả 2 reservations chuyển Consumed, ghi 2 bút toán Sổ cái OUT cho 2 kho
        var (verifyDb, _) = CreatePostgresDbContext(tenantId);
        await using (verifyDb)
        {
            var reservations = await verifyDb.Reservations.Where(r => r.OrderId == order.Id).ToListAsync();
            reservations.Should().HaveCount(2);
            reservations.Should().OnlyContain(r => r.Status == "Consumed");

            var ledgers = await verifyDb.InventoryTransactions.Where(t => t.Type == "OUT").ToListAsync();
            ledgers.Should().HaveCount(2);
            ledgers.Sum(l => l.Quantity).Should().Be(3m);

            var bBal = await verifyDb.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bBal.OnHand.Should().Be(0m);
            bBal.Reserved.Should().Be(0m);
        }
    }

    // =========================================================================
    // T3-F04: Cho phép hủy đơn Draft legacy chưa thanh toán của ReservationService
    // =========================================================================
    [Fact]
    public async Task T3_F04_CancelOrder_LegacyUnpaidDraft_FromReservationService_ShouldReleaseReservation()
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
        string skuCode;
        decimal retailPrice;

        var (db, _) = CreatePostgresDbContext(tenantId);
        Guid legacyOrderId;
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            var warehouse = await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId);
            warehouseId = warehouse.Id;
            var supplier = await db.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            supplierId = supplier.Id;
            var user = await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local");
            userId = user.Id;
            var sku = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = sku.Id;
            skuCode = sku.SKU;
            retailPrice = sku.RetailPrice;
        }

        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 5m, 100000m);

        // Tạo đơn Draft legacy kèm Reservation thông qua ReservationService (Sprint 2)
        var (resDb, resTenantService) = CreatePostgresDbContext(tenantId);
        await using (resDb)
        {
            var reservationService = new ReservationService(resDb, resTenantService);
            var reserveResult = await reservationService.ReserveStockAsync(new ReserveStockRequestDto
            {
                BranchId = branchId,
                WarehouseId = warehouseId,
                SkuId = skuId,
                Quantity = 2m,
                RequestKey = Guid.NewGuid()
            }, userId);

            legacyOrderId = reserveResult.OrderId;
        }

        // Xác minh đơn Draft legacy tồn tại với Channel = "POS", Status = "Draft"
        var (checkDb, _) = CreatePostgresDbContext(tenantId);
        await using (checkDb)
        {
            var legacyOrder = await checkDb.Orders.FirstAsync(o => o.Id == legacyOrderId);
            legacyOrder.Channel.Should().Be("POS");
            legacyOrder.Status.Should().Be("Draft");

            var bBalBefore = await checkDb.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bBalBefore.Reserved.Should().Be(2m);
            bBalBefore.OnHand.Should().Be(5m);
        }

        // 1. Thử duyệt đơn Draft legacy qua confirm -> Phải bị từ chối 409 INVALID_STATE_TRANSITION
        var confirmRes = await _client.PostAsJsonAsync($"/api/orders/{legacyOrderId}/confirm", new ConfirmOrderRequestDto());
        confirmRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var confirmBody = await confirmRes.Content.ReadAsStringAsync();
        confirmBody.Should().Contain("INVALID_STATE_TRANSITION");

        // 2. Hủy đơn Draft legacy qua POST /api/orders/{id}/cancel -> Phải THÀNH CÔNG 200 OK
        var cancelRes = await _client.PostAsJsonAsync($"/api/orders/{legacyOrderId}/cancel", new CancelOrderRequestDto
        {
            Reason = "Hủy đơn nháp giữ chỗ cũ"
        });
        cancelRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelDto = await cancelRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
        cancelDto.Should().NotBeNull();
        cancelDto!.Status.Should().Be("Cancelled");

        // 3. Đối soát CSDL: Reservation sang Released, Reserved hoàn về 0, OnHand không đổi (5m), không có sổ cái OUT
        var (verifyDb, _) = CreatePostgresDbContext(tenantId);
        await using (verifyDb)
        {
            var dbOrder = await verifyDb.Orders.FirstAsync(o => o.Id == legacyOrderId);
            dbOrder.Status.Should().Be("Cancelled");

            var dbRes = await verifyDb.Reservations.FirstAsync(r => r.OrderId == legacyOrderId);
            dbRes.Status.Should().Be("Released");

            var bBalAfter = await verifyDb.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bBalAfter.Reserved.Should().Be(0m, "Reserved balance phải được hoàn lại.");
            bBalAfter.OnHand.Should().Be(5m, "OnHand vật lý không thay đổi.");

            var whBalAfter = await verifyDb.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == warehouseId && w.SkuId == skuId);
            whBalAfter.Reserved.Should().Be(0m);
            whBalAfter.OnHand.Should().Be(5m);
        }

        // 4. Hủy lần hai trên cùng đơn đã Cancelled -> Phải báo 409 INVALID_STATE_TRANSITION, không trừ Reserved hai lần
        var cancelAgainRes = await _client.PostAsJsonAsync($"/api/orders/{legacyOrderId}/cancel", new CancelOrderRequestDto
        {
            Reason = "Thử hủy lại lần hai"
        });
        cancelAgainRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var (verifyDb2, _) = CreatePostgresDbContext(tenantId);
        await using (verifyDb2)
        {
            var bBal = await verifyDb2.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bBal.Reserved.Should().Be(0m, "Không bị trừ âm tồn.");
        }
    }

    [Fact]
    public async Task T3_F04_CancelOrder_PaidCompletedPosOrder_ShouldReturn409InvalidStateTransition()
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
        string skuCode;
        decimal retailPrice;

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            var warehouse = await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId);
            warehouseId = warehouse.Id;
            var supplier = await db.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            supplierId = supplier.Id;
            var user = await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local");
            userId = user.Id;
            var sku = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = sku.Id;
            skuCode = sku.SKU;
            retailPrice = sku.RetailPrice;
        }

        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 10m, 100000m);

        // Tạo đơn POS Completed thông qua POS checkout
        var checkoutRes = await _client.PostAsJsonAsync("/api/pos/checkout", new PosCheckoutRequestDto
        {
            BranchId = branchId,
            Channel = "POS",
            PaymentMethod = "Cash",
            AmountGiven = retailPrice,
            TotalAmount = retailPrice,
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1m, UnitPrice = retailPrice }
            }
        });
        checkoutRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var checkoutDto = await checkoutRes.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);
        checkoutDto.Should().NotBeNull();

        // Thử hủy đơn POS đã thanh toán qua /api/orders/{id}/cancel -> Bị từ chối 409 INVALID_STATE_TRANSITION
        var cancelRes = await _client.PostAsJsonAsync($"/api/orders/{checkoutDto!.OrderId}/cancel", new CancelOrderRequestDto
        {
            Reason = "Cố tình hủy đơn POS đã thanh toán"
        });
        cancelRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await cancelRes.Content.ReadAsStringAsync();
        body.Should().Contain("INVALID_STATE_TRANSITION");
    }

    // =========================================================================
    // T3-F05: Fault Injection & Atomic Rollback (Create, Confirm, Cancel)
    // =========================================================================
    [Fact]
    public async Task T3_F05_FaultInjection_CreateOrder_MidstreamFailure_ShouldRollbackCompletely_AndAllowRetry()
    {
        await SeedDatabaseAsync();

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
        string skuCode;
        decimal retailPrice;

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            var warehouse = await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId);
            warehouseId = warehouse.Id;
            var supplier = await db.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            supplierId = supplier.Id;
            var user = await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local");
            userId = user.Id;
            var sku = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = sku.Id;
            skuCode = sku.SKU;
            retailPrice = sku.RetailPrice;
        }

        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 10m, 100000m);

        var requestKey = Guid.NewGuid();
        var request = new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = requestKey,
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2m, UnitPrice = retailPrice }
            }
        };

        // 1. Fault injection bằng Interceptor: ném ngoại lệ ngay sau SaveChangesAsync trước commit
        var interceptor = new MidstreamFailureInterceptor { ShouldFail = true };
        var (failingCtx, failingTenantService) = CreatePostgresDbContext(tenantId, interceptor);
        await using (failingCtx)
        {
            var orderService = new OrderService(failingCtx, failingTenantService, Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>.Instance);
            Func<Task> actFailing = async () => await orderService.CreateOrderAsync(request, userId);
            await actFailing.Should().ThrowAsync<MidstreamOrderFaultException>();
            interceptor.SavedChangesCount.Should().BeGreaterThan(0, "Xác nhận các câu lệnh SQL đã được gửi xuống PostgreSQL.");
        }

        // 2. Kiểm chứng rollback 100%: không có order, không có reservation, reserved balance = 0
        var (verifyDb, _) = CreatePostgresDbContext(tenantId);
        await using (verifyDb)
        {
            var orderExists = await verifyDb.Orders.AnyAsync(o => o.RequestKey == requestKey);
            orderExists.Should().BeFalse("Giao dịch lỗi không được để lại Order.");

            var resCount = await verifyDb.Reservations.CountAsync(r => r.TenantId == tenantId);
            resCount.Should().Be(0, "Không được để lại Reservation.");

            var bBal = await verifyDb.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bBal.Reserved.Should().Be(0m);
            bBal.OnHand.Should().Be(10m);
        }

        // 3. Retry với cùng RequestKey và cùng nội dung -> Phải thành công sạch sẽ
        var (retryCtx, retryTenantService) = CreatePostgresDbContext(tenantId);
        await using (retryCtx)
        {
            var orderService = new OrderService(retryCtx, retryTenantService, Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>.Instance);
            var result = await orderService.CreateOrderAsync(request, userId);
            result.Should().NotBeNull();
            result.Status.Should().Be("Reserved");
        }
    }

    [Fact]
    public async Task T3_F05_FaultInjection_ConfirmOrder_MidstreamFailure_ShouldRollbackCompletely_AndAllowRetry()
    {
        await SeedDatabaseAsync();

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
        string skuCode;
        decimal retailPrice;

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            var warehouse = await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId);
            warehouseId = warehouse.Id;
            var supplier = await db.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            supplierId = supplier.Id;
            var user = await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local");
            userId = user.Id;
            var sku = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = sku.Id;
            skuCode = sku.SKU;
            retailPrice = sku.RetailPrice;
        }

        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 10m, 100000m);

        // Tạo đơn hàng thành công trước
        Guid orderId;
        var (cleanCtx, cleanTenantService) = CreatePostgresDbContext(tenantId);
        await using (cleanCtx)
        {
            var orderService = new OrderService(cleanCtx, cleanTenantService, Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>.Instance);
            var order = await orderService.CreateOrderAsync(new CreateOrderRequestDto
            {
                BranchId = branchId,
                Channel = "Admin",
                RequestKey = Guid.NewGuid(),
                Items = new List<OrderItemRequestDto>
                {
                    new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2m, UnitPrice = retailPrice }
                }
            }, userId);
            orderId = order.Id;
        }

        // 1. Fault injection khi ConfirmOrder
        var interceptor = new MidstreamFailureInterceptor { ShouldFail = true };
        var (failingCtx, failingTenantService) = CreatePostgresDbContext(tenantId, interceptor);
        await using (failingCtx)
        {
            var orderService = new OrderService(failingCtx, failingTenantService, Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>.Instance);
            Func<Task> actFailing = async () => await orderService.ConfirmOrderAsync(orderId, new ConfirmOrderRequestDto(), userId);
            await actFailing.Should().ThrowAsync<MidstreamOrderFaultException>();
            interceptor.SavedChangesCount.Should().BeGreaterThan(0);
        }

        // 2. Xác minh rollback hoàn toàn: Order vẫn Reserved, Reservation vẫn Active, OnHand chưa bị trừ, Ledger OUT = 0
        var (verifyDb, _) = CreatePostgresDbContext(tenantId);
        await using (verifyDb)
        {
            var dbOrder = await verifyDb.Orders.FirstAsync(o => o.Id == orderId);
            dbOrder.Status.Should().Be("Reserved", "Order phải giữ nguyên Reserved.");

            var res = await verifyDb.Reservations.FirstAsync(r => r.OrderId == orderId);
            res.Status.Should().Be("Active", "Reservation phải giữ nguyên Active.");

            var bBal = await verifyDb.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bBal.OnHand.Should().Be(10m, "OnHand không được trừ.");
            bBal.Reserved.Should().Be(2m, "Reserved vẫn giữ nguyên.");

            var outCount = await verifyDb.InventoryTransactions.CountAsync(t => t.Type == "OUT");
            outCount.Should().Be(0, "Không được để lại bút toán Sổ cái OUT.");
        }

        // 3. Retry Confirm thành công
        var (retryCtx, retryTenantService) = CreatePostgresDbContext(tenantId);
        await using (retryCtx)
        {
            var orderService = new OrderService(retryCtx, retryTenantService, Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>.Instance);
            var result = await orderService.ConfirmOrderAsync(orderId, new ConfirmOrderRequestDto(), userId);
            result.Status.Should().Be("Confirmed");
        }
    }

    [Fact]
    public async Task T3_F05_FaultInjection_CancelOrder_MidstreamFailure_ShouldRollbackCompletely_AndAllowRetry()
    {
        await SeedDatabaseAsync();

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
        string skuCode;
        decimal retailPrice;

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            var warehouse = await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId);
            warehouseId = warehouse.Id;
            var supplier = await db.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            supplierId = supplier.Id;
            var user = await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local");
            userId = user.Id;
            var sku = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = sku.Id;
            skuCode = sku.SKU;
            retailPrice = sku.RetailPrice;
        }

        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 10m, 100000m);

        // Tạo đơn hàng thành công trước
        Guid orderId;
        var (cleanCtx, cleanTenantService) = CreatePostgresDbContext(tenantId);
        await using (cleanCtx)
        {
            var orderService = new OrderService(cleanCtx, cleanTenantService, Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>.Instance);
            var order = await orderService.CreateOrderAsync(new CreateOrderRequestDto
            {
                BranchId = branchId,
                Channel = "Admin",
                RequestKey = Guid.NewGuid(),
                Items = new List<OrderItemRequestDto>
                {
                    new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2m, UnitPrice = retailPrice }
                }
            }, userId);
            orderId = order.Id;
        }

        // 1. Fault injection khi CancelOrder
        var interceptor = new MidstreamFailureInterceptor { ShouldFail = true };
        var (failingCtx, failingTenantService) = CreatePostgresDbContext(tenantId, interceptor);
        await using (failingCtx)
        {
            var orderService = new OrderService(failingCtx, failingTenantService, Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>.Instance);
            Func<Task> actFailing = async () => await orderService.CancelOrderAsync(orderId, new CancelOrderRequestDto { Reason = "test failure" }, userId);
            await actFailing.Should().ThrowAsync<MidstreamOrderFaultException>();
            interceptor.SavedChangesCount.Should().BeGreaterThan(0);
        }

        // 2. Xác minh rollback hoàn toàn: Order vẫn Reserved, Reservation vẫn Active, Reserved balance vẫn là 2m
        var (verifyDb, _) = CreatePostgresDbContext(tenantId);
        await using (verifyDb)
        {
            var dbOrder = await verifyDb.Orders.FirstAsync(o => o.Id == orderId);
            dbOrder.Status.Should().Be("Reserved");

            var res = await verifyDb.Reservations.FirstAsync(r => r.OrderId == orderId);
            res.Status.Should().Be("Active");

            var bBal = await verifyDb.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bBal.Reserved.Should().Be(2m);
        }

        // 3. Retry Cancel thành công
        var (retryCtx, retryTenantService) = CreatePostgresDbContext(tenantId);
        await using (retryCtx)
        {
            var orderService = new OrderService(retryCtx, retryTenantService, Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>.Instance);
            var result = await orderService.CancelOrderAsync(orderId, new CancelOrderRequestDto { Reason = "retry cancel" }, userId);
            result.Status.Should().Be("Cancelled");
        }
    }

    [Fact]
    public async Task T3_F05_Concurrent_ConfirmOrder_SameOrder_ExactlyOneSucceeds_OtherGets409()
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
        string skuCode;
        decimal retailPrice;

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            var warehouse = await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId);
            warehouseId = warehouse.Id;
            var supplier = await db.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            supplierId = supplier.Id;
            var user = await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local");
            userId = user.Id;
            var sku = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = sku.Id;
            skuCode = sku.SKU;
            retailPrice = sku.RetailPrice;
        }

        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 10m, 100000m);

        var createRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2m, UnitPrice = retailPrice }
            }
        });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = await createRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
        order.Should().NotBeNull();

        // 2 request Confirm đồng thời
        using var client1 = _factory.CreateClient();
        using var client2 = _factory.CreateClient();
        client1.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        client2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var task1 = client1.PostAsJsonAsync($"/api/orders/{order!.Id}/confirm", new ConfirmOrderRequestDto());
        var task2 = client2.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequestDto());

        var responses = await Task.WhenAll(task1, task2);
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        successCount.Should().Be(1, "Chỉ duy nhất 1 tác vụ Confirm được thành công.");
        conflictCount.Should().Be(1, "Tác vụ Confirm còn lại phải nhận 409 Conflict.");

        // Đối soát CSDL: OnHand chỉ bị trừ đúng 2 sản phẩm (10 -> 8), Ledger OUT chỉ có 1 bút toán
        var (verifyDb, _) = CreatePostgresDbContext(tenantId);
        await using (verifyDb)
        {
            var bBal = await verifyDb.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bBal.OnHand.Should().Be(8m);
            bBal.Reserved.Should().Be(0m);

            var orderItemIds = await verifyDb.OrderItems.Where(i => i.OrderId == order.Id).Select(i => i.Id).ToListAsync();
            var ledgerOut = await verifyDb.InventoryTransactions.Where(t => t.OrderItemId.HasValue && orderItemIds.Contains(t.OrderItemId.Value) && t.Type == "OUT").ToListAsync();
            ledgerOut.Should().HaveCount(1);
            ledgerOut[0].Quantity.Should().Be(2m);
        }
    }

    [Fact]
    public async Task T3_F05_Concurrent_CancelOrder_SameOrder_ExactlyOneSucceeds_OtherGets409()
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
        string skuCode;
        decimal retailPrice;

        var (db, _) = CreatePostgresDbContext(tenantId);
        await using (db)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            var warehouse = await db.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branchId);
            warehouseId = warehouse.Id;
            var supplier = await db.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            supplierId = supplier.Id;
            var user = await db.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "owner@shopa.local");
            userId = user.Id;
            var sku = await db.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = sku.Id;
            skuCode = sku.SKU;
            retailPrice = sku.RetailPrice;
        }

        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 10m, 100000m);

        var createRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2m, UnitPrice = retailPrice }
            }
        });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = await createRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
        order.Should().NotBeNull();

        // 2 request Cancel đồng thời
        using var client1 = _factory.CreateClient();
        using var client2 = _factory.CreateClient();
        client1.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        client2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var task1 = client1.PostAsJsonAsync($"/api/orders/{order!.Id}/cancel", new CancelOrderRequestDto { Reason = "Cancel request 1" });
        var task2 = client2.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", new CancelOrderRequestDto { Reason = "Cancel request 2" });

        var responses = await Task.WhenAll(task1, task2);
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        successCount.Should().Be(1, "Chỉ duy nhất 1 tác vụ Cancel được thành công.");
        conflictCount.Should().Be(1, "Tác vụ Cancel còn lại phải nhận 409 Conflict.");

        // Đối soát CSDL: Reserved balance hoàn về 0, không bị trừ âm
        var (verifyDb, _) = CreatePostgresDbContext(tenantId);
        await using (verifyDb)
        {
            var bBal = await verifyDb.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bBal.OnHand.Should().Be(10m);
            bBal.Reserved.Should().Be(0m);
        }
    }

    [Fact]
    public async Task T3_F05_CrossTenant_AllFourEndpoints_MustPreventCrossTenantAccess()
    {
        await SeedDatabaseAsync();

        // 1. Tạo đơn hàng thuộc Tenant A
        var ownerAToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerAToken);

        var tenantAId = OismsDatabaseSeeder.TenantAId;
        Guid branchAId, warehouseAId, supplierAId, userAId, skuAId;
        string skuACode;
        decimal retailPriceA;

        var (dbA, _) = CreatePostgresDbContext(tenantAId);
        await using (dbA)
        {
            var branch = await dbA.Branches.FirstAsync(b => b.TenantId == tenantAId);
            branchAId = branch.Id;
            var warehouse = await dbA.Warehouses.FirstAsync(w => w.TenantId == tenantAId && w.BranchId == branchAId);
            warehouseAId = warehouse.Id;
            var supplier = await dbA.Suppliers.FirstAsync(s => s.TenantId == tenantAId);
            supplierAId = supplier.Id;
            var user = await dbA.Users.FirstAsync(u => u.TenantId == tenantAId && u.Email == "owner@shopa.local");
            userAId = user.Id;
            var sku = await dbA.ProductVariants.FirstAsync(v => v.TenantId == tenantAId && v.IsActive);
            skuAId = sku.Id;
            skuACode = sku.SKU;
            retailPriceA = sku.RetailPrice;
        }

        await SeedStockAsync(tenantAId, branchAId, warehouseAId, supplierAId, userAId, skuAId, 10m, 100000m);

        var createOrderARes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchAId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuAId, SkuCode = skuACode, Quantity = 2m, UnitPrice = retailPriceA }
            }
        });
        createOrderARes.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderA = await createOrderARes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
        orderA.Should().NotBeNull();

        // 2. Đăng nhập bằng Owner của Tenant B (owner@shopb.local)
        var ownerBToken = await LoginAsAsync("owner@shopb.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerBToken);

        // a) CreateOrder nhắm vào Chi nhánh của Tenant A -> 404 Not Found (BRANCH_INACTIVE_OR_INVALID)
        var crossCreateRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchAId, // Chi nhánh của Tenant A!
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuAId, SkuCode = skuACode, Quantity = 1m, UnitPrice = retailPriceA }
            }
        });
        crossCreateRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var crossCreateBody = await crossCreateRes.Content.ReadAsStringAsync();
        crossCreateBody.Should().Contain("BRANCH_INACTIVE_OR_INVALID");

        // b) ConfirmOrder nhắm vào Order của Tenant A -> 404 Not Found (ORDER_NOT_FOUND)
        var crossConfirmRes = await _client.PostAsJsonAsync($"/api/orders/{orderA!.Id}/confirm", new ConfirmOrderRequestDto());
        crossConfirmRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var crossConfirmBody = await crossConfirmRes.Content.ReadAsStringAsync();
        crossConfirmBody.Should().Contain("ORDER_NOT_FOUND");

        // c) CompleteOrder nhắm vào Order của Tenant A -> 404 Not Found (ORDER_NOT_FOUND)
        var crossCompleteRes = await _client.PostAsJsonAsync($"/api/orders/{orderA.Id}/complete", new CompleteOrderRequestDto());
        crossCompleteRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var crossCompleteBody = await crossCompleteRes.Content.ReadAsStringAsync();
        crossCompleteBody.Should().Contain("ORDER_NOT_FOUND");

        // d) CancelOrder nhắm vào Order của Tenant A -> 404 Not Found (ORDER_NOT_FOUND)
        var crossCancelRes = await _client.PostAsJsonAsync($"/api/orders/{orderA.Id}/cancel", new CancelOrderRequestDto
        {
            Reason = "Tenant B tấn công hủy đơn của Tenant A"
        });
        crossCancelRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var crossCancelBody = await crossCancelRes.Content.ReadAsStringAsync();
        crossCancelBody.Should().Contain("ORDER_NOT_FOUND");

        // 3. Đối soát CSDL: Đơn hàng và tồn kho của Tenant A hoàn toàn không bị ảnh hưởng
        var (verifyDb, _) = CreatePostgresDbContext(tenantAId);
        await using (verifyDb)
        {
            var dbOrder = await verifyDb.Orders.FirstAsync(o => o.Id == orderA.Id);
            dbOrder.Status.Should().Be("Reserved", "Trạng thái đơn hàng của Tenant A phải được bảo toàn.");

            var bBal = await verifyDb.InventoryBalances.FirstAsync(b => b.BranchId == branchAId && b.SkuId == skuAId);
            bBal.OnHand.Should().Be(10m);
            bBal.Reserved.Should().Be(2m);
        }
    }
}
