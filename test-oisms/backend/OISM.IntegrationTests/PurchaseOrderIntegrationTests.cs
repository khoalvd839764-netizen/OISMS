using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.RegisterTenant;
using OISM.Application.Features.Catalog.Products;
using OISM.Application.Features.Catalog.Units;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.PurchaseOrders.Dtos;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Bộ kiểm thử tích hợp cho OISMS-47-U01A (Phiếu mua hàng và Nhập kho từ phiếu mua)
/// chạy trên PostgreSQL 16 thật, bao phủ AC-01 đến AC-07 theo thiết kế mục 8.
/// </summary>
[Collection("PostgresIntegrationTests")]
public class PurchaseOrderIntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class TestTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    public PurchaseOrderIntegrationTests(WebApplicationFactoryWithPostgres factory)
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
    // AC-01: MIGRATION TRÊN DỮ LIỆU CŨ & CHẠY LẶP IDEMPOTENT
    // =========================================================================

    [Fact]
    public async Task AC01_Migration_RunsOnExistingData_IsIdempotent_AndPreservesLegacyReceipts()
    {
        await SeedDatabaseAsync();
        var migrator = new DatabaseMigrationService();
        var adminConnStr = PostgresTestEnvironment.AdminDatabaseConnectionString;

        await using var conn = new NpgsqlConnection(adminConnStr);
        await conn.OpenAsync();

        var tenantId = OismsDatabaseSeeder.TenantAId;
        Guid branchId, warehouseId, supplierId, userId, skuId;
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == tenantId);
            var wh = await ctx.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branch.Id);
            var sup = await ctx.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            var usr = await ctx.Users.FirstAsync(u => u.TenantId == tenantId && u.Role == "Owner");
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == tenantId);
            branchId = branch.Id;
            warehouseId = wh.Id;
            supplierId = sup.Id;
            userId = usr.Id;
            skuId = sku.Id;
        }

        // Chèn dữ liệu phiếu nhập cũ (Confirmed và Draft với purchase_order_id = NULL)
        var legacyConfirmedId = Guid.NewGuid();
        var legacyDraftId = Guid.NewGuid();
        await using (var insertCmd = new NpgsqlCommand(@"
            INSERT INTO public.goods_receipts (id, tenant_id, number, branch_id, warehouse_id, supplier_id, status, created_by, confirmed_by, confirmed_at)
            VALUES
                (@cId, @tId, 'GR-LEGACY-CONF', @bId, @wId, @sId, 'Confirmed', @uId, @uId, NOW()),
                (@dId, @tId, 'GR-LEGACY-DRAFT', @bId, @wId, @sId, 'Draft', @uId, NULL, NULL);

            INSERT INTO public.goods_receipt_items (id, tenant_id, receipt_id, sku_id, unit_name_snapshot, factor_snapshot, quantity, unit_price)
            VALUES
                (gen_random_uuid(), @tId, @cId, @skuId, 'piece', 1, 10, 50000),
                (gen_random_uuid(), @tId, @dId, @skuId, 'piece', 1, 5, 45000);
        ", conn))
        {
            insertCmd.Parameters.AddWithValue("cId", legacyConfirmedId);
            insertCmd.Parameters.AddWithValue("dId", legacyDraftId);
            insertCmd.Parameters.AddWithValue("tId", tenantId);
            insertCmd.Parameters.AddWithValue("bId", branchId);
            insertCmd.Parameters.AddWithValue("wId", warehouseId);
            insertCmd.Parameters.AddWithValue("sId", supplierId);
            insertCmd.Parameters.AddWithValue("uId", userId);
            insertCmd.Parameters.AddWithValue("skuId", skuId);
            await insertCmd.ExecuteNonQueryAsync();
        }

        // Mô phỏng trạng thái trước migration 20261102_purchase_orders.sql
        await using (var dropCmd = new NpgsqlCommand(@"
            ALTER TABLE public.goods_receipt_items DROP CONSTRAINT IF EXISTS fk_goods_receipt_items_po_item;
            ALTER TABLE public.goods_receipt_items DROP COLUMN IF EXISTS purchase_order_item_id;
            ALTER TABLE public.goods_receipts DROP CONSTRAINT IF EXISTS fk_goods_receipts_purchase_order_id;
            ALTER TABLE public.goods_receipts DROP COLUMN IF EXISTS purchase_order_id;
            DROP TABLE IF EXISTS public.purchase_order_items CASCADE;
            DROP TABLE IF EXISTS public.purchase_orders CASCADE;
            DROP FUNCTION IF EXISTS public.oisms_purchase_order_guard CASCADE;
            DROP FUNCTION IF EXISTS public.oisms_purchase_order_item_guard CASCADE;

            -- Khôi phục function trigger bảo vệ goods_receipt_items về phiên bản pre-U01A (không có purchase_order_item_id)
            CREATE OR REPLACE FUNCTION public.oisms_protect_receipt_item() RETURNS trigger
            LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
            DECLARE confirmed timestamptz;
            BEGIN
              SELECT confirmed_at INTO confirmed FROM public.goods_receipts WHERE id=OLD.receipt_id AND tenant_id=OLD.tenant_id FOR UPDATE;
              IF TG_OP='DELETE' AND confirmed IS NOT NULL THEN
                RAISE EXCEPTION 'Confirmed goods receipt items cannot be deleted' USING ERRCODE='23514';
              END IF;
              IF TG_OP='UPDATE' AND confirmed IS NOT NULL AND
                 ((NEW.tenant_id,NEW.receipt_id,NEW.sku_id,NEW.quantity,NEW.unit_price,NEW.factor_snapshot) IS DISTINCT FROM
                  (OLD.tenant_id,OLD.receipt_id,OLD.sku_id,OLD.quantity,OLD.unit_price,OLD.factor_snapshot)
                  OR (OLD.unit_id IS NOT NULL AND (NEW.unit_id,NEW.unit_name_snapshot) IS DISTINCT FROM (OLD.unit_id,OLD.unit_name_snapshot))) THEN
                RAISE EXCEPTION 'Confirmed goods receipt item snapshot is immutable' USING ERRCODE='23514';
              END IF;
              IF TG_OP='UPDATE' THEN RETURN NEW; END IF;
              RETURN OLD;
            END $$;
        ", conn))
        {
            await dropCmd.ExecuteNonQueryAsync();
        }

        // Chạy migration lần 1 và lần 2
        var up1 = await migrator.UpgradeDatabaseSchemaAsync(adminConnStr);
        up1.Success.Should().BeTrue(up1.Message);

        var up2 = await migrator.UpgradeDatabaseSchemaAsync(adminConnStr);
        up2.Success.Should().BeTrue(up2.Message);

        // Kiểm tra toàn diện schema (39 bảng, triggers, FKs, constraints)
        var check = await migrator.InitializeOrBaselineDatabaseAsync(adminConnStr);
        check.Success.Should().BeTrue(check.Message);

        // Kiểm tra dữ liệu cũ giữ nguyên vẹn
        await using var verifyCmd = new NpgsqlCommand(@"
            SELECT status, purchase_order_id FROM public.goods_receipts WHERE id IN (@cId, @dId) ORDER BY number;
        ", conn);
        verifyCmd.Parameters.AddWithValue("cId", legacyConfirmedId);
        verifyCmd.Parameters.AddWithValue("dId", legacyDraftId);
        await using var reader = await verifyCmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetString(0).Should().Be("Confirmed");
        reader.IsDBNull(1).Should().BeTrue();

        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetString(0).Should().Be("Draft");
        reader.IsDBNull(1).Should().BeTrue();
    }

    // =========================================================================
    // AC-02 & AC-03: VÒNG ĐỜI PHIẾU MUA, NHẬP NHIỀU ĐỢT (60 + 40), WAC, ĐÓNG, HỦY, GIÁ 0
    // =========================================================================

    [Fact]
    public async Task AC02_AC03_Lifecycle_MultiBatchReceipt_60Plus40_Wac_Ledger_Close_Cancel_AndZeroPrice()
    {
        await SeedDatabaseAsync();
        var tenantId = OismsDatabaseSeeder.TenantAId;
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        Guid branchId, warehouseId, supplierId, skuId;
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == tenantId);
            var wh = await ctx.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branch.Id);
            var sup = await ctx.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == tenantId);
            branchId = branch.Id;
            warehouseId = wh.Id;
            supplierId = sup.Id;
            skuId = sku.Id;
        }

        // 1. Tạo phiếu mua Draft (ban đầu đặt 80 cái @ 90.000đ)
        using var createMsg = CreatePostWithKey("/api/purchase-orders", new CreatePurchaseOrderDto
        {
            BranchId = branchId,
            SupplierId = supplierId,
            Note = "Phiếu mua ban đầu",
            Items = new List<CreatePurchaseOrderItemDto>
            {
                new() { SkuId = skuId, Quantity = 80m, UnitPrice = 90000m }
            }
        });
        var createRes = await _client.SendAsync(createMsg);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created, await createRes.Content.ReadAsStringAsync());
        var draftPo = (await createRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;
        draftPo.Status.Should().Be("Draft");

        // 2. Sửa phiếu mua khi còn Draft -> nâng lên 100 cái @ 100.000đ
        var updateRes = await _client.PutAsJsonAsync($"/api/purchase-orders/{draftPo.Id}", new UpdatePurchaseOrderDraftDto
        {
            BranchId = branchId,
            SupplierId = supplierId,
            Note = "Phiếu mua 100 cái @ 100k",
            Items = new List<CreatePurchaseOrderItemDto>
            {
                new() { SkuId = skuId, Quantity = 100m, UnitPrice = 100000m }
            }
        });
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK, await updateRes.Content.ReadAsStringAsync());
        var updatedPo = (await updateRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;
        updatedPo.Items.Should().HaveCount(1);
        updatedPo.Items[0].Quantity.Should().Be(100m);
        updatedPo.Items[0].UnitPrice.Should().Be(100000m);

        // Chặn tạo phiếu nhập khi phiếu mua còn Draft -> 409 PURCHASE_ORDER_NOT_RECEIVABLE
        var grBeforeApproveRes = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            PurchaseOrderId = updatedPo.Id,
            WarehouseId = warehouseId,
            Items = new List<CreateGoodsReceiptItemDto>
            {
                new() { PurchaseOrderItemId = updatedPo.Items[0].Id, Quantity = 10m }
            }
        });
        grBeforeApproveRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemAsync(grBeforeApproveRes)).GetProperty("errorCode").GetString().Should().Be("PURCHASE_ORDER_NOT_RECEIVABLE");

        // 3. Owner duyệt phiếu mua
        var approveRes = await _client.PostAsJsonAsync($"/api/purchase-orders/{updatedPo.Id}/approve", new ApprovePurchaseOrderRequestDto());
        approveRes.StatusCode.Should().Be(HttpStatusCode.OK, await approveRes.Content.ReadAsStringAsync());
        var approvedPo = (await approveRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;
        approvedPo.Status.Should().Be("Approved");
        approvedPo.ApprovedAt.Should().NotBeNull();

        // Không được sửa phiếu mua sau khi đã Approved -> 409 INVALID_STATE_TRANSITION
        var editAfterApproveRes = await _client.PutAsJsonAsync($"/api/purchase-orders/{approvedPo.Id}", new UpdatePurchaseOrderDraftDto
        {
            BranchId = branchId,
            SupplierId = supplierId,
            Items = new List<CreatePurchaseOrderItemDto> { new() { SkuId = skuId, Quantity = 50m, UnitPrice = 50000m } }
        });
        editAfterApproveRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemAsync(editAfterApproveRes)).GetProperty("errorCode").GetString().Should().Be("INVALID_STATE_TRANSITION");

        var poItemId = approvedPo.Items[0].Id;

        // 4. Lập và xác nhận phiếu nhập Đợt 1: 60 cái
        var gr1Res = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            PurchaseOrderId = approvedPo.Id,
            WarehouseId = warehouseId,
            Note = "Nhập đợt 1 - 60",
            Items = new List<CreateGoodsReceiptItemDto> { new() { PurchaseOrderItemId = poItemId, Quantity = 60m } }
        });
        gr1Res.StatusCode.Should().Be(HttpStatusCode.Created);
        var gr1 = (await gr1Res.Content.ReadFromJsonAsync<GoodsReceiptResponseDto>(_jsonOptions))!;

        var duplicateResponse = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            PurchaseOrderId = approvedPo.Id,
            WarehouseId = warehouseId,
            Number = gr1.Number,
            Items = [new() { PurchaseOrderItemId = poItemId, Quantity = 1m }]
        });
        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        duplicateResponse.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var duplicateProblem = await ReadProblemAsync(duplicateResponse);
        duplicateProblem.GetProperty("errorCode").GetString().Should().Be("VALIDATION_ERROR");
        duplicateProblem.GetProperty("title").GetString().Should().Be("Xung đột dữ liệu");
        duplicateProblem.GetProperty("message").GetString().Should().Be(duplicateProblem.GetProperty("detail").GetString());

        var cf1Res = await _client.PostAsync($"/api/goods-receipts/{gr1.Id}/confirm", null);
        cf1Res.StatusCode.Should().Be(HttpStatusCode.OK, await cf1Res.Content.ReadAsStringAsync());

        // Kiểm tra phiếu mua chuyển sang PartiallyReceived, đã nhận 60, còn lại 40
        var poAfterBatch1 = (await _client.GetFromJsonAsync<PurchaseOrderResponseDto>($"/api/purchase-orders/{approvedPo.Id}", _jsonOptions))!;
        poAfterBatch1.Status.Should().Be("PartiallyReceived");
        poAfterBatch1.Items[0].ReceivedBaseQuantity.Should().Be(60m);
        poAfterBatch1.Items[0].RemainingBaseQuantity.Should().Be(40m);

        // Chặn hủy phiếu mua khi đã nhận 1 phần -> 409 INVALID_STATE_TRANSITION
        var cancelPartialRes = await _client.PostAsync($"/api/purchase-orders/{approvedPo.Id}/cancel", null);
        cancelPartialRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemAsync(cancelPartialRes)).GetProperty("errorCode").GetString().Should().Be("INVALID_STATE_TRANSITION");

        // 5. Lập và xác nhận phiếu nhập Đợt 2: 40 cái
        var gr2Res = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            PurchaseOrderId = approvedPo.Id,
            WarehouseId = warehouseId,
            Note = "Nhập đợt 2 - 40",
            Items = new List<CreateGoodsReceiptItemDto> { new() { PurchaseOrderItemId = poItemId, Quantity = 40m } }
        });
        gr2Res.StatusCode.Should().Be(HttpStatusCode.Created);
        var gr2 = (await gr2Res.Content.ReadFromJsonAsync<GoodsReceiptResponseDto>(_jsonOptions))!;

        var cf2Res = await _client.PostAsync($"/api/goods-receipts/{gr2.Id}/confirm", null);
        cf2Res.StatusCode.Should().Be(HttpStatusCode.OK, await cf2Res.Content.ReadAsStringAsync());

        // Kiểm tra phiếu mua chuyển sang Received, tồn kho = 100, WAC = 100.000, sổ kho 2 dòng IN
        var poAfterBatch2 = (await _client.GetFromJsonAsync<PurchaseOrderResponseDto>($"/api/purchase-orders/{approvedPo.Id}", _jsonOptions))!;
        poAfterBatch2.Status.Should().Be("Received");
        poAfterBatch2.Items[0].ReceivedBaseQuantity.Should().Be(100m);
        poAfterBatch2.Items[0].RemainingBaseQuantity.Should().Be(0m);

        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var bal = await ctx.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bal.OnHand.Should().Be(100m);
            bal.AverageCost.Should().Be(100000m);
            bal.LastSequence.Should().Be(2);

            var whBal = await ctx.WarehouseInventoryBalances.FirstAsync(w => w.WarehouseId == warehouseId && w.SkuId == skuId);
            whBal.OnHand.Should().Be(100m);
            whBal.LastSequence.Should().Be(2);

            var txs = await ctx.InventoryTransactions
                .Where(t => t.BranchId == branchId && t.SkuId == skuId)
                .OrderBy(t => t.SequenceNo)
                .ToListAsync();
            txs.Should().HaveCount(2);
            txs[0].Quantity.Should().Be(60m);
            txs[0].UnitCost.Should().Be(100000m);
            txs[0].BalanceAfter.Should().Be(60m);
            txs[1].Quantity.Should().Be(40m);
            txs[1].UnitCost.Should().Be(100000m);
            txs[1].BalanceAfter.Should().Be(100m);
        }

        // 6. Kiểm tra Đóng phần còn lại (Close): tạo PO 50 cái, nhận 20 cái -> PartiallyReceived -> Close có lý do -> Closed
        var poToClose = await PurchaseOrderSeed.CreateApprovedPoViaApiAsync(_client, branchId, supplierId, (skuId, 50m, 110000m, null));
        var grPartialRes = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            PurchaseOrderId = poToClose.Id,
            WarehouseId = warehouseId,
            Items = new List<CreateGoodsReceiptItemDto> { new() { PurchaseOrderItemId = poToClose.Items[0].Id, Quantity = 20m } }
        });
        var grPartial = (await grPartialRes.Content.ReadFromJsonAsync<GoodsReceiptResponseDto>(_jsonOptions))!;
        (await _client.PostAsync($"/api/goods-receipts/{grPartial.Id}/confirm", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Close không có lý do -> 400 BadRequest
        var closeNoReasonRes = await _client.PostAsJsonAsync($"/api/purchase-orders/{poToClose.Id}/close", new ClosePurchaseOrderRequestDto { Reason = "   " });
        closeNoReasonRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var closeRes = await _client.PostAsJsonAsync($"/api/purchase-orders/{poToClose.Id}/close", new ClosePurchaseOrderRequestDto { Reason = "Nhà cung cấp hết hàng phần còn lại" });
        closeRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var closedPo = (await closeRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;
        closedPo.Status.Should().Be("Closed");
        closedPo.CloseReason.Should().Be("Nhà cung cấp hết hàng phần còn lại");

        // Không được nhập tiếp từ PO đã Closed -> 409 PURCHASE_ORDER_NOT_RECEIVABLE
        var grAfterCloseRes = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            PurchaseOrderId = poToClose.Id,
            WarehouseId = warehouseId,
            Items = new List<CreateGoodsReceiptItemDto> { new() { PurchaseOrderItemId = poToClose.Items[0].Id, Quantity = 10m } }
        });
        grAfterCloseRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemAsync(grAfterCloseRes)).GetProperty("errorCode").GetString().Should().Be("PURCHASE_ORDER_NOT_RECEIVABLE");

        // 7. Kiểm tra Hủy PO ở trạng thái Draft và Approved (chưa nhận hàng)
        using var draftCancelMsg = CreatePostWithKey("/api/purchase-orders", new CreatePurchaseOrderDto
        {
            BranchId = branchId,
            SupplierId = supplierId,
            Items = new List<CreatePurchaseOrderItemDto> { new() { SkuId = skuId, Quantity = 10m, UnitPrice = 80000m } }
        });
        var poCancelDraft = (await (await _client.SendAsync(draftCancelMsg)).Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;
        var cancelDraftRes = await _client.PostAsync($"/api/purchase-orders/{poCancelDraft.Id}/cancel", null);
        cancelDraftRes.StatusCode.Should().Be(HttpStatusCode.OK);
        (await cancelDraftRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!.Status.Should().Be("Cancelled");

        var poCancelApproved = await PurchaseOrderSeed.CreateApprovedPoViaApiAsync(_client, branchId, supplierId, (skuId, 10m, 80000m, null));
        var cancelApprovedRes = await _client.PostAsync($"/api/purchase-orders/{poCancelApproved.Id}/cancel", null);
        cancelApprovedRes.StatusCode.Should().Be(HttpStatusCode.OK);
        (await cancelApprovedRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!.Status.Should().Be("Cancelled");

        // 8. Kiểm tra Giá 0 (Zero-Price Confirmation)
        using var zeroPriceMsg = CreatePostWithKey("/api/purchase-orders", new CreatePurchaseOrderDto
        {
            BranchId = branchId,
            SupplierId = supplierId,
            Note = "Hàng tặng kèm giá 0",
            Items = new List<CreatePurchaseOrderItemDto> { new() { SkuId = skuId, Quantity = 5m, UnitPrice = 0m } }
        });
        var zeroPo = (await (await _client.SendAsync(zeroPriceMsg)).Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;

        var approveZeroWithoutConfirm = await _client.PostAsJsonAsync($"/api/purchase-orders/{zeroPo.Id}/approve", new ApprovePurchaseOrderRequestDto { ConfirmZeroPrice = false });
        approveZeroWithoutConfirm.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemAsync(approveZeroWithoutConfirm)).GetProperty("errorCode").GetString().Should().Be("PURCHASE_ORDER_ZERO_PRICE_CONFIRM_REQUIRED");

        var approveZeroWithConfirm = await _client.PostAsJsonAsync($"/api/purchase-orders/{zeroPo.Id}/approve", new ApprovePurchaseOrderRequestDto { ConfirmZeroPrice = true });
        approveZeroWithConfirm.StatusCode.Should().Be(HttpStatusCode.OK);
        (await approveZeroWithConfirm.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!.Status.Should().Be("Approved");
    }

    // =========================================================================
    // AC-03: CHẶN NHẬP DƯ Ở CẢ BƯỚC TẠO VÀ XÁC NHẬN, GỬI UNITPRICE/SUPPLIERID VÔ HIỆU
    // =========================================================================

    [Fact]
    public async Task AC03_OverReceiptBlocked_AtCreateAndConfirm_AndClientPriceSupplierIgnored()
    {
        await SeedDatabaseAsync();
        var tenantId = OismsDatabaseSeeder.TenantAId;
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        Guid branchId, warehouseId, supplierId, skuId;
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == tenantId);
            var wh = await ctx.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branch.Id);
            var sup = await ctx.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == tenantId);
            branchId = branch.Id;
            warehouseId = wh.Id;
            supplierId = sup.Id;
            skuId = sku.Id;
        }

        // Phiếu mua đặt 100 cái @ 120.000đ
        var po = await PurchaseOrderSeed.CreateApprovedPoViaApiAsync(_client, branchId, supplierId, (skuId, 100m, 120000m, null));
        var poItemId = po.Items[0].Id;

        // Dòng của phiếu mua khác và dòng trùng đều bị từ chối
        var otherPo = await PurchaseOrderSeed.CreateApprovedPoViaApiAsync(_client, branchId, supplierId, (skuId, 10m, 120000m, null));
        var foreignLineRes = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            PurchaseOrderId = po.Id,
            WarehouseId = warehouseId,
            Items = new List<CreateGoodsReceiptItemDto> { new() { PurchaseOrderItemId = otherPo.Items[0].Id, Quantity = 1m } }
        });
        foreignLineRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemAsync(foreignLineRes)).GetProperty("errorCode").GetString().Should().Be("PURCHASE_ORDER_ITEM_MISMATCH");

        var duplicateLineRes = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            PurchaseOrderId = po.Id,
            WarehouseId = warehouseId,
            Items = new List<CreateGoodsReceiptItemDto>
            {
                new() { PurchaseOrderItemId = poItemId, Quantity = 1m },
                new() { PurchaseOrderItemId = poItemId, Quantity = 1m }
            }
        });
        duplicateLineRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemAsync(duplicateLineRes)).GetProperty("errorCode").GetString().Should().Be("PURCHASE_ORDER_ITEM_MISMATCH");

        // Gửi lại cùng key với dòng rỗng phải trả 400, không phải 500
        var nullLineKey = Guid.NewGuid();
        var nullLineFirst = new HttpRequestMessage(HttpMethod.Post, "/api/purchase-orders")
        {
            Content = JsonContent.Create(new { supplierId, branchId, items = new[] { new { skuId, quantity = 1m, unitPrice = 1m } } })
        };
        nullLineFirst.Headers.Add("Idempotency-Key", nullLineKey.ToString());
        (await _client.SendAsync(nullLineFirst)).StatusCode.Should().Be(HttpStatusCode.Created);
        var nullLineReplay = new HttpRequestMessage(HttpMethod.Post, "/api/purchase-orders")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { supplierId, branchId, items = new object?[] { null } }), System.Text.Encoding.UTF8, "application/json")
        };
        nullLineReplay.Headers.Add("Idempotency-Key", nullLineKey.ToString());
        (await _client.SendAsync(nullLineReplay)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 1. Tạo phiếu nhập vượt lượng đặt (101 > 100) -> bị chặn ngay bước tạo (409 PURCHASE_ORDER_OVER_RECEIPT)
        var overCreateRes = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            PurchaseOrderId = po.Id,
            WarehouseId = warehouseId,
            Items = new List<CreateGoodsReceiptItemDto> { new() { PurchaseOrderItemId = poItemId, Quantity = 101m } }
        });
        overCreateRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemAsync(overCreateRes)).GetProperty("errorCode").GetString().Should().Be("PURCHASE_ORDER_OVER_RECEIPT");

        // 2. Tạo 2 phiếu nhập nháp: Phiếu A (60 cái) có gửi kèm unitPrice = 1, supplierId giả mạo trong JSON body; Phiếu B (50 cái)
        var rawJsonWithTamperedFields = JsonSerializer.Serialize(new
        {
            purchaseOrderId = po.Id,
            warehouseId,
            supplierId = Guid.NewGuid(),
            note = "Thử gửi unitPrice và supplierId giả mạo",
            items = new[]
            {
                new
                {
                    purchaseOrderItemId = poItemId,
                    skuId = Guid.NewGuid(),
                    quantity = 60m,
                    unitPrice = 1m
                }
            }
        });

        var grARes = await _client.PostAsync("/api/goods-receipts", new StringContent(rawJsonWithTamperedFields, Encoding.UTF8, "application/json"));
        grARes.StatusCode.Should().Be(HttpStatusCode.Created, await grARes.Content.ReadAsStringAsync());
        var grA = (await grARes.Content.ReadFromJsonAsync<GoodsReceiptResponseDto>(_jsonOptions))!;

        // Kiểm tra supplierId và unitPrice trên phiếu nhập A hoàn toàn lấy từ PO (120.000đ), bỏ qua giá trị client gửi
        grA.SupplierId.Should().Be(supplierId);
        grA.Items[0].SkuId.Should().Be(skuId);
        grA.Items[0].UnitPrice.Should().Be(120000m);

        var grBRes = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            PurchaseOrderId = po.Id,
            WarehouseId = warehouseId,
            Note = "Phiếu nháp B 50 cái",
            Items = new List<CreateGoodsReceiptItemDto> { new() { PurchaseOrderItemId = poItemId, Quantity = 50m } }
        });
        grBRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var grB = (await grBRes.Content.ReadFromJsonAsync<GoodsReceiptResponseDto>(_jsonOptions))!;

        // 3. Xác nhận Phiếu A (60 cái) -> thành công
        var confirmARes = await _client.PostAsync($"/api/goods-receipts/{grA.Id}/confirm", null);
        confirmARes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Xác nhận Phiếu B (50 cái, tổng 60 + 50 = 110 > 100) -> bị chặn 409 PURCHASE_ORDER_OVER_RECEIPT
        var confirmBRes = await _client.PostAsync($"/api/goods-receipts/{grB.Id}/confirm", null);
        confirmBRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemAsync(confirmBRes)).GetProperty("errorCode").GetString().Should().Be("PURCHASE_ORDER_OVER_RECEIPT");

        // Kiểm tra tồn kho, WAC, sổ kho và dòng phiếu mua giữ nguyên ở mức 60 cái @ 120.000đ
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var bal = await ctx.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bal.OnHand.Should().Be(60m);
            bal.AverageCost.Should().Be(120000m);
            bal.LastSequence.Should().Be(1);

            var txCount = await ctx.InventoryTransactions.CountAsync(t => t.BranchId == branchId && t.SkuId == skuId);
            txCount.Should().Be(1);

            var dbPoItem = await ctx.PurchaseOrderItems.FirstAsync(i => i.Id == poItemId);
            dbPoItem.ReceivedBaseQuantity.Should().Be(60m);

            var dbGrB = await ctx.GoodsReceipts.FirstAsync(r => r.Id == grB.Id);
            dbGrB.Status.Should().Be("Draft");
        }
    }

    // =========================================================================
    // AC-04: MUA THEO ĐƠN VỊ QUY ĐỔI & ĐỔI HỆ SỐ SKU SAU KHI DUYỆT KHÔNG ẢNH HƯỞNG PHIẾU
    // =========================================================================

    [Fact]
    public async Task AC04_MultiUnitPurchase_FactorSnapshotPreserved_WhenSkuUnitFactorChangesAfterApproval()
    {
        var runCode = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        await _client.PostAsJsonAsync("/api/auth/register-tenant", new RegisterTenantRequest
        {
            TenantName = $"Shop MultiUnit PO {runCode}",
            TenantCode = $"MUPO_{runCode}",
            FullName = "Owner MultiUnit",
            Email = $"mupo_{runCode}@shop.local",
            Password = "Password@123"
        });

        var token = await LoginAsAsync($"mupo_{runCode}@shop.local", "Password@123");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var branchId = (await (await _client.PostAsJsonAsync("/api/branches", new { Name = "CN Chính" }))
            .Content.ReadFromJsonAsync<JsonElement>(_jsonOptions)).GetProperty("id").GetGuid();
        var warehouseId = (await (await _client.PostAsJsonAsync("/api/warehouses", new { BranchId = branchId, Name = "Kho Chính" }))
            .Content.ReadFromJsonAsync<JsonElement>(_jsonOptions)).GetProperty("id").GetGuid();
        var supplierId = (await (await _client.PostAsJsonAsync("/api/suppliers", new { Name = "NCC Gạo" }))
            .Content.ReadFromJsonAsync<JsonElement>(_jsonOptions)).GetProperty("id").GetGuid();

        var units = (await _client.GetFromJsonAsync<List<UnitResponse>>("/api/units", _jsonOptions))!;
        var kgId = units.Single(u => u.Code == "kg").Id;
        var baoId = units.Single(u => u.Code == "bao").Id;

        var createProd = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Gạo ST25",
            Variants = new()
            {
                new CreateVariantRequest
                {
                    SKU = $"GAO-{runCode}",
                    VariantName = "Gạo ST25",
                    BaseUnitId = kgId,
                    Unit = "kg",
                    RetailPrice = 25000m,
                    WholesalePrice = 22000m
                }
            }
        });
        var prodId = (await createProd.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions)).GetProperty("productId").GetGuid();
        var prod = (await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{prodId}", _jsonOptions))!;
        var skuId = prod.Variants.Single().Id;

        // Thêm đơn vị quy đổi: 1 bao = 25 kg
        var addBaoRes = await _client.PostAsJsonAsync($"/api/products/{prodId}/variants/{skuId}/units", new CreateSkuUnitRequest
        {
            UnitId = baoId,
            FactorToBase = 25m,
            CanPurchase = true,
            CanSell = true,
            QuantityDecimalPlaces = 0
        });
        addBaoRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var baoSkuUnit = (await addBaoRes.Content.ReadFromJsonAsync<SkuUnitResponse>(_jsonOptions))!;

        // 1. Tạo và duyệt phiếu mua 2 bao (hệ số 25 -> 50 kg) với đơn giá 500.000đ/bao (20.000đ/kg)
        var po = await PurchaseOrderSeed.CreateApprovedPoViaApiAsync(_client, branchId, supplierId, (skuId, 2m, 500000m, baoId));
        po.Items[0].FactorSnapshot.Should().Be(25m);
        po.Items[0].OrderedBaseQuantity.Should().Be(50m);

        // 2. Nhập đợt 1: 1 bao (25 kg)
        var gr1Res = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            PurchaseOrderId = po.Id,
            WarehouseId = warehouseId,
            Items = new List<CreateGoodsReceiptItemDto> { new() { PurchaseOrderItemId = po.Items[0].Id, Quantity = 1m } }
        });
        var gr1 = (await gr1Res.Content.ReadFromJsonAsync<GoodsReceiptResponseDto>(_jsonOptions))!;
        (await _client.PostAsync($"/api/goods-receipts/{gr1.Id}/confirm", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Đổi hệ số của bao trên SKU từ 25 sang 50
        var updateFactorRes = await _client.PutAsJsonAsync(
            $"/api/products/{prodId}/variants/{skuId}/units/{baoSkuUnit.Id}",
            new UpdateSkuUnitRequest { FactorToBase = 50m });
        updateFactorRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Kiểm tra phiếu mua cũ vẫn giữ nguyên hệ số 25, lượng còn lại = 25 kg (1 bao)
        var poCheck = (await _client.GetFromJsonAsync<PurchaseOrderResponseDto>($"/api/purchase-orders/{po.Id}", _jsonOptions))!;
        poCheck.Items[0].FactorSnapshot.Should().Be(25m);
        poCheck.Items[0].ReceivedBaseQuantity.Should().Be(25m);
        poCheck.Items[0].RemainingBaseQuantity.Should().Be(25m);

        // 5. Nhập đợt 2: 1 bao còn lại -> chỉ cộng đúng 25 kg (tổng 50 kg) với giá vốn 20.000đ/kg
        var gr2Res = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            PurchaseOrderId = po.Id,
            WarehouseId = warehouseId,
            Items = new List<CreateGoodsReceiptItemDto> { new() { PurchaseOrderItemId = po.Items[0].Id, Quantity = 1m } }
        });
        gr2Res.StatusCode.Should().Be(HttpStatusCode.Created);
        var gr2 = (await gr2Res.Content.ReadFromJsonAsync<GoodsReceiptResponseDto>(_jsonOptions))!;
        gr2.Items[0].FactorSnapshot.Should().Be(25m);
        gr2.Items[0].BaseQuantity.Should().Be(25m);

        var cf2Res = await _client.PostAsync($"/api/goods-receipts/{gr2.Id}/confirm", null);
        cf2Res.StatusCode.Should().Be(HttpStatusCode.OK);

        var balances = (await _client.GetFromJsonAsync<PagedResult<InventoryBalanceDto>>($"/api/inventory/balances?branchId={branchId}", _jsonOptions))!;
        var skuBal = balances.Items.Single(b => b.SkuId == skuId);
        skuBal.OnHand.Should().Be(50m);
        skuBal.AverageCost.Should().Be(20000m);
    }

    // =========================================================================
    // AC-05: ĐỒNG THỜI TRÊN POSTGRESQL THẬT (10 PHIẾU 20 KG TRÊN DÒNG 100 KG + ĐUA CLOSE/CANCEL)
    // =========================================================================

    [Fact]
    public async Task AC05_Concurrency_10ReceiptsOf20Kg_On100KgPoLine_Exactly5Succeed_5ReturnOverReceipt_AndConcurrentCloseCancelConsistent()
    {
        await SeedDatabaseAsync();
        var tenantId = OismsDatabaseSeeder.TenantAId;
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        Guid branchId, warehouseId, supplierId, skuId;
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == tenantId);
            var wh = await ctx.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branch.Id);
            var sup = await ctx.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == tenantId);
            branchId = branch.Id;
            warehouseId = wh.Id;
            supplierId = sup.Id;
            skuId = sku.Id;
        }

        // 1. Tạo phiếu mua 100 kg @ 50.000đ và 10 phiếu nhập nháp mỗi phiếu 20 kg
        var po = await PurchaseOrderSeed.CreateApprovedPoViaApiAsync(_client, branchId, supplierId, (skuId, 100m, 50000m, null));
        var poItemId = po.Items[0].Id;

        var draftReceiptIds = new List<Guid>();
        for (int i = 0; i < 10; i++)
        {
            var grRes = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
            {
                PurchaseOrderId = po.Id,
                WarehouseId = warehouseId,
                Note = $"Concurrent receipt #{i + 1}",
                Items = new List<CreateGoodsReceiptItemDto> { new() { PurchaseOrderItemId = poItemId, Quantity = 20m } }
            });
            grRes.StatusCode.Should().Be(HttpStatusCode.Created);
            var gr = (await grRes.Content.ReadFromJsonAsync<GoodsReceiptResponseDto>(_jsonOptions))!;
            draftReceiptIds.Add(gr.Id);
        }

        // 2. Xác nhận đồng thời cả 10 phiếu nhập trên PostgreSQL thật
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmTasks = draftReceiptIds.Select(async receiptId =>
        {
            using var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
            await gate.Task;
            var res = await client.PostAsync($"/api/goods-receipts/{receiptId}/confirm", null);
            var body = await res.Content.ReadAsStringAsync();
            return (res.StatusCode, Body: body);
        }).ToList();

        gate.SetResult();
        var results = await Task.WhenAll(confirmTasks);

        // Không có bất kỳ lỗi 500 nào
        results.Should().NotContain(r => r.StatusCode == HttpStatusCode.InternalServerError);

        var okResults = results.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();
        var conflictResults = results.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();

        okResults.Should().HaveCount(5, "100 kg chia cho 20 kg mỗi phiếu chỉ cho phép đúng 5 phiếu thành công");
        conflictResults.Should().HaveCount(5, "5 phiếu còn lại phải bị chặn bởi PURCHASE_ORDER_OVER_RECEIPT");

        foreach (var failed in conflictResults)
        {
            using var doc = JsonDocument.Parse(failed.Body);
            doc.RootElement.GetProperty("errorCode").GetString().Should().Be("PURCHASE_ORDER_OVER_RECEIPT");
        }

        // Kiểm tra tồn kho = 100, ReceivedBaseQuantity = 100, PO Status = Received, sổ kho đúng 5 dòng
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var bal = await ctx.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuId);
            bal.OnHand.Should().Be(100m);
            bal.LastSequence.Should().Be(5);

            var dbPo = await ctx.PurchaseOrders.Include(p => p.Items).FirstAsync(p => p.Id == po.Id);
            dbPo.Status.Should().Be("Received");
            dbPo.Items.Single().ReceivedBaseQuantity.Should().Be(100m);

            var ledgerCount = await ctx.InventoryTransactions.CountAsync(t => t.BranchId == branchId && t.SkuId == skuId);
            ledgerCount.Should().Be(5);
        }

        // 3. Đua đồng thời giữa ConfirmReceipt và CancelPurchaseOrder trên 1 PO Approved mới
        var poRaceCancel = await PurchaseOrderSeed.CreateApprovedPoViaApiAsync(_client, branchId, supplierId, (skuId, 30m, 50000m, null));
        var grRaceCancelRes = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            PurchaseOrderId = poRaceCancel.Id,
            WarehouseId = warehouseId,
            Items = new List<CreateGoodsReceiptItemDto> { new() { PurchaseOrderItemId = poRaceCancel.Items[0].Id, Quantity = 10m } }
        });
        var grRaceCancel = (await grRaceCancelRes.Content.ReadFromJsonAsync<GoodsReceiptResponseDto>(_jsonOptions))!;

        var cancelGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmTask = Task.Run(async () =>
        {
            using var c = _factory.CreateClient();
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
            await cancelGate.Task;
            return await c.PostAsync($"/api/goods-receipts/{grRaceCancel.Id}/confirm", null);
        });
        var cancelTask = Task.Run(async () =>
        {
            using var c = _factory.CreateClient();
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
            await cancelGate.Task;
            return await c.PostAsync($"/api/purchase-orders/{poRaceCancel.Id}/cancel", null);
        });
        cancelGate.SetResult();
        var raceCancelResponses = await Task.WhenAll(confirmTask, cancelTask);

        raceCancelResponses.Should().NotContain(r => r.StatusCode == HttpStatusCode.InternalServerError);
        raceCancelResponses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1, "Chỉ 1 trong 2 thao tác (Confirm hoặc Cancel) được phép thắng");
        raceCancelResponses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);

        // 4. Đua đồng thời giữa ConfirmReceipt (nhận nốt 20) và ClosePurchaseOrder trên PO PartiallyReceived
        var poRaceClose = await PurchaseOrderSeed.CreateApprovedPoViaApiAsync(_client, branchId, supplierId, (skuId, 40m, 50000m, null));
        var grInitRes = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            PurchaseOrderId = poRaceClose.Id,
            WarehouseId = warehouseId,
            Items = new List<CreateGoodsReceiptItemDto> { new() { PurchaseOrderItemId = poRaceClose.Items[0].Id, Quantity = 20m } }
        });
        var grInit = (await grInitRes.Content.ReadFromJsonAsync<GoodsReceiptResponseDto>(_jsonOptions))!;
        (await _client.PostAsync($"/api/goods-receipts/{grInit.Id}/confirm", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var grSecondRes = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            PurchaseOrderId = poRaceClose.Id,
            WarehouseId = warehouseId,
            Items = new List<CreateGoodsReceiptItemDto> { new() { PurchaseOrderItemId = poRaceClose.Items[0].Id, Quantity = 20m } }
        });
        var grSecond = (await grSecondRes.Content.ReadFromJsonAsync<GoodsReceiptResponseDto>(_jsonOptions))!;

        var closeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmSecondTask = Task.Run(async () =>
        {
            using var c = _factory.CreateClient();
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
            await closeGate.Task;
            return await c.PostAsync($"/api/goods-receipts/{grSecond.Id}/confirm", null);
        });
        var closePoTask = Task.Run(async () =>
        {
            using var c = _factory.CreateClient();
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
            await closeGate.Task;
            return await c.PostAsJsonAsync($"/api/purchase-orders/{poRaceClose.Id}/close", new ClosePurchaseOrderRequestDto { Reason = "Đóng đồng thời" });
        });
        closeGate.SetResult();
        var raceCloseResponses = await Task.WhenAll(confirmSecondTask, closePoTask);

        raceCloseResponses.Should().NotContain(r => r.StatusCode == HttpStatusCode.InternalServerError);
        raceCloseResponses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        raceCloseResponses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
    }

    // =========================================================================
    // AC-02 & AC-06: IDEMPOTENCY, RETRY, TRUY VẾT SỔ KHO, TENANT ISOLATION, RBAC & DB GUARDS
    // =========================================================================

    [Fact]
    public async Task AC02_AC06_Idempotency_Traceability_Rbac_TenantIsolation_AndRuntimeDbGuards()
    {
        await SeedDatabaseAsync();
        var tenantA = OismsDatabaseSeeder.TenantAId;
        var tenantB = OismsDatabaseSeeder.TenantBId;

        var ownerAToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        var staffAToken = await LoginAsAsync("staff@shopa.local", PostgresTestEnvironment.AppPassword);
        var cashierAToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);
        var ownerBToken = await LoginAsAsync("owner@shopb.local", PostgresTestEnvironment.AppPassword);

        Guid branchAId, warehouseAId, supplierAId, skuAId;
        await using (var ctx = CreatePostgresDbContext(tenantA).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == tenantA);
            var wh = await ctx.Warehouses.FirstAsync(w => w.TenantId == tenantA && w.BranchId == branch.Id);
            var sup = await ctx.Suppliers.FirstAsync(s => s.TenantId == tenantA);
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == tenantA);
            branchAId = branch.Id;
            warehouseAId = wh.Id;
            supplierAId = sup.Id;
            skuAId = sku.Id;
        }

        // 1. Idempotency khi tạo PO: cùng key & cùng payload trả 200 OK cùng phiếu; khác payload trả 409 IDEMPOTENCY_PAYLOAD_MISMATCH
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffAToken);
        var idemKey = Guid.NewGuid();
        var poPayload = new CreatePurchaseOrderDto
        {
            BranchId = branchAId,
            SupplierId = supplierAId,
            Note = "Phiếu do Staff tạo",
            Items = new List<CreatePurchaseOrderItemDto> { new() { SkuId = skuAId, Quantity = 15m, UnitPrice = 70000m } }
        };

        using var req1 = CreatePostWithKey("/api/purchase-orders", poPayload, idemKey);
        var res1 = await _client.SendAsync(req1);
        res1.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdPo = (await res1.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;

        using var req2 = CreatePostWithKey("/api/purchase-orders", poPayload, idemKey);
        var res2 = await _client.SendAsync(req2);
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayedPo = (await res2.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;
        replayedPo.Id.Should().Be(createdPo.Id);

        using var reqMismatch = CreatePostWithKey("/api/purchase-orders", new CreatePurchaseOrderDto
        {
            BranchId = branchAId,
            SupplierId = supplierAId,
            Note = "Phiếu do Staff tạo",
            Items = new List<CreatePurchaseOrderItemDto> { new() { SkuId = skuAId, Quantity = 25m, UnitPrice = 70000m } }
        }, idemKey);
        var resMismatch = await _client.SendAsync(reqMismatch);
        resMismatch.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemAsync(resMismatch)).GetProperty("errorCode").GetString().Should().Be("IDEMPOTENCY_PAYLOAD_MISMATCH");

        // 2. RBAC: Staff gọi approve, close, cancel bị chặn 403; Cashier gọi bất kỳ endpoint PO nào bị chặn 403
        (await _client.PostAsJsonAsync($"/api/purchase-orders/{createdPo.Id}/approve", new ApprovePurchaseOrderRequestDto())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.PostAsJsonAsync($"/api/purchase-orders/{createdPo.Id}/close", new ClosePurchaseOrderRequestDto { Reason = "Test" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.PostAsync($"/api/purchase-orders/{createdPo.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierAToken);
        (await _client.GetAsync("/api/purchase-orders")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client.GetAsync($"/api/purchase-orders/{createdPo.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var cashierPost = CreatePostWithKey("/api/purchase-orders", poPayload);
        (await _client.SendAsync(cashierPost)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 3. Owner duyệt phiếu do Staff tạo
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerAToken);
        var ownerApproveRes = await _client.PostAsJsonAsync($"/api/purchase-orders/{createdPo.Id}/approve", new ApprovePurchaseOrderRequestDto());
        ownerApproveRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Tenant Isolation: Tenant B truy cập hoặc nhập kho từ PO của Tenant A đều nhận 404 NotFound
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerBToken);
        (await _client.GetAsync($"/api/purchase-orders/{createdPo.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.PostAsync($"/api/purchase-orders/{createdPo.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        Guid warehouseBId;
        await using (var ctxB = CreatePostgresDbContext(tenantB).Context)
        {
            warehouseBId = (await ctxB.Warehouses.FirstAsync(w => w.TenantId == tenantB)).Id;
        }
        var crossTenantGrRes = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            PurchaseOrderId = createdPo.Id,
            WarehouseId = warehouseBId,
            Items = new List<CreateGoodsReceiptItemDto> { new() { PurchaseOrderItemId = createdPo.Items[0].Id, Quantity = 5m } }
        });
        crossTenantGrRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 5. Truy vết (Traceability): từ dòng IN trong inventory_transactions -> receipt_item_id -> purchase_order_item_id -> purchase_order_id
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffAToken);
        var grRes = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            PurchaseOrderId = createdPo.Id,
            WarehouseId = warehouseAId,
            Items = new List<CreateGoodsReceiptItemDto> { new() { PurchaseOrderItemId = createdPo.Items[0].Id, Quantity = 15m } }
        });
        var gr = (await grRes.Content.ReadFromJsonAsync<GoodsReceiptResponseDto>(_jsonOptions))!;
        (await _client.PostAsync($"/api/goods-receipts/{gr.Id}/confirm", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Retry xác nhận lần 2 không cộng tồn lần 2, trả 409 INVALID_STATE_TRANSITION
        var retryConfirmRes = await _client.PostAsync($"/api/goods-receipts/{gr.Id}/confirm", null);
        retryConfirmRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemAsync(retryConfirmRes)).GetProperty("errorCode").GetString().Should().Be("INVALID_STATE_TRANSITION");

        await using (var ctxA = CreatePostgresDbContext(tenantA).Context)
        {
            var inTx = await ctxA.InventoryTransactions
                .Include(t => t.ReceiptItem)
                    .ThenInclude(ri => ri!.PurchaseOrderItem)
                .SingleAsync(t => t.ReceiptItemId == gr.Items[0].Id);

            inTx.Type.Should().Be("IN");
            inTx.ReceiptItem.Should().NotBeNull();
            inTx.ReceiptItem!.PurchaseOrderItemId.Should().Be(createdPo.Items[0].Id);
            inTx.ReceiptItem.PurchaseOrderItem!.PurchaseOrderId.Should().Be(createdPo.Id);
        }

        // 6. Database Guards với role oisms_runtime: chặn UPDATE giá trên PO đã duyệt, chặn DELETE, chặn received_base_quantity > ordered_base_quantity
        await using var runtimeConn = new NpgsqlConnection(PostgresTestEnvironment.RuntimeConnectionString);
        await runtimeConn.OpenAsync();

        await using (var cmdTamperPrice = new NpgsqlCommand(
            "UPDATE public.purchase_order_items SET unit_price = 1 WHERE id = @id;", runtimeConn))
        {
            cmdTamperPrice.Parameters.AddWithValue("id", createdPo.Items[0].Id);
            Func<Task> actTamperPrice = async () => await cmdTamperPrice.ExecuteNonQueryAsync();
            await actTamperPrice.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "23514");
        }

        await using (var cmdDeletePo = new NpgsqlCommand(
            "DELETE FROM public.purchase_orders WHERE id = @id;", runtimeConn))
        {
            cmdDeletePo.Parameters.AddWithValue("id", createdPo.Id);
            Func<Task> actDeletePo = async () => await cmdDeletePo.ExecuteNonQueryAsync();
            await actDeletePo.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "23514" || e.SqlState == "42501");
        }

        await using (var cmdOverReceiveSql = new NpgsqlCommand(
            "UPDATE public.purchase_order_items SET received_base_quantity = 999999 WHERE id = @id;", runtimeConn))
        {
            cmdOverReceiveSql.Parameters.AddWithValue("id", createdPo.Items[0].Id);
            Func<Task> actOverReceiveSql = async () => await cmdOverReceiveSql.ExecuteNonQueryAsync();
            await actOverReceiveSql.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "23514");
        }

        await using (var cmdUnlinkConfirmedItem = new NpgsqlCommand(
            "UPDATE public.goods_receipt_items SET purchase_order_item_id = NULL WHERE id = @id;", runtimeConn))
        {
            cmdUnlinkConfirmedItem.Parameters.AddWithValue("id", gr.Items[0].Id);
            Func<Task> actUnlinkConfirmedItem = async () => await cmdUnlinkConfirmedItem.ExecuteNonQueryAsync();
            await actUnlinkConfirmedItem.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "23514");
        }
    }

    // =========================================================================
    // AC-07: PHIẾU NHÁP CŨ KHÔNG GẮN PO TRẢ PURCHASE_ORDER_REQUIRED KHI CONFIRM & HỦY ĐƯỢC
    // =========================================================================

    [Fact]
    public async Task AC07_LegacyDraftReceiptWithoutPo_ConfirmReturnsPurchaseOrderRequired_AndCanBeCancelled()
    {
        await SeedDatabaseAsync();
        var tenantId = OismsDatabaseSeeder.TenantAId;
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        Guid branchId, warehouseId, supplierId, userId, skuId;
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == tenantId);
            var wh = await ctx.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branch.Id);
            var sup = await ctx.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            var usr = await ctx.Users.FirstAsync(u => u.TenantId == tenantId && u.Role == "Owner");
            var sku = await ctx.ProductVariants.FirstAsync(v => v.TenantId == tenantId);
            branchId = branch.Id;
            warehouseId = wh.Id;
            supplierId = sup.Id;
            userId = usr.Id;
            skuId = sku.Id;
        }

        // Chèn 1 phiếu nhập nháp cũ (purchase_order_id = NULL) trực tiếp vào DB
        var orphanDraftId = Guid.NewGuid();
        await using (var adminConn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await adminConn.OpenAsync();
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO public.goods_receipts (id, tenant_id, number, purchase_order_id, branch_id, warehouse_id, supplier_id, status, created_by)
                VALUES (@id, @tId, 'GR-ORPHAN-001', NULL, @bId, @wId, @sId, 'Draft', @uId);

                INSERT INTO public.goods_receipt_items (id, tenant_id, receipt_id, purchase_order_item_id, sku_id, unit_name_snapshot, factor_snapshot, quantity, unit_price)
                VALUES (gen_random_uuid(), @tId, @id, NULL, @skuId, 'piece', 1, 10, 50000);
            ", adminConn);
            cmd.Parameters.AddWithValue("id", orphanDraftId);
            cmd.Parameters.AddWithValue("tId", tenantId);
            cmd.Parameters.AddWithValue("bId", branchId);
            cmd.Parameters.AddWithValue("wId", warehouseId);
            cmd.Parameters.AddWithValue("sId", supplierId);
            cmd.Parameters.AddWithValue("uId", userId);
            cmd.Parameters.AddWithValue("skuId", skuId);
            await cmd.ExecuteNonQueryAsync();
        }

        // 1. Gọi xác nhận phiếu nháp mồ côi -> 422 PURCHASE_ORDER_REQUIRED (ProblemDetails chuẩn)
        var confirmOrphanRes = await _client.PostAsync($"/api/goods-receipts/{orphanDraftId}/confirm", null);
        confirmOrphanRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await ReadProblemAsync(confirmOrphanRes);
        problem.GetProperty("errorCode").GetString().Should().Be("PURCHASE_ORDER_REQUIRED");

        // 2. Gọi hủy phiếu nháp mồ côi -> 200 OK, chuyển sang Cancelled
        var cancelOrphanRes = await _client.PostAsync($"/api/goods-receipts/{orphanDraftId}/cancel", null);
        cancelOrphanRes.StatusCode.Should().Be(HttpStatusCode.OK, await cancelOrphanRes.Content.ReadAsStringAsync());
        var cancelledReceipt = (await cancelOrphanRes.Content.ReadFromJsonAsync<GoodsReceiptResponseDto>(_jsonOptions))!;
        cancelledReceipt.Status.Should().Be("Cancelled");

        // 3. Hủy lại lần nữa hoặc xác nhận phiếu đã hủy -> 409 INVALID_STATE_TRANSITION
        var cancelAgainRes = await _client.PostAsync($"/api/goods-receipts/{orphanDraftId}/cancel", null);
        cancelAgainRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemAsync(cancelAgainRes)).GetProperty("errorCode").GetString().Should().Be("INVALID_STATE_TRANSITION");

        var confirmCancelledRes = await _client.PostAsync($"/api/goods-receipts/{orphanDraftId}/confirm", null);
        confirmCancelledRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemAsync(confirmCancelledRes)).GetProperty("errorCode").GetString().Should().Be("INVALID_STATE_TRANSITION");
    }
}
