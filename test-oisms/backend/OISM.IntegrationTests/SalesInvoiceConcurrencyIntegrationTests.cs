using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Orders.Dtos;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// AC-03: Kiểm thử đồng thời cho POS checkout và Order Confirm (PostgreSQL thật).
/// Được thiết kế riêng biệt để kiểm tra cổng GATE: Chạy 3 lần liên tiếp đạt 100% PASS.
/// </summary>
[Collection("PostgresIntegrationTests")]
public class SalesInvoiceConcurrencyIntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class TestTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    public SalesInvoiceConcurrencyIntegrationTests(WebApplicationFactoryWithPostgres factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

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

    [Fact]
    public async Task AC03_ConcurrentPosCheckout_20Threads_SameRequestKey_MustProduceExactlyOneInvoiceAndNoDuplicateOut()
    {
        await SeedDatabaseAsync();
        var token = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
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
            userId = user.Id;
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == tenantId && v.IsActive);
            skuId = sku.Id;
            skuCode = sku.SKU;
            price = sku.RetailPrice;
        }

        // Bơm tồn kho 1000 qua luồng nhập chuẩn để toàn vẹn số cái và quầy POS không bị thiếu hàng
        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, userId, skuId, 1000m, 50000m);

        var requestKey = Guid.NewGuid();
        var checkoutDto = new PosCheckoutRequestDto
        {
            BranchId = branchId,
            RequestKey = requestKey,
            PaymentMethod = "Cash",
            AmountGiven = 1000000m,
            CustomerName = "Khách đồng thời",
            CustomerPhone = "0909999888",
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2m, UnitPrice = price }
            }
        };

        const int concurrencyLevel = 20;
        var tasks = new Task<HttpResponseMessage>[concurrencyLevel];

        for (int i = 0; i < concurrencyLevel; i++)
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/pos/checkout")
            {
                Content = JsonContent.Create(checkoutDto)
            };
            req.Headers.Add("Idempotency-Key", requestKey.ToString());
            tasks[i] = client.SendAsync(req);
        }

        var responses = await Task.WhenAll(tasks);

        // Mọi phản hồi thành công (200 OK hoặc 201 Created)
        var receiptNumbers = new List<string>();
        foreach (var resp in responses)
        {
            var content = await resp.Content.ReadAsStringAsync();
            (resp.StatusCode == HttpStatusCode.OK || resp.StatusCode == HttpStatusCode.Created)
                .Should().BeTrue($"Mọi phản hồi đồng thời với cùng RequestKey phải thành công, but got {resp.StatusCode}: {content}");
            var result = JsonSerializer.Deserialize<PosCheckoutResponseDto>(content, _jsonOptions);
            result.Should().NotBeNull();
            result!.Receipt.Should().NotBeNull();
            receiptNumbers.Add(result.Receipt.ReceiptNumber);
        }

        // Mọi phản hồi phải trả về cùng một ReceiptNumber (chính là số hóa đơn)
        var distinctReceipts = receiptNumbers.Distinct().ToList();
        distinctReceipts.Should().HaveCount(1, "Mọi phản hồi checkout đồng thời phải trả cùng số ReceiptNumber");
        distinctReceipts[0].Should().StartWith("INV-");

        // Kiểm tra trong CSDL: Chỉ có đúng 1 đơn hàng, 1 hóa đơn, 1 thanh toán, 1 dòng OUT
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var orders = await ctx.Orders.Where(o => o.TenantId == tenantId && o.RequestKey == requestKey).ToListAsync();
            orders.Should().HaveCount(1, "Chỉ tạo duy nhất 1 đơn hàng");
            var order = orders[0];

            var invoices = await ctx.SalesInvoices.Where(s => s.TenantId == tenantId && s.OrderId == order.Id).ToListAsync();
            invoices.Should().HaveCount(1, "Chỉ tạo duy nhất 1 hóa đơn bán hàng");
            invoices[0].Number.Should().Be(distinctReceipts[0]);

            var payments = await ctx.Payments.Where(p => p.TenantId == tenantId && p.OrderId == order.Id).ToListAsync();
            payments.Should().HaveCount(1, "Chỉ có 1 bản ghi thanh toán");

            var orderItem = await ctx.OrderItems.FirstAsync(i => i.OrderId == order.Id);
            var outTransactions = await ctx.InventoryTransactions
                .Where(t => t.TenantId == tenantId && t.OrderItemId == orderItem.Id && t.Type == "OUT")
                .ToListAsync();
            outTransactions.Should().HaveCount(1, "OUT không bị nhân đôi");
            outTransactions[0].Quantity.Should().Be(2m);

            var bBal = await ctx.InventoryBalances.FirstAsync(b => b.TenantId == tenantId && b.BranchId == branchId && b.SkuId == skuId);
            bBal.OnHand.Should().Be(998m, "Tồn kho chỉ bị trừ đúng 1 lần");
        }
    }

    [Fact]
    public async Task AC03_ConcurrentOrderConfirm_20Threads_OneOrder_ExactlyOneSucceeds_Rest409_No500_ExactlyOneInvoice()
    {
        await SeedDatabaseAsync();
        var token = await LoginAsAsync("staff@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

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

        // Bơm tồn kho 100 chiếc qua quy trình chuẩn
        await SeedStockAsync(tenantId, branchId, warehouseId, supplierId, staffUserId, skuId, 100m, 50000m);

        // Tạo 1 đơn online ở trạng thái Reserved qua API chuẩn
        var createOrderRes = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = branchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = skuId, SkuCode = skuCode, Quantity = 2m, UnitPrice = price }
            }
        });
        createOrderRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdOrder = await createOrderRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
        var orderId = createdOrder!.Id;

        const int concurrencyLevel = 20;
        var tasks = new Task<HttpResponseMessage>[concurrencyLevel];

        for (int i = 0; i < concurrencyLevel; i++)
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            tasks[i] = client.PostAsJsonAsync($"/api/orders/{orderId}/confirm", new ConfirmOrderRequestDto());
        }

        var responses = await Task.WhenAll(tasks);

        int countSuccess = 0;
        int countConflict = 0;
        int countOther = 0;

        foreach (var r in responses)
        {
            if (r.StatusCode == HttpStatusCode.OK)
            {
                countSuccess++;
            }
            else if (r.StatusCode == HttpStatusCode.Conflict)
            {
                countConflict++;
            }
            else
            {
                countOther++;
            }
        }

        countOther.Should().Be(0, "Tuyệt đối không có mã lỗi 500 hay lỗi khác ngoài 200 và 409");
        countSuccess.Should().Be(1, "Chính xác 1 request Confirm thành công");
        countConflict.Should().Be(19, "19 request còn lại phải nhận 409 Conflict");

        // Kiểm tra trong CSDL: Chỉ có đúng 1 hóa đơn
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var invoices = await ctx.SalesInvoices.Where(s => s.TenantId == tenantId && s.OrderId == orderId).ToListAsync();
            invoices.Should().HaveCount(1, "Chỉ tạo duy nhất 1 hóa đơn cho đơn hàng");

            var order = await ctx.Orders.FirstAsync(o => o.Id == orderId);
            order.Status.Should().Be("Confirmed");
            order.ConfirmedBy.Should().NotBeNull();

            var outTx = await ctx.InventoryTransactions.Where(t => t.TenantId == tenantId && t.Type == "OUT").ToListAsync();
            outTx.Where(t => t.OrderItemId != null).Should().HaveCount(1, "Chỉ có đúng 1 dòng OUT được ghi");
        }
    }
}
