using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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
/// Bộ kiểm thử tích hợp cho OISMS-66-L2-G6:
/// (1) Chỉ người tạo sửa/gửi duyệt Draft; Owner xem Draft của người khác chỉ đọc, vẫn Hủy được; Staff gửi đơn của mình OK.
/// (2) Đơn do Owner tạo, bị Trả về thành Draft -> Owner là người tạo -> sửa/gửi lại OK.
/// (3) approvalReadiness theo đúng luật chung với ApproveAsync; Staff nhận null.
/// (4) Approve khi isReady=false -> 422 PURCHASE_ORDER_INCOMPLETE_FOR_APPROVAL.
/// (5) marginPercent bắt buộc 0 <= margin < 100 (chặn -1, 100, 150 -> 400; 0 và 99.99 lưu được; 50 tính đúng giá).
/// </summary>
[Collection("PostgresIntegrationTests")]
public class PostgresPurchaseOrderG6IntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class TestTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    public PostgresPurchaseOrderG6IntegrationTests(WebApplicationFactoryWithPostgres factory)
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
    // GATE (1) & (2): QUYỀN ĐƠN NHÁP (SUBMIT, UPDATE, CANCEL) VÀ ĐƠN TRẢ VỀ
    // =========================================================================

    [Fact]
    public async Task Gate01_02_DraftRbac_CreatorOnlySubmitAndEdit_OwnerCancelOk_OwnerReturnedDraftOk()
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        var staffToken = await LoginAsAsync("staff@shopa.local", PostgresTestEnvironment.AppPassword);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var branchRes = await _client.GetFromJsonAsync<JsonElement>("/api/branches", _jsonOptions);
        var branchId = branchRes[0].GetProperty("id").GetGuid();

        var tenantId = OismsDatabaseSeeder.TenantAId;
        var (ctx, _) = CreatePostgresDbContext(tenantId);
        Guid skuId;
        Guid supplierId;
        await using (ctx)
        {
            skuId = await ctx.ProductVariants.Where(v => v.TenantId == tenantId).Select(v => v.Id).FirstAsync();
            supplierId = await ctx.Suppliers.Where(s => s.TenantId == tenantId).Select(s => s.Id).FirstAsync();
        }

        // 1. Staff tạo đơn nháp 1
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        using var createDraft1Msg = CreatePostWithKey("/api/purchase-orders", new CreatePurchaseOrderDto
        {
            BranchId = branchId,
            Note = "Đơn nháp của Staff 1",
            Items = new List<CreatePurchaseOrderItemDto>
            {
                new() { SkuId = skuId, Quantity = 10m }
            }
        });
        var createDraft1Res = await _client.SendAsync(createDraft1Msg);
        createDraft1Res.StatusCode.Should().Be(HttpStatusCode.Created);
        var draft1 = (await createDraft1Res.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;
        draft1.Status.Should().Be("Draft");

        // Owner gửi duyệt đơn nháp của Staff -> 403 PURCHASE_ORDER_NOT_CREATOR, đơn không đổi
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var ownerSubmitRes = await _client.PostAsync($"/api/purchase-orders/{draft1.Id}/submit", null);
        ownerSubmitRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var submitProblem = await ReadProblemAsync(ownerSubmitRes);
        submitProblem.GetProperty("errorCode").GetString().Should().Be("PURCHASE_ORDER_NOT_CREATOR");

        // Owner sửa đơn nháp của Staff -> 403 PURCHASE_ORDER_NOT_CREATOR, đơn không đổi
        var ownerEditRes = await _client.PutAsJsonAsync($"/api/purchase-orders/{draft1.Id}", new UpdatePurchaseOrderDraftDto
        {
            BranchId = branchId,
            Note = "Owner cố sửa đơn nháp của Staff",
            Items = new List<CreatePurchaseOrderItemDto>
            {
                new() { SkuId = skuId, Quantity = 50m }
            }
        });
        ownerEditRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var editProblem = await ReadProblemAsync(ownerEditRes);
        editProblem.GetProperty("errorCode").GetString().Should().Be("PURCHASE_ORDER_NOT_CREATOR");

        // Kiểm tra đơn nháp 1 vẫn nguyên trạng (Draft, số lượng 10)
        var draft1Check = (await _client.GetFromJsonAsync<PurchaseOrderResponseDto>($"/api/purchase-orders/{draft1.Id}", _jsonOptions))!;
        draft1Check.Status.Should().Be("Draft");
        draft1Check.Items[0].Quantity.Should().Be(10m);

        // Staff gửi duyệt đơn của chính mình -> OK (chuyển sang PendingApproval)
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        var staffSubmitRes = await _client.PostAsync($"/api/purchase-orders/{draft1.Id}/submit", null);
        staffSubmitRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var submittedDraft1 = (await staffSubmitRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;
        submittedDraft1.Status.Should().Be("PendingApproval");

        // 2. Staff tạo đơn nháp 2; Owner Hủy đơn nháp của Staff -> OK (Q11)
        using var createDraft2Msg = CreatePostWithKey("/api/purchase-orders", new CreatePurchaseOrderDto
        {
            BranchId = branchId,
            Note = "Đơn nháp của Staff 2",
            Items = new List<CreatePurchaseOrderItemDto>
            {
                new() { SkuId = skuId, Quantity = 15m }
            }
        });
        var createDraft2Res = await _client.SendAsync(createDraft2Msg);
        createDraft2Res.StatusCode.Should().Be(HttpStatusCode.Created);
        var draft2 = (await createDraft2Res.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var ownerCancelDraftRes = await _client.PostAsync($"/api/purchase-orders/{draft2.Id}/cancel", null);
        ownerCancelDraftRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelledPo = (await ownerCancelDraftRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;
        cancelledPo.Status.Should().Be("Cancelled");

        // 3. Đơn do Owner tạo -> vào PendingApproval -> Owner Trả về thành Draft -> Owner sửa/gửi lại -> OK
        using var createOwnerPoMsg = CreatePostWithKey("/api/purchase-orders", new CreatePurchaseOrderDto
        {
            BranchId = branchId,
            SupplierId = supplierId,
            Note = "Đơn do Owner tạo",
            Items = new List<CreatePurchaseOrderItemDto>
            {
                new() { SkuId = skuId, Quantity = 5m, UnitPrice = 12000m }
            }
        });
        var createOwnerPoRes = await _client.SendAsync(createOwnerPoMsg);
        createOwnerPoRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var ownerPo = (await createOwnerPoRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;
        ownerPo.Status.Should().Be("PendingApproval");

        // Owner Trả về Draft
        var returnRes = await _client.PostAsJsonAsync($"/api/purchase-orders/{ownerPo.Id}/return", new ReturnPurchaseOrderRequestDto
        {
            Reason = "Cần điều chỉnh số lượng"
        });
        returnRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var returnedOwnerPo = (await returnRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;
        returnedOwnerPo.Status.Should().Be("Draft");

        // Owner sửa đơn nháp do chính mình tạo -> OK
        var ownerEditOwnDraftRes = await _client.PutAsJsonAsync($"/api/purchase-orders/{ownerPo.Id}", new UpdatePurchaseOrderDraftDto
        {
            BranchId = branchId,
            SupplierId = supplierId,
            Note = "Owner sửa đơn nháp của chính mình",
            Items = new List<CreatePurchaseOrderItemDto>
            {
                new() { SkuId = skuId, Quantity = 8m, UnitPrice = 13000m }
            }
        });
        ownerEditOwnDraftRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedOwnerDraft = (await ownerEditOwnDraftRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;
        updatedOwnerDraft.Items[0].Quantity.Should().Be(8m);

        // Owner gửi lại đơn nháp do chính mình tạo -> OK
        var ownerSubmitOwnRes = await _client.PostAsync($"/api/purchase-orders/{ownerPo.Id}/submit", null);
        ownerSubmitOwnRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var resubmittedPo = (await ownerSubmitOwnRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;
        resubmittedPo.Status.Should().Be("PendingApproval");
    }

    // =========================================================================
    // GATE (3) & (4): APPROVAL READINESS & APPROVE CHẶN KHI IS_READY = FALSE
    // =========================================================================

    [Fact]
    public async Task Gate03_04_ApprovalReadiness_And_ApproveBlockWhenNotReady()
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        var staffToken = await LoginAsAsync("staff@shopa.local", PostgresTestEnvironment.AppPassword);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var branchRes = await _client.GetFromJsonAsync<JsonElement>("/api/branches", _jsonOptions);
        var branchId = branchRes[0].GetProperty("id").GetGuid();

        var tenantId = OismsDatabaseSeeder.TenantAId;
        var (ctx, _) = CreatePostgresDbContext(tenantId);
        Guid skuId;
        Guid supplierId;
        Guid caiId;
        Guid retailListId;
        await using (ctx)
        {
            skuId = await ctx.ProductVariants.Where(v => v.TenantId == tenantId).Select(v => v.Id).FirstAsync();
            supplierId = await ctx.Suppliers.Where(s => s.TenantId == tenantId).Select(s => s.Id).FirstAsync();
            caiId = await ctx.Units.Where(u => u.TenantId == tenantId && (u.Code == "piece" || u.Name == "Cái")).Select(u => u.Id).FirstAsync();
            retailListId = await ctx.PriceLists.Where(p => p.TenantId == tenantId && (p.Code == "retail" || p.Code == "RETAIL")).Select(p => p.Id).FirstAsync();
        }

        // 1. Staff tạo đề nghị mua và gửi duyệt
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);
        using var createPoMsg = CreatePostWithKey("/api/purchase-orders", new CreatePurchaseOrderDto
        {
            BranchId = branchId,
            Items = new List<CreatePurchaseOrderItemDto>
            {
                new() { SkuId = skuId, Quantity = 20m }
            }
        });
        var createPoRes = await _client.SendAsync(createPoMsg);
        createPoRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var staffPo = (await createPoRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;

        var submitRes = await _client.PostAsync($"/api/purchase-orders/{staffPo.Id}/submit", null);
        submitRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Staff gọi GET: ApprovalReadiness nhận null
        var staffGetRes = (await _client.GetFromJsonAsync<PurchaseOrderResponseDto>($"/api/purchase-orders/{staffPo.Id}", _jsonOptions))!;
        staffGetRes.ApprovalReadiness.Should().BeNull("Staff không được xem trường approvalReadiness nhạy cảm");

        // Owner gọi GET: đơn Staff vừa gửi -> isReady=false, missing có SUPPLIER và UNIT_PRICE
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var ownerGet1 = (await _client.GetFromJsonAsync<PurchaseOrderResponseDto>($"/api/purchase-orders/{staffPo.Id}", _jsonOptions))!;
        ownerGet1.ApprovalReadiness.Should().NotBeNull();
        ownerGet1.ApprovalReadiness!.IsReady.Should().BeFalse();
        ownerGet1.ApprovalReadiness.Missing.Should().Contain("SUPPLIER");
        ownerGet1.ApprovalReadiness.Missing.Should().Contain("UNIT_PRICE");
        ownerGet1.ApprovalReadiness.HasZeroPrice.Should().BeFalse();
        ownerGet1.ApprovalReadiness.HasLossPrice.Should().BeFalse();

        // Gate 4: Approve khi isReady=false -> 422 PURCHASE_ORDER_INCOMPLETE_FOR_APPROVAL
        var approveIncompleteRes = await _client.PostAsJsonAsync($"/api/purchase-orders/{staffPo.Id}/approve", new ApprovePurchaseOrderRequestDto
        {
            Version = ownerGet1.Version
        });
        approveIncompleteRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var approveProb = await ReadProblemAsync(approveIncompleteRes);
        approveProb.GetProperty("errorCode").GetString().Should().Be("PURCHASE_ORDER_INCOMPLETE_FOR_APPROVAL");

        // Owner lưu đủ NCC và giá mua hợp lệ (> 0)
        var ownerUpdateRes = await _client.PutAsJsonAsync($"/api/purchase-orders/{staffPo.Id}", new UpdatePurchaseOrderDraftDto
        {
            BranchId = branchId,
            SupplierId = supplierId,
            Version = ownerGet1.Version,
            Items = new List<CreatePurchaseOrderItemDto>
            {
                new() { SkuId = skuId, Quantity = 20m, UnitPrice = 15000m, UnitId = caiId }
            }
        });
        ownerUpdateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Owner gọi GET: isReady = true, missing rỗng, hasZeroPrice = false, hasLossPrice = false
        var ownerGet2 = (await _client.GetFromJsonAsync<PurchaseOrderResponseDto>($"/api/purchase-orders/{staffPo.Id}", _jsonOptions))!;
        ownerGet2.ApprovalReadiness!.IsReady.Should().BeTrue();
        ownerGet2.ApprovalReadiness.Missing.Should().BeEmpty();
        ownerGet2.ApprovalReadiness.HasZeroPrice.Should().BeFalse();
        ownerGet2.ApprovalReadiness.HasLossPrice.Should().BeFalse();

        // Owner cập nhật có giá 0đ
        var ownerZeroPriceRes = await _client.PutAsJsonAsync($"/api/purchase-orders/{staffPo.Id}", new UpdatePurchaseOrderDraftDto
        {
            BranchId = branchId,
            SupplierId = supplierId,
            Version = ownerGet2.Version,
            Items = new List<CreatePurchaseOrderItemDto>
            {
                new() { SkuId = skuId, Quantity = 20m, UnitPrice = 0m, UnitId = caiId }
            }
        });
        ownerZeroPriceRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var ownerGet3 = (await _client.GetFromJsonAsync<PurchaseOrderResponseDto>($"/api/purchase-orders/{staffPo.Id}", _jsonOptions))!;
        ownerGet3.ApprovalReadiness!.HasZeroPrice.Should().BeTrue();

        // Đổi lại giá > 0 và cấu hình giá bán bán lỗ (FixedPrice < UnitPrice)
        var ownerRestorePriceRes = await _client.PutAsJsonAsync($"/api/purchase-orders/{staffPo.Id}", new UpdatePurchaseOrderDraftDto
        {
            BranchId = branchId,
            SupplierId = supplierId,
            Version = ownerGet3.Version,
            Items = new List<CreatePurchaseOrderItemDto>
            {
                new() { SkuId = skuId, Quantity = 20m, UnitPrice = 50000m, UnitId = caiId }
            }
        });
        ownerRestorePriceRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var poRestored = (await ownerRestorePriceRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;

        var lossConfigRes = await _client.PutAsJsonAsync($"/api/purchase-orders/{staffPo.Id}/sale-price-configs", new UpdatePurchaseOrderSalePriceConfigsDto
        {
            Items = new List<PurchaseOrderItemSalePriceConfigUpdateDto>
            {
                new()
                {
                    PurchaseOrderItemId = poRestored.Items[0].Id,
                    Configs = new List<PurchaseOrderSalePriceConfigInputDto>
                    {
                        new()
                        {
                            UnitId = caiId,
                            PriceListId = retailListId,
                            Mode = "FIXED",
                            FixedPrice = 20000m, // 20.000đ < 50.000đ giá vốn
                            PriceSource = "OWNER"
                        }
                    }
                }
            }
        });
        lossConfigRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var ownerGet4 = (await _client.GetFromJsonAsync<PurchaseOrderResponseDto>($"/api/purchase-orders/{staffPo.Id}", _jsonOptions))!;
        ownerGet4.ApprovalReadiness!.HasLossPrice.Should().BeTrue();
    }

    // =========================================================================
    // GATE (5): % LÃI GỘP WAC_MARGIN (KHOẢNG 0 <= MARGIN < 100, CÔNG THỨC CEIL)
    // =========================================================================

    [Fact]
    public async Task Gate05_MarginValidation_And_CeilFormula()
    {
        await SeedDatabaseAsync();

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var branchRes = await _client.GetFromJsonAsync<JsonElement>("/api/branches", _jsonOptions);
        var branchId = branchRes[0].GetProperty("id").GetGuid();

        var tenantId = OismsDatabaseSeeder.TenantAId;
        var (ctx, _) = CreatePostgresDbContext(tenantId);
        Guid skuId;
        Guid supplierId;
        Guid caiId;
        Guid retailListId;
        await using (ctx)
        {
            skuId = await ctx.ProductVariants.Where(v => v.TenantId == tenantId).Select(v => v.Id).FirstAsync();
            supplierId = await ctx.Suppliers.Where(s => s.TenantId == tenantId).Select(s => s.Id).FirstAsync();
            caiId = await ctx.Units.Where(u => u.TenantId == tenantId && (u.Code == "piece" || u.Name == "Cái")).Select(u => u.Id).FirstAsync();
            retailListId = await ctx.PriceLists.Where(p => p.TenantId == tenantId && (p.Code == "retail" || p.Code == "RETAIL")).Select(p => p.Id).FirstAsync();
        }

        // Tạo đơn mua với giá 10.000đ
        using var createPoMsg = CreatePostWithKey("/api/purchase-orders", new CreatePurchaseOrderDto
        {
            BranchId = branchId,
            SupplierId = supplierId,
            Items = new List<CreatePurchaseOrderItemDto>
            {
                new() { SkuId = skuId, Quantity = 10m, UnitPrice = 10000m, UnitId = caiId }
            }
        });
        var createPoRes = await _client.SendAsync(createPoMsg);
        createPoRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var po = (await createPoRes.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;

        var itemId = po.Items[0].Id;

        // 1. Kiểm tra margin -1 -> 400
        var negMarginRes = await _client.PutAsJsonAsync($"/api/purchase-orders/{po.Id}/sale-price-configs", new UpdatePurchaseOrderSalePriceConfigsDto
        {
            Items = new List<PurchaseOrderItemSalePriceConfigUpdateDto>
            {
                new()
                {
                    PurchaseOrderItemId = itemId,
                    Configs = new List<PurchaseOrderSalePriceConfigInputDto>
                    {
                        new() { UnitId = caiId, PriceListId = retailListId, Mode = "WAC_MARGIN", MarginPercent = -1m }
                    }
                }
            }
        });
        negMarginRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var negProb = await ReadProblemAsync(negMarginRes);
        negProb.GetProperty("detail").GetString().Should().Be("Lãi gộp tính trên giá bán, phải từ 0% đến dưới 100%.");

        // 2. Kiểm tra margin 100 -> 400
        var margin100Res = await _client.PutAsJsonAsync($"/api/purchase-orders/{po.Id}/sale-price-configs", new UpdatePurchaseOrderSalePriceConfigsDto
        {
            Items = new List<PurchaseOrderItemSalePriceConfigUpdateDto>
            {
                new()
                {
                    PurchaseOrderItemId = itemId,
                    Configs = new List<PurchaseOrderSalePriceConfigInputDto>
                    {
                        new() { UnitId = caiId, PriceListId = retailListId, Mode = "WAC_MARGIN", MarginPercent = 100m }
                    }
                }
            }
        });
        margin100Res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var m100Prob = await ReadProblemAsync(margin100Res);
        m100Prob.GetProperty("detail").GetString().Should().Be("Lãi gộp tính trên giá bán, phải từ 0% đến dưới 100%.");

        // 3. Kiểm tra margin 150 -> 400
        var margin150Res = await _client.PutAsJsonAsync($"/api/purchase-orders/{po.Id}/sale-price-configs", new UpdatePurchaseOrderSalePriceConfigsDto
        {
            Items = new List<PurchaseOrderItemSalePriceConfigUpdateDto>
            {
                new()
                {
                    PurchaseOrderItemId = itemId,
                    Configs = new List<PurchaseOrderSalePriceConfigInputDto>
                    {
                        new() { UnitId = caiId, PriceListId = retailListId, Mode = "WAC_MARGIN", MarginPercent = 150m }
                    }
                }
            }
        });
        margin150Res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var m150Prob = await ReadProblemAsync(margin150Res);
        m150Prob.GetProperty("detail").GetString().Should().Be("Lãi gộp tính trên giá bán, phải từ 0% đến dưới 100%.");

        // 4. Kiểm tra margin 0 -> lưu được (200 OK)
        var margin0Res = await _client.PutAsJsonAsync($"/api/purchase-orders/{po.Id}/sale-price-configs", new UpdatePurchaseOrderSalePriceConfigsDto
        {
            Items = new List<PurchaseOrderItemSalePriceConfigUpdateDto>
            {
                new()
                {
                    PurchaseOrderItemId = itemId,
                    Configs = new List<PurchaseOrderSalePriceConfigInputDto>
                    {
                        new() { UnitId = caiId, PriceListId = retailListId, Mode = "WAC_MARGIN", MarginPercent = 0m }
                    }
                }
            }
        });
        margin0Res.StatusCode.Should().Be(HttpStatusCode.OK);
        var poMargin0 = (await margin0Res.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;
        var config0 = poMargin0.Items[0].SalePriceConfigs!.First(c => c.PriceListId == retailListId);
        config0.MarginPercent.Should().Be(0m);

        // 5. Kiểm tra margin 99.99 -> lưu được (200 OK)
        var margin99Res = await _client.PutAsJsonAsync($"/api/purchase-orders/{po.Id}/sale-price-configs", new UpdatePurchaseOrderSalePriceConfigsDto
        {
            Items = new List<PurchaseOrderItemSalePriceConfigUpdateDto>
            {
                new()
                {
                    PurchaseOrderItemId = itemId,
                    Configs = new List<PurchaseOrderSalePriceConfigInputDto>
                    {
                        new() { UnitId = caiId, PriceListId = retailListId, Mode = "WAC_MARGIN", MarginPercent = 99.99m }
                    }
                }
            }
        });
        margin99Res.StatusCode.Should().Be(HttpStatusCode.OK);
        var poMargin99 = (await margin99Res.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;
        var config99 = poMargin99.Items[0].SalePriceConfigs!.First(c => c.PriceListId == retailListId);
        config99.MarginPercent.Should().Be(99.99m);

        // 6. Kiểm tra margin 50 -> giá = ceil(WAC / 0.5 / 1000) * 1000
        var margin50Res = await _client.PutAsJsonAsync($"/api/purchase-orders/{po.Id}/sale-price-configs", new UpdatePurchaseOrderSalePriceConfigsDto
        {
            Items = new List<PurchaseOrderItemSalePriceConfigUpdateDto>
            {
                new()
                {
                    PurchaseOrderItemId = itemId,
                    Configs = new List<PurchaseOrderSalePriceConfigInputDto>
                    {
                        new() { UnitId = caiId, PriceListId = retailListId, Mode = "WAC_MARGIN", MarginPercent = 50m }
                    }
                }
            }
        });
        margin50Res.StatusCode.Should().Be(HttpStatusCode.OK);
        var poMargin50 = (await margin50Res.Content.ReadFromJsonAsync<PurchaseOrderResponseDto>(_jsonOptions))!;
        var config50 = poMargin50.Items[0].SalePriceConfigs!.First(c => c.PriceListId == retailListId);
        config50.MarginPercent.Should().Be(50m);

        var projectedWac = config50.ProjectedUnitWac;
        var expectedPrice = Math.Ceiling((projectedWac / (1m - 0.50m)) / 1000m) * 1000m;
        config50.ProjectedPrice.Should().Be(expectedPrice);
    }
}
