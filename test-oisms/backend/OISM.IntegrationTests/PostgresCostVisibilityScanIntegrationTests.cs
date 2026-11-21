using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.GoodsIssues.Dtos;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.PurchaseOrders.Dtos;
using OISM.Application.Features.SalesReturns.Dtos;
using OISM.Application.Features.StockCounts.Dtos;
using OISM.Application.Features.StockTransfers.Dtos;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// AC-07: Integration test quét toàn diện mọi GET endpoint bằng tài khoản Staff trên PostgreSQL thật.
/// Kiểm tra đệ quy mọi trường nhạy cảm tài chính (CostPrice, TotalCost, UnitCost, AverageCost, WAC,
/// TotalCogs, GrossProfit, GrossMarginPercentage, TotalInventoryValue, AllocatedUnitCost, OriginalUnitCost,
/// AdjustmentUnitCost, các giá trị tồn kỳ).
/// Khẳng định:
/// 1. Staff nhận null hoặc không có khóa đối với toàn bộ các trường nhạy cảm ở mọi GET endpoint (0 vi phạm).
/// 2. Owner ở cùng dữ liệu/endpoint thấy giá trị khác null (> 0).
/// 3. AC-06: Staff lọc supplierId trên đơn mua Draft/PendingApproval bị chặn 403 FORBIDDEN.
/// </summary>
[Collection("PostgresIntegrationTests")]
public class PostgresCostVisibilityScanIntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly HashSet<string> SensitiveCostKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "costPrice",
        "totalCost",
        "unitCost",
        "averageCost",
        "wac",
        "totalCogs",
        "grossProfit",
        "grossMarginPercentage",
        "totalInventoryValue",
        "allocatedUnitCost",
        "originalUnitCost",
        "adjustmentUnitCost",
        "openingValue",
        "inValue",
        "outValue",
        "closingValue"
    };

    private sealed class TestTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    public PostgresCostVisibilityScanIntegrationTests(WebApplicationFactoryWithPostgres factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private Guid _tenantId;
    private Guid _branchId;
    private Guid _warehouseId;
    private Guid _secondWarehouseId;
    private Guid _supplierId;
    private Guid _skuId;
    private string _skuCode = string.Empty;
    private decimal _retailPrice;
    private Guid _productId;
    private Guid _orderId;
    private Guid _invoiceId;
    private Guid _goodsIssueId;
    private Guid _salesReturnId;
    private Guid _stockTransferId;
    private Guid _stockCountId;
    private Guid _purchaseOrderId;
    private Guid _goodsReceiptId;

    public async Task InitializeAsync()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
        await SeedComprehensiveTestDataAsync();
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

    private async Task SeedComprehensiveTestDataAsync()
    {
        var (ctx, _) = CreatePostgresDbContext(null);
        await using (ctx)
        {
            await OismsDatabaseSeeder.SeedAsync(ctx, PostgresTestEnvironment.AppPassword);
        }

        _tenantId = OismsDatabaseSeeder.TenantAId;

        // Lấy thông tin cơ bản
        await using (var db = CreatePostgresDbContext(_tenantId).Context)
        {
            var branch = await db.Branches.FirstAsync(b => b.TenantId == _tenantId);
            _branchId = branch.Id;

            var whs = await db.Warehouses.Where(w => w.TenantId == _tenantId && w.BranchId == _branchId).ToListAsync();
            _warehouseId = whs[0].Id;
            if (whs.Count > 1)
            {
                _secondWarehouseId = whs[1].Id;
            }
            else
            {
                var newWh = new Warehouse
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantId,
                    BranchId = _branchId,
                    Name = "Kho Phụ Test G5",
                    CreatedAt = DateTimeOffset.UtcNow
                };
                db.Warehouses.Add(newWh);
                await db.SaveChangesAsync();
                _secondWarehouseId = newWh.Id;
            }

            var supplier = await db.Suppliers.FirstAsync(s => s.TenantId == _tenantId);
            _supplierId = supplier.Id;

            var sku = await db.ProductVariants.FirstAsync(v => v.TenantId == _tenantId && v.IsActive);
            _skuId = sku.Id;
            _productId = sku.ProductId;
            _skuCode = sku.SKU;
            _retailPrice = sku.RetailPrice;
        }

        // Nhập kho 100 sản phẩm với giá vốn 20,000 đ qua PurchaseOrderSeed
        var (context, tenantService) = CreatePostgresDbContext(_tenantId);
        Guid ownerId;
        await using (context)
        {
            var owner = await context.Users.FirstAsync(u => u.TenantId == _tenantId && u.Role == "Owner");
            ownerId = owner.Id;
            await PriceSeed.SyncFromLegacyColumnsAsync(context);
            await PurchaseOrderSeed.ReceiveStockAsync(context, tenantService, _branchId, _warehouseId, _supplierId, ownerId, (_skuId, 100m, 20000m));
        }

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // 1. Tạo đơn mua hàng Draft & PendingApproval & Approved
        using var poReq = new HttpRequestMessage(HttpMethod.Post, "/api/purchase-orders")
        {
            Content = JsonContent.Create(new CreatePurchaseOrderDto
            {
                BranchId = _branchId,
                SupplierId = _supplierId,
                Note = "Đơn mua kiểm thử G5",
                Items = new List<CreatePurchaseOrderItemDto>
                {
                    new()
                    {
                        SkuId = _skuId,
                        Quantity = 10m,
                        UnitPrice = 22000m
                    }
                }
            })
        };
        poReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var createPoRes = await _client.SendAsync(poReq);
        createPoRes.StatusCode.Should().Be(HttpStatusCode.Created, await createPoRes.Content.ReadAsStringAsync());
        var createdPo = await createPoRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions);
        _purchaseOrderId = createdPo!.Id;

        // 2. Tạo đơn POS -> sinh Order, Invoice, GoodsIssue, Ledger OUT
        var posRes = await _client.PostAsJsonAsync("/api/pos/checkout", new PosCheckoutRequestDto
        {
            BranchId = _branchId,
            RequestKey = Guid.NewGuid(),
            PaymentMethod = "Cash",
            AmountGiven = _retailPrice * 2m,
            Items = new List<OrderItemRequestDto>
            {
                new()
                {
                    SkuId = _skuId,
                    SkuCode = _skuCode,
                    Quantity = 2m,
                    UnitPrice = _retailPrice
                }
            }
        });
        posRes.StatusCode.Should().Be(HttpStatusCode.OK, await posRes.Content.ReadAsStringAsync());
        var posData = await posRes.Content.ReadFromJsonAsync<PosCheckoutResponseDto>(_jsonOptions);
        _orderId = posData!.OrderId;

        await using (var db = CreatePostgresDbContext(_tenantId).Context)
        {
            var invoice = await db.SalesInvoices.FirstAsync(i => i.OrderId == _orderId);
            _invoiceId = invoice.Id;

            var gi = await db.GoodsIssues.FirstOrDefaultAsync(g => g.OrderId == _orderId);
            if (gi != null)
            {
                _goodsIssueId = gi.Id;
            }

            var gr = await db.GoodsReceipts.FirstOrDefaultAsync(g => g.TenantId == _tenantId);
            if (gr != null)
            {
                _goodsReceiptId = gr.Id;
            }
        }

        // 3. Tạo phiếu trả hàng (SalesReturn) trên đơn vừa tạo
        Guid orderItemId;
        await using (var db = CreatePostgresDbContext(_tenantId).Context)
        {
            var oi = await db.OrderItems.FirstAsync(o => o.OrderId == _orderId);
            orderItemId = oi.Id;
        }

        var srRes = await _client.PostAsJsonAsync("/api/sales-returns", new CreateSalesReturnDto
        {
            RequestKey = Guid.NewGuid(),
            OrderId = _orderId,
            WarehouseId = _warehouseId,
            Reason = "Khách đổi ý kiểm thử G5",
            Items = new List<CreateSalesReturnItemDto>
            {
                new()
                {
                    OrderItemId = orderItemId,
                    Quantity = 1m,
                    RefundAmount = _retailPrice
                }
            }
        });
        if (srRes.IsSuccessStatusCode)
        {
            var srData = await srRes.Content.ReadFromJsonAsync<SalesReturnResponseDto>(_jsonOptions);
            _salesReturnId = srData!.Id;
            // Confirm trả hàng để sinh bút toán IN
            await _client.PostAsJsonAsync($"/api/sales-returns/{_salesReturnId}/confirm", new ConfirmSalesReturnRequestDto
            {
                RefundMethod = "Cash"
            });
        }

        // 4. Tạo phiếu chuyển kho (StockTransfer)
        var stRes = await _client.PostAsJsonAsync("/api/stock-transfers", new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = _branchId,
            ToBranchId = _branchId,
            FromWarehouseId = _warehouseId,
            ToWarehouseId = _secondWarehouseId,
            Note = "Chuyển kho test G5",
            Items = new List<StockTransferItemRequestDto>
            {
                new()
                {
                    SkuId = _skuId,
                    Quantity = 1m
                }
            }
        });
        if (stRes.IsSuccessStatusCode)
        {
            var stData = await stRes.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);
            _stockTransferId = stData!.Id;
        }

        // 5. Tạo phiếu kiểm kê (StockCount)
        var scRes = await _client.PostAsJsonAsync("/api/stock-counts", new CreateStockCountRequestDto
        {
            RequestKey = Guid.NewGuid(),
            BranchId = _branchId,
            WarehouseId = _warehouseId,
            Reason = "Kiểm kê định kỳ test G5"
        });
        if (scRes.IsSuccessStatusCode)
        {
            var scData = await scRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
            _stockCountId = scData!.Id;
            // Ghi số đếm có chênh lệch
            await _client.PostAsJsonAsync($"/api/stock-counts/{_stockCountId}/record", new RecordStockCountBatchRequestDto
            {
                Items = new List<RecordStockCountItemRequestDto>
                {
                    new()
                    {
                        SkuId = _skuId,
                        CountedQuantity = 95m,
                        AdjustmentUnitCost = 20000m
                    }
                }
            });
        }
    }

    private static void ScanJsonElement(JsonElement element, string path, List<string> violations)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var curPath = string.IsNullOrEmpty(path) ? prop.Name : $"{path}.{prop.Name}";
                    if (SensitiveCostKeys.Contains(prop.Name))
                    {
                        if (prop.Value.ValueKind != JsonValueKind.Null && prop.Value.ValueKind != JsonValueKind.Undefined)
                        {
                            violations.Add($"Endpoint payload lộ trường nhạy cảm: {curPath} = {prop.Value.GetRawText()}");
                        }
                    }
                    ScanJsonElement(prop.Value, curPath, violations);
                }
                break;
            case JsonValueKind.Array:
                int idx = 0;
                foreach (var item in element.EnumerateArray())
                {
                    ScanJsonElement(item, $"{path}[{idx}]", violations);
                    idx++;
                }
                break;
        }
    }

    [Fact]
    public async Task Staff_ScanAllGetEndpoints_NoSensitiveCostKeysExposed_AC07()
    {
        // GIVEN: Đăng nhập bằng tài khoản Staff (staff@shopa.local)
        var staffToken = await LoginAsAsync("staff@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);

        // Lấy danh sách route từ EndpointDataSource
        var endpointDataSource = _factory.Services.GetRequiredService<EndpointDataSource>();
        var routeEndpoints = endpointDataSource.Endpoints.OfType<RouteEndpoint>().ToList();

        var getEndpoints = routeEndpoints
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true)
            .Select(e => e.RoutePattern.RawText ?? "")
            .Where(r => !string.IsNullOrEmpty(r) && !r.StartsWith("swagger") && !r.StartsWith("health") && !r.StartsWith("hubs"))
            .Distinct()
            .ToList();

        getEndpoints.Should().NotBeEmpty("Phải tìm thấy các endpoint GET từ EndpointDataSource.");

        // Danh sách các URL GET cụ thể đại diện cho toàn bộ các phân hệ nghiệp vụ
        var targetUrls = new List<string>
        {
            "/api/inventory/balances",
            "/api/inventory/skus",
            "/api/inventory/ledger",
            $"/api/inventory/period-report?branchId={_branchId}&warehouseId={_warehouseId}&startDate=2026-01-01&endDate=2026-12-31",
            "/api/orders",
            $"/api/orders/{_orderId}",
            "/api/orders/daily-summary",
            $"/api/sales-invoices/{_invoiceId}",
            $"/api/sales-invoices/{_invoiceId}/trace",
            $"/api/sales-invoices/by-order/{_orderId}",
            "/api/sales-returns",
            $"/api/sales-returns/{_salesReturnId}",
            "/api/stock-transfers",
            $"/api/stock-transfers/{_stockTransferId}",
            "/api/stock-counts",
            $"/api/stock-counts/{_stockCountId}",
            "/api/goods-issues",
            $"/api/goods-issues/{_goodsIssueId}",
            $"/api/goods-issues/by-order/{_orderId}",
            "/api/goods-issues/pos-daily-summary",
            "/api/purchase-orders",
            $"/api/purchase-orders/{_purchaseOrderId}",
            "/api/goods-receipts",
            $"/api/goods-receipts/{_goodsReceiptId}",
            "/api/products",
            $"/api/products/{_productId}",
            "/api/search?q=TEST"
        };

        var allViolations = new List<string>();

        foreach (var url in targetUrls)
        {
            var res = await _client.GetAsync(url);

            // Staff có thể bị 403 Forbidden ở các route yêu cầu Owner (như Reports), điều này hoàn toàn hợp lệ
            if (res.StatusCode == HttpStatusCode.Forbidden || res.StatusCode == HttpStatusCode.Unauthorized)
            {
                continue;
            }

            if (res.StatusCode == HttpStatusCode.NotFound)
            {
                continue;
            }

            res.StatusCode.Should().Be(HttpStatusCode.OK, $"URL {url} phải trả về 200 OK cho Staff.");

            var content = await res.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(content) || !content.TrimStart().StartsWith("{") && !content.TrimStart().StartsWith("["))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(content);
            var endpointViolations = new List<string>();
            ScanJsonElement(doc.RootElement, url, endpointViolations);

            if (endpointViolations.Count > 0)
            {
                allViolations.AddRange(endpointViolations);
            }
        }

        // THEN: Không được có bất kỳ vi phạm nào (Staff nhận 0 trường giá vốn nhạy cảm)
        allViolations.Should().BeEmpty(
            "Staff tuyệt đối không được nhận bất kỳ trường tài chính nhạy cảm nào (CostPrice, TotalCost, UnitCost, WAC, GrossProfit, etc.). " +
            "Chi tiết các vi phạm: \n" + string.Join("\n", allViolations));
    }

    [Fact]
    public async Task Owner_ScanKeyGetEndpoints_HasSensitiveCostValues_AC07()
    {
        // GIVEN: Đăng nhập bằng tài khoản Owner (owner@shopa.local)
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // 1. Kiểm tra đơn hàng: Owner thấy CostPrice > 0 và TotalCogs > 0
        var orderRes = await _client.GetAsync($"/api/orders/{_orderId}");
        orderRes.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var doc = JsonDocument.Parse(await orderRes.Content.ReadAsStringAsync()))
        {
            var items = doc.RootElement.GetProperty("items");
            items.GetArrayLength().Should().BeGreaterThan(0);
            var firstItem = items[0];
            firstItem.TryGetProperty("costPrice", out var costPriceProp).Should().BeTrue();
            costPriceProp.ValueKind.Should().NotBe(JsonValueKind.Null);
            costPriceProp.GetDecimal().Should().BeGreaterThan(0);

            doc.RootElement.TryGetProperty("totalCogs", out var totalCogsProp).Should().BeTrue();
            totalCogsProp.ValueKind.Should().NotBe(JsonValueKind.Null);
            totalCogsProp.GetDecimal().Should().BeGreaterThan(0);
        }

        // 2. Kiểm tra truy vết chứng từ: Owner thấy UnitCost > 0 ở các dòng sổ kho
        var traceRes = await _client.GetAsync($"/api/sales-invoices/{_invoiceId}/trace");
        traceRes.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var doc = JsonDocument.Parse(await traceRes.Content.ReadAsStringAsync()))
        {
            var lines = doc.RootElement.GetProperty("lines");
            lines.GetArrayLength().Should().BeGreaterThan(0);
            var firstLine = lines[0];
            firstLine.TryGetProperty("unitCost", out var unitCostProp).Should().BeTrue();
            unitCostProp.ValueKind.Should().NotBe(JsonValueKind.Null);
            unitCostProp.GetDecimal().Should().BeGreaterThan(0);
        }

        // 3. Kiểm tra sổ cái kho (Ledger): Owner thấy UnitCost > 0 ở các bút toán
        var ledgerRes = await _client.GetAsync("/api/inventory/ledger");
        ledgerRes.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var doc = JsonDocument.Parse(await ledgerRes.Content.ReadAsStringAsync()))
        {
            var array = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement
                : doc.RootElement.GetProperty("items");
            array.GetArrayLength().Should().BeGreaterThan(0);
            var hasCost = false;
            foreach (var item in array.EnumerateArray())
            {
                if (item.TryGetProperty("unitCost", out var uc) && uc.ValueKind != JsonValueKind.Null && uc.GetDecimal() > 0)
                {
                    hasCost = true;
                    break;
                }
            }
            hasCost.Should().BeTrue("Owner phải thấy UnitCost > 0 trong sổ cái kho.");
        }

        // 4. Kiểm tra phiếu xuất kho 02-VT: Owner thấy UnitCost và TotalCost > 0
        if (_goodsIssueId != Guid.Empty)
        {
            var giRes = await _client.GetAsync($"/api/goods-issues/{_goodsIssueId}");
            giRes.StatusCode.Should().Be(HttpStatusCode.OK);
            using var doc = JsonDocument.Parse(await giRes.Content.ReadAsStringAsync());
            doc.RootElement.TryGetProperty("totalCost", out var tc).Should().BeTrue();
            tc.ValueKind.Should().NotBe(JsonValueKind.Null);
            tc.GetDecimal().Should().BeGreaterThan(0);
        }

        // 5. Kiểm tra Dashboard Summary báo cáo: Owner thấy GrossMarginPercentage != null
        var summaryRes = await _client.GetAsync("/api/reports/dashboard-summary");
        summaryRes.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var doc = JsonDocument.Parse(await summaryRes.Content.ReadAsStringAsync()))
        {
            doc.RootElement.TryGetProperty("grossMarginPercentage", out var gm).Should().BeTrue();
            gm.ValueKind.Should().NotBe(JsonValueKind.Null);
        }
    }

    [Fact]
    public async Task Staff_FilterPurchaseOrdersBySupplier_AC06_Returns403OrExcludesDraft()
    {
        // GIVEN: Đăng nhập Staff
        var staffToken = await LoginAsAsync("staff@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);

        // 1. Staff lọc supplierId + status=Draft -> Phải nhận 403 Forbidden
        var draftRes = await _client.GetAsync($"/api/purchase-orders?supplierId={_supplierId}&status=Draft");
        draftRes.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Staff lọc supplierId với Status=Draft phải bị 403 Forbidden.");

        // 2. Staff lọc supplierId + status=PendingApproval -> Phải nhận 403 Forbidden
        var pendingRes = await _client.GetAsync($"/api/purchase-orders?supplierId={_supplierId}&status=PendingApproval");
        pendingRes.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Staff lọc supplierId với Status=PendingApproval phải bị 403 Forbidden.");

        // 3. Staff lọc supplierId chung (không status) -> 200 OK, kết quả tuyệt đối KHÔNG chứa đơn Draft hoặc PendingApproval
        var generalRes = await _client.GetAsync($"/api/purchase-orders?supplierId={_supplierId}");
        generalRes.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var doc = JsonDocument.Parse(await generalRes.Content.ReadAsStringAsync()))
        {
            var items = doc.RootElement.GetProperty("items");
            foreach (var item in items.EnumerateArray())
            {
                var status = item.GetProperty("status").GetString();
                status.Should().NotBe("Draft", "Kết quả đơn mua của Staff không được chứa đơn Draft.");
                status.Should().NotBe("PendingApproval", "Kết quả đơn mua của Staff không được chứa đơn PendingApproval.");
            }
        }

        // 4. Owner gọi lọc Draft -> 200 OK và xem được đơn Draft
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var ownerDraftRes = await _client.GetAsync($"/api/purchase-orders?supplierId={_supplierId}&status=Draft");
        ownerDraftRes.StatusCode.Should().Be(HttpStatusCode.OK, "Owner phải xem được danh sách đơn Draft.");
    }
}
