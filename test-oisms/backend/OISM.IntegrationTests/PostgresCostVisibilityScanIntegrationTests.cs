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
        "closingValue",
        "openingInTransitValue",
        "closingInTransitValue",
        "transferInValue",
        "transferOutValue",
        "adjustmentInValue",
        "adjustmentOutValue",
        "purchaseValue",
        "returnValue",
        "posValue",
        "ecommerceValue",
        "adminValue",
        "balanceAfterValue",
        "totalOpeningValue",
        "totalClosingValue",
        "totalInValue",
        "totalOutValue",
        "totalOpeningInTransitValue",
        "totalClosingInTransitValue",
        "totalGrossProfit",
        "costOfGoodsSold",
        "totalCostOfGoodsSold",
        "returnCogs",
        "netCogs",
        "currentWac",
        "currentBaseWac",
        "currentUnitWac",
        "projectedUnitWac",
        "currentMarginPercent",
        "marginPercent",
        "currentTheoreticalValue",
        "totalAllocatedCost"
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
    private Guid _adjustmentId;

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
            await _client.PostAsJsonAsync($"/api/stock-counts/{_stockCountId}/submit", new SubmitStockCountRequestDto());
            var confirmScRes = await _client.PostAsJsonAsync($"/api/stock-counts/{_stockCountId}/confirm", new ConfirmStockCountRequestDto { Note = "Duyệt kiểm kê test G5" });
            if (confirmScRes.IsSuccessStatusCode)
            {
                var confirmedSc = await confirmScRes.Content.ReadFromJsonAsync<StockCountResponseDto>(_jsonOptions);
                _adjustmentId = confirmedSc?.GeneratedAdjustmentId ?? Guid.Empty;
            }
        }
    }

    private static void ScanJsonElement(JsonElement element, string path, List<string> violations, bool isPurchaseDocument = false)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var curPath = string.IsNullOrEmpty(path) ? prop.Name : $"{path}.{prop.Name}";
                    if (SensitiveCostKeys.Contains(prop.Name) || (isPurchaseDocument && PurchasePriceKeys.Contains(prop.Name)))
                    {
                        if (prop.Value.ValueKind != JsonValueKind.Null && prop.Value.ValueKind != JsonValueKind.Undefined)
                        {
                            violations.Add($"Endpoint payload lộ trường nhạy cảm: {curPath} = {prop.Value.GetRawText()}");
                        }
                    }
                    ScanJsonElement(prop.Value, curPath, violations, isPurchaseDocument);
                }
                break;
            case JsonValueKind.Array:
                int idx = 0;
                foreach (var item in element.EnumerateArray())
                {
                    ScanJsonElement(item, $"{path}[{idx}]", violations, isPurchaseDocument);
                    idx++;
                }
                break;
        }
    }

    /// <summary>
    /// Trường giá mua chỉ nhạy cảm trên đơn mua và phiếu nhập (ở đơn bán, unitPrice là giá bán, Staff được xem).
    /// </summary>
    private static readonly HashSet<string> PurchasePriceKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "unitPrice", "lineTotal", "allocatedLineTotal", "subtotal", "shippingFee", "discountAmount", "vatAmount",
        "totalAmount", "suggestedUnitPrice", "lastAllocatedUnitCost"
    };

    /// <summary>Route GET không trả JSON nghiệp vụ hoặc không dùng JWT, kèm lý do (AC-07: ngoại lệ phải ghi lý do).</summary>
    private static readonly Dictionary<string, string> ExcludedGetRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["api/webhooks/simulator/whoami"] = "Xác thực bằng khóa simulator, không dùng JWT Staff; chỉ trả tên tenant",
        ["api/webhooks/simulator/catalog"] = "Xác thực bằng khóa simulator, không dùng JWT Staff; chỉ trả giá bán niêm yết",
        ["api/reports/inventory-period/export-excel"] = "Trả file .xlsx, kiểm riêng ở Staff_ExcelExports_HaveNoValueColumns_AC03",
        ["api/reports/stock-card/export-excel"] = "Trả file .xlsx, kiểm riêng ở Staff_ExcelExports_HaveNoValueColumns_AC03",
    };

    /// <summary>Route chỉ Owner được gọi: Staff phải nhận 403.</summary>
    private static readonly HashSet<string> OwnerOnlyGetRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "api/payment-accounts",
        "api/purchase-orders/price-suggestion",
        "api/reports/daily-summaries",
        "api/shop-integrations",
        "api/shop-integrations/{id:guid}",
        "api/users",
        "api/users/{id:guid}",
    };

    private static string NormalizeRoute(string raw) => raw.Trim().TrimStart('/');

    private async Task<Dictionary<string, string>> BuildGetRouteUrlsAsync()
    {
        string barcode;
        Guid ledgerId, unitId, skuUnitId, priceChangeRequestId, userId;
        Guid? shopIntegrationId;
        string invoiceNumber;
        await using (var db = CreatePostgresDbContext(_tenantId).Context)
        {
            var sku = await db.ProductVariants.FirstAsync(v => v.Id == _skuId);
            barcode = sku.Barcode ?? sku.SKU;
            // Dòng OUT bán hàng của đơn POS (có giá vốn) để quét truy vết sổ kho
            var orderItemIds = await db.OrderItems.Where(i => i.OrderId == _orderId).Select(i => i.Id).ToListAsync();
            ledgerId = await db.InventoryTransactions
                .Where(t => t.OrderItemId != null && orderItemIds.Contains(t.OrderItemId.Value))
                .Select(t => t.Id)
                .FirstAsync();
            var skuUnit = await db.SkuUnits.FirstAsync(u => u.SkuId == _skuId);
            unitId = skuUnit.UnitId;
            skuUnitId = skuUnit.Id;
            priceChangeRequestId = await db.PriceChangeRequests.Select(r => r.Id).FirstAsync();
            userId = await db.Users.Where(u => u.Role == "Staff").Select(u => u.Id).FirstAsync();
            shopIntegrationId = await db.ShopIntegrations.Select(i => (Guid?)i.Id).FirstOrDefaultAsync();
            invoiceNumber = await db.SalesInvoices.Where(i => i.Id == _invoiceId).Select(i => i.Number).FirstAsync();
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var from = today.AddDays(-7).ToString("yyyy-MM-dd");
        var to = today.ToString("yyyy-MM-dd");
        var b = _branchId;

        var urls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["api/ai/models"] = "/api/ai/models",
            ["api/ai/export-history"] = $"/api/ai/export-history?branchId={b}&skuId={_skuId}",
            ["api/ai/reorder-recommendations"] = $"/api/ai/reorder-recommendations?branchId={b}",
            ["api/auth/me"] = "/api/auth/me",
            ["api/auth/password-reset/status"] = "/api/auth/password-reset/status",
            ["api/branches"] = "/api/branches",
            ["api/branches/{id}"] = $"/api/branches/{b}",
            ["api/brands"] = "/api/brands",
            ["api/categories"] = "/api/categories",
            ["api/goods-issues"] = "/api/goods-issues",
            ["api/goods-issues/{id:guid}"] = $"/api/goods-issues/{_goodsIssueId}",
            ["api/goods-issues/by-order/{orderId:guid}"] = $"/api/goods-issues/by-order/{_orderId}",
            ["api/orders/{orderId:guid}/goods-issues"] = $"/api/orders/{_orderId}/goods-issues",
            ["api/goods-issues/by-orders"] = $"/api/goods-issues/by-orders?orderIds={_orderId}",
            ["api/goods-issues/pos-daily-summary"] = $"/api/goods-issues/pos-daily-summary?date={to}&branchId={b}",
            ["api/goods-issues/pos-statement"] = $"/api/goods-issues/pos-statement?date={to}&branchId={b}",
            ["api/goods-receipts"] = "/api/goods-receipts",
            ["api/goods-receipts/{id}"] = $"/api/goods-receipts/{_goodsReceiptId}",
            ["api/inventory/balances"] = "/api/inventory/balances",
            ["api/inventory/export-history"] = $"/api/inventory/export-history?branchId={b}&skuId={_skuId}",
            ["api/inventory/balances/sku/{skuId}/branch/{branchId}"] = $"/api/inventory/balances/sku/{_skuId}/branch/{b}",
            ["api/inventory/ledger"] = "/api/inventory/ledger",
            ["api/inventory-transactions"] = "/api/inventory-transactions",
            ["api/inventory/ledger/{id:guid}/trace"] = $"/api/inventory/ledger/{ledgerId}/trace",
            ["api/inventory/adjustments/{id:guid}"] = $"/api/inventory/adjustments/{_adjustmentId}",
            ["api/orders"] = "/api/orders",
            ["api/orders/{id:guid}"] = $"/api/orders/{_orderId}",
            ["api/orders/{id:guid}/trace"] = $"/api/orders/{_orderId}/trace",
            ["api/orders/{id:guid}/returnable-items"] = $"/api/orders/{_orderId}/returnable-items",
            ["api/payment-accounts"] = "/api/payment-accounts",
            ["api/payment-accounts/active"] = "/api/payment-accounts/active",
            ["api/pos/search"] = $"/api/pos/search?query={_skuCode}&branchId={b}",
            ["api/pos/barcode/{barcode}"] = $"/api/pos/barcode/{Uri.EscapeDataString(barcode)}",
            ["api/pos/orders/{orderId:guid}/goods-issues"] = $"/api/pos/orders/{_orderId}/goods-issues",
            ["api/price-lists"] = "/api/price-lists",
            ["api/price-change-requests"] = "/api/price-change-requests",
            ["api/price-change-requests/{id:guid}"] = $"/api/price-change-requests/{priceChangeRequestId}",
            ["api/sku-unit-prices/effective"] = $"/api/sku-unit-prices/effective?skuId={_skuId}&priceListCode=retail",
            ["api/sku-unit-prices/history"] = $"/api/sku-unit-prices/history?skuId={_skuId}",
            ["api/pricing/pending-po-prices"] = $"/api/pricing/pending-po-prices?skuId={_skuId}",
            ["api/price-change-requests/pending-po-prices"] = $"/api/price-change-requests/pending-po-prices?skuId={_skuId}",
            ["api/products"] = "/api/products",
            ["api/products/{id}"] = $"/api/products/{_productId}",
            ["api/purchase-orders"] = "/api/purchase-orders",
            ["api/purchase-orders/price-suggestion"] = $"/api/purchase-orders/price-suggestion?branchId={b}&skuId={_skuId}",
            ["api/purchase-orders/{id:guid}"] = $"/api/purchase-orders/{_purchaseOrderId}",
            ["api/reports/revenue-profit"] = $"/api/reports/revenue-profit?fromDate={from}&toDate={to}",
            ["api/reports/inventory-velocity"] = $"/api/reports/inventory-velocity?fromDate={from}&toDate={to}",
            ["api/reports/stock-alerts"] = $"/api/reports/stock-alerts?branchId={b}",
            ["api/reports/dashboard-summary"] = "/api/reports/dashboard-summary",
            ["api/reports/daily-summaries"] = $"/api/reports/daily-summaries?fromDate={from}&toDate={to}",
            ["api/reports/inventory-period"] = $"/api/reports/inventory-period?fromDate={from}&toDate={to}&branchId={b}",
            ["api/reports/stock-card"] = $"/api/reports/stock-card?skuId={_skuId}&branchId={b}&fromDate={from}&toDate={to}",
            ["api/sales-invoices"] = "/api/sales-invoices",
            ["api/sales-invoices/{id:guid}"] = $"/api/sales-invoices/{_invoiceId}",
            ["api/sales-invoices/receipt"] = $"/api/sales-invoices/receipt?number={Uri.EscapeDataString(invoiceNumber)}",
            ["api/sales-invoices/{id:guid}/receipt"] = $"/api/sales-invoices/{_invoiceId}/receipt",
            ["api/sales-invoices/{id:guid}/trace"] = $"/api/sales-invoices/{_invoiceId}/trace",
            ["api/sales-returns"] = "/api/sales-returns",
            ["api/sales-returns/{id:guid}"] = $"/api/sales-returns/{_salesReturnId}",
            ["api/search"] = $"/api/search?q={_skuCode}",
            ["api/shop-integrations"] = "/api/shop-integrations",
            ["api/stock-count-periods"] = "/api/stock-count-periods",
            ["api/stock-count-schedules"] = "/api/stock-count-schedules",
            ["api/stock-counts"] = "/api/stock-counts",
            ["api/stock-counts/{id:guid}"] = $"/api/stock-counts/{_stockCountId}",
            ["api/stock-transfers"] = "/api/stock-transfers",
            ["api/stock-transfers/{id:guid}"] = $"/api/stock-transfers/{_stockTransferId}",
            ["api/suppliers"] = "/api/suppliers",
            ["api/suppliers/{id}"] = $"/api/suppliers/{_supplierId}",
            ["api/units"] = "/api/units",
            ["api/units/{id:guid}"] = $"/api/units/{unitId}",
            ["api/products/{productId:guid}/variants/{variantId:guid}/units"] = $"/api/products/{_productId}/variants/{_skuId}/units",
            ["api/products/{productId:guid}/variants/{variantId:guid}/units/{skuUnitId:guid}/history"] = $"/api/products/{_productId}/variants/{_skuId}/units/{skuUnitId}/history",
            ["api/users"] = "/api/users",
            ["api/users/{id:guid}"] = $"/api/users/{userId}",
            ["api/warehouses"] = "/api/warehouses",
            ["api/warehouses/{id}"] = $"/api/warehouses/{_warehouseId}",
        };

        if (shopIntegrationId.HasValue)
        {
            urls["api/shop-integrations/{id:guid}"] = $"/api/shop-integrations/{shopIntegrationId}";
        }

        return urls;
    }

    [Fact]
    public async Task Staff_ScanAllGetEndpoints_NoSensitiveCostKeysExposed_AC07()
    {
        // Dữ liệu seed phải đủ để mọi route có tham số trả về bản ghi thật (không bỏ qua 404).
        _goodsIssueId.Should().NotBeEmpty("POS checkout phải lập phiếu xuất kho");
        _goodsReceiptId.Should().NotBeEmpty("seed phải có phiếu nhập");
        _salesReturnId.Should().NotBeEmpty("seed phải tạo được phiếu trả hàng");
        _stockTransferId.Should().NotBeEmpty("seed phải tạo được phiếu chuyển kho");
        _stockCountId.Should().NotBeEmpty("seed phải tạo được phiếu kiểm kê");

        var urls = await BuildGetRouteUrlsAsync();

        // Lấy danh sách route GET từ EndpointDataSource: route GET mới chưa được khai báo URL hoặc ngoại lệ thì test fail.
        var endpointDataSource = _factory.Services.GetRequiredService<EndpointDataSource>();
        var getRoutes = endpointDataSource.Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true)
            .Select(e => NormalizeRoute(e.RoutePattern.RawText ?? string.Empty))
            .Where(r => r.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();

        getRoutes.Should().HaveCountGreaterThan(50, "phải đọc được toàn bộ route GET của API");

        var shopIntegrationRoute = "api/shop-integrations/{id:guid}";
        var uncovered = getRoutes
            .Where(r => !urls.ContainsKey(r) && !ExcludedGetRoutes.ContainsKey(r))
            .Where(r => !(r.Equals(shopIntegrationRoute, StringComparison.OrdinalIgnoreCase) && !urls.ContainsKey(r)))
            .ToList();
        uncovered.Should().BeEmpty("mọi route GET phải được quét hoặc ghi lý do ngoại lệ: " + string.Join(", ", uncovered));

        var staffToken = await LoginAsAsync("staff@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);

        var violations = new List<string>();
        var scanned = 0;
        foreach (var route in getRoutes.Where(urls.ContainsKey))
        {
            var url = urls[route];
            using var res = await _client.GetAsync(url);
            var content = await res.Content.ReadAsStringAsync();

            if (OwnerOnlyGetRoutes.Contains(route))
            {
                if (res.StatusCode != HttpStatusCode.Forbidden)
                {
                    violations.Add($"{url}: route chỉ Owner nhưng Staff nhận {(int)res.StatusCode}");
                }
                continue;
            }

            if (res.StatusCode != HttpStatusCode.OK)
            {
                violations.Add($"{url}: Staff nhận {(int)res.StatusCode}, cần 200 để quét. {content[..Math.Min(content.Length, 300)]}");
                continue;
            }

            scanned++;
            var trimmed = content.TrimStart();
            if (!trimmed.StartsWith('{') && !trimmed.StartsWith('['))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(content);
            var isPurchaseDocument = route.StartsWith("api/purchase-orders", StringComparison.OrdinalIgnoreCase)
                || route.StartsWith("api/goods-receipts", StringComparison.OrdinalIgnoreCase);
            ScanJsonElement(doc.RootElement, url, violations, isPurchaseDocument);
        }

        scanned.Should().BeGreaterThan(50, "Staff phải quét được phần lớn route GET");
        violations.Should().BeEmpty(
            "Staff không được nhận trường giá vốn, giá mua hay giá trị tồn ở bất kỳ route GET nào. Chi tiết:\n"
            + string.Join("\n", violations));
    }

    [Fact]
    public async Task Staff_ExcelExports_HaveNoValueColumns_AC03()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var from = today.AddDays(-7).ToString("yyyy-MM-dd");
        var to = today.ToString("yyyy-MM-dd");
        var exports = new[]
        {
            $"/api/reports/inventory-period/export-excel?fromDate={from}&toDate={to}&branchId={_branchId}",
            $"/api/reports/stock-card/export-excel?skuId={_skuId}&branchId={_branchId}&fromDate={from}&toDate={to}",
        };
        var valueMarkers = new[] { "Giá trị", "Đơn giá", "Thành tiền", "Giá vốn" };

        foreach (var (email, expectValues) in new[] { ("staff@shopa.local", false), ("owner@shopa.local", true) })
        {
            var token = await LoginAsAsync(email, PostgresTestEnvironment.AppPassword);
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            foreach (var url in exports)
            {
                using var res = await _client.GetAsync(url);
                res.StatusCode.Should().Be(HttpStatusCode.OK, $"{email} xuất {url}");
                await using var stream = await res.Content.ReadAsStreamAsync();
                using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
                var text = string.Join("\n", workbook.Worksheets
                    .SelectMany(ws => ws.CellsUsed())
                    .Select(c => c.GetString()));

                var hasValueColumn = valueMarkers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase));
                hasValueColumn.Should().Be(expectValues, $"{email} xuất {url}: cột giá trị chỉ Owner có");
            }
        }
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
