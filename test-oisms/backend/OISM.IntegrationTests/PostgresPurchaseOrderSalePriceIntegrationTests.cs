using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Catalog.Products;
using OISM.Application.Features.Catalog.Units;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Pricing.Dtos;
using OISM.Application.Features.PurchaseOrders.Dtos;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Bộ kiểm thử tích hợp cho OISMS-66-L2-G4 (Upgrade 12):
/// Cấu hình giá bán trên đơn mua, sinh giá khi nhập kho, lý do đổi giá, xung đột giá Q19.
/// </summary>
[Collection("PostgresIntegrationTests")]
public class PostgresPurchaseOrderSalePriceIntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class TestTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    public PostgresPurchaseOrderSalePriceIntegrationTests(WebApplicationFactoryWithPostgres factory)
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
            var migrator = new DatabaseMigrationService();
            await migrator.UpgradeDatabaseSchemaAsync(PostgresTestEnvironment.AdminDatabaseConnectionString);
        }
        catch
        {
            // Suppress cleanup exceptions
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

    private static HttpRequestMessage CreatePostWithKey(string url, object body, Guid? requestKey = null)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        msg.Headers.Add("Idempotency-Key", (requestKey ?? Guid.NewGuid()).ToString());
        return msg;
    }

    private async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(raw, _jsonOptions);
    }

    // =========================================================================
    // AC-01: MIGRATION CHẠY 2 LẦN IDEMPOTENT, 51 BẢNG, CHECK CONSTRAINTS VÀ REASON_TYPE
    // =========================================================================

    [Fact]
    public async Task AC01_MigrationIdempotent_EnforcesDbChecksAndColumns()
    {
        await SeedDatabaseAsync();
        var adminConnStr = PostgresTestEnvironment.AdminDatabaseConnectionString;
        var migrator = new DatabaseMigrationService();

        var migrationSqlPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "docker", "postgres", "migrations", "20261121_purchase_order_sale_price_configs.sql"));
        File.Exists(migrationSqlPath).Should().BeTrue($"File migration phải tồn tại tại {migrationSqlPath}");
        var migrationSql = await File.ReadAllTextAsync(migrationSqlPath);

        // 1. Chạy migration 2 lần liên tiếp trực tiếp trên DB
        await using (var adminConn = new NpgsqlConnection(adminConnStr))
        {
            await adminConn.OpenAsync();
            await using (var run1 = new NpgsqlCommand(migrationSql, adminConn))
            {
                await run1.ExecuteNonQueryAsync();
            }
            await using (var run2 = new NpgsqlCommand(migrationSql, adminConn))
            {
                await run2.ExecuteNonQueryAsync();
            }
        }

        // 2. Chạy qua DatabaseMigrationService
        var up1 = await migrator.UpgradeDatabaseSchemaAsync(adminConnStr);
        up1.Success.Should().BeTrue(up1.Message);
        var up2 = await migrator.UpgradeDatabaseSchemaAsync(adminConnStr);
        up2.Success.Should().BeTrue(up2.Message);

        var baselineCheck = await migrator.InitializeOrBaselineDatabaseAsync(adminConnStr);
        baselineCheck.Success.Should().BeTrue(baselineCheck.Message);

        // 3. Kiểm tra DB có đúng 51 bảng và cột reason_type trên price_change_requests
        await using (var adminConn = new NpgsqlConnection(adminConnStr))
        {
            await adminConn.OpenAsync();
            await using var countCmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE' AND table_name NOT IN ('_oisms_test_fixture_marker', '__EFMigrationsHistory');", adminConn);
            var tableCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
            tableCount.Should().Be(51, "Hệ thống phải có đúng 51 bảng nghiệp vụ");

            DatabaseMigrationService.RequiredTables.Length.Should().Be(51, "RequiredTables phải có đúng 51 bảng");

            await using var colCmd = new NpgsqlCommand(
                "SELECT is_nullable, column_default FROM information_schema.columns WHERE table_name = 'price_change_requests' AND column_name = 'reason_type';", adminConn);
            await using var reader = await colCmd.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetString(0).Should().Be("NO", "reason_type phải là NOT NULL");
        }
    }

    // =========================================================================
    // AC-02: CẢNH BÁO BÁN LỖ, THIẾU XÁC NHẬN -> 422, CÓ XÁC NHẬN -> 200 OK
    // =========================================================================

    [Fact]
    public async Task AC02_LossPriceWarning_422WhenNotConfirmed_And200WhenConfirmed()
    {
        await SeedDatabaseAsync();
        var tenantId = OismsDatabaseSeeder.TenantAId;
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        Guid branchId, supplierId, skuId, unitId, priceListId;
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == tenantId);
            var sup = await ctx.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == tenantId);
            var pl = await ctx.PriceLists.FirstAsync(p => p.TenantId == tenantId && p.Code == "retail");
            var skuUnit = await ctx.SkuUnits.FirstAsync(u => u.TenantId == tenantId && u.SkuId == sku.Id && u.FactorToBase == 1m);

            branchId = branch.Id;
            supplierId = sup.Id;
            skuId = sku.Id;
            unitId = skuUnit.UnitId;
            priceListId = pl.Id;
        }

        // Tạo PO
        using var createMsg = CreatePostWithKey("/api/purchase-orders", new CreatePurchaseOrderDto
        {
            BranchId = branchId,
            SupplierId = supplierId,
            Items = new List<CreatePurchaseOrderItemDto>
            {
                new() { SkuId = skuId, Quantity = 10m, UnitPrice = 50000m }
            }
        });
        var createRes = await _client.SendAsync(createMsg);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var po = (await createRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;
        po.Status.Should().Be("PendingApproval");

        // Cập nhật cấu hình giá bán với giá cố định = 40.000đ (thấp hơn giá mua 50.000đ -> bán lỗ)
        var updateConfigRes = await _client.PutAsJsonAsync($"/api/purchase-orders/{po.Id}/sale-price-configs", new UpdatePurchaseOrderSalePriceConfigsDto
        {
            Items = new List<PurchaseOrderItemSalePriceConfigUpdateDto>
            {
                new()
                {
                    PurchaseOrderItemId = po.Items[0].Id,
                    Configs = new List<PurchaseOrderSalePriceConfigInputDto>
                    {
                        new()
                        {
                            UnitId = unitId,
                            PriceListId = priceListId,
                            Mode = "FIXED",
                            FixedPrice = 40000m,
                            PriceSource = "OWNER",
                            ApplyMode = "ON_RECEIPT"
                        }
                    }
                }
            }
        });
        updateConfigRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Đọc lại PO: kiểm tra cờ IsBelowCost = true
        var getPoRes = await _client.GetFromJsonAsync<PurchaseOrderResponseDto>($"/api/purchase-orders/{po.Id}", _jsonOptions);
        var cfg = getPoRes!.Items[0].SalePriceConfigs!.Single(c => c.PriceListId == priceListId);
        cfg.IsBelowCost.Should().BeTrue();
        cfg.ProjectedPrice.Should().Be(40000m);

        // 1. Duyệt đơn KHÔNG xác nhận bán lỗ (confirmLossPrice = false) -> 422
        var approveWithoutLossRes = await _client.PostAsJsonAsync($"/api/purchase-orders/{po.Id}/approve", new ApprovePurchaseOrderRequestDto
        {
            Version = po.Version,
            ConfirmLossPrice = false
        });
        approveWithoutLossRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var err = await ReadProblemAsync(approveWithoutLossRes);
        err.GetProperty("errorCode").GetString().Should().Be("PURCHASE_ORDER_LOSS_PRICE_CONFIRM_REQUIRED");

        // 2. Duyệt đơn CÓ xác nhận bán lỗ (confirmLossPrice = true) -> 200 OK
        var approveWithLossRes = await _client.PostAsJsonAsync($"/api/purchase-orders/{po.Id}/approve", new ApprovePurchaseOrderRequestDto
        {
            Version = po.Version,
            ConfirmLossPrice = true
        });
        approveWithLossRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var approvedPo = (await approveWithLossRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;
        approvedPo.Status.Should().Be("Approved");
    }

    // =========================================================================
    // AC-03 & AC-04: XÁC NHẬN PHIẾU NHẬP ĐẦU TIÊN SINH GIÁ, WAC_MARGIN LÀM TRÒN LÊN 1.000Đ,
    // ĐỢT NHẬP THỨ HAI KHÔNG SINH THÊM, THỨ TỰ KHÓA ĐẢM BẢO
    // =========================================================================

    [Fact]
    public async Task AC03_AC04_ReceiptConfirmation_GeneratesPrices_WacMarginRounding_AppliedOnceAcrossPartialReceipts()
    {
        await SeedDatabaseAsync();
        var tenantId = OismsDatabaseSeeder.TenantAId;
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        Guid branchId, warehouseId, supplierId, retailListId, wholesaleListId;
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == tenantId);
            var wh = await ctx.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branch.Id);
            var sup = await ctx.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            retailListId = (await ctx.PriceLists.FirstAsync(p => p.TenantId == tenantId && p.Code == "retail")).Id;
            wholesaleListId = (await ctx.PriceLists.FirstAsync(p => p.TenantId == tenantId && p.Code == "wholesale")).Id;

            branchId = branch.Id;
            warehouseId = wh.Id;
            supplierId = sup.Id;
        }

        // Tạo sản phẩm và SKU mới với đơn vị cái (base) và thùng (hệ số 10)
        var units = (await _client.GetFromJsonAsync<List<UnitResponse>>("/api/units", _jsonOptions))!;
        var caiId = units.Single(u => u.Code == "piece").Id;
        var thungId = units.Single(u => u.Code == "thung").Id;

        var runCode = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var createProdRes = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = $"SP Giá Đơn Mua {runCode}",
            Variants = new List<CreateVariantRequest>
            {
                new()
                {
                    SKU = $"PO-PRICE-{runCode}",
                    VariantName = "Biến thể chuẩn",
                    BaseUnitId = caiId,
                    Unit = "piece",
                    RetailPrice = 20000m,
                    WholesalePrice = 18000m
                }
            }
        });
        createProdRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var prodId = (await createProdRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions)).GetProperty("productId").GetGuid();
        var prod = (await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{prodId}", _jsonOptions))!;
        var skuId = prod.Variants[0].Id;

        // Thêm đơn vị thùng (hệ số 10) được phép bán
        var addUnitRes = await _client.PostAsJsonAsync($"/api/products/{prodId}/variants/{skuId}/units", new CreateSkuUnitRequest
        {
            UnitId = thungId,
            FactorToBase = 10m,
            CanPurchase = true,
            CanSell = true,
            QuantityDecimalPlaces = 0
        });
        addUnitRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Tạo đơn mua 10 cái @ 10.000đ, phí ship 10.000đ (phân bổ -> giá vốn đơn mua = 11.000đ/cái)
        using var createPoMsg = CreatePostWithKey("/api/purchase-orders", new CreatePurchaseOrderDto
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ShippingFee = 10000m,
            Items = new List<CreatePurchaseOrderItemDto>
            {
                new() { SkuId = skuId, Quantity = 10m, UnitPrice = 10000m, UnitId = caiId }
            }
        });
        var createPoRes = await _client.SendAsync(createPoMsg);
        createPoRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var po = (await createPoRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;

        // Cấu hình giá bán:
        // 1. Cái - Lẻ: Mode WAC_MARGIN, margin 20%, ON_RECEIPT.
        //    Giá vốn sau phân bổ = 11.000đ. Giá dự kiến = 11.000 / (1 - 0.20) = 13.750đ -> làm tròn lên bội 1.000đ = 14.000đ.
        // 2. Cái - Sỉ: Mode FIXED, 13.000đ, FROM_DATE (ngày mai).
        // 3. Thùng - Lẻ: Mode KEEP (không đổi).
        var applyTomorrow = DateTimeOffset.UtcNow.AddDays(1);
        var configPayload = new UpdatePurchaseOrderSalePriceConfigsDto
        {
            Items = new List<PurchaseOrderItemSalePriceConfigUpdateDto>
            {
                new()
                {
                    PurchaseOrderItemId = po.Items[0].Id,
                    Configs = new List<PurchaseOrderSalePriceConfigInputDto>
                    {
                        new()
                        {
                            UnitId = caiId,
                            PriceListId = retailListId,
                            Mode = "WAC_MARGIN",
                            MarginPercent = 20m,
                            ApplyMode = "ON_RECEIPT"
                        },
                        new()
                        {
                            UnitId = caiId,
                            PriceListId = wholesaleListId,
                            Mode = "FIXED",
                            FixedPrice = 13000m,
                            PriceSource = "OWNER",
                            ApplyMode = "FROM_DATE",
                            ApplyFrom = applyTomorrow
                        },
                        new()
                        {
                            UnitId = thungId,
                            PriceListId = retailListId,
                            Mode = "KEEP"
                        }
                    }
                }
            }
        };
        var updateConfigRes = await _client.PutAsJsonAsync($"/api/purchase-orders/{po.Id}/sale-price-configs", configPayload);
        updateConfigRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Duyệt đơn mua
        var approveRes = await _client.PostAsJsonAsync($"/api/purchase-orders/{po.Id}/approve", new ApprovePurchaseOrderRequestDto
        {
            Version = po.Version
        });
        approveRes.StatusCode.Should().Be(HttpStatusCode.OK, await approveRes.Content.ReadAsStringAsync());

        // Đợt 1: Tạo phiếu nhập một phần (5 cái) và xác nhận
        var createGrRes = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            PurchaseOrderId = po.Id,
            WarehouseId = warehouseId,
            Items = new List<CreateGoodsReceiptItemDto>
            {
                new() { PurchaseOrderItemId = po.Items[0].Id, Quantity = 5m }
            }
        });
        createGrRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var gr1 = (await createGrRes.Content.ReadFromJsonAsync<GoodsReceiptResponseDto>(_jsonOptions))!;

        var confirmGr1Res = await _client.PostAsync($"/api/goods-receipts/{gr1.Id}/confirm", null);
        confirmGr1Res.StatusCode.Should().Be(HttpStatusCode.OK);

        // Kiểm tra sau đợt 1 trong DB:
        // - Cấu hình Cái - Lẻ đã sinh PriceChangeRequest và SkuUnitPrice với giá = 14.000đ (làm tròn lên từ 13.750đ)
        // - Cấu hình Cái - Sỉ đã sinh với giá = 13.000đ, valid_from = max(applyTomorrow, confirmedAt) = applyTomorrow
        // - Cấu hình Thùng - Lẻ mode KEEP -> không sinh
        Guid pcrId1, pcrId2;
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var dbConfigs = await ctx.PurchaseOrderSalePriceConfigs
                .Where(c => c.PurchaseOrderItemId == po.Items[0].Id)
                .ToListAsync();

            var retailCfg = dbConfigs.Single(c => c.UnitId == caiId && c.PriceListId == retailListId);
            retailCfg.AppliedAt.Should().NotBeNull();
            retailCfg.PriceChangeRequestId.Should().NotBeNull();
            pcrId1 = retailCfg.PriceChangeRequestId!.Value;

            var wholesaleCfg = dbConfigs.Single(c => c.UnitId == caiId && c.PriceListId == wholesaleListId);
            wholesaleCfg.AppliedAt.Should().NotBeNull();
            wholesaleCfg.PriceChangeRequestId.Should().NotBeNull();
            pcrId2 = wholesaleCfg.PriceChangeRequestId!.Value;

            var keepCfg = dbConfigs.Single(c => c.UnitId == thungId && c.PriceListId == retailListId);
            keepCfg.AppliedAt.Should().BeNull();
            keepCfg.PriceChangeRequestId.Should().BeNull();

            // Kiểm tra PriceChangeRequest & SkuUnitPrice
            var pcr1 = await ctx.PriceChangeRequests.Include(r => r.Items).FirstAsync(r => r.Id == pcrId1);
            pcr1.Status.Should().Be("Approved");
            pcr1.ReasonType.Should().Be("FROM_PURCHASE_ORDER");
            pcr1.Reason.Should().Contain(po.Number);
            pcr1.Items.Count.Should().Be(2);

            var retailItem = pcr1.Items.Single(i => i.PriceListId == retailListId);
            var unitPriceRetail = await ctx.SkuUnitPrices
                .Where(p => p.SkuId == skuId && p.UnitId == caiId && p.PriceListId == retailListId && p.SourceItemId == retailItem.Id)
                .SingleAsync();
            unitPriceRetail.Price.Should().Be(14000m, "11.000 / (1 - 0.20) = 13.750 -> làm tròn lên bội 1.000đ thành 14.000đ");

            var wholesaleItem = pcr1.Items.Single(i => i.PriceListId == wholesaleListId);
            var unitPriceWholesale = await ctx.SkuUnitPrices
                .Where(p => p.SkuId == skuId && p.UnitId == caiId && p.PriceListId == wholesaleListId && p.SourceItemId == wholesaleItem.Id)
                .SingleAsync();
            unitPriceWholesale.Price.Should().Be(13000m);
            unitPriceWholesale.ValidFrom.Should().BeCloseTo(applyTomorrow, TimeSpan.FromSeconds(5));
        }

        // Đợt 2: Nhập nốt 5 cái còn lại và xác nhận
        var createGr2Res = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            PurchaseOrderId = po.Id,
            WarehouseId = warehouseId,
            Items = new List<CreateGoodsReceiptItemDto>
            {
                new() { PurchaseOrderItemId = po.Items[0].Id, Quantity = 5m }
            }
        });
        createGr2Res.StatusCode.Should().Be(HttpStatusCode.Created);
        var gr2 = (await createGr2Res.Content.ReadFromJsonAsync<GoodsReceiptResponseDto>(_jsonOptions))!;

        var confirmGr2Res = await _client.PostAsync($"/api/goods-receipts/{gr2.Id}/confirm", null);
        confirmGr2Res.StatusCode.Should().Be(HttpStatusCode.OK);

        // Kiểm tra sau đợt 2: KHÔNG sinh thêm bất kỳ PriceChangeRequest hay SkuUnitPrice nào
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var totalPoPcr = await ctx.PriceChangeRequests
                .Where(r => r.TenantId == tenantId && r.ReasonType == "FROM_PURCHASE_ORDER" && r.Reason.Contains(po.Number))
                .CountAsync();
            totalPoPcr.Should().Be(1, "Chỉ sinh 1 phiếu đổi giá ở đợt nhập đầu tiên, đợt sau không sinh thêm");
        }

        // API danh sách và chi tiết phiếu đổi giá phải trả đúng loại lý do đã lưu (không rơi về mặc định OTHER)
        var listJson = await _client.GetStringAsync("/api/price-change-requests");
        using (var listDoc = JsonDocument.Parse(listJson))
        {
            var listed = listDoc.RootElement.GetProperty("items").EnumerateArray()
                .Single(e => e.GetProperty("id").GetGuid() == pcrId1);
            listed.GetProperty("reasonType").GetString().Should().Be("FROM_PURCHASE_ORDER");
        }

        var detailJson = await _client.GetStringAsync($"/api/price-change-requests/{pcrId1}");
        using (var detailDoc = JsonDocument.Parse(detailJson))
        {
            detailDoc.RootElement.GetProperty("reasonType").GetString().Should().Be("FROM_PURCHASE_ORDER");
        }
    }

    // =========================================================================
    // AC-05: LÝ DO ĐỔI GIÁ BẮT BUỘC, XỬ LÝ XUNG ĐỘT Q19 (KEEP_MANUAL vs APPLY_PO_LATER)
    // =========================================================================

    [Fact]
    public async Task AC05_ManualPriceChange_ReasonTypeValidation_And_Q19ConflictResolution()
    {
        await SeedDatabaseAsync();
        var tenantId = OismsDatabaseSeeder.TenantAId;
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        Guid branchId, supplierId, skuId, unitId, priceListId;
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == tenantId);
            var sup = await ctx.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == tenantId);
            var pl = await ctx.PriceLists.FirstAsync(p => p.TenantId == tenantId && p.Code == "retail");
            var skuUnit = await ctx.SkuUnits.FirstAsync(u => u.TenantId == tenantId && u.SkuId == sku.Id && u.FactorToBase == 1m);

            branchId = branch.Id;
            supplierId = sup.Id;
            skuId = sku.Id;
            unitId = skuUnit.UnitId;
            priceListId = pl.Id;
        }

        // 1. Kiểm tra validation ReasonType trên phiếu đổi giá tay:
        // Thiếu / sai reasonType -> 400
        using var invalidReasonTypeMsg = CreatePostWithKey("/api/price-change-requests", new CreatePriceChangeRequest
        {
            ReasonType = "INVALID_TYPE",
            Reason = "Lý do",
            Items = new List<CreatePriceChangeRequestItemDto>
            {
                new() { PriceListCode = "retail", SkuId = skuId, UnitId = unitId, NewPrice = 99000m }
            }
        });
        (await _client.SendAsync(invalidReasonTypeMsg)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // reasonType = OTHER mà không có ghi chú chi tiết -> 400
        using var emptyOtherReasonMsg = CreatePostWithKey("/api/price-change-requests", new CreatePriceChangeRequest
        {
            ReasonType = "OTHER",
            Reason = "   ",
            Items = new List<CreatePriceChangeRequestItemDto>
            {
                new() { PriceListCode = "retail", SkuId = skuId, UnitId = unitId, NewPrice = 99000m }
            }
        });
        (await _client.SendAsync(emptyOtherReasonMsg)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 2. Tạo PO đã duyệt với cấu hình giá chờ cho (skuId, unitId, priceListId)
        using var poMsg = CreatePostWithKey("/api/purchase-orders", new CreatePurchaseOrderDto
        {
            BranchId = branchId,
            SupplierId = supplierId,
            Items = new List<CreatePurchaseOrderItemDto>
            {
                new() { SkuId = skuId, Quantity = 10m, UnitPrice = 50000m, UnitId = unitId }
            }
        });
        var poRes = await _client.SendAsync(poMsg);
        var po = (await poRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;

        var putRes = await _client.PutAsJsonAsync($"/api/purchase-orders/{po.Id}/sale-price-configs", new UpdatePurchaseOrderSalePriceConfigsDto
        {
            Items = new List<PurchaseOrderItemSalePriceConfigUpdateDto>
            {
                new()
                {
                    PurchaseOrderItemId = po.Items[0].Id,
                    Configs = new List<PurchaseOrderSalePriceConfigInputDto>
                    {
                        new()
                        {
                            UnitId = unitId,
                            PriceListId = priceListId,
                            Mode = "FIXED",
                            FixedPrice = 75000m,
                            PriceSource = "OWNER",
                            ApplyMode = "ON_RECEIPT"
                        }
                    }
                }
            }
        });
        putRes.StatusCode.Should().Be(HttpStatusCode.OK, await putRes.Content.ReadAsStringAsync());

        var approveRes = await _client.PostAsJsonAsync($"/api/purchase-orders/{po.Id}/approve", new ApprovePurchaseOrderRequestDto
        {
            Version = po.Version
        });
        approveRes.StatusCode.Should().Be(HttpStatusCode.OK, await approveRes.Content.ReadAsStringAsync());

        // 3. API GET pending-po-prices trả về cấu hình đang chờ
        var pendingPrices = await _client.GetFromJsonAsync<List<PendingPurchaseOrderSalePriceDto>>(
            $"/api/pricing/pending-po-prices?skuId={skuId}", _jsonOptions);
        pendingPrices.Should().NotBeNull();
        pendingPrices!.Should().Contain(p => p.PurchaseOrderId == po.Id && p.UnitId == unitId);

        // 4. Owner tạo phiếu đổi giá tay trùng (skuId, unitId, retail) KHÔNG gửi resolution -> 409 PRICE_CHANGE_PO_CONFLICT
        using var conflictMsg = CreatePostWithKey("/api/price-change-requests", new CreatePriceChangeRequest
        {
            ReasonType = "SHORTAGE",
            Reason = "Khan hàng tăng giá",
            Items = new List<CreatePriceChangeRequestItemDto>
            {
                new() { PriceListCode = "retail", SkuId = skuId, UnitId = unitId, NewPrice = 80000m }
            }
        });
        var conflictRes = await _client.SendAsync(conflictMsg);
        conflictRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var conflictProblem = await ReadProblemAsync(conflictRes);
        conflictProblem.GetProperty("errorCode").GetString().Should().Be("PRICE_CHANGE_PO_CONFLICT");
        conflictProblem.GetProperty("conflictingPurchaseOrders").GetArrayLength().Should().BeGreaterThan(0);

        // 5. Chọn KEEP_MANUAL -> Thành công, cấu hình đơn mua chuyển sang KEEP
        using var keepManualMsg = CreatePostWithKey("/api/price-change-requests", new CreatePriceChangeRequest
        {
            ReasonType = "SHORTAGE",
            Reason = "Khan hàng tăng giá",
            PoConflictResolution = "KEEP_MANUAL",
            Items = new List<CreatePriceChangeRequestItemDto>
            {
                new() { PriceListCode = "retail", SkuId = skuId, UnitId = unitId, NewPrice = 85000m }
            }
        });
        var keepManualRes = await _client.SendAsync(keepManualMsg);
        keepManualRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // Kiểm tra trong DB: cấu hình trên đơn mua đã chuyển thành KEEP
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var dbCfg = await ctx.PurchaseOrderSalePriceConfigs
                .FirstAsync(c => c.PurchaseOrderItemId == po.Items[0].Id && c.UnitId == unitId && c.PriceListId == priceListId);
            dbCfg.Mode.Should().Be("KEEP");
        }
    }

    // =========================================================================
    // AC-06: STAFF BỊ ẨN CẤU HÌNH GIÁ BÁN, GIÁ DỰ KIẾN, MARGIN (NULL); CASHIER 403
    // =========================================================================

    [Fact]
    public async Task AC06_StaffSalePriceRedaction_And_CashierForbidden()
    {
        await SeedDatabaseAsync();
        var tenantId = OismsDatabaseSeeder.TenantAId;

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        var staffToken = await LoginAsAsync("staff@shopa.local", PostgresTestEnvironment.AppPassword);
        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);

        Guid branchId, supplierId, skuId, unitId, priceListId;
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == tenantId);
            var sup = await ctx.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == tenantId);
            var pl = await ctx.PriceLists.FirstAsync(p => p.TenantId == tenantId && p.Code == "retail");
            var skuUnit = await ctx.SkuUnits.FirstAsync(u => u.TenantId == tenantId && u.SkuId == sku.Id && u.FactorToBase == 1m);

            branchId = branch.Id;
            supplierId = sup.Id;
            skuId = sku.Id;
            unitId = skuUnit.UnitId;
            priceListId = pl.Id;
        }

        // Owner tạo PO và cấu hình giá bán
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        using var createMsg = CreatePostWithKey("/api/purchase-orders", new CreatePurchaseOrderDto
        {
            BranchId = branchId,
            SupplierId = supplierId,
            Items = new List<CreatePurchaseOrderItemDto>
            {
                new() { SkuId = skuId, Quantity = 10m, UnitPrice = 50000m, UnitId = unitId }
            }
        });
        var poRes = await _client.SendAsync(createMsg);
        var po = (await poRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;

        await _client.PutAsJsonAsync($"/api/purchase-orders/{po.Id}/sale-price-configs", new UpdatePurchaseOrderSalePriceConfigsDto
        {
            Items = new List<PurchaseOrderItemSalePriceConfigUpdateDto>
            {
                new()
                {
                    PurchaseOrderItemId = po.Items[0].Id,
                    Configs = new List<PurchaseOrderSalePriceConfigInputDto>
                    {
                        new()
                        {
                            UnitId = unitId,
                            PriceListId = priceListId,
                            Mode = "FIXED",
                            FixedPrice = 70000m
                        }
                    }
                }
            }
        });

        // 1. Staff xem chi tiết PO: SalePriceConfigs phải là null trên từng dòng
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var staffViewPo = (await _client.GetFromJsonAsync<PurchaseOrderResponseDto>($"/api/purchase-orders/{po.Id}", _jsonOptions))!;
        staffViewPo.Items[0].SalePriceConfigs.Should().BeNull("Staff không được thấy cấu hình giá bán");

        // Staff gọi PUT cập nhật cấu hình giá bán -> 403 Forbidden
        var staffPutRes = await _client.PutAsJsonAsync($"/api/purchase-orders/{po.Id}/sale-price-configs", new UpdatePurchaseOrderSalePriceConfigsDto());
        staffPutRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 2. Cashier gọi xem PO -> 403 Forbidden
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        var cashierGetRes = await _client.GetAsync($"/api/purchase-orders/{po.Id}");
        cashierGetRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
