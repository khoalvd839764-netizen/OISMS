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
using OISM.Application.Features.PurchaseOrders.Dtos;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Bộ kiểm thử tích hợp cho OISMS-66-L2-G1 (Upgrade 12):
/// Đơn mua PendingApproval, phân bổ phí/chiết khấu VAS 02, WAC theo allocated_unit_cost,
/// gỡ API hóa đơn NCC, che giá với Staff, kiểm tra CHECK DB và migration idempotent chạy 2 lần.
/// </summary>
[Collection("PostgresIntegrationTests")]
public class PostgresPurchaseOrderU12IntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class TestTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    public PostgresPurchaseOrderU12IntegrationTests(WebApplicationFactoryWithPostgres factory)
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
    // AC-01 & AC-06: MIGRATION CHẠY 2 LẦN, CHUYỂN DRAFT CŨ ĐỦ NCC+GIÁ -> PENDINGAPPROVAL,
    // CHECK CONSTRAINTS DB, REVOKE INSERT/UPDATE/DELETE HÓA ĐƠN NCC & GỠ ENDPOINT
    // =========================================================================

    [Fact]
    public async Task AC01_AC06_MigrationIdempotent_ConvertsLegacyPricedDraft_EnforcesDbChecks_RevokesSupplierInvoices_AndRemovesEndpoints()
    {
        await SeedDatabaseAsync();
        var tenantId = OismsDatabaseSeeder.TenantAId;
        var adminConnStr = PostgresTestEnvironment.AdminDatabaseConnectionString;
        var migrator = new DatabaseMigrationService();

        Guid branchId, supplierId, ownerId, skuId;
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            branchId = (await ctx.Branches.FirstAsync(b => b.TenantId == tenantId)).Id;
            supplierId = (await ctx.Suppliers.FirstAsync(s => s.TenantId == tenantId)).Id;
            ownerId = (await ctx.Users.FirstAsync(u => u.TenantId == tenantId && u.Role == "Owner")).Id;
            skuId = (await ctx.ProductVariants.FirstAsync(v => v.TenantId == tenantId)).Id;
        }

        // 1. Chèn 2 đơn Draft trực tiếp vào DB: 1 đơn đủ NCC + giá (sẽ chuyển sang PendingApproval), 1 đơn không có NCC/giá (giữ nguyên Draft)
        var legacyPricedDraftId = Guid.NewGuid();
        var legacyUnpricedDraftId = Guid.NewGuid();

        await using (var adminConn = new NpgsqlConnection(adminConnStr))
        {
            await adminConn.OpenAsync();
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO public.purchase_orders (id, tenant_id, number, request_key, supplier_id, branch_id, status, created_by)
                VALUES
                    (@pId, @tId, 'PO-LEGACY-PRICED', gen_random_uuid(), @sId, @bId, 'Draft', @uId),
                    (@uPoId, @tId, 'PO-LEGACY-UNPRICED', gen_random_uuid(), NULL, @bId, 'Draft', @uId);

                INSERT INTO public.purchase_order_items (id, tenant_id, purchase_order_id, sku_id, unit_name_snapshot, factor_snapshot, quantity_decimal_places, quantity, unit_price)
                VALUES
                    (gen_random_uuid(), @tId, @pId, @skuId, 'piece', 1, 0, 10, 50000),
                    (gen_random_uuid(), @tId, @uPoId, @skuId, 'piece', 1, 0, 10, NULL);
            ", adminConn);
            cmd.Parameters.AddWithValue("pId", legacyPricedDraftId);
            cmd.Parameters.AddWithValue("uPoId", legacyUnpricedDraftId);
            cmd.Parameters.AddWithValue("tId", tenantId);
            cmd.Parameters.AddWithValue("sId", supplierId);
            cmd.Parameters.AddWithValue("bId", branchId);
            cmd.Parameters.AddWithValue("uId", ownerId);
            cmd.Parameters.AddWithValue("skuId", skuId);
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. Chạy migration 20261119_purchase_order_pending_approval.sql 2 lần liên tiếp
        var migrationSqlPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "docker", "postgres", "migrations", "20261119_purchase_order_pending_approval.sql"));
        File.Exists(migrationSqlPath).Should().BeTrue($"File migration phải tồn tại tại {migrationSqlPath}");
        var migrationSql = await File.ReadAllTextAsync(migrationSqlPath);

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

        // Kiểm tra qua DatabaseMigrationService
        var up1 = await migrator.UpgradeDatabaseSchemaAsync(adminConnStr);
        up1.Success.Should().BeTrue(up1.Message);
        var up2 = await migrator.UpgradeDatabaseSchemaAsync(adminConnStr);
        up2.Success.Should().BeTrue(up2.Message);

        var baselineCheck = await migrator.InitializeOrBaselineDatabaseAsync(adminConnStr);
        baselineCheck.Success.Should().BeTrue(baselineCheck.Message);

        // 3. Kiểm tra đơn Draft đủ NCC + giá đã chuyển sang PendingApproval, đơn thiếu NCC/giá vẫn là Draft
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var pricedPo = await ctx.PurchaseOrders.FirstAsync(p => p.Id == legacyPricedDraftId);
            pricedPo.Status.Should().Be("PendingApproval");

            var unpricedPo = await ctx.PurchaseOrders.FirstAsync(p => p.Id == legacyUnpricedDraftId);
            unpricedPo.Status.Should().Be("Draft");
        }

        // 4. Kiểm tra CHECK constraints & Triggers trên DB:
        // - Approved mà thiếu supplier_id -> lỗi 23514
        // - Phí âm (shipping_fee < 0) -> lỗi 23514
        await using (var runtimeConn = new NpgsqlConnection(PostgresTestEnvironment.RuntimeConnectionString))
        {
            await runtimeConn.OpenAsync();

            await using (var cmdNullSupplierApprove = new NpgsqlCommand(
                "UPDATE public.purchase_orders SET status = 'Approved', approved_by = @uId, approved_at = now() WHERE id = @id;", runtimeConn))
            {
                cmdNullSupplierApprove.Parameters.AddWithValue("uId", ownerId);
                cmdNullSupplierApprove.Parameters.AddWithValue("id", legacyUnpricedDraftId);
                Func<Task> act = async () => await cmdNullSupplierApprove.ExecuteNonQueryAsync();
                await act.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "23514");
            }

            await using (var cmdNegativeFee = new NpgsqlCommand(
                "UPDATE public.purchase_orders SET shipping_fee = -1 WHERE id = @id;", runtimeConn))
            {
                cmdNegativeFee.Parameters.AddWithValue("id", legacyPricedDraftId);
                Func<Task> act = async () => await cmdNegativeFee.ExecuteNonQueryAsync();
                await act.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "23514");
            }

            // 5. Kiểm tra REVOKE INSERT/UPDATE/DELETE trên bảng supplier_invoices đối với runtime role
            await using (var cmdInsertInvoice = new NpgsqlCommand(@"
                INSERT INTO public.supplier_invoices (id, tenant_id, request_key, goods_receipt_id, supplier_id, invoice_series, invoice_number, invoice_date, status, subtotal, vat_amount, shipping_fee, total_amount, created_by)
                VALUES (gen_random_uuid(), @tId, gen_random_uuid(), gen_random_uuid(), @sId, '1C26TAA', '00001', CURRENT_DATE, 'Draft', 0, 0, 0, 0, @uId);
            ", runtimeConn))
            {
                cmdInsertInvoice.Parameters.AddWithValue("tId", tenantId);
                cmdInsertInvoice.Parameters.AddWithValue("sId", supplierId);
                cmdInsertInvoice.Parameters.AddWithValue("uId", ownerId);
                Func<Task> act = async () => await cmdInsertInvoice.ExecuteNonQueryAsync();
                await act.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "42501");
            }
        }

        // 6. AC-06: Không còn endpoint /api/supplier-invoices*
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var getInvoicesRes = await _client.GetAsync("/api/supplier-invoices");
        getInvoicesRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var getPendingInvoicesRes = await _client.GetAsync("/api/supplier-invoices/pending");
        getPendingInvoicesRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // =========================================================================
    // AC-02, AC-03 & AC-07: LUỒNG TRẠNG THÁI × VAI TRÒ, KHÓA LẠC QUAN VERSION,
    // TRẢ VỀ DRAFT, CHE GIÁ & NCC VỚI STAFF TRÊN ĐƠN MUA VÀ PHIẾU NHẬP
    // =========================================================================

    [Fact]
    public async Task AC02_AC03_AC07_StateTransitionsByRole_VersionMismatch_ReturnToDraft_AndStaffPriceRedaction()
    {
        await SeedDatabaseAsync();
        var tenantId = OismsDatabaseSeeder.TenantAId;

        var staffToken = await LoginAsAsync("staff@shopa.local", PostgresTestEnvironment.AppPassword);
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);

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

        // 1. Cashier gọi tạo/xem đơn mua -> 403 Forbidden
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        (await _client.GetAsync("/api/purchase-orders")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 2. Staff tạo đơn mua (cố tình gửi kèm SupplierId, UnitPrice, ShippingFee -> bị bỏ qua, vào Draft)
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        using var staffCreateMsg = CreatePostWithKey("/api/purchase-orders", new CreatePurchaseOrderDto
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ShippingFee = 50000m,
            DiscountAmount = 10000m,
            VatAmount = 20000m,
            Note = "Đề nghị mua hàng từ Staff",
            Items = new List<CreatePurchaseOrderItemDto>
            {
                new() { SkuId = skuId, Quantity = 10m, UnitPrice = 99999m }
            }
        });
        var staffCreateRes = await _client.SendAsync(staffCreateMsg);
        staffCreateRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var staffDraft = (await staffCreateRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;

        staffDraft.Status.Should().Be("Draft");
        staffDraft.SupplierId.Should().BeNull();
        staffDraft.SupplierName.Should().BeNull();
        staffDraft.UnitPriceFieldsAreNull().Should().BeTrue();

        // Kiểm tra trong DB: supplier_id = NULL, unit_price = NULL, phí = 0
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var dbPo = await ctx.PurchaseOrders.Include(p => p.Items).FirstAsync(p => p.Id == staffDraft.Id);
            dbPo.SupplierId.Should().BeNull();
            dbPo.ShippingFee.Should().Be(0m);
            dbPo.Items.Single().UnitPrice.Should().BeNull();
        }

        // 3. Staff sửa Draft của mình (nâng SL lên 20), sau đó Submit -> PendingApproval
        var staffUpdateRes = await _client.PutAsJsonAsync($"/api/purchase-orders/{staffDraft.Id}", new UpdatePurchaseOrderDraftDto
        {
            BranchId = branchId,
            Note = "Staff cập nhật số lượng lên 20",
            Items = new List<CreatePurchaseOrderItemDto>
            {
                new() { SkuId = skuId, Quantity = 20m, UnitPrice = 88888m }
            }
        });
        staffUpdateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var submitRes = await _client.PostAsync($"/api/purchase-orders/{staffDraft.Id}/submit", null);
        submitRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var pendingPo = (await submitRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;
        pendingPo.Status.Should().Be("PendingApproval");

        // Staff cố sửa hoặc hủy khi đã ở PendingApproval -> 409 INVALID_STATE_TRANSITION
        var staffEditPendingRes = await _client.PutAsJsonAsync($"/api/purchase-orders/{pendingPo.Id}", new UpdatePurchaseOrderDraftDto
        {
            BranchId = branchId,
            Items = new List<CreatePurchaseOrderItemDto> { new() { SkuId = skuId, Quantity = 30m } }
        });
        staffEditPendingRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemAsync(staffEditPendingRes)).GetProperty("errorCode").GetString().Should().Be("INVALID_STATE_TRANSITION");

        var staffCancelPendingRes = await _client.PostAsync($"/api/purchase-orders/{pendingPo.Id}/cancel", null);
        staffCancelPendingRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 4. Owner duyệt khi chưa có NCC / chưa có giá -> 422 PURCHASE_ORDER_INCOMPLETE_FOR_APPROVAL
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var approveIncompleteRes = await _client.PostAsJsonAsync($"/api/purchase-orders/{pendingPo.Id}/approve", new ApprovePurchaseOrderRequestDto());
        approveIncompleteRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemAsync(approveIncompleteRes)).GetProperty("errorCode").GetString().Should().Be("PURCHASE_ORDER_INCOMPLETE_FOR_APPROVAL");

        // 5. Owner Trả về (Return): không lý do -> 422 PURCHASE_ORDER_RETURN_REASON_REQUIRED; có lý do -> về Draft
        var returnNoReasonRes = await _client.PostAsJsonAsync($"/api/purchase-orders/{pendingPo.Id}/return", new ReturnPurchaseOrderRequestDto { Reason = "  " });
        returnNoReasonRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemAsync(returnNoReasonRes)).GetProperty("errorCode").GetString().Should().Be("PURCHASE_ORDER_RETURN_REASON_REQUIRED");

        var returnOkRes = await _client.PostAsJsonAsync($"/api/purchase-orders/{pendingPo.Id}/return", new ReturnPurchaseOrderRequestDto { Reason = "Điều chỉnh lại số lượng đặt" });
        returnOkRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var returnedPo = (await returnOkRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;
        returnedPo.Status.Should().Be("Draft");
        returnedPo.ReturnReason.Should().Be("Điều chỉnh lại số lượng đặt");
        returnedPo.ReturnedBy.Should().NotBeNull();
        returnedPo.ReturnedAt.Should().NotBeNull();

        // 6. Staff sửa lại Draft và Submit lại lên PendingApproval
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        (await _client.PostAsync($"/api/purchase-orders/{returnedPo.Id}/submit", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        // 7. Owner cập nhật NCC, giá, phí ở PendingApproval
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var ownerEditRes = await _client.PutAsJsonAsync($"/api/purchase-orders/{returnedPo.Id}", new UpdatePurchaseOrderDraftDto
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ShippingFee = 40000m,
            DiscountAmount = 0m,
            VatAmount = 100000m,
            Items = new List<CreatePurchaseOrderItemDto>
            {
                new() { SkuId = skuId, Quantity = 20m, UnitPrice = 50000m }
            }
        });
        ownerEditRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var pricedPendingPo = (await ownerEditRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;

        // AC-07: Khi còn ở PendingApproval, Staff gọi GET detail/list vẫn thấy SupplierId/SupplierName = null và toàn bộ giá = null
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var staffViewPending = (await _client.GetFromJsonAsync<PurchaseOrderResponseDto>($"/api/purchase-orders/{pricedPendingPo.Id}", _jsonOptions))!;
        staffViewPending.SupplierId.Should().BeNull();
        staffViewPending.SupplierName.Should().BeNull();
        staffViewPending.UnitPriceFieldsAreNull().Should().BeTrue();

        // 8. AC-03: Owner duyệt với Version lệch -> 409 PURCHASE_ORDER_VERSION_MISMATCH
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var approveWrongVersionRes = await _client.PostAsJsonAsync($"/api/purchase-orders/{pricedPendingPo.Id}/approve", new ApprovePurchaseOrderRequestDto
        {
            Version = pricedPendingPo.Version - 1
        });
        approveWrongVersionRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemAsync(approveWrongVersionRes)).GetProperty("errorCode").GetString().Should().Be("PURCHASE_ORDER_VERSION_MISMATCH");

        // Owner duyệt đúng Version -> 200 OK
        var approveOkRes = await _client.PostAsJsonAsync($"/api/purchase-orders/{pricedPendingPo.Id}/approve", new ApprovePurchaseOrderRequestDto
        {
            Version = pricedPendingPo.Version
        });
        approveOkRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var approvedPo = (await approveOkRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;
        approvedPo.Status.Should().Be("Approved");
        approvedPo.Items[0].AllocatedUnitCost.Should().Be(52000m); // (20*50k + 40k) / 20 = 52.000đ

        // 9. AC-07: Sau khi Approved, Staff thấy Supplier nhưng mọi trường giá trên Đơn mua và Phiếu nhập đều là null
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var staffViewApproved = (await _client.GetFromJsonAsync<PurchaseOrderResponseDto>($"/api/purchase-orders/{approvedPo.Id}", _jsonOptions))!;
        staffViewApproved.SupplierId.Should().Be(supplierId);
        staffViewApproved.SupplierName.Should().NotBeNullOrWhiteSpace();
        staffViewApproved.UnitPriceFieldsAreNull().Should().BeTrue();

        var staffListPo = (await _client.GetFromJsonAsync<PagedResult<PurchaseOrderResponseDto>>("/api/purchase-orders", _jsonOptions))!;
        staffListPo.Items.Should().NotBeEmpty();
        staffListPo.Items.Should().OnlyContain(p => p.UnitPriceFieldsAreNull());

        // Staff lập và xác nhận phiếu nhập -> mọi trường giá trên phiếu nhập trả về cho Staff là null
        var grCreateRes = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            PurchaseOrderId = approvedPo.Id,
            WarehouseId = warehouseId,
            Items = new List<CreateGoodsReceiptItemDto>
            {
                new() { PurchaseOrderItemId = approvedPo.Items[0].Id, Quantity = 20m }
            }
        });
        grCreateRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var staffGr = (await grCreateRes.Content.ReadFromJsonAsync<GoodsReceiptResponseDto>(_jsonOptions))!;
        staffGr.TotalAmount.Should().BeNull();
        staffGr.Items.Should().OnlyContain(i => i.UnitPrice == null && i.LineTotal == null);

        var grConfirmRes = await _client.PostAsync($"/api/goods-receipts/{staffGr.Id}/confirm", null);
        grConfirmRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var staffConfirmedGr = (await grConfirmRes.Content.ReadFromJsonAsync<GoodsReceiptResponseDto>(_jsonOptions))!;
        staffConfirmedGr.TotalAmount.Should().BeNull();
        staffConfirmedGr.Items.Should().OnlyContain(i => i.UnitPrice == null && i.LineTotal == null);

        var staffGetGrList = (await _client.GetFromJsonAsync<PagedResult<GoodsReceiptResponseDto>>("/api/goods-receipts", _jsonOptions))!;
        staffGetGrList.Items.Should().OnlyContain(r => r.TotalAmount == null && r.Items.All(i => i.UnitPrice == null && i.LineTotal == null));
    }

    // =========================================================================
    // AC-03, AC-04 & AC-05: PHÂN BỔ PHÍ + CHIẾT KHẤU (DƯ LÀM TRÒN DỒN DÒNG CUỐI),
    // CHIẾT KHẤU > TỔNG HÀNG -> 422, WAC THEO ALLOCATED_UNIT_COST / FACTORSNAPSHOT,
    // VÀ API GỢI Ý GIÁ MUA GẦN NHẤT + WAC
    // =========================================================================

    [Fact]
    public async Task AC03_AC04_AC05_CostAllocationRounding_DiscountValidation_WacFromAllocatedCost_AndPriceSuggestion()
    {
        await SeedDatabaseAsync();
        var tenantId = OismsDatabaseSeeder.TenantAId;
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        Guid branchId, warehouseId, supplierId;
        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var branch = await ctx.Branches.FirstAsync(b => b.TenantId == tenantId);
            var wh = await ctx.Warehouses.FirstAsync(w => w.TenantId == tenantId && w.BranchId == branch.Id);
            var sup = await ctx.Suppliers.FirstAsync(s => s.TenantId == tenantId);
            branchId = branch.Id;
            warehouseId = wh.Id;
            supplierId = sup.Id;
        }

        // Tạo sản phẩm có 2 biến thể SKU và đơn vị quy đổi (1 thùng = 10 cái)
        var units = (await _client.GetFromJsonAsync<List<UnitResponse>>("/api/units", _jsonOptions))!;
        var caiId = units.Single(u => u.Code == "piece").Id;
        var thungId = units.Single(u => u.Code == "thung").Id;

        var runCode = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var createProdRes = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = $"SP Phân bổ U12 {runCode}",
            Variants = new List<CreateVariantRequest>
            {
                new()
                {
                    SKU = $"U12-A-{runCode}",
                    VariantName = "Biến thể A",
                    BaseUnitId = caiId,
                    Unit = "piece",
                    RetailPrice = 150000m,
                    WholesalePrice = 130000m
                },
                new()
                {
                    SKU = $"U12-B-{runCode}",
                    VariantName = "Biến thể B",
                    BaseUnitId = caiId,
                    Unit = "piece",
                    RetailPrice = 250000m,
                    WholesalePrice = 220000m
                }
            }
        });
        createProdRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var prodId = (await createProdRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions)).GetProperty("productId").GetGuid();
        var prod = (await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{prodId}", _jsonOptions))!;
        var skuAId = prod.Variants.Single(v => v.SKU.StartsWith("U12-A-")).Id;
        var skuBId = prod.Variants.Single(v => v.SKU.StartsWith("U12-B-")).Id;

        // Thêm đơn vị thùng (hệ số 10) cho SKU A
        var addUnitRes = await _client.PostAsJsonAsync($"/api/products/{prodId}/variants/{skuAId}/units", new CreateSkuUnitRequest
        {
            UnitId = thungId,
            FactorToBase = 10m,
            CanPurchase = true,
            CanSell = false,
            QuantityDecimalPlaces = 0
        });
        addUnitRes.StatusCode.Should().Be(HttpStatusCode.OK, await addUnitRes.Content.ReadAsStringAsync());

        // 1. Kiểm tra Chiết khấu > Tổng tiền hàng -> 422 PURCHASE_ORDER_DISCOUNT_EXCEEDS_SUBTOTAL
        using var discountExceedMsg = CreatePostWithKey("/api/purchase-orders", new CreatePurchaseOrderDto
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ShippingFee = 10000m,
            DiscountAmount = 200000m, // > subtotal 100.000
            Items = new List<CreatePurchaseOrderItemDto>
            {
                new() { SkuId = skuAId, Quantity = 1m, UnitPrice = 100000m }
            }
        });
        var discountPo = (await (await _client.SendAsync(discountExceedMsg)).Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;
        var approveDiscountExceedRes = await _client.PostAsJsonAsync($"/api/purchase-orders/{discountPo.Id}/approve", new ApprovePurchaseOrderRequestDto());
        approveDiscountExceedRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemAsync(approveDiscountExceedRes)).GetProperty("errorCode").GetString().Should().Be("PURCHASE_ORDER_DISCOUNT_EXCEEDS_SUBTOTAL");

        // 2. Kiểm tra Phân bổ phí + chiết khấu và dồn dư làm tròn vào dòng cuối, VAT không vào giá vốn:
        // - Dòng A (SKU A): 1 thùng (hệ số 10) @ 100.000đ/thùng -> tiền hàng = 100.000đ
        // - Dòng B (SKU B): 1 cái (hệ số 1) @ 200.000đ/cái -> tiền hàng = 200.000đ
        // Tổng tiền hàng (Subtotal) = 300.000đ.
        // ShippingFee = 15.000đ, DiscountAmount = 5.000đ -> Net cộng thêm = +10.000đ. VAT = 31.000đ (không vào giá vốn).
        // Tổng giá trị sau phân bổ = 310.000đ.
        using var allocCreateMsg = CreatePostWithKey("/api/purchase-orders", new CreatePurchaseOrderDto
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ShippingFee = 15000m,
            DiscountAmount = 5000m,
            VatAmount = 31000m,
            Items = new List<CreatePurchaseOrderItemDto>
            {
                new() { SkuId = skuAId, UnitId = thungId, Quantity = 1m, UnitPrice = 100000m },
                new() { SkuId = skuBId, UnitId = caiId, Quantity = 1m, UnitPrice = 200000m }
            }
        });
        var allocCreateRes = await _client.SendAsync(allocCreateMsg);
        allocCreateRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var allocPo = (await allocCreateRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;
        allocPo.Status.Should().Be("PendingApproval");

        var approveAllocRes = await _client.PostAsJsonAsync($"/api/purchase-orders/{allocPo.Id}/approve", new ApprovePurchaseOrderRequestDto
        {
            Version = allocPo.Version
        });
        approveAllocRes.StatusCode.Should().Be(HttpStatusCode.OK, await approveAllocRes.Content.ReadAsStringAsync());
        var approvedAllocPo = (await approveAllocRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;

        approvedAllocPo.Subtotal.Should().Be(300000m);
        approvedAllocPo.TotalAllocatedCost.Should().Be(310000m);
        approvedAllocPo.TotalAmount.Should().Be(341000m); // 310k + 31k VAT

        var itemA = approvedAllocPo.Items.Single(i => i.SkuId == skuAId);
        var itemB = approvedAllocPo.Items.Single(i => i.SkuId == skuBId);

        // Một trong 2 dòng đứng trước theo (CreatedAt, Id) sẽ nhận làm tròn 6 chữ số, dòng cuối nhận phần bù để tổng = 310.000đ chính xác
        itemA.AllocatedUnitCost.Should().BeOneOf(103333.333333m, 103333.333334m);
        itemB.AllocatedUnitCost.Should().BeOneOf(206666.666667m, 206666.666666m);

        // Tổng phân bổ các dòng khớp đúng 100% với tổng tiền hàng + phí - chiết khấu = 310.000đ
        var sumAllocatedLines = decimal.Round(itemA.AllocatedUnitCost!.Value * itemA.Quantity, 6)
                              + decimal.Round(itemB.AllocatedUnitCost!.Value * itemB.Quantity, 6);
        sumAllocatedLines.Should().Be(310000m);

        // 3. AC-04: Xác nhận phiếu nhập dùng allocated_unit_cost / FactorSnapshot làm giá nhập cho WAC
        var grRes = await _client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            PurchaseOrderId = approvedAllocPo.Id,
            WarehouseId = warehouseId,
            Items = new List<CreateGoodsReceiptItemDto>
            {
                new() { PurchaseOrderItemId = itemA.Id, Quantity = 1m },
                new() { PurchaseOrderItemId = itemB.Id, Quantity = 1m }
            }
        });
        grRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var gr = (await grRes.Content.ReadFromJsonAsync<GoodsReceiptResponseDto>(_jsonOptions))!;

        var confirmGrRes = await _client.PostAsync($"/api/goods-receipts/{gr.Id}/confirm", null);
        confirmGrRes.StatusCode.Should().Be(HttpStatusCode.OK, await confirmGrRes.Content.ReadAsStringAsync());

        // Kiểm tra WAC:
        // SKU A nhập 1 thùng = 10 cái, allocated_unit_cost = itemA.AllocatedUnitCost -> giá vốn đơn vị gốc = itemA.AllocatedUnitCost / 10 (6 chữ số thập phân)
        // SKU B nhập 1 cái, allocated_unit_cost = itemB.AllocatedUnitCost -> WAC = itemB.AllocatedUnitCost (6 chữ số thập phân)
        var expectedBaseWacA = decimal.Round(itemA.AllocatedUnitCost!.Value / 10m, 6, MidpointRounding.AwayFromZero);
        var expectedBaseWacB = decimal.Round(itemB.AllocatedUnitCost!.Value, 6, MidpointRounding.AwayFromZero);

        await using (var ctx = CreatePostgresDbContext(tenantId).Context)
        {
            var balA = await ctx.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuAId);
            balA.OnHand.Should().Be(10m);
            balA.AverageCost.Should().Be(expectedBaseWacA);

            var balB = await ctx.InventoryBalances.FirstAsync(b => b.BranchId == branchId && b.SkuId == skuBId);
            balB.OnHand.Should().Be(1m);
            balB.AverageCost.Should().Be(expectedBaseWacB);

            var txA = await ctx.InventoryTransactions.SingleAsync(t => t.BranchId == branchId && t.SkuId == skuAId);
            txA.UnitCost.Should().Be(expectedBaseWacA);
        }

        // 4. AC-05: GET /api/purchase-orders/price-suggestion trả đúng giá đơn đã duyệt gần nhất + WAC chi nhánh quy theo đơn vị
        var suggestionRes = await _client.GetAsync(
            $"/api/purchase-orders/price-suggestion?branchId={branchId}&supplierId={supplierId}&skuId={skuAId}&unitId={thungId}");
        suggestionRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var suggestion = (await suggestionRes.Content.ReadFromJsonAsync<PurchaseOrderPriceSuggestionDto>(_jsonOptions))!;

        suggestion.SuggestedUnitPrice.Should().Be(100000m);
        suggestion.LastAllocatedUnitCost.Should().Be(itemA.AllocatedUnitCost);
        suggestion.LastPurchaseOrderId.Should().Be(approvedAllocPo.Id);
        suggestion.FactorToBase.Should().Be(10m);
        suggestion.CurrentBaseWac.Should().Be(expectedBaseWacA);
        suggestion.CurrentUnitWac.Should().Be(decimal.Round(expectedBaseWacA * 10m, 6, MidpointRounding.AwayFromZero));
    }
}

internal static class PurchaseOrderTestExtensions
{
    public static bool UnitPriceFieldsAreNull(this PurchaseOrderResponseDto po)
    {
        return po.Subtotal == null
               && po.ShippingFee == null
               && po.DiscountAmount == null
               && po.VatAmount == null
               && po.TotalAllocatedCost == null
               && po.TotalAmount == null
               && po.Items.All(i => i.UnitPrice == null
                                   && i.AllocatedUnitCost == null
                                   && i.LineTotal == null
                                   && i.AllocatedLineTotal == null);
    }
}
