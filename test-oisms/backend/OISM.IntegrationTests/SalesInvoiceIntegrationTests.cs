using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.SalesInvoices.Dtos;
using OISM.Application.Features.StockTransfers.Dtos;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Bộ kiểm thử tích hợp đầy đủ cho OISMS-47-U02: Hóa đơn bán và truy vết chứng từ xuất kho.
/// Chạy trên PostgreSQL 16 thật, bao phủ AC-02, AC-04, AC-05, AC-06, AC-07.
/// </summary>
[Collection("PostgresIntegrationTests")]
public class SalesInvoiceIntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class TestTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    public SalesInvoiceIntegrationTests(WebApplicationFactoryWithPostgres factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
    }

    public async Task DisposeAsync()
    {
        try
        {
            await using var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(@"
                DROP TRIGGER IF EXISTS oisms_test_fail_invoice_trg ON public.sales_invoices;
                DROP FUNCTION IF EXISTS public.oisms_test_fail_invoice();
            ", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private (AppDbContext Context, TestTenantService TenantService) CreatePostgresDbContext(Guid? tenantId = null)
    {
        var ts = new TestTenantService();
        if (tenantId.HasValue)
        {
            ts.SetTenant(tenantId.Value);
        }
        var ctx = PostgresTestEnvironment.CreateRuntimeDbContext(ts);
        return (ctx, ts);
    }

    private async Task SeedDatabaseAsync()
    {
        var (ctx, _) = CreatePostgresDbContext(null);
        await using (ctx)
        {
            await OismsDatabaseSeeder.SeedAsync(ctx, PostgresTestEnvironment.AppPassword);
        }
    }

    private async Task<string> LoginAsAsync(string email, string password)
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var res = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = password
        });
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        return auth!.AccessToken;
    }

    private async Task SeedStockAsync(Guid tenantId, Guid branchId, Guid warehouseId, Guid supplierId, Guid userId, Guid skuId, decimal quantity, decimal unitPrice)
    {
        var (context, tenantService) = CreatePostgresDbContext(tenantId);
        await using (context)
        {
            await PriceSeed.SyncFromLegacyColumnsAsync(context);
            await PurchaseOrderSeed.ReceiveStockAsync(context, tenantService, branchId, warehouseId, supplierId, userId, (skuId, quantity, unitPrice));
        }
    }

    // =========================================================================
    // AC-02: TẠO HÓA ĐƠN NGUYÊN TỬ & HOÀN TÁC TOÀN BỘ KHI LỖI
    // =========================================================================
    [Fact]
    public async Task AC02_PosAdminMarketplace_CreateExactlyOneInvoice_AndRollbackCompletelyOnFailure()
    {
        await SeedDatabaseAsync();
        var staffToken = await LoginAsAsync("staff@shopa.local", PostgresTestEnvironment.AppPassword);
        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, staffUserId, skuId;
        string skuCode;
        decimal price;

        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            var wh = await ctx.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branch.Id);
            warehouseId = wh.Id;
            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            supplierId = supplier.Id;
            var user = await ctx.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "staff@shopa.local");
            staffUserId = user.Id;
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = sku.Id;
            skuCode = sku.SKU;
            price = sku.RetailPrice;
        }

        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, staffUserId, skuId, 500m, 50000m);

        // 1. POS Checkout tạo đúng 1 hóa đơn
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        var posReqKey = Guid.NewGuid();
        var posRes = await _client.PostAsJsonAsync("/api/pos/checkout", new PosCheckoutRequestDto
        {
            BranchId = branchId,
            RequestKey = posReqKey,
            PaymentMethod = "Cash",
            AmountGiven = 1000000m,
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1m, UnitPrice = price } }
        });
        posRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var posData = await posRes.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);

        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var invCount = await ctx.SalesInvoices.CountAsync(s => s.TenantId == tenantId && s.OrderId == posData!.OrderId);
            invCount.Should().Be(1, "POS checkout phải sinh đúng 1 hóa đơn");
        }

        // 2. Admin Order sau Confirm có đúng 1 hóa đơn
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var adminOrderRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1m, UnitPrice = price } }
        });
        adminOrderRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var adminOrder = await adminOrderRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);

        var confirmRes = await _client.PostAsJsonAsync($"/api/orders/{adminOrder!.Id}/confirm", new ConfirmOrderRequestDto());
        confirmRes.StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var invCount = await ctx.SalesInvoices.CountAsync(s => s.TenantId == tenantId && s.OrderId == adminOrder.Id);
            invCount.Should().Be(1, "Admin order sau confirm phải sinh đúng 1 hóa đơn");
        }

        // 3. Rollback test: Tạo trigger tạm chỉ trong test làm INSERT sales_invoices lỗi
        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(@"
                CREATE OR REPLACE FUNCTION public.oisms_test_fail_invoice() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Simulated invoice insertion failure for AC-02' USING ERRCODE='23514';
                END $$;
                DROP TRIGGER IF EXISTS oisms_test_fail_invoice_trg ON public.sales_invoices;
                CREATE TRIGGER oisms_test_fail_invoice_trg BEFORE INSERT ON public.sales_invoices
                FOR EACH ROW EXECUTE FUNCTION public.oisms_test_fail_invoice();
            ", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        try
        {
            // 3a. POS Checkout với trigger lỗi: Phải rollback hoàn toàn (không đơn, không payment, không OUT, tồn không đổi)
            decimal onHandBefore;
            await using (var ctx = CreatePostgresDbContext(tenantId).Context)
            {
                var bal = await ctx.InventoryBalances.FirstAsync(b => b.TenantId == tenantId && b.BranchId == branchId && b.SkuId == skuId);
                onHandBefore = bal.OnHand;
            }

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
            var failingKey = Guid.NewGuid();
            var failingPosRes = await _client.PostAsJsonAsync("/api/pos/checkout", new PosCheckoutRequestDto
            {
                BranchId = branchId,
                RequestKey = failingKey,
                PaymentMethod = "Cash",
                AmountGiven = 1000000m,
                Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 3m, UnitPrice = price } }
            });

            failingPosRes.IsSuccessStatusCode.Should().BeFalse("POS checkout phải thất bại khi INSERT sales_invoices bị lỗi");

            await using (var ctx = CreatePostgresDbContext(tenantId).Context)
            {
                var order = await ctx.Orders.FirstOrDefaultAsync(o => o.TenantId == tenantId && o.RequestKey == failingKey);
                order.Should().BeNull("Không được lưu đơn hàng vào DB");

                var bal = await ctx.InventoryBalances.FirstAsync(b => b.TenantId == tenantId && b.BranchId == branchId && b.SkuId == skuId);
                bal.OnHand.Should().Be(onHandBefore, "Tồn kho không được thay đổi khi POS lỗi");
            }

            // 3b. Confirm với trigger lỗi: Đơn vẫn ở trạng thái Reserved, không OUT
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
            var reservedOrderRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
            {
                BranchId = branchId,
                Channel = "Admin",
                RequestKey = Guid.NewGuid(),
                Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1m, UnitPrice = price } }
            });
            var reservedOrder = await reservedOrderRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);

            var failingConfirmRes = await _client.PostAsJsonAsync($"/api/orders/{reservedOrder!.Id}/confirm", new ConfirmOrderRequestDto());
            failingConfirmRes.IsSuccessStatusCode.Should().BeFalse();

            await using (var ctx = CreatePostgresDbContext(tenantId).Context)
            {
                var dbOrder = await ctx.Orders.FirstAsync(o => o.TenantId == tenantId && o.Id == reservedOrder.Id);
                dbOrder.Status.Should().Be("Reserved", "Order phải giữ nguyên trạng thái Reserved");

                var matchingOut = await ctx.InventoryTransactions
                    .AnyAsync(t => t.TenantId == tenantId && ctx.OrderItems.Any(i => i.OrderId == reservedOrder.Id && i.Id == t.OrderItemId));
                matchingOut.Should().BeFalse("Không được sinh dòng OUT khi Confirm lỗi");
            }
        }
        finally
        {
            // Tháo bỏ trigger test
            await using var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString);
            await conn.OpenAsync();
            await using var dropCmd = new NpgsqlCommand(@"
                DROP TRIGGER IF EXISTS oisms_test_fail_invoice_trg ON public.sales_invoices;
                DROP FUNCTION IF EXISTS public.oisms_test_fail_invoice();
            ", conn);
            await dropCmd.ExecuteNonQueryAsync();
        }
    }

    // =========================================================================
    // AC-04: GHI NHẬN CONFIRMED_BY VÀ DISPATCHED_BY TỪ JWT
    // =========================================================================
    [Fact]
    public async Task AC04_ConfirmedByAndDispatchedBy_AreRecordedFromJwtUser()
    {
        await SeedDatabaseAsync();
        var staffToken = await LoginAsAsync("staff@shopa.local", PostgresTestEnvironment.AppPassword);
        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, staffUserId, cashierUserId, skuId;
        string skuCode;
        decimal price;

        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            var wh = await ctx.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branch.Id);
            warehouseId = wh.Id;
            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            supplierId = supplier.Id;
            var staff = await ctx.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "staff@shopa.local");
            staffUserId = staff.Id;
            var cashier = await ctx.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "cashier@shopa.local");
            cashierUserId = cashier.Id;
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = sku.Id;
            skuCode = sku.SKU;
            price = sku.RetailPrice;
        }

        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, staffUserId, skuId, 500m, 50000m);

        // 1. Confirm đơn online bằng tài khoản Staff -> orders.confirmed_by = staffUserId
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var orderRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1m, UnitPrice = price } }
        });
        var orderData = await orderRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);

        var confirmRes = await _client.PostAsJsonAsync($"/api/orders/{orderData!.Id}/confirm", new ConfirmOrderRequestDto());
        confirmRes.StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var dbOrder = await ctx.Orders.FirstAsync(o => o.TenantId == tenantId && o.Id == orderData.Id);
            dbOrder.ConfirmedBy.Should().Be(staffUserId, "orders.confirmed_by phải là user trong JWT");
        }

        // 2. POS Checkout bằng tài khoản Cashier -> orders.confirmed_by = cashierUserId, invoice.issued_by = cashierUserId
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        var posKey = Guid.NewGuid();
        var posRes = await _client.PostAsJsonAsync("/api/pos/checkout", new PosCheckoutRequestDto
        {
            BranchId = branchId,
            RequestKey = posKey,
            PaymentMethod = "Cash",
            AmountGiven = 1000000m,
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1m, UnitPrice = price } }
        });
        posRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var posData = await posRes.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);

        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var dbOrder = await ctx.Orders.FirstAsync(o => o.TenantId == tenantId && o.Id == posData!.OrderId);
            dbOrder.ConfirmedBy.Should().Be(cashierUserId, "POS orders.confirmed_by phải là thu ngân");

            var dbInvoice = await ctx.SalesInvoices.FirstAsync(s => s.TenantId == tenantId && s.OrderId == posData!.OrderId);
            dbInvoice.IssuedBy.Should().Be(cashierUserId, "sales_invoices.issued_by phải là thu ngân");
        }

        // 3. Dispatch chuyển kho bằng tài khoản Staff -> stock_transfers.dispatched_by = staffUserId
        Guid toBranchId, toWhId;
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var toBranch = await ctx.Branches.FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Name == "Chi nhánh Quận 3");
            if (toBranch == null)
            {
                toBranch = new Branch
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Name = "Chi nhánh Quận 3",
                    Address = "456 CMT8, Quận 3, TP.HCM",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                ctx.Branches.Add(toBranch);
            }
            toBranchId = toBranch.Id;

            var toWh = await ctx.Warehouses.FirstOrDefaultAsync(w => w.TenantId == tenantId && w.BranchId == toBranch.Id);
            if (toWh == null)
            {
                toWh = new Warehouse
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    BranchId = toBranch.Id,
                    Name = "Kho Tổng Quận 3",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                ctx.Warehouses.Add(toWh);
            }
            toWhId = toWh.Id;
            await ctx.SaveChangesAsync();
        }

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var transferReq = new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = branchId,
            ToBranchId = toBranchId,
            FromWarehouseId = warehouseId,
            ToWarehouseId = toWhId,
            Note = "Chuyển kho AC-04",
            Items = new List<StockTransferItemRequestDto> { new() { SkuId = skuId, Quantity = 2m } }
        };
        var createTransferRes = await _client.PostAsJsonAsync("/api/stock-transfers", transferReq);
        createTransferRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var transferData = await createTransferRes.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);

        var dispatchRes = await _client.PostAsJsonAsync($"/api/stock-transfers/{transferData!.Id}/dispatch", new DispatchStockTransferRequestDto());
        dispatchRes.StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var dbTransfer = await ctx.StockTransfers.FirstAsync(t => t.TenantId == tenantId && t.Id == transferData.Id);
            dbTransfer.DispatchedBy.Should().Be(staffUserId, "stock_transfers.dispatched_by phải là user xuất chuyển");
        }
    }

    // =========================================================================
    // AC-05: APPEND-ONLY, BẢO TOÀN SNAPSHOT KHI IN LẠI & ĐƠN CŨ HOÀN TẤT
    // =========================================================================
    [Fact]
    public async Task AC05_AppendOnlyBlocked_SnapshotImmutableOnReprint_AndLegacyOrderCompletesGracefully()
    {
        await SeedDatabaseAsync();
        var staffToken = await LoginAsAsync("staff@shopa.local", PostgresTestEnvironment.AppPassword);
        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, cashierUserId, skuId, orderId, invoiceId;
        string invoiceNumber, originalBranchName, originalSkuName, skuCode;
        decimal price;

        // 1. Tạo 1 đơn POS thành công
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            originalBranchName = branch.Name;
            var wh = await ctx.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branch.Id);
            warehouseId = wh.Id;
            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            supplierId = supplier.Id;
            var user = await ctx.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "cashier@shopa.local");
            cashierUserId = user.Id;
            var sku = await ctx.ProductVariants.Include(v => v.Product).FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = sku.Id;
            skuCode = sku.SKU;
            price = sku.RetailPrice;
            originalSkuName = sku.Product?.Name ?? sku.SKU;
        }

        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, cashierUserId, skuId, 100m, 50000m);

        var posRes = await _client.PostAsJsonAsync("/api/pos/checkout", new PosCheckoutRequestDto
        {
            BranchId = branchId,
            RequestKey = Guid.NewGuid(),
            PaymentMethod = "Cash",
            AmountGiven = 1000000m,
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1m, UnitPrice = price } }
        });
        posRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var posResult = await posRes.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);
        orderId = posResult!.OrderId;
        invoiceNumber = posResult.Receipt.ReceiptNumber;

        string snapshotSkuName;
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var inv = await ctx.SalesInvoices.FirstAsync(s => s.TenantId == tenantId && s.OrderId == orderId);
            invoiceId = inv.Id;
            var item = await ctx.OrderItems.FirstAsync(i => i.OrderId == orderId);
            snapshotSkuName = item.SkuNameSnapshot;
        }

        // 2. Dưới tài khoản oisms_runtime: UPDATE / DELETE trên sales_invoices phải bị chặn
        await using (var runtimeConn = new NpgsqlConnection(PostgresTestEnvironment.RuntimeConnectionString))
        {
            await runtimeConn.OpenAsync();

            // 2a. UPDATE sales_invoices bị chặn
            Func<Task> actUpdateInvoice = async () =>
            {
                await using var cmd = new NpgsqlCommand("UPDATE public.sales_invoices SET total_amount = 0 WHERE id = @id;", runtimeConn);
                cmd.Parameters.AddWithValue("id", invoiceId);
                await cmd.ExecuteNonQueryAsync();
            };
            await actUpdateInvoice.Should().ThrowAsync<PostgresException>("Tài khoản runtime không có quyền UPDATE sales_invoices");

            // 2b. DELETE sales_invoices bị chặn
            Func<Task> actDeleteInvoice = async () =>
            {
                await using var cmd = new NpgsqlCommand("DELETE FROM public.sales_invoices WHERE id = @id;", runtimeConn);
                cmd.Parameters.AddWithValue("id", invoiceId);
                await cmd.ExecuteNonQueryAsync();
            };
            await actDeleteInvoice.Should().ThrowAsync<PostgresException>("Tài khoản runtime không có quyền DELETE sales_invoices");

            // 2c. Đổi confirmed_by trên đơn đã xác nhận bị trigger chặn
            Func<Task> actUpdateConfirmedBy = async () =>
            {
                await using var cmd = new NpgsqlCommand("UPDATE public.orders SET confirmed_by = gen_random_uuid() WHERE id = @id;", runtimeConn);
                cmd.Parameters.AddWithValue("id", orderId);
                await cmd.ExecuteNonQueryAsync();
            };
            await actUpdateConfirmedBy.Should().ThrowAsync<PostgresException>("Trigger oisms_protect_order chặn đổi confirmed_by");

            // 2d. Đổi sku_name_snapshot trên order_items của đơn đã xác nhận bị trigger chặn
            Func<Task> actUpdateSkuName = async () =>
            {
                await using var cmd = new NpgsqlCommand("UPDATE public.order_items SET sku_name_snapshot = 'Hàng mới' WHERE order_id = @id;", runtimeConn);
                cmd.Parameters.AddWithValue("id", orderId);
                await cmd.ExecuteNonQueryAsync();
            };
            await actUpdateSkuName.Should().ThrowAsync<PostgresException>("Trigger oisms_protect_order_item chặn đổi sku_name_snapshot");
        }

        // 3. Đổi tên SKU, Chi nhánh trong bảng danh mục, rồi in lại: Vẫn giữ dữ liệu snapshot cũ
        await using (var adminConn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await adminConn.OpenAsync();
            await using var cmd = new NpgsqlCommand(@"
                UPDATE public.branches SET name = 'Chi nhánh Đổi Tên AC-05' WHERE id = @bId;
                UPDATE public.products SET name = 'Sản phẩm Đổi Tên AC-05' WHERE id = (SELECT product_id FROM public.skus WHERE id = @sId);
            ", adminConn);
            cmd.Parameters.AddWithValue("bId", branchId);
            cmd.Parameters.AddWithValue("sId", skuId);
            await cmd.ExecuteNonQueryAsync();
        }

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        var reprintRes = await _client.GetAsync($"/api/sales-invoices/{invoiceId}/receipt");
        reprintRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var reprintData = await reprintRes.Content.ReadFromJsonAsync<SalesInvoiceReceiptDto>(_jsonOptions);

        reprintData!.BranchName.Should().Be(originalBranchName, "Tên chi nhánh trên hóa đơn in lại phải lấy từ snapshot");
        reprintData.Items[0].ProductName.Should().Be(snapshotSkuName, "Tên sản phẩm trên hóa đơn in lại phải lấy từ snapshot order_items");
        reprintData.Items[0].ProductName.Should().NotContain("Đổi Tên AC-05");

        // 4. Đơn cũ trước nâng cấp (không có hóa đơn): Vẫn Complete được, Trace trả salesInvoice = null, không 500
        Guid legacyOrderId = Guid.NewGuid();
        await using (var adminConn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await adminConn.OpenAsync();
            await using var tx = await adminConn.BeginTransactionAsync();
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO public.orders (id, tenant_id, number, branch_id, channel, status, total_amount, request_key, created_at, confirmed_at)
                VALUES (@oId, @tId, 'ORD-LEGACY-01', @bId, 'Admin', 'Confirmed', 100000, gen_random_uuid(), NOW(), NOW());

                INSERT INTO public.order_items (id, tenant_id, order_id, sku_id, sku_code_snapshot, sku_name_snapshot, quantity, unit_price, cost_price, created_at)
                VALUES (gen_random_uuid(), @tId, @oId, @sId, 'SKU-LEGACY', 'Sản phẩm cũ', 1, 100000, 50000, NOW());
            ", adminConn, tx);
            cmd.Parameters.AddWithValue("oId", legacyOrderId);
            cmd.Parameters.AddWithValue("tId", tenantId);
            cmd.Parameters.AddWithValue("bId", branchId);
            cmd.Parameters.AddWithValue("sId", skuId);
            await cmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var completeRes = await _client.PostAsJsonAsync($"/api/orders/{legacyOrderId}/complete", new CompleteOrderRequestDto());
        completeRes.StatusCode.Should().Be(HttpStatusCode.OK, "Đơn cũ Confirmed vẫn Complete thành công");

        var traceRes = await _client.GetAsync($"/api/orders/{legacyOrderId}/trace");
        traceRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var traceData = await traceRes.Content.ReadFromJsonAsync<DocumentTraceDto>(_jsonOptions);
        traceData.Should().NotBeNull();
        traceData!.SalesInvoice.Should().BeNull("Đơn cũ trước nâng cấp trace trả salesInvoice null");
        traceData.Source.Number.Should().Be("ORD-LEGACY-01");
    }

    // =========================================================================
    // AC-06: ĐỘ CHÍNH XÁC GIÁ VỐN 6 CHỮ SỐ THẬP PHÂN
    // =========================================================================
    [Fact]
    public async Task AC06_AverageCostPrecision_SixDecimalPlaces_MatchedExactly()
    {
        await SeedDatabaseAsync();
        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
        string skuCode;
        decimal price;
        const decimal testAverageCost = 12.345678m;

        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            var wh = await ctx.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branch.Id);
            warehouseId = wh.Id;
            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            supplierId = supplier.Id;
            var user = await ctx.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "cashier@shopa.local");
            userId = user.Id;
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = sku.Id;
            skuCode = sku.SKU;
            price = sku.RetailPrice;
        }

        // Nhập kho với đơn giá = 12.345678m để AverageCost đạt chính xác 12.345678m
        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 100m, testAverageCost);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        var checkoutRes = await _client.PostAsJsonAsync("/api/pos/checkout", new PosCheckoutRequestDto
        {
            BranchId = branchId,
            RequestKey = Guid.NewGuid(),
            PaymentMethod = "Cash",
            AmountGiven = 1000000m,
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2m, UnitPrice = price } }
        });
        checkoutRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var checkoutData = await checkoutRes.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);

        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var orderItem = await ctx.OrderItems.FirstAsync(i => i.OrderId == checkoutData!.OrderId && i.SkuId == skuId);
            orderItem.CostPrice.Should().Be(testAverageCost, "order_items.cost_price phải giữ trọn vẹn 6 số thập phân");

            var outTx = await ctx.InventoryTransactions.FirstAsync(t => t.OrderItemId == orderItem.Id && t.Type == "OUT");
            outTx.UnitCost.Should().Be(testAverageCost, "dòng OUT.unit_cost phải bằng chính xác order_items.cost_price = 12.345678");
        }
    }

    // =========================================================================
    // AC-07: TẤT CẢ ENDPOINT, CÁC LOẠI TRACE, PHÂN QUYỀN VÀ MÃ HÓA GIÁ VỐN
    // =========================================================================
    [Fact]
    public async Task AC07_AllEndpoints_TraceTypes_CashierForbiddenAndDataMasking_TenantIsolation()
    {
        await SeedDatabaseAsync();
        var staffToken = await LoginAsAsync("staff@shopa.local", PostgresTestEnvironment.AppPassword);
        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        var tenantBToken = await LoginAsAsync("owner@shopb.local", PostgresTestEnvironment.AppPassword);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, cashierUserId, skuId;
        string skuCode;
        decimal price;

        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == tenantId);
            branchId = branch.Id;
            var wh = await ctx.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branch.Id);
            warehouseId = wh.Id;
            var supplier = await ctx.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            supplierId = supplier.Id;
            var user = await ctx.Users.FirstAsync(u => u.TenantId == tenantId && u.Email == "cashier@shopa.local");
            cashierUserId = user.Id;
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = sku.Id;
            skuCode = sku.SKU;
            price = sku.RetailPrice;
        }

        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, cashierUserId, skuId, 500m, 50000m);

        // Tạo 1 hóa đơn qua POS
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        var posRes = await _client.PostAsJsonAsync("/api/pos/checkout", new PosCheckoutRequestDto
        {
            BranchId = branchId,
            RequestKey = Guid.NewGuid(),
            PaymentMethod = "Cash",
            AmountGiven = 1000000m,
            Items = new List<OrderItemRequestDto> { new() { SkuId = skuId, SkuCode = skuCode, Quantity = 1m, UnitPrice = price } }
        });
        posRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var posData = await posRes.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);
        var invoiceNumber = posData!.Receipt.ReceiptNumber;

        Guid invoiceId;
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var inv = await ctx.SalesInvoices.FirstAsync(s => s.OrderId == posData.OrderId);
            invoiceId = inv.Id;
        }

        // 1. Kiểm tra phân quyền Cashier:
        // - Cashier gọi /receipt: 200 OK
        var cashierReceiptRes1 = await _client.GetAsync($"/api/sales-invoices/{invoiceId}/receipt");
        cashierReceiptRes1.StatusCode.Should().Be(HttpStatusCode.OK);

        var cashierReceiptRes2 = await _client.GetAsync($"/api/sales-invoices/receipt?number={invoiceNumber}");
        cashierReceiptRes2.StatusCode.Should().Be(HttpStatusCode.OK);

        // - JSON receipt không chứa trường giá vốn
        var receiptRawJson = await cashierReceiptRes2.Content.ReadAsStringAsync();
        receiptRawJson.Should().NotContainEquivalentOf("costPrice");
        receiptRawJson.Should().NotContainEquivalentOf("unitCost");
        receiptRawJson.Should().NotContainEquivalentOf("totalCogs");

        // - Cashier gọi list /api/sales-invoices hoặc trace: 403 Forbidden
        var cashierListRes = await _client.GetAsync("/api/sales-invoices");
        cashierListRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var cashierDetailRes = await _client.GetAsync($"/api/sales-invoices/{invoiceId}");
        cashierDetailRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var cashierTraceRes = await _client.GetAsync($"/api/sales-invoices/{invoiceId}/trace");
        cashierTraceRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var cashierOrderTraceRes = await _client.GetAsync($"/api/orders/{posData.OrderId}/trace");
        cashierOrderTraceRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 2. Staff gọi list và trace: 200 OK
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var staffListRes = await _client.GetAsync("/api/sales-invoices");
        staffListRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var staffDetailRes = await _client.GetAsync($"/api/sales-invoices/{invoiceId}");
        staffDetailRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var staffTraceRes = await _client.GetAsync($"/api/sales-invoices/{invoiceId}/trace");
        staffTraceRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var staffTraceData = await staffTraceRes.Content.ReadFromJsonAsync<DocumentTraceDto>(_jsonOptions);
        staffTraceData!.Source.Type.Should().Be("Order");
        staffTraceData.Lines.Should().NotBeEmpty();

        var orderTraceRes = await _client.GetAsync($"/api/orders/{posData.OrderId}/trace");
        orderTraceRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var ledgerId = staffTraceData.Lines[0].LedgerId;
        var ledgerTraceRes = await _client.GetAsync($"/api/inventory/ledger/{ledgerId}/trace");
        ledgerTraceRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Cô lập Tenant: Tenant B truy vấn hóa đơn của Tenant A -> 404
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenantBToken);
        var crossDetailRes = await _client.GetAsync($"/api/sales-invoices/{invoiceId}");
        crossDetailRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var crossReceiptRes = await _client.GetAsync($"/api/sales-invoices/{invoiceId}/receipt");
        crossReceiptRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var crossTraceRes = await _client.GetAsync($"/api/sales-invoices/{invoiceId}/trace");
        crossTraceRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
