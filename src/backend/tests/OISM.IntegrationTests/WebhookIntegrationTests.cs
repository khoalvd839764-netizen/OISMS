using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Inventory.Services;
using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Orders.Services;
using OISM.Application.Features.Realtime;
using OISM.Application.Features.Realtime.Dtos;
using OISM.Application.Features.StockTransfers.Dtos;
using OISM.Application.Features.Webhooks.Dtos;

using OISM.Application.Features.Webhooks.Services;

using OISM.Domain.Entities;

using OISM.Infrastructure.Persistence;

using Xunit;

namespace OISM.IntegrationTests;

/// <summary>

/// Bộ kiểm thử tích hợp toàn diện cho chức năng Webhook Tiếp nhận đơn hàng TMĐT (Task S3-BE1-T05-WEBHOOK-REALTIME / v1.0).

/// Thực thi trên PostgreSQL thật (oisms_test) tuân thủ AC-01, AC-02, AC-03, AC-04, AC-05, AC-06, AC-11, AC-12, AC-13.

/// </summary>

public class WebhookIntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>

{

    private readonly WebApplicationFactoryWithPostgres _factory;

    private readonly HttpClient _client;

    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public WebhookIntegrationTests(WebApplicationFactoryWithPostgres factory)

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

    private (AppDbContext Context, TestTenantService TenantService) CreatePostgresDbContext(Guid? tenantId = null, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)

    {

        var tenantService = new TestTenantService { TenantId = tenantId };

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

    private record TestShopInfra(

        Guid TenantId,

        Guid BranchId,

        Guid WarehouseId,

        Guid SkuId,

        string SkuCode,

        decimal RetailPrice,

        Guid ShopIntegrationId,

        string SecretKey,

        string ExternalShopId);

    private async Task<TestShopInfra> SetupShopInfraAsync(string channel = "Shopee", decimal initialOnHand = 100m, decimal reorderPoint = 10m)
    {
        await SeedDatabaseAsync();
        var (context, tenantService) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
        await using (context)
        {
            var branch = await context.Branches.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId && b.IsActive);
            var wh = await context.Warehouses.FirstAsync(w => w.TenantId == OismsDatabaseSeeder.TenantAId && w.BranchId == branch.Id && w.IsActive);
            var sku = await context.ProductVariants.Include(v => v.Product).FirstAsync(v => v.TenantId == OismsDatabaseSeeder.TenantAId && v.IsActive);
            var supplier = await context.Suppliers.FirstAsync(s => s.TenantId == OismsDatabaseSeeder.TenantAId && s.IsActive);
            var user = await context.Users.FirstAsync(u => u.TenantId == OismsDatabaseSeeder.TenantAId && u.Email == "owner@shopa.local");

            if (initialOnHand > 0)
            {
                var wacCalc = new WacCalculator();
                var grService = new GoodsReceiptService(context, tenantService, wacCalc);
                var receiptDto = new CreateGoodsReceiptDto
                {
                    BranchId = branch.Id,
                    WarehouseId = wh.Id,
                    SupplierId = supplier.Id,
                    Note = "Seed stock via GoodsReceipt",
                    Items = new List<CreateGoodsReceiptItemDto>
                    {
                        new() { SkuId = sku.Id, Quantity = initialOnHand, UnitPrice = 50_000m }
                    }
                };
                var receipt = await grService.CreateReceiptAsync(receiptDto, user.Id);
                await grService.ConfirmReceiptAsync(receipt.Id, user.Id);
            }

            var bBal = await context.InventoryBalances.FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId && b.BranchId == branch.Id && b.SkuId == sku.Id);
            bBal.ReorderPoint = reorderPoint;

            var rawSecret = $"shopee_secret_{Guid.NewGuid():N}";
            var secretHash = WebhookSecretHasher.HashSecret(rawSecret);
            var extShopId = $"SHOP_{Guid.NewGuid():N}"[..16];

            var integration = new ShopIntegration
            {
                Id = Guid.NewGuid(),
                TenantId = branch.TenantId,
                Channel = channel,
                ExternalShopId = extShopId,
                BranchId = branch.Id,
                IsActive = true,
                SecretHash = secretHash,
                CreatedAt = DateTimeOffset.UtcNow
            };
            context.ShopIntegrations.Add(integration);

            await context.SaveChangesAsync();

            return new TestShopInfra(
                TenantId: branch.TenantId,
                BranchId: branch.Id,
                WarehouseId: wh.Id,
                SkuId: sku.Id,
                SkuCode: sku.SKU,
                RetailPrice: sku.RetailPrice,
                ShopIntegrationId: integration.Id,
                SecretKey: rawSecret,
                ExternalShopId: extShopId);
        }
    }

    #region AC-01: Schema & DB Constraints

    [Fact]

    public async Task AC01_SchemaAndConstraints_ShopIntegrationAndOrderSignature_EnforcesCrossTenantFkAndUniqueExternalIdentity()

    {

        await SeedDatabaseAsync();

        var (context, _) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);

        await using (context)

        using (context.OpenPrivilegedScope())

        {

            var branchA = await context.Branches.IgnoreQueryFilters().FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantAId);

            var branchB = await context.Branches.IgnoreQueryFilters().FirstAsync(b => b.TenantId == OismsDatabaseSeeder.TenantBId);

            branchA.TenantId.Should().NotBe(branchB.TenantId);

            // 1. Cross-tenant branch assignment in shop_integrations must be rejected by PostgreSQL composite FK

            var invalidIntegration = new ShopIntegration

            {

                Id = Guid.NewGuid(),

                TenantId = branchA.TenantId,

                BranchId = branchB.Id, // Chi nhánh của Tenant B gán vào Tenant A -> Vi phạm FK (tenant_id, branch_id)

                Channel = "Shopee",

                ExternalShopId = "SHOP_CROSS_TENANT",

                SecretHash = WebhookSecretHasher.HashSecret("secret_cross_fk"),

                IsActive = true,

                CreatedAt = DateTimeOffset.UtcNow

            };

            context.ShopIntegrations.Add(invalidIntegration);

            Func<Task> saveCrossTenant = async () => await context.SaveChangesAsync();

            await saveCrossTenant.Should().ThrowAsync<DbUpdateException>()

                .Where(ex => ex.InnerException != null && (ex.InnerException.Message.Contains("23503") || ex.InnerException.Message.Contains("fk_shop_integrations_branch")));

            context.ClearChangeTracker();

            // 2. Unique external identity: (tenant_id, channel, external_shop_id) must be unique

            var int1 = new ShopIntegration

            {

                Id = Guid.NewGuid(),

                TenantId = branchA.TenantId,

                BranchId = branchA.Id,

                Channel = "Shopee",

                ExternalShopId = "SHOP_UNIQUE_TEST",

                SecretHash = WebhookSecretHasher.HashSecret("secret_uq_1"),

                IsActive = true,

                CreatedAt = DateTimeOffset.UtcNow

            };

            var int2 = new ShopIntegration

            {

                Id = Guid.NewGuid(),

                TenantId = branchA.TenantId,

                BranchId = branchA.Id,

                Channel = "Shopee",

                ExternalShopId = "SHOP_UNIQUE_TEST", // Cùng tenant, cùng channel, cùng shop

                SecretHash = WebhookSecretHasher.HashSecret("secret_uq_2"),

                IsActive = true,

                CreatedAt = DateTimeOffset.UtcNow

            };

            context.ShopIntegrations.AddRange(int1, int2);

            Func<Task> saveDuplicateShop = async () => await context.SaveChangesAsync();

            await saveDuplicateShop.Should().ThrowAsync<DbUpdateException>()

                .Where(ex => ex.InnerException != null && (ex.InnerException.Message.Contains("23505") || ex.InnerException.Message.Contains("uq_shop_integrations_2")));

            context.ClearChangeTracker();

            // 3. Unique secret hash across all integrations: uq_shop_integrations_secret_hash (T5-F02)

            var duplicateSecret = "shared_secret_across_tenants";

            var duplicateSecretHash = WebhookSecretHasher.HashSecret(duplicateSecret);

            var sec1 = new ShopIntegration

            {

                Id = Guid.NewGuid(),

                TenantId = branchA.TenantId,

                BranchId = branchA.Id,

                Channel = "Shopee",

                ExternalShopId = "SHOP_SEC_A",

                SecretHash = duplicateSecretHash,

                IsActive = true,

                CreatedAt = DateTimeOffset.UtcNow

            };

            var sec2 = new ShopIntegration

            {

                Id = Guid.NewGuid(),

                TenantId = branchB.TenantId,

                BranchId = branchB.Id,

                Channel = "TikTok",

                ExternalShopId = "SHOP_SEC_B",

                SecretHash = duplicateSecretHash, // Trùng SecretHash giữa 2 tenant

                IsActive = true,

                CreatedAt = DateTimeOffset.UtcNow

            };

            context.ShopIntegrations.AddRange(sec1, sec2);

            Func<Task> saveDuplicateSecret = async () => await context.SaveChangesAsync();

            await saveDuplicateSecret.Should().ThrowAsync<DbUpdateException>()

                .Where(ex => ex.InnerException != null && (ex.InnerException.Message.Contains("23505") || ex.InnerException.Message.Contains("uq_shop_integrations_secret_hash")));

            context.ClearChangeTracker();

            // 4. Unique external order in orders: (tenant_id, channel, external_shop_id, external_order_id)

            var ord1 = new Order

            {

                Id = Guid.NewGuid(),

                TenantId = branchA.TenantId,

                Number = $"ORD-{Guid.NewGuid():N}"[..20],

                BranchId = branchA.Id,

                Channel = "Shopee",

                ExternalShopId = "SHOP_UQ_ORDER",

                ExternalOrderId = "EXT_ORDER_001",

                WebhookPayloadSignature = new string('a', 64),

                Status = "Reserved",

                RequestKey = Guid.NewGuid(),

                CreatedAt = DateTimeOffset.UtcNow

            };

            var ord2 = new Order

            {

                Id = Guid.NewGuid(),

                TenantId = branchA.TenantId,

                Number = $"ORD-{Guid.NewGuid():N}"[..20],

                BranchId = branchA.Id,

                Channel = "Shopee",

                ExternalShopId = "SHOP_UQ_ORDER",

                ExternalOrderId = "EXT_ORDER_001", // Cùng external order

                WebhookPayloadSignature = new string('b', 64),

                Status = "Reserved",

                RequestKey = Guid.NewGuid(),

                CreatedAt = DateTimeOffset.UtcNow

            };

            context.Orders.AddRange(ord1, ord2);

            Func<Task> saveDuplicateOrder = async () => await context.SaveChangesAsync();

            await saveDuplicateOrder.Should().ThrowAsync<DbUpdateException>()

                .Where(ex => ex.InnerException != null && (ex.InnerException.Message.Contains("23505") || ex.InnerException.Message.Contains("uq_orders_4")));

        }

    }

    #endregion

    #region AC-02: Authentication & Fulfillment Resolution

    [Fact]

    public async Task AC02_AuthenticationAndFulfillmentResolution_SecretAndJwtModes()

    {

        var infra = await SetupShopInfraAsync("Shopee", initialOnHand: 50);

        var payload = new CanonicalWebhookPayloadDto

        {

            Channel = "Shopee",

            ExternalShopId = infra.ExternalShopId,

            ExternalOrderId = $"ORDER_{Guid.NewGuid():N}"[..16],

            CustomerName = "Nguyễn Văn Test",

            CustomerPhone = "0987654321",

            ShippingAddress = "123 Đường Test, Quận 1, TP.HCM",

            OrderTimestamp = DateTimeOffset.UtcNow,

            TotalAmount = 150_000m,

            Items = new List<CanonicalWebhookItemDto>

            {

                new() { SkuCode = infra.SkuCode, Quantity = 2, UnitPrice = 75_000m }

            }

        };

        // 1. Không có cả Secret lẫn JWT -> 401 UNAUTHORIZED

        _client.DefaultRequestHeaders.Authorization = null;

        _client.DefaultRequestHeaders.Remove("X-Webhook-Secret");

        var resNoAuth = await _client.PostAsJsonAsync("/api/webhooks/simulator/shopee", payload);

        resNoAuth.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 2. Secret sai -> 401 UNAUTHORIZED

        using (var reqWrongSecret = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/simulator/shopee"))

        {

            reqWrongSecret.Headers.Add("X-Webhook-Secret", "wrong_secret_12345");

            reqWrongSecret.Content = JsonContent.Create(payload);

            var resWrongSecret = await _client.SendAsync(reqWrongSecret);

            resWrongSecret.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        }

        // 3. Secret đúng -> 200/201 Thành công và tự động phân giải chi nhánh

        using (var reqValidSecret = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/simulator/shopee"))

        {

            reqValidSecret.Headers.Add("X-Webhook-Secret", infra.SecretKey);

            reqValidSecret.Content = JsonContent.Create(payload);

            var resValidSecret = await _client.SendAsync(reqValidSecret);

            resValidSecret.StatusCode.Should().Be(HttpStatusCode.Created);

            var result = await resValidSecret.Content.ReadFromJsonAsync<WebhookSimulationResultDto>(_jsonOptions);

            result.Should().NotBeNull();

            result!.Success.Should().BeTrue();

            result.Status.Should().Be("Reserved");

        }

        // 4. JWT Mode: Cashier bị chặn 403 FORBIDDEN

        var cashierToken = await LoginAsAsync("cashier@shopa.local", PostgresTestEnvironment.AppPassword);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);

        _client.DefaultRequestHeaders.Remove("X-Webhook-Secret");

        var payload2 = new CanonicalWebhookPayloadDto

        {

            Channel = "Shopee",

            ExternalShopId = infra.ExternalShopId,

            ExternalOrderId = $"ORDER_{Guid.NewGuid():N}"[..16],

            OrderTimestamp = DateTimeOffset.UtcNow,

            TotalAmount = 75_000m,

            Items = new List<CanonicalWebhookItemDto>

            {

                new() { SkuCode = infra.SkuCode, Quantity = 1, UnitPrice = 75_000m }

            }

        };

        var resCashier = await _client.PostAsJsonAsync("/api/webhooks/simulator/shopee", payload2);

        resCashier.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 5. JWT Mode: Owner thành công

        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var resOwner = await _client.PostAsJsonAsync("/api/webhooks/simulator/shopee", payload2);

        resOwner.StatusCode.Should().Be(HttpStatusCode.Created);

        // 6. Inactive Tenant bị từ chối 401 UNAUTHORIZED (T5-F02)

        var (tenantCtx, _) = CreatePostgresDbContext(null);

        await using (tenantCtx)

        using (tenantCtx.OpenPrivilegedScope())

        {

            var tenant = await tenantCtx.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Id == infra.TenantId);

            tenant.IsActive = false;

            await tenantCtx.SaveChangesAsync();

        }

        using (var reqInactive = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/simulator/shopee"))

        {

            reqInactive.Headers.Add("X-Webhook-Secret", infra.SecretKey);

            reqInactive.Content = JsonContent.Create(payload);

            var resInactive = await _client.SendAsync(reqInactive);

            resInactive.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var bodyInactive = await resInactive.Content.ReadAsStringAsync();

            bodyInactive.Should().Contain("Doanh nghiệp đã ngừng hoạt động");

        }

        // Khôi phục Tenant active
        var (restoreTenantCtx, _) = CreatePostgresDbContext(null);
        await using (restoreTenantCtx)
        using (restoreTenantCtx.OpenPrivilegedScope())
        {
            var tenant = await restoreTenantCtx.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Id == infra.TenantId);
            tenant.IsActive = true;
            await restoreTenantCtx.SaveChangesAsync();
        }

    }

    #endregion

    #region AC-03: Canonical Payload & Marketplace Pricing

    [Fact]

    public async Task AC03_PayloadValidationAndMarketplacePricing_CheckedOverflowAndMismatchGuards()

    {

        var infra = await SetupShopInfraAsync("Shopee", initialOnHand: 100);

        _client.DefaultRequestHeaders.Authorization = null;

        _client.DefaultRequestHeaders.Remove("X-Webhook-Secret");

        _client.DefaultRequestHeaders.Add("X-Webhook-Secret", infra.SecretKey);

        // 1. Kênh sai lệch giữa URL và Payload -> 400 CHANNEL_MISMATCH

        var payloadMismatchChannel = new CanonicalWebhookPayloadDto

        {

            Channel = "TikTok", // URL là shopee nhưng body là TikTok

            ExternalShopId = infra.ExternalShopId,

            ExternalOrderId = "ORDER_CH_MISMATCH",

            OrderTimestamp = DateTimeOffset.UtcNow,

            TotalAmount = 100_000m,

            Items = new List<CanonicalWebhookItemDto>

            {

                new() { SkuCode = infra.SkuCode, Quantity = 1, UnitPrice = 100_000m }

            }

        };

        var resChMismatch = await _client.PostAsJsonAsync("/api/webhooks/simulator/shopee", payloadMismatchChannel);

        resChMismatch.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var bodyChMismatch = await resChMismatch.Content.ReadAsStringAsync();

        bodyChMismatch.Should().Contain("CHANNEL_MISMATCH");

        // 2. Tổng tiền gửi lên không khớp với phép tính Server -> 400 TOTAL_AMOUNT_MISMATCH

        var payloadBadTotal = new CanonicalWebhookPayloadDto

        {

            Channel = "Shopee",

            ExternalShopId = infra.ExternalShopId,

            ExternalOrderId = "ORDER_BAD_TOTAL",

            OrderTimestamp = DateTimeOffset.UtcNow,

            TotalAmount = 999_999m, // Server tính: 1 * 50_000 = 50_000

            Items = new List<CanonicalWebhookItemDto>

            {

                new() { SkuCode = infra.SkuCode, Quantity = 1, UnitPrice = 50_000m }

            }

        };

        var resBadTotal = await _client.PostAsJsonAsync("/api/webhooks/simulator/shopee", payloadBadTotal);

        resBadTotal.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var bodyBadTotal = await resBadTotal.Content.ReadAsStringAsync();

        bodyBadTotal.Should().Contain("TOTAL_AMOUNT_MISMATCH");

        // 3. SKU không tồn tại trong Catalog -> 422 SKU_NOT_FOUND

        var payloadMissingSku = new CanonicalWebhookPayloadDto

        {

            Channel = "Shopee",

            ExternalShopId = infra.ExternalShopId,

            ExternalOrderId = "ORDER_MISSING_SKU",

            OrderTimestamp = DateTimeOffset.UtcNow,

            TotalAmount = 100_000m,

            Items = new List<CanonicalWebhookItemDto>

            {

                new() { SkuCode = "NON_EXISTENT_SKU_CODE", Quantity = 1, UnitPrice = 100_000m }

            }

        };

        var resMissingSku = await _client.PostAsJsonAsync("/api/webhooks/simulator/shopee", payloadMissingSku);

        resMissingSku.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var bodyMissingSku = await resMissingSku.Content.ReadAsStringAsync();

        bodyMissingSku.Should().Contain("SKU_NOT_FOUND");

        // 4. Thiếu OrderTimestamp -> 400 Bad Request (T5-F03)

        var rawJsonMissingTimestamp = $"{{\"channel\":\"Shopee\",\"externalShopId\":\"{infra.ExternalShopId}\",\"externalOrderId\":\"EXT_NO_TIME\",\"totalAmount\":100000,\"items\":[{{\"skuCode\":\"{infra.SkuCode}\",\"quantity\":1,\"unitPrice\":100000}}]}}";

        using (var reqNoTime = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/simulator/shopee"))

        {

            reqNoTime.Headers.Add("X-Webhook-Secret", infra.SecretKey);

            reqNoTime.Content = new System.Net.Http.StringContent(rawJsonMissingTimestamp, System.Text.Encoding.UTF8, "application/json");

            var resNoTime = await _client.SendAsync(reqNoTime);

            resNoTime.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        }

        // 5. TotalAmount âm hoặc scale > 2 -> 400 Bad Request (T5-F03)

        var payloadInvalidScale = new CanonicalWebhookPayloadDto

        {

            Channel = "Shopee",

            ExternalShopId = infra.ExternalShopId,

            ExternalOrderId = "ORDER_BAD_SCALE",

            OrderTimestamp = DateTimeOffset.UtcNow,

            TotalAmount = 50_000.123m, // 3 chữ số thập phân

            Items = new List<CanonicalWebhookItemDto>

            {

                new() { SkuCode = infra.SkuCode, Quantity = 1, UnitPrice = 50_000.123m }

            }

        };

        var resBadScale = await _client.PostAsJsonAsync("/api/webhooks/simulator/shopee", payloadInvalidScale);

        resBadScale.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 6. CustomerName vượt 200 ký tự -> 400 Bad Request, không 500 / 22001 (T5-F03)

        var payloadLongName = new CanonicalWebhookPayloadDto

        {

            Channel = "Shopee",

            ExternalShopId = infra.ExternalShopId,

            ExternalOrderId = "ORDER_LONG_NAME",

            CustomerName = new string('A', 250),

            OrderTimestamp = DateTimeOffset.UtcNow,

            TotalAmount = 50_000m,

            Items = new List<CanonicalWebhookItemDto>

            {

                new() { SkuCode = infra.SkuCode, Quantity = 1, UnitPrice = 50_000m }

            }

        };

        var resLongName = await _client.PostAsJsonAsync("/api/webhooks/simulator/shopee", payloadLongName);
        resLongName.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 7. JSON thiếu channel -> 400 Bad Request (VALIDATION_ERROR), DB không tạo đơn (T5-F03)
        var missingChannelJson = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["externalShopId"] = infra.ExternalShopId,
            ["externalOrderId"] = "ORDER_MISSING_CHANNEL",
            ["orderTimestamp"] = DateTimeOffset.UtcNow.ToString("O"),
            ["totalAmount"] = 50_000m,
            ["items"] = new[] { new { skuCode = infra.SkuCode, quantity = 1m, unitPrice = 50_000m } }
        });
        using var missingChannelContent = new StringContent(missingChannelJson, System.Text.Encoding.UTF8, "application/json");
        var resMissingChannel = await _client.PostAsync("/api/webhooks/simulator/Shopee", missingChannelContent);
        resMissingChannel.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 8. JSON item thiếu unitPrice dù totalAmount = 0 -> 400 Bad Request (VALIDATION_ERROR), DB không tạo đơn (T5-F03)
        var missingUnitPriceJson = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["channel"] = "Shopee",
            ["externalShopId"] = infra.ExternalShopId,
            ["externalOrderId"] = "ORDER_MISSING_PRICE",
            ["orderTimestamp"] = DateTimeOffset.UtcNow.ToString("O"),
            ["totalAmount"] = 0m,
            ["items"] = new[] { new { skuCode = infra.SkuCode, quantity = 1m } }
        });
        using var missingUnitPriceContent = new StringContent(missingUnitPriceJson, System.Text.Encoding.UTF8, "application/json");
        var resMissingUnitPrice = await _client.PostAsync("/api/webhooks/simulator/Shopee", missingUnitPriceContent);
        resMissingUnitPrice.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 9. JSON gửi unitPrice = 0 tường minh với totalAmount = 0 -> 201 Created (hợp lệ theo contract) (T5-F03)
        var explicitZeroPriceJson = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["channel"] = "Shopee",
            ["externalShopId"] = infra.ExternalShopId,
            ["externalOrderId"] = "ORDER_EXPLICIT_ZERO_PRICE",
            ["orderTimestamp"] = DateTimeOffset.UtcNow.ToString("O"),
            ["totalAmount"] = 0m,
            ["items"] = new[] { new { skuCode = infra.SkuCode, quantity = 1m, unitPrice = 0m } }
        });
        using var explicitZeroPriceContent = new StringContent(explicitZeroPriceJson, System.Text.Encoding.UTF8, "application/json");
        var resExplicitZeroPrice = await _client.PostAsync("/api/webhooks/simulator/Shopee", explicitZeroPriceContent);
        resExplicitZeroPrice.StatusCode.Should().Be(HttpStatusCode.Created);

        var (checkDb, _) = CreatePostgresDbContext(OismsDatabaseSeeder.TenantAId);
        await using (checkDb)
        {
            var missingChanOrderExists = await checkDb.Orders.AnyAsync(o => o.ExternalOrderId == "ORDER_MISSING_CHANNEL");
            missingChanOrderExists.Should().BeFalse("Không được tạo đơn khi thiếu trường channel bắt buộc.");

            var missingPriceOrderExists = await checkDb.Orders.AnyAsync(o => o.ExternalOrderId == "ORDER_MISSING_PRICE");
            missingPriceOrderExists.Should().BeFalse("Không được tạo đơn khi thiếu trường unitPrice bắt buộc.");

            var explicitZeroOrder = await checkDb.Orders.FirstOrDefaultAsync(o => o.ExternalOrderId == "ORDER_EXPLICIT_ZERO_PRICE");
            explicitZeroOrder.Should().NotBeNull("Đơn giá 0 tường minh phải tạo thành công đơn Reserved.");
            explicitZeroOrder!.Status.Should().Be("Reserved");
        }

    }

    #endregion

    #region AC-04: Atomic Reserved Order & Zero Ledger/Payment

    [Fact]

    public async Task AC04_AtomicReservedOrderCreation_WarehouseAllocationAndZeroLedgerPayment()

    {

        var infra = await SetupShopInfraAsync("Shopee", initialOnHand: 50, reorderPoint: 5);

        _client.DefaultRequestHeaders.Authorization = null;

        _client.DefaultRequestHeaders.Remove("X-Webhook-Secret");

        _client.DefaultRequestHeaders.Add("X-Webhook-Secret", infra.SecretKey);

        var extOrderId = $"EXT_SHP_{Guid.NewGuid():N}"[..16];

        var payload = new CanonicalWebhookPayloadDto

        {

            Channel = "Shopee",

            ExternalShopId = infra.ExternalShopId,

            ExternalOrderId = extOrderId,

            CustomerName = "Khách Sàn Shopee",

            CustomerPhone = "0909000111",

            ShippingAddress = "789 Phố Mới, Hà Nội",

            OrderTimestamp = DateTimeOffset.UtcNow,

            TotalAmount = 240_000m,

            Items = new List<CanonicalWebhookItemDto>

            {

                new() { SkuCode = infra.SkuCode, Quantity = 3, UnitPrice = 80_000m }

            }

        };

        var response = await _client.PostAsJsonAsync("/api/webhooks/simulator/shopee", payload);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<WebhookSimulationResultDto>(_jsonOptions);

        result.Should().NotBeNull();

        result!.Success.Should().BeTrue();

        result.Status.Should().Be("Reserved");

        // Đối soát trực tiếp Database độc lập

        var (context, _) = CreatePostgresDbContext(infra.TenantId);

        await using (context)

        {

            var order = await context.Orders

                .Include(o => o.Items)

                .Include(o => o.Reservations)

                .Include(o => o.Payment)

                .FirstOrDefaultAsync(o => o.Id == result.OrderId);

            order.Should().NotBeNull();

            order!.Status.Should().Be("Reserved");

            order.Channel.Should().Be("Shopee");

            order.ExternalShopId.Should().Be(infra.ExternalShopId);

            order.ExternalOrderId.Should().Be(extOrderId);

            order.WebhookPayloadSignature.Should().NotBeNullOrEmpty();

            order.TotalAmount.Should().Be(240_000m);

            // Kiểm tra items

            order.Items.Should().HaveCount(1);

            var item = order.Items.First();

            item.Quantity.Should().Be(3);

            item.UnitPrice.Should().Be(80_000m);

            item.CostPrice.Should().BeNull("CostPrice tuyệt đối không snapshot tại bước tạo đơn Reserved.");

            // Kiểm tra reservations

            order.Reservations.Should().HaveCount(1);

            var res = order.Reservations.First();

            res.Status.Should().Be("Active");

            res.Quantity.Should().Be(3);

            // Tồn kho: OnHand không đổi, Reserved tăng 3

            var bBal = await context.InventoryBalances.FirstAsync(b => b.TenantId == infra.TenantId && b.BranchId == infra.BranchId && b.SkuId == infra.SkuId);

            bBal.OnHand.Should().Be(50m, "OnHand tuyệt đối không bị trừ khi đơn mới ở trạng thái Reserved.");

            bBal.Reserved.Should().Be(3m, "Reserved phải tăng đúng 3 đơn vị giữ chỗ.");

            var wBal = await context.WarehouseInventoryBalances.FirstAsync(wb => wb.TenantId == infra.TenantId && wb.BranchId == infra.BranchId && wb.WarehouseId == infra.WarehouseId && wb.SkuId == infra.SkuId);

            wBal.OnHand.Should().Be(50m);

            wBal.Reserved.Should().Be(3m);

            // Sổ cái và Thanh toán: Tuyệt đối không có bút toán nào

            var ledgers = await context.InventoryTransactions.Where(t => t.TenantId == infra.TenantId && t.OrderItemId == item.Id).ToListAsync();

            ledgers.Should().BeEmpty("Tuyệt đối không ghi sổ cái InventoryTransactions tại bước Reserved.");

            order.Payment.Should().BeNull("Tuyệt đối không tạo Payment tại bước Reserved.");

        }

    }

    #endregion

    #region AC-05: Deduplication & Idempotent Replay

    [Fact]

    public async Task AC05_DeduplicationAndIdempotentReplay_CanonicalSignatureVerification()

    {

        var infra = await SetupShopInfraAsync("Shopee", initialOnHand: 50);

        _client.DefaultRequestHeaders.Authorization = null;

        _client.DefaultRequestHeaders.Remove("X-Webhook-Secret");

        _client.DefaultRequestHeaders.Add("X-Webhook-Secret", infra.SecretKey);

        var extOrderId = $"EXT_DEDUP_{Guid.NewGuid():N}"[..16];

        var fixedTime = DateTimeOffset.UtcNow;

        var payload = new CanonicalWebhookPayloadDto

        {

            Channel = "Shopee",

            ExternalShopId = infra.ExternalShopId,

            ExternalOrderId = extOrderId,

            CustomerName = "Khách A",

            CustomerPhone = "0911222333",

            ShippingAddress = "100 Lê Duẩn, Đà Nẵng",

            OrderTimestamp = fixedTime,

            TotalAmount = 100_000m,

            Items = new List<CanonicalWebhookItemDto>

            {

                new() { SkuCode = infra.SkuCode, Quantity = 2, UnitPrice = 50_000m }

            }

        };

        // 1. Gửi lần 1: Tạo mới thành công

        var res1 = await _client.PostAsJsonAsync("/api/webhooks/simulator/shopee", payload);

        res1.StatusCode.Should().Be(HttpStatusCode.Created);

        var dto1 = await res1.Content.ReadFromJsonAsync<WebhookSimulationResultDto>(_jsonOptions);

        dto1.Should().NotBeNull();

        dto1!.Success.Should().BeTrue();

        // 2. Gửi lần 2 cùng external identity và cùng canonical payload -> 200 OK replay không sinh thêm order/reservation

        var res2 = await _client.PostAsJsonAsync("/api/webhooks/simulator/shopee", payload);

        res2.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto2 = await res2.Content.ReadFromJsonAsync<WebhookSimulationResultDto>(_jsonOptions);

        dto2.Should().NotBeNull();

        dto2!.OrderId.Should().Be(dto1.OrderId);

        dto2.OrderNumber.Should().Be(dto1.OrderNumber);

        dto2.Message.Should().Contain("tồn tại trước đó");

        // 3. Gửi lần 3 cùng external identity nhưng sửa payload (đơn giá khác) -> 409 WEBHOOK_PAYLOAD_MISMATCH

        var modifiedPayload = new CanonicalWebhookPayloadDto

        {

            Channel = "Shopee",

            ExternalShopId = infra.ExternalShopId,

            ExternalOrderId = extOrderId,

            CustomerName = "Khách A",

            CustomerPhone = "0911222333",

            ShippingAddress = "100 Lê Duẩn, Đà Nẵng",

            OrderTimestamp = fixedTime,

            TotalAmount = 120_000m, // Đổi giá

            Items = new List<CanonicalWebhookItemDto>

            {

                new() { SkuCode = infra.SkuCode, Quantity = 2, UnitPrice = 60_000m }

            }

        };

        var res3 = await _client.PostAsJsonAsync("/api/webhooks/simulator/shopee", modifiedPayload);

        res3.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body3 = await res3.Content.ReadAsStringAsync();

        body3.Should().Contain("WEBHOOK_PAYLOAD_MISMATCH");

        // 4. Đối soát Database: Chỉ có đúng 1 order và 1 reservation

        var (context, _) = CreatePostgresDbContext(infra.TenantId);

        await using (context)

        {

            var orderCount = await context.Orders.CountAsync(o => o.TenantId == infra.TenantId && o.Channel == "Shopee" && o.ExternalShopId == infra.ExternalShopId && o.ExternalOrderId == extOrderId);

            orderCount.Should().Be(1, "Replay không bao giờ được tạo bản ghi Order thứ hai.");

            var bBal = await context.InventoryBalances.FirstAsync(b => b.TenantId == infra.TenantId && b.BranchId == infra.BranchId && b.SkuId == infra.SkuId);

            bBal.Reserved.Should().Be(2m, "Reserved chỉ tăng đúng 2 của lần gọi đầu tiên.");

        }

        // 5. Kiểm chứng độ chính xác Canonical Signature với 7 chữ số phần thập phân (.fffffff) (T5-F04)

        var dtTick1 = new DateTimeOffset(2026, 10, 25, 12, 0, 0, 123, TimeSpan.Zero).AddTicks(4567);

        var dtTick2 = new DateTimeOffset(2026, 10, 25, 12, 0, 0, 123, TimeSpan.Zero).AddTicks(4568);

        var sig1 = WebhookCanonicalSignatureHelper.ComputeCanonicalSignature(new CanonicalWebhookPayloadDto

        {

            Channel = "Shopee",

            ExternalShopId = infra.ExternalShopId,

            ExternalOrderId = "EXT_TICK",

            OrderTimestamp = dtTick1,

            TotalAmount = 100_000m,

            Items = new List<CanonicalWebhookItemDto> { new() { SkuCode = infra.SkuCode, Quantity = 1, UnitPrice = 100_000m } }

        });

        var sig2 = WebhookCanonicalSignatureHelper.ComputeCanonicalSignature(new CanonicalWebhookPayloadDto

        {

            Channel = "Shopee",

            ExternalShopId = infra.ExternalShopId,

            ExternalOrderId = "EXT_TICK",

            OrderTimestamp = dtTick2,

            TotalAmount = 100_000m,

            Items = new List<CanonicalWebhookItemDto> { new() { SkuCode = infra.SkuCode, Quantity = 1, UnitPrice = 100_000m } }

        });

        sig1.Should().NotBe(sig2, "Hai timestamp lệch nhau 100ns (1 tick) phải sinh 2 chữ ký khác biệt tuyệt đối.");

        // 6. Fast Replay khi tài nguyên thay đổi (Chi nhánh inactive sau khi đơn đã tạo) -> Vẫn 200 OK replay (T5-F05)

        var (branchCtx, _) = CreatePostgresDbContext(null);

        await using (branchCtx)

        using (branchCtx.OpenPrivilegedScope())

        {

            var branch = await branchCtx.Branches.IgnoreQueryFilters().FirstAsync(b => b.Id == infra.BranchId);

            branch.IsActive = false;

            await branchCtx.SaveChangesAsync();

        }

        var resReplayInactiveBranch = await _client.PostAsJsonAsync("/api/webhooks/simulator/shopee", payload);

        resReplayInactiveBranch.StatusCode.Should().Be(HttpStatusCode.OK, "Fast Replay phải nhận diện đơn đã tồn tại trước khi kiểm tra trạng thái chi nhánh.");

        // Khôi phục chi nhánh active
        var (restoreBranchCtx, _) = CreatePostgresDbContext(null);
        await using (restoreBranchCtx)
        using (restoreBranchCtx.OpenPrivilegedScope())
        {
            var branch = await restoreBranchCtx.Branches.IgnoreQueryFilters().FirstAsync(b => b.Id == infra.BranchId);
            branch.IsActive = true;
            await restoreBranchCtx.SaveChangesAsync();
        }

        // 9. Admin fast replay khi branch inactive & catalog price change (T5-F05)
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        using var ownerClient = _factory.CreateClient();
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var adminReqKey = Guid.NewGuid();
        var adminReq = new CreateOrderRequestDto
        {
            RequestKey = adminReqKey,
            BranchId = infra.BranchId,
            Channel = "Admin",
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = infra.SkuId, SkuCode = infra.SkuCode, Quantity = 1m, UnitPrice = infra.RetailPrice }
            }
        };

        var adminFirstRes = await ownerClient.PostAsJsonAsync("/api/orders", adminReq);
        adminFirstRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstData = await adminFirstRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
        firstData.Should().NotBeNull();

        // Vô hiệu hóa branch
        var (deactCtx, _) = CreatePostgresDbContext(null);
        await using (deactCtx)
        using (deactCtx.OpenPrivilegedScope())
        {
            var branch = await deactCtx.Branches.IgnoreQueryFilters().FirstAsync(b => b.Id == infra.BranchId);
            branch.IsActive = false;
            await deactCtx.SaveChangesAsync();
        }

        // Replay đúng payload & RequestKey khi branch đã inactive -> 200 OK (IsReplay = true)
        var adminReplayRes = await ownerClient.PostAsJsonAsync("/api/orders", adminReq);
        adminReplayRes.StatusCode.Should().Be(HttpStatusCode.OK, "Admin replay phải thành công kể cả khi branch đã ngừng hoạt động.");
        var replayData = await adminReplayRes.Content.ReadFromJsonAsync<OrderResponseDto>(_jsonOptions);
        replayData.Should().NotBeNull();
        replayData!.Id.Should().Be(firstData!.Id, "Admin replay phải trả về đúng đơn hàng ban đầu.");

        // Replay với payload khác (sai số lượng) -> 409 Conflict
        var mismatchReq = new CreateOrderRequestDto
        {
            RequestKey = adminReqKey,
            BranchId = infra.BranchId,
            Channel = "Admin",
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = infra.SkuId, SkuCode = infra.SkuCode, Quantity = 2m, UnitPrice = infra.RetailPrice }
            }
        };
        var adminMismatchRes = await ownerClient.PostAsJsonAsync("/api/orders", mismatchReq);
        adminMismatchRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Khôi phục branch active
        var (reactCtx, _) = CreatePostgresDbContext(null);
        await using (reactCtx)
        using (reactCtx.OpenPrivilegedScope())
        {
            var branch = await reactCtx.Branches.IgnoreQueryFilters().FirstAsync(b => b.Id == infra.BranchId);
            branch.IsActive = true;
            await reactCtx.SaveChangesAsync();
        }

    }

    #endregion

    #region AC-06: Order Lifecycle & Contract Preservation

    [Fact]

    public async Task AC06_OrderLifecycle_ConfirmationAndContractPreservation()

    {

        var infra = await SetupShopInfraAsync("Shopee", initialOnHand: 20);

        _client.DefaultRequestHeaders.Authorization = null;

        _client.DefaultRequestHeaders.Remove("X-Webhook-Secret");

        _client.DefaultRequestHeaders.Add("X-Webhook-Secret", infra.SecretKey);

        var extOrderId = $"EXT_LC_{Guid.NewGuid():N}"[..16];

        var payload = new CanonicalWebhookPayloadDto

        {

            Channel = "Shopee",

            ExternalShopId = infra.ExternalShopId,

            ExternalOrderId = extOrderId,

            OrderTimestamp = DateTimeOffset.UtcNow,

            TotalAmount = 70_000m,

            Items = new List<CanonicalWebhookItemDto>

            {

                new() { SkuCode = infra.SkuCode, Quantity = 1, UnitPrice = 70_000m }

            }

        };

        var resWebhook = await _client.PostAsJsonAsync("/api/webhooks/simulator/shopee", payload);

        resWebhook.StatusCode.Should().Be(HttpStatusCode.Created);

        var simResult = await resWebhook.Content.ReadFromJsonAsync<WebhookSimulationResultDto>(_jsonOptions);

        // Đăng nhập Staff để Xác nhận đơn hàng (POST /api/orders/{id}/confirm)

        var staffToken = await LoginAsAsync("staff@shopa.local", PostgresTestEnvironment.AppPassword);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);

        _client.DefaultRequestHeaders.Remove("X-Webhook-Secret");

        var confirmRes = await _client.PostAsJsonAsync($"/api/orders/{simResult!.OrderId}/confirm", new ConfirmOrderRequestDto());

        confirmRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Đối soát trạng thái sau Confirm

        var (context, _) = CreatePostgresDbContext(infra.TenantId);

        await using (context)

        {

            var order = await context.Orders

                .Include(o => o.Items)

                .Include(o => o.Reservations)

                .FirstAsync(o => o.Id == simResult.OrderId);

            order.Status.Should().Be("Confirmed");

            var item = order.Items.First();

            item.CostPrice.Should().Be(50_000m, "WAC tại chi nhánh được snapshot vào CostPrice khi Confirmed.");

            // OnHand giảm 1 (từ 20 còn 19), Reserved giảm 1 (về 0)

            var bBal = await context.InventoryBalances.FirstAsync(b => b.TenantId == infra.TenantId && b.BranchId == infra.BranchId && b.SkuId == infra.SkuId);

            bBal.OnHand.Should().Be(19m);

            bBal.Reserved.Should().Be(0m);

            // Có 1 bút toán sổ cái OUT

            var ledger = await context.InventoryTransactions.FirstOrDefaultAsync(t => t.TenantId == infra.TenantId && t.OrderItemId == item.Id);

            ledger.Should().NotBeNull();

            ledger!.Type.Should().Be("OUT");

            ledger.Quantity.Should().Be(1m);

            ledger.UnitCost.Should().Be(50_000m);

        }

        // Kiểm tra Admin POST /api/orders bảo toàn tính năng: Chặn kênh khác Admin

        var adminPayloadWithWrongChannel = new CreateOrderRequestDto

        {

            BranchId = infra.BranchId,

            Channel = "Shopee", // Không được phép qua endpoint /api/orders

            RequestKey = Guid.NewGuid(),

            Items = new List<OrderItemRequestDto>

            {

                new() { SkuId = infra.SkuId, Quantity = 1, UnitPrice = infra.RetailPrice }

            }

        };

        var resAdminBadChannel = await _client.PostAsJsonAsync("/api/orders", adminPayloadWithWrongChannel);

        resAdminBadChannel.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    }

    #endregion

    #region AC-09: Realtime Event Coverage (Transfer Dispatch & Receive on PostgreSQL + Real SignalR Hub)

    [Fact]
    public async Task AC09_RealtimeEvent_StockTransferDispatchAndReceive_DispatchesEventsToBranchAndTenantGroups()
    {
        var infra = await SetupShopInfraAsync("Shopee", initialOnHand: 40m, reorderPoint: 35m);
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);

        // Đảm bảo Branch B và Warehouse B1 tồn tại cho Tenant A
        Guid branchBId;
        Guid warehouseBId;
        var (branchCtx, _) = CreatePostgresDbContext(infra.TenantId);
        await using (branchCtx)
        using (branchCtx.OpenPrivilegedScope())
        {
            var branchB = await branchCtx.Branches.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.TenantId == infra.TenantId && b.Name == "Chi nhánh Quận 3");
            if (branchB == null)
            {
                branchB = new Branch
                {
                    Id = Guid.NewGuid(),
                    TenantId = infra.TenantId,
                    Name = "Chi nhánh Quận 3",
                    Address = "456 CMT8, Quận 3, TP.HCM",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                branchCtx.Branches.Add(branchB);
                await branchCtx.SaveChangesAsync();
            }
            branchBId = branchB.Id;

            var whB = await branchCtx.Warehouses.IgnoreQueryFilters().FirstOrDefaultAsync(w => w.TenantId == infra.TenantId && w.BranchId == branchBId);
            if (whB == null)
            {
                whB = new Warehouse
                {
                    Id = Guid.NewGuid(),
                    TenantId = infra.TenantId,
                    BranchId = branchBId,
                    Name = "Kho Tổng Quận 3",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                branchCtx.Warehouses.Add(whB);
                await branchCtx.SaveChangesAsync();
            }
            warehouseBId = whB.Id;
        }

        var stockEvents = new ConcurrentQueue<StockChangedRealtimeDto>();
        var lowAlertEvents = new ConcurrentQueue<LowStockAlertRealtimeDto>();

        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/notifications?access_token=" + ownerToken).ToString(), o =>
            {
                o.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        hub.On<StockChangedRealtimeDto>("StockLevelChanged", stockEvents.Enqueue);
        hub.On<LowStockAlertRealtimeDto>("LowStockWarning", lowAlertEvents.Enqueue);

        await hub.StartAsync();
        await hub.InvokeAsync("JoinBranchGroup", infra.BranchId);
        await hub.InvokeAsync("JoinBranchGroup", branchBId);

        using var ownerClient = _factory.CreateClient();
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // Tạo phiếu chuyển kho từ Branch A (40m) sang Branch B (chuyển 10m -> tồn Branch A còn 30m <= reorder_point 35m)
        var createTrfRes = await ownerClient.PostAsJsonAsync("/api/stock-transfers", new CreateStockTransferRequestDto
        {
            RequestKey = Guid.NewGuid(),
            FromBranchId = infra.BranchId,
            FromWarehouseId = infra.WarehouseId,
            ToBranchId = branchBId,
            ToWarehouseId = warehouseBId,
            Items = new List<StockTransferItemRequestDto>
            {
                new() { SkuId = infra.SkuId, Quantity = 10m }
            }
        });
        createTrfRes.EnsureSuccessStatusCode();
        var transferDto = await createTrfRes.Content.ReadFromJsonAsync<StockTransferResponseDto>(_jsonOptions);
        transferDto.Should().NotBeNull();

        // 1. Dispatch Transfer -> Phát StockLevelChanged cho Branch A và LowStockWarning vì qua ngưỡng 35m (T5-F06)
        var dispatchRes = await ownerClient.PostAsJsonAsync($"/api/stock-transfers/{transferDto!.Id}/dispatch", new DispatchStockTransferRequestDto());
        dispatchRes.EnsureSuccessStatusCode();

        for (int i = 0; i < 30 && stockEvents.IsEmpty; i++) await Task.Delay(100);

        stockEvents.Should().Contain(s => s.BranchId == infra.BranchId && s.SkuId == infra.SkuId && s.NewAvailableStock == 30m,
            "Dispatch phải phát StockLevelChanged cho chi nhánh xuất với tồn khả dụng mới.");
        lowAlertEvents.Should().Contain(l => l.BranchId == infra.BranchId && l.SkuId == infra.SkuId && l.CurrentAvailable == 30m && l.Threshold == 35m,
            "Dispatch làm giảm tồn xuống dưới ReorderPoint (40 -> 30 <= 35) phải phát LowStockWarning.");

        // 2. Receive Transfer -> Phát StockLevelChanged cho Branch B (T5-F06 - hỗ trợ cả SKU mới nhận lần đầu)
        var receiveRes = await ownerClient.PostAsJsonAsync($"/api/stock-transfers/{transferDto.Id}/receive", new ReceiveStockTransferRequestDto());
        receiveRes.EnsureSuccessStatusCode();

        for (int i = 0; i < 30 && !stockEvents.Any(s => s.BranchId == branchBId); i++) await Task.Delay(100);

        stockEvents.Should().Contain(s => s.BranchId == branchBId && s.SkuId == infra.SkuId && s.NewAvailableStock == 10m,
            "Receive phải phát StockLevelChanged cho chi nhánh đích ngay cả khi SKU được nhập lần đầu vào kho đích.");

        await hub.StopAsync();
    }

    #endregion

    #region AC-10: Commit-Before-Send Gate & Notifier Failure Tolerance (PostgreSQL + Real SignalR Hub)

    [Fact]
    public async Task AC10_PostgresCommitGate_BeforeCommitNoEventsOrDbVisible_AfterCommitDispatched_NotifierFailureDurable()
    {
        var infra = await SetupShopInfraAsync("Shopee", initialOnHand: 50m);
        var token = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);

        var receivedOrders = new ConcurrentQueue<OrderSummaryRealtimeDto>();
        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/notifications?access_token=" + token).ToString(), o =>
            {
                o.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        hub.On<OrderSummaryRealtimeDto>("OrderCreated", receivedOrders.Enqueue);
        await hub.StartAsync();

        var contextTenant = new ProbeTenant(infra.TenantId);
        var gate = new CommitGate();
        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(contextTenant, gate);

        using var scope = _factory.Services.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<IRealtimeNotifier>();
        var service = new OrderService(db, contextTenant, NullLogger<OrderService>.Instance, notifier);

        var reqKey = Guid.NewGuid();
        var dto = new CreateOrderRequestDto
        {
            RequestKey = reqKey,
            BranchId = infra.BranchId,
            Channel = "Admin",
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = infra.SkuId, SkuCode = infra.SkuCode, Quantity = 1m, UnitPrice = infra.RetailPrice }
            }
        };

        var user = await db.Users.FirstAsync(u => u.TenantId == infra.TenantId && u.Email == "owner@shopa.local");
        var mutationTask = service.CreateOrderAsync(dto, user.Id);

        // Đợi transaction tiến tới CommitGate (ngay trước COMMIT)
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));

        // Tại Gate trước COMMIT: Context độc lập KHÔNG nhìn thấy đơn hàng trong CSDL và Hub nhận 0 sự kiện
        bool beforeVisible;
        await using (var readDb = PostgresTestEnvironment.CreateRuntimeDbContext(contextTenant))
        {
            beforeVisible = await readDb.Orders.AnyAsync(o => o.RequestKey == reqKey);
        }
        beforeVisible.Should().BeFalse("Trước khi COMMIT thành công, context CSDL độc lập không được nhìn thấy đơn hàng.");
        receivedOrders.Count.Should().Be(0, "Trước khi COMMIT thành công, SignalR Hub không được phát sự kiện OrderCreated.");

        // Mở cổng Gate để COMMIT diễn ra
        gate.Release.TrySetResult();
        var orderResult = await mutationTask;
        orderResult.Status.Should().Be("Reserved");

        // Sau COMMIT: Đơn hàng hiển thị trên context độc lập và SignalR Hub nhận đúng 1 sự kiện OrderCreated
        for (int wait = 0; wait < 30 && receivedOrders.IsEmpty; wait++) await Task.Delay(100);

        bool afterVisible;
        await using (var readDb = PostgresTestEnvironment.CreateRuntimeDbContext(contextTenant))
        {
            afterVisible = await readDb.Orders.AnyAsync(o => o.RequestKey == reqKey);
        }
        afterVisible.Should().BeTrue("Sau khi COMMIT thành công, context CSDL độc lập phải nhìn thấy đơn hàng.");
        receivedOrders.Count.Should().Be(1, "Sau khi COMMIT thành công, SignalR Hub phải nhận đúng 1 sự kiện OrderCreated.");

        // Fault injection sau commit: Notifier quăng lỗi nhưng nghiệp vụ vẫn thành công (bảo toàn đơn hàng trong CSDL)
        await using var failingDb = PostgresTestEnvironment.CreateRuntimeDbContext(contextTenant);
        var failureService = new OrderService(failingDb, contextTenant, NullLogger<OrderService>.Instance, new WebhookThrowingNotifier());
        var faultReqKey = Guid.NewGuid();
        dto.RequestKey = faultReqKey;

        var durableResult = await failureService.CreateOrderAsync(dto, user.Id);
        durableResult.Status.Should().Be("Reserved");

        await using (var readDb = PostgresTestEnvironment.CreateRuntimeDbContext(contextTenant))
        {
            var orderCount = await readDb.Orders.CountAsync(o => o.RequestKey == faultReqKey);
            orderCount.Should().Be(1, "Khi Notifier gặp lỗi sau commit, đơn hàng vẫn phải được bảo toàn chính xác 1 bản ghi.");
        }

        await hub.StopAsync();
    }

    #endregion

    #region AC-11: Concurrency (50 submissions same key, 50 competing for 1 stock)

    [Fact]

    public async Task AC11_Concurrency_50SubmissionsSameKey_ExactlyOneCreated_FortyNineReplayed()

    {

        var infra = await SetupShopInfraAsync("Shopee", initialOnHand: 100);

        var extOrderId = $"EXT_CONC_{Guid.NewGuid():N}"[..16];

        var fixedTime = DateTimeOffset.UtcNow;

        var payload = new CanonicalWebhookPayloadDto

        {

            Channel = "Shopee",

            ExternalShopId = infra.ExternalShopId,

            ExternalOrderId = extOrderId,

            CustomerName = "Đua 50 Luồng",

            OrderTimestamp = fixedTime,

            TotalAmount = 50_000m,

            Items = new List<CanonicalWebhookItemDto>

            {

                new() { SkuCode = infra.SkuCode, Quantity = 1, UnitPrice = 50_000m }

            }

        };

        // Kết nối SignalR Hub thật để đếm số lượng logical event OrderCreated được phát ra
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        var orderCreatedEvents = new ConcurrentQueue<OrderSummaryRealtimeDto>();
        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/notifications?access_token=" + ownerToken).ToString(), o =>
            {
                o.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        hub.On<OrderSummaryRealtimeDto>("OrderCreated", e => orderCreatedEvents.Enqueue(e));
        await hub.StartAsync();

        // Chạy 50 requests đồng thời bằng các HttpClient độc lập
        var tasks = Enumerable.Range(0, 50).Select(async _ =>
        {
            using var client = _factory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/simulator/shopee");
            req.Headers.Add("X-Webhook-Secret", infra.SecretKey);
            req.Content = JsonContent.Create(payload);
            var res = await client.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            return new { StatusCode = res.StatusCode, Body = body };
        }).ToList();

        var results = await Task.WhenAll(tasks);

        // Khẳng định chặt chẽ bằng HTTP status: đúng 1 status 201 Created và 49 status 200 OK
        var status201Count = results.Count(r => r.StatusCode == HttpStatusCode.Created);
        var status200Count = results.Count(r => r.StatusCode == HttpStatusCode.OK);

        status201Count.Should().Be(1, "Trong 50 submissions đồng thời cùng key, đúng 1 request đầu tiên nhận HTTP 201 Created.");
        status200Count.Should().Be(49, "49 submissions còn lại phải nhận HTTP 200 OK (idempotent replay).");

        var deserialized = results.Select(r => JsonSerializer.Deserialize<WebhookSimulationResultDto>(r.Body, _jsonOptions)!).ToList();
        var firstOrderId = deserialized.First().OrderId;
        firstOrderId.Should().NotBeNull();
        deserialized.Should().OnlyContain(d => d.OrderId == firstOrderId, "Tất cả 50 responses phải trả về cùng OrderId của đơn hàng ban đầu.");

        // Đúng 1 request nhận thông báo mới, 49 requests nhận thông báo replay
        var createdCount = deserialized.Count(d => d.Message.Contains("Tiếp nhận đơn hàng TMĐT thành công"));
        var replayedCount = deserialized.Count(d => d.Message.Contains("tồn tại trước đó"));

        createdCount.Should().Be(1, "Trong 50 submissions đồng thời, đúng 1 request đầu tiên được tạo mới.");
        replayedCount.Should().Be(49, "49 submissions còn lại phải nhận kết quả replay idempotent.");

        // Đếm logical OrderCreated attempt: Duy nhất đúng 1 sự kiện OrderCreated được dispatch tới tenant group
        for (int wait = 0; wait < 15 && orderCreatedEvents.IsEmpty; wait++) await Task.Delay(100);
        orderCreatedEvents.Count.Should().Be(1, "Duy nhất 1 sự kiện OrderCreated được phát ra tới tenant group cho 50 submissions cùng key.");

        // Đối soát Database: Duy nhất 1 Order, Reserved chỉ tăng 1

        var (context, _) = CreatePostgresDbContext(infra.TenantId);

        await using (context)

        {

            var dbOrders = await context.Orders.Where(o => o.TenantId == infra.TenantId && o.Channel == "Shopee" && o.ExternalShopId == infra.ExternalShopId && o.ExternalOrderId == extOrderId).ToListAsync();

            dbOrders.Should().HaveCount(1);

            var bBal = await context.InventoryBalances.FirstAsync(b => b.TenantId == infra.TenantId && b.BranchId == infra.BranchId && b.SkuId == infra.SkuId);

            bBal.Reserved.Should().Be(1m, "Tổng Reserved lũy kế đúng 1 đơn vị.");

        }

    }

    [Fact]

    public async Task AC11_Concurrency_50DifferentOrdersCompeteForOneStock_ExactlyOneWins_FortyNineFail()

    {

        // Tồn kho ban đầu đúng 1 đơn vị

        var infra = await SetupShopInfraAsync("Shopee", initialOnHand: 1m);

        // 50 external order ids khác nhau tranh chấp đúng 1 sản phẩm

        var tasks = Enumerable.Range(0, 50).Select(async i =>

        {

            using var client = _factory.CreateClient();

            var payload = new CanonicalWebhookPayloadDto

            {

                Channel = "Shopee",

                ExternalShopId = infra.ExternalShopId,

                ExternalOrderId = $"ORDER_RACE_{i}_{Guid.NewGuid():N}"[..20],

                OrderTimestamp = DateTimeOffset.UtcNow,

                TotalAmount = 50_000m,

                Items = new List<CanonicalWebhookItemDto>

                {

                    new() { SkuCode = infra.SkuCode, Quantity = 1m, UnitPrice = 50_000m }

                }

            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/simulator/shopee");

            req.Headers.Add("X-Webhook-Secret", infra.SecretKey);

            req.Content = JsonContent.Create(payload);

            var res = await client.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            return new { StatusCode = res.StatusCode, Body = body };
        }).ToList();

        var results = await Task.WhenAll(tasks);

        var successResponses = results.Where(r => r.StatusCode == HttpStatusCode.Created).ToList();
        var conflictResponses = results.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();

        successResponses.Count.Should().Be(1, "Chỉ có duy nhất 1 đơn hàng thành công giành được tồn kho.");
        conflictResponses.Count.Should().Be(49, "49 đơn hàng còn lại phải bị từ chối 409 INSUFFICIENT_STOCK.");

        foreach (var conflict in conflictResponses)
        {
            conflict.Body.Should().Contain("INSUFFICIENT_STOCK", "Tất cả các response 409 phải chứa đúng mã lỗi INSUFFICIENT_STOCK.");
        }

        // Đối soát Database: Reserved = 1, Available = 0, Active Reservations sum = 1, tuyệt đối không âm / oversell
        var (context, _) = CreatePostgresDbContext(infra.TenantId);
        await using (context)
        {
            var bBal = await context.InventoryBalances.FirstAsync(b => b.TenantId == infra.TenantId && b.BranchId == infra.BranchId && b.SkuId == infra.SkuId);
            bBal.OnHand.Should().Be(1m);
            bBal.Reserved.Should().Be(1m);
            var available = bBal.OnHand - bBal.Reserved;
            available.Should().Be(0m, "Tồn khả dụng phải bằng đúng 0, không được âm.");

            var activeReservations = await context.Reservations
                .Where(r => r.TenantId == infra.TenantId && r.BranchId == infra.BranchId && r.SkuId == infra.SkuId && r.Status == "Active")
                .ToListAsync();
            activeReservations.Count.Should().Be(1, "Chỉ duy nhất 1 bản ghi reservation Active tồn tại trong hệ thống.");
            activeReservations.Sum(r => r.Quantity).Should().Be(bBal.Reserved, "Tổng số lượng reservation Active phải khớp chính xác với branch Reserved.");
        }
    }

    [Fact]
    public async Task AC11_Concurrency_CrossFlow_WebhookVsPosVsAdmin_ZeroOversell()
    {
        // Tồn kho ban đầu đúng 2 đơn vị
        var infra = await SetupShopInfraAsync("Shopee", initialOnHand: 2m);
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);

        // Chuẩn bị 3 tác vụ đồng thời: Webhook (qty 2), Admin (qty 2), POS Checkout (qty 2)
        var webhookTask = Task.Run(async () =>
        {
            using var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Webhook-Secret", infra.SecretKey);
            var payload = new CanonicalWebhookPayloadDto
            {
                Channel = "Shopee",
                ExternalShopId = infra.ExternalShopId,
                ExternalOrderId = $"EXT_CROSS_{Guid.NewGuid():N}"[..16],
                CustomerName = "Đua Webhook",
                OrderTimestamp = DateTimeOffset.UtcNow,
                TotalAmount = 2 * infra.RetailPrice,
                Items = new List<CanonicalWebhookItemDto>
                {
                    new() { SkuCode = infra.SkuCode, Quantity = 2m, UnitPrice = infra.RetailPrice }
                }
            };
            return await client.PostAsJsonAsync("/api/webhooks/simulator/Shopee", payload);
        });

        var adminTask = Task.Run(async () =>
        {
            using var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
            var req = new CreateOrderRequestDto
            {
                RequestKey = Guid.NewGuid(),
                BranchId = infra.BranchId,
                Channel = "Admin",
                Items = new List<OrderItemRequestDto>
                {
                    new() { SkuId = infra.SkuId, SkuCode = infra.SkuCode, Quantity = 2m, UnitPrice = infra.RetailPrice }
                }
            };
            return await client.PostAsJsonAsync("/api/orders", req);
        });

        var posTask = Task.Run(async () =>
        {
            using var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
            var req = new PosCheckoutRequestDto
            {
                RequestKey = Guid.NewGuid(),
                BranchId = infra.BranchId,
                PaymentMethod = "Cash",
                AmountGiven = 2 * infra.RetailPrice,
                TotalAmount = 2 * infra.RetailPrice,
                Items = new List<OrderItemRequestDto>
                {
                    new() { SkuId = infra.SkuId, SkuCode = infra.SkuCode, Quantity = 2m, UnitPrice = infra.RetailPrice }
                }
            };
            return await client.PostAsJsonAsync("/api/pos/checkout", req);
        });

        var responses = await Task.WhenAll(webhookTask, adminTask, posTask);

        var successCount = responses.Count(r => r.IsSuccessStatusCode);
        var conflictResponses = responses.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();

        successCount.Should().Be(1, "Chỉ duy nhất 1 tác vụ được phép thành công khi tranh chấp toàn bộ tồn kho 2 đơn vị.");
        conflictResponses.Count.Should().Be(2, "2 tác vụ còn lại phải nhận 409 Conflict (INSUFFICIENT_STOCK).");

        foreach (var conflict in conflictResponses)
        {
            var body = await conflict.Content.ReadAsStringAsync();
            body.Should().Contain("INSUFFICIENT_STOCK", "Các response 409 trong cross-flow concurrency phải chỉ rõ lỗi INSUFFICIENT_STOCK.");
        }

        var (verifyDb, _) = CreatePostgresDbContext(infra.TenantId);
        await using (verifyDb)
        {
            var bBal = await verifyDb.InventoryBalances.FirstAsync(b => b.TenantId == infra.TenantId && b.BranchId == infra.BranchId && b.SkuId == infra.SkuId);
            bBal.OnHand.Should().BeGreaterThanOrEqualTo(0m);
            bBal.Reserved.Should().BeGreaterThanOrEqualTo(0m);
            (bBal.OnHand - bBal.Reserved).Should().Be(0m, "Tồn khả dụng chi nhánh phải bằng 0 sau khi 1 tác vụ chiếm toàn bộ tồn kho.");

            var wBal = await verifyDb.WarehouseInventoryBalances.FirstAsync(w => w.TenantId == infra.TenantId && w.BranchId == infra.BranchId && w.SkuId == infra.SkuId);
            wBal.OnHand.Should().BeGreaterThanOrEqualTo(0m);
            wBal.Reserved.Should().BeGreaterThanOrEqualTo(0m);
            (wBal.OnHand - wBal.Reserved).Should().Be(0m, "Tồn khả dụng kho phải bằng 0 sau khi 1 tác vụ chiếm toàn bộ tồn kho.");

            bBal.OnHand.Should().Be(wBal.OnHand);
            bBal.Reserved.Should().Be(wBal.Reserved);
        }
    }

    [Fact]
    public async Task AC11_Concurrency_MultiSkuInvertedLockOrder_NoDeadlock_MaintainsInvariants()
    {
        var infra = await SetupShopInfraAsync("Shopee", initialOnHand: 20m);
        var ownerToken = await LoginAsAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);

        // Đảm bảo SKU thứ 2 có tồn kho trong Branch A
        ProductVariant sku2;
        var (setupDb, tService) = CreatePostgresDbContext(infra.TenantId);
        await using (setupDb)
        {
            var product = await setupDb.Products.FirstAsync(p => p.TenantId == infra.TenantId && p.IsActive);
            sku2 = new ProductVariant
            {
                Id = Guid.NewGuid(),
                TenantId = infra.TenantId,
                ProductId = product.Id,
                Code = "POLO-NAM-TRANG-L-" + Guid.NewGuid().ToString("N")[..6],
                SKU = "POLO-TRANG-L-" + Guid.NewGuid().ToString("N")[..6],
                VariantName = "Polo Nam Trắng L",
                RetailPrice = 120_000m,
                CostPrice = 60_000m,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            setupDb.ProductVariants.Add(sku2);
            await setupDb.SaveChangesAsync();

            var user = await setupDb.Users.FirstAsync(u => u.TenantId == infra.TenantId && u.Email == "owner@shopa.local");
            var supplier = await setupDb.Suppliers.FirstAsync(s => s.TenantId == infra.TenantId);
            var grService = new GoodsReceiptService(setupDb, tService, new WacCalculator());
            var receipt = await grService.CreateReceiptAsync(new CreateGoodsReceiptDto
            {
                BranchId = infra.BranchId,
                WarehouseId = infra.WarehouseId,
                SupplierId = supplier.Id,
                Note = "Seed stock multi-sku",
                Items = new List<CreateGoodsReceiptItemDto>
                {
                    new() { SkuId = infra.SkuId, Quantity = 10m, UnitPrice = 50_000m },
                    new() { SkuId = sku2.Id, Quantity = 10m, UnitPrice = 60_000m }
                }
            }, user.Id);
            await grService.ConfirmReceiptAsync(receipt.Id, user.Id);
        }

        // Đua 2 tác vụ đảo thứ tự SKU kèm theo Observer quan sát mọi mã lỗi SQL trước retry:
        // Task A: [infra.SkuId, sku2.Id]
        // Task B: [sku2.Id, infra.SkuId]
        var observer = new SqlStateDeadlockObserver();
        using var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<AppDbContext>(options =>
                {
                    options.UseNpgsql(PostgresTestEnvironment.RuntimeConnectionString, npgsqlOptions =>
                    {
                        npgsqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                        npgsqlOptions.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
                    })
                    .UseSnakeCaseNamingConvention()
                    .AddInterceptors(observer);
                });
            });
        });

        // Positive control: Chứng minh observer cùng wiring thật trong customFactory bắt được SQL failure / 40P01 nhân tạo
        using (var scope = customFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var actControl = async () => await db.Database.ExecuteSqlRawAsync("DO $$ BEGIN RAISE EXCEPTION 'deadlock test' USING ERRCODE = '40P01'; END $$;");
            await actControl.Should().ThrowAsync<PostgresException>().Where(ex => ex.SqlState == "40P01");
        }
        observer.Deadlock40P01Count.Should().Be(1, "Positive control chứng minh observer bắt được 40P01 qua DbContext options.");
        observer.Reset();
        observer.Deadlock40P01Count.Should().Be(0, "Observer phải được reset sạch trước khi bước vào race multi-SKU.");

        var taskA = Task.Run(async () =>
        {
            using var client = customFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
            return await client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
            {
                RequestKey = Guid.NewGuid(),
                BranchId = infra.BranchId,
                Channel = "Admin",
                Items = new List<OrderItemRequestDto>
                {
                    new() { SkuId = infra.SkuId, SkuCode = infra.SkuCode, Quantity = 1m, UnitPrice = infra.RetailPrice },
                    new() { SkuId = sku2.Id, SkuCode = sku2.Code, Quantity = 1m, UnitPrice = sku2.RetailPrice }
                }
            });
        });

        var taskB = Task.Run(async () =>
        {
            using var client = customFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
            return await client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
            {
                RequestKey = Guid.NewGuid(),
                BranchId = infra.BranchId,
                Channel = "Admin",
                Items = new List<OrderItemRequestDto>
                {
                    new() { SkuId = sku2.Id, SkuCode = sku2.Code, Quantity = 1m, UnitPrice = sku2.RetailPrice },
                    new() { SkuId = infra.SkuId, SkuCode = infra.SkuCode, Quantity = 1m, UnitPrice = infra.RetailPrice }
                }
            });
        });

        var responses = await Task.WhenAll(taskA, taskB);

        foreach (var r in responses)
        {
            r.StatusCode.Should().Be(HttpStatusCode.Created, "Cả hai đơn multi-SKU đảo thứ tự phải hoàn tất thành công mà không gặp deadlock 40P01.");
        }

        observer.Deadlock40P01Count.Should().Be(0, "Không được xuất hiện bất kỳ mã lỗi deadlock 40P01 nào trên PostgreSQL trong toàn bộ các attempts trước retry.");

        // Kiểm tra đối soát tồn kho CSDL: Cả 2 SKU đều phải được giữ chỗ chính xác 2 đơn vị
        var (verifyDb, _) = CreatePostgresDbContext(infra.TenantId);
        await using (verifyDb)
        {
            var bal1 = await verifyDb.InventoryBalances.FirstAsync(b => b.TenantId == infra.TenantId && b.BranchId == infra.BranchId && b.SkuId == infra.SkuId);
            bal1.Reserved.Should().Be(2m, "SKU 1 phải được giữ chỗ chính xác 2 đơn vị.");
            var bal2 = await verifyDb.InventoryBalances.FirstAsync(b => b.TenantId == infra.TenantId && b.BranchId == infra.BranchId && b.SkuId == sku2.Id);
            bal2.Reserved.Should().Be(2m, "SKU 2 phải được giữ chỗ chính xác 2 đơn vị.");
        }
    }

    #endregion

    #region AC-12: Fault Injection Rollback & Clean Retry

    public class MidstreamWebhookFaultInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor

    {

        public bool ShouldFail { get; set; } = true;

        public int SaveChangesCalls { get; private set; }

        public override ValueTask<int> SavedChangesAsync(

            Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesCompletedEventData eventData,

            int result,

            CancellationToken cancellationToken = default)

        {

            SaveChangesCalls++;

            if (ShouldFail)

            {

                throw new InvalidOperationException("Mô phỏng sự cố mạng/crash sau khi SaveChanges nhưng trước khi COMMIT transaction webhook.");

            }

            return base.SavedChangesAsync(eventData, result, cancellationToken);

        }

    }

    [Fact]

    public async Task AC12_FaultInjectionRollbackAndCleanRetry()

    {

        var infra = await SetupShopInfraAsync("Shopee", initialOnHand: 30);

        var interceptor = new MidstreamWebhookFaultInterceptor();
        var (faultContext, _) = CreatePostgresDbContext(infra.TenantId, interceptor);
        var recordingNotifier = new RecordingRealtimeNotifier();
        var orderService = new OrderService(
            faultContext,
            new TestTenantService { TenantId = infra.TenantId },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>.Instance,
            recordingNotifier);

        // Snapshot trạng thái CSDL trước khi inject fault
        var (snapshotCtx, _) = CreatePostgresDbContext(infra.TenantId);
        InventoryBalance initBranchBal;
        WarehouseInventoryBalance initWhBal;
        int initOrdersCount, initOrderItemsCount, initReservationsCount, initPaymentsCount, initLedgerCount;
        await using (snapshotCtx)
        {
            initBranchBal = await snapshotCtx.InventoryBalances.AsNoTracking().FirstAsync(b => b.TenantId == infra.TenantId && b.BranchId == infra.BranchId && b.SkuId == infra.SkuId);
            initWhBal = await snapshotCtx.WarehouseInventoryBalances.AsNoTracking().FirstAsync(w => w.TenantId == infra.TenantId && w.BranchId == infra.BranchId && w.SkuId == infra.SkuId);
            initOrdersCount = await snapshotCtx.Orders.CountAsync(o => o.TenantId == infra.TenantId);
            initOrderItemsCount = await snapshotCtx.OrderItems.CountAsync(o => o.TenantId == infra.TenantId);
            initReservationsCount = await snapshotCtx.Reservations.CountAsync(r => r.TenantId == infra.TenantId);
            initPaymentsCount = await snapshotCtx.Payments.CountAsync(p => p.TenantId == infra.TenantId);
            initLedgerCount = await snapshotCtx.InventoryTransactions.CountAsync(t => t.TenantId == infra.TenantId);
        }

        var extOrderId = $"EXT_FAULT_{Guid.NewGuid():N}"[..16];
        var orderContext = new InternalMarketplaceOrderContext
        {
            TenantId = infra.TenantId,
            BranchId = infra.BranchId,
            Channel = "Shopee",
            ExternalShopId = infra.ExternalShopId,
            ExternalOrderId = extOrderId,
            WebhookPayloadSignature = new string('f', 64),
            RequestKey = Guid.NewGuid(),
            ExpectedTotalAmount = 50_000m,
            OrderTimestamp = DateTimeOffset.UtcNow,
            Items = new List<InternalMarketplaceOrderItemContext>
            {
                new() { SkuCode = infra.SkuCode, Quantity = 1, UnitPrice = 50_000m }
            }
        };

        // 1. Lần gọi 1: Bị fault inject sau SaveChanges -> Phải rollback 100%
        Func<Task> actFault = async () => await orderService.CreateMarketplaceReservedOrderAsync(orderContext);
        await actFault.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Mô phỏng sự cố mạng/crash*");

        // Khẳng định không có bất kỳ sự kiện thời gian thực nào bị phát sớm khi transaction rollback
        recordingNotifier.OrderCreatedEvents.Should().BeEmpty("Tuyệt đối không được phát sự kiện OrderCreated khi transaction bị rollback.");
        recordingNotifier.StockChangedEvents.Should().BeEmpty("Tuyệt đối không được phát sự kiện StockLevelChanged khi transaction bị rollback.");
        recordingNotifier.LowStockEvents.Should().BeEmpty("Tuyệt đối không được phát sự kiện LowStockWarning khi transaction bị rollback.");

        // Đối soát qua DbContext độc lập: Snapshot CSDL sau rollback phải khớp 100% với trạng thái ban đầu
        var (verifyContext, _) = CreatePostgresDbContext(infra.TenantId);
        await using (verifyContext)
        {
            var orderExists = await verifyContext.Orders.AnyAsync(o => o.TenantId == infra.TenantId && o.ExternalOrderId == extOrderId);
            orderExists.Should().BeFalse("Giao dịch phải được rollback 100%, không để lại Order rác.");

            var postOrdersCount = await verifyContext.Orders.CountAsync(o => o.TenantId == infra.TenantId);
            var postOrderItemsCount = await verifyContext.OrderItems.CountAsync(o => o.TenantId == infra.TenantId);
            var postReservationsCount = await verifyContext.Reservations.CountAsync(r => r.TenantId == infra.TenantId);
            var postPaymentsCount = await verifyContext.Payments.CountAsync(p => p.TenantId == infra.TenantId);
            var postLedgerCount = await verifyContext.InventoryTransactions.CountAsync(t => t.TenantId == infra.TenantId);

            postOrdersCount.Should().Be(initOrdersCount, "Số lượng Orders không đổi sau rollback.");
            postOrderItemsCount.Should().Be(initOrderItemsCount, "Số lượng OrderItems không đổi sau rollback.");
            postReservationsCount.Should().Be(initReservationsCount, "Số lượng Reservations không đổi sau rollback.");
            postPaymentsCount.Should().Be(initPaymentsCount, "Số lượng Payments không đổi sau rollback.");
            postLedgerCount.Should().Be(initLedgerCount, "Số lượng Sổ cái (Ledger) không đổi sau rollback.");

            var bBal = await verifyContext.InventoryBalances.FirstAsync(b => b.TenantId == infra.TenantId && b.BranchId == infra.BranchId && b.SkuId == infra.SkuId);
            bBal.OnHand.Should().Be(initBranchBal.OnHand);
            bBal.Reserved.Should().Be(initBranchBal.Reserved);
            bBal.AverageCost.Should().Be(initBranchBal.AverageCost);
            bBal.LastSequence.Should().Be(initBranchBal.LastSequence);
            bBal.UpdatedAt.Should().Be(initBranchBal.UpdatedAt);

            var wBal = await verifyContext.WarehouseInventoryBalances.FirstAsync(w => w.TenantId == infra.TenantId && w.BranchId == infra.BranchId && w.SkuId == infra.SkuId);
            wBal.OnHand.Should().Be(initWhBal.OnHand);
            wBal.Reserved.Should().Be(initWhBal.Reserved);
            wBal.LastSequence.Should().Be(initWhBal.LastSequence);
            wBal.UpdatedAt.Should().Be(initWhBal.UpdatedAt);
        }

        // 2. Tắt fault injection và Thử lại (Retry) cùng identity -> Thành công
        interceptor.ShouldFail = false;
        var retryResult = await orderService.CreateMarketplaceReservedOrderAsync(orderContext);
        retryResult.Should().NotBeNull();
        retryResult.Status.Should().Be("Reserved");

        // Sau retry thành công: Sự kiện OrderCreated và StockLevelChanged được phát đúng 1 lần
        recordingNotifier.OrderCreatedEvents.Count.Should().Be(1, "Sau retry thành công, đúng 1 sự kiện OrderCreated được phát ra.");
        recordingNotifier.StockChangedEvents.Count.Should().Be(1, "Sau retry thành công, đúng 1 sự kiện StockLevelChanged được phát ra.");

        // Đối soát lại Database: Có đúng 1 order, Reserved tăng 1
        var (verifyContext2, _) = CreatePostgresDbContext(infra.TenantId);
        await using (verifyContext2)
        {
            var dbOrder = await verifyContext2.Orders.FirstOrDefaultAsync(o => o.TenantId == infra.TenantId && o.ExternalOrderId == extOrderId);
            dbOrder.Should().NotBeNull();
            dbOrder!.Status.Should().Be("Reserved");
            dbOrder.WebhookPayloadSignature.Should().Be(orderContext.WebhookPayloadSignature);

            var bBal = await verifyContext2.InventoryBalances.FirstAsync(b => b.TenantId == infra.TenantId && b.BranchId == infra.BranchId && b.SkuId == infra.SkuId);
            bBal.Reserved.Should().Be(initBranchBal.Reserved + 1m);

            var wBal = await verifyContext2.WarehouseInventoryBalances.FirstAsync(w => w.TenantId == infra.TenantId && w.BranchId == infra.BranchId && w.SkuId == infra.SkuId);
            wBal.Reserved.Should().Be(initWhBal.Reserved + 1m);
        }

    }

    #endregion

    #region AC-13: Schema Migration Idempotency & Legacy Data Safety

    [Fact]
    public async Task AC13_SchemaMigrationIdempotencyAndLegacyDataSafety()
    {
        var infra = await SetupShopInfraAsync("Shopee", initialOnHand: 40m, reorderPoint: 5m);
        var contextTenant = new ProbeTenant(infra.TenantId);

        // 1. Dựng dữ liệu nghiệp vụ legacy qua các core engines (Admin, Marketplace, POS)
        using (var scope = _factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ICurrentTenantService>().SetTenant(infra.TenantId);
            var service = scope.ServiceProvider.GetRequiredService<IOrderService>();
            var user = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.FirstAsync(u => u.TenantId == infra.TenantId && u.Email == "owner@shopa.local");

            // Admin order Reserved
            await service.CreateOrderAsync(new CreateOrderRequestDto
            {
                RequestKey = Guid.NewGuid(),
                BranchId = infra.BranchId,
                Channel = "Admin",
                Items = new() { new() { SkuId = infra.SkuId, SkuCode = infra.SkuCode, Quantity = 1m, UnitPrice = infra.RetailPrice } }
            }, user.Id);

            // Marketplace order Reserved
            await service.CreateMarketplaceReservedOrderAsync(new InternalMarketplaceOrderContext
            {
                TenantId = infra.TenantId,
                BranchId = infra.BranchId,
                Channel = "Shopee",
                ExternalShopId = "LEGACY-SHOP",
                ExternalOrderId = "LEGACY-ORDER",
                WebhookPayloadSignature = new string('a', 64),
                ExpectedTotalAmount = 10m,
                OrderTimestamp = DateTimeOffset.UtcNow,
                Items = new() { new() { SkuCode = infra.SkuCode, Quantity = 1m, UnitPrice = 10m } }
            });

            // POS order Completed có thanh toán và xuất kho
            await service.CheckoutPosAsync(new PosCheckoutRequestDto
            {
                RequestKey = Guid.NewGuid(),
                BranchId = infra.BranchId,
                PaymentMethod = "Cash",
                AmountGiven = infra.RetailPrice,
                TotalAmount = infra.RetailPrice,
                Items = new() { new() { SkuId = infra.SkuId, SkuCode = infra.SkuCode, Quantity = 1m, UnitPrice = infra.RetailPrice } }
            }, user.Id);
        }

        var adminConnection = PostgresTestEnvironment.AdminDatabaseConnectionString;
        PostgresTestEnvironment.AssertSafeTestTarget(adminConnection);
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();

        async Task<string> Snapshot()
        {
            var rows = new List<string>();
            foreach (var table in new[] { "orders", "order_items", "reservations", "inventory_transactions", "payments", "inventory_balances", "warehouse_inventory_balances" })
            {
                await using var sql = new NpgsqlCommand($"SELECT COALESCE(jsonb_agg(to_jsonb(t) - 'webhook_payload_signature' ORDER BY t.tenant_id, t.id), '[]'::jsonb)::text FROM {table} t", connection);
                rows.Add((string)(await sql.ExecuteScalarAsync())!);
            }
            return string.Join("\n", rows);
        }

        // 2. Mô phỏng schema trước Task 5 trong database test do fixture quản lý
        await using (var sql = new NpgsqlCommand("DROP TABLE IF EXISTS shop_integrations; ALTER TABLE orders DROP COLUMN IF EXISTS webhook_payload_signature;", connection))
        {
            await sql.ExecuteNonQueryAsync();
        }

        var before = await Snapshot();

        // 3. Nâng cấp schema lần 1
        var migrationService = new DatabaseMigrationService();
        var first = await migrationService.UpgradeDatabaseSchemaAsync(adminConnection);
        first.Success.Should().BeTrue(first.Message);

        // 4. Nâng cấp schema lần 2 (idempotent, không lỗi)
        var second = await migrationService.UpgradeDatabaseSchemaAsync(adminConnection);
        second.Success.Should().BeTrue(second.Message);

        // 5. Đối soát dữ liệu nghiệp vụ: Không đổi bất kỳ dòng nào (bỏ qua cột webhook_payload_signature mới)
        var after = await Snapshot();
        (before == after).Should().BeTrue("Dữ liệu nghiệp vụ cũ không được thay đổi sau khi upgrade schema.");

        // 6. Comprehensive validator kiểm tra toàn diện schema
        var validation = await DatabaseMigrationService.ValidateSchemaComprehensiveAsync(connection, null, default);
        validation.IsValid.Should().BeTrue(string.Join("; ", validation.DriftErrors));

        // 7. Kiểm tra phát hiện drift nullability (T5-F01)
        // a) shop_integrations.secret_hash bị DROP NOT NULL -> Validator phải IsValid = false
        await using (var sql = new NpgsqlCommand("ALTER TABLE shop_integrations ALTER COLUMN secret_hash DROP NOT NULL;", connection))
        {
            await sql.ExecuteNonQueryAsync();
        }
        var drift1 = await DatabaseMigrationService.ValidateSchemaComprehensiveAsync(connection, null, default);
        drift1.IsValid.Should().BeFalse("Validator phải phát hiện lỗi khi shop_integrations.secret_hash bị mất NOT NULL.");
        drift1.DriftErrors.Should().Contain(e => e.Contains("shop_integrations.secret_hash") && e.Contains("nullability"));

        // Khôi phục NOT NULL
        await using (var sql = new NpgsqlCommand("ALTER TABLE shop_integrations ALTER COLUMN secret_hash SET NOT NULL;", connection))
        {
            await sql.ExecuteNonQueryAsync();
        }

        // b) orders.webhook_payload_signature bị gán NOT NULL -> Validator phải IsValid = false
        // Lưu ý: Cần điền tạm giá trị placeholder cho các dòng có webhook_payload_signature = NULL trước khi SET NOT NULL
        await using (var sql = new NpgsqlCommand("UPDATE orders SET webhook_payload_signature = 'legacy_sig_drift' WHERE webhook_payload_signature IS NULL;", connection))
        {
            await sql.ExecuteNonQueryAsync();
        }
        await using (var sql = new NpgsqlCommand("ALTER TABLE orders ALTER COLUMN webhook_payload_signature SET NOT NULL;", connection))
        {
            await sql.ExecuteNonQueryAsync();
        }
        var drift2 = await DatabaseMigrationService.ValidateSchemaComprehensiveAsync(connection, null, default);
        drift2.IsValid.Should().BeFalse("Validator phải phát hiện lỗi khi orders.webhook_payload_signature bị đặt NOT NULL.");
        drift2.DriftErrors.Should().Contain(e => e.Contains("orders.webhook_payload_signature") && e.Contains("nullability"));

        // Khôi phục nullable và giá trị null ban đầu
        await using (var sql = new NpgsqlCommand("ALTER TABLE orders ALTER COLUMN webhook_payload_signature DROP NOT NULL;", connection))
        {
            await sql.ExecuteNonQueryAsync();
        }
        await using (var sql = new NpgsqlCommand("UPDATE orders SET webhook_payload_signature = NULL WHERE webhook_payload_signature = 'legacy_sig_drift';", connection))
        {
            await sql.ExecuteNonQueryAsync();
        }

        var finalValidation = await DatabaseMigrationService.ValidateSchemaComprehensiveAsync(connection, null, default);
        finalValidation.IsValid.Should().BeTrue(string.Join("; ", finalValidation.DriftErrors));
    }

    #endregion

    private sealed class CommitGate : DbTransactionInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken ct = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            return result;
        }
    }

    private sealed class WebhookThrowingNotifier : IRealtimeNotifier
    {
        public Task NotifyNewOrderAsync(Guid tenantId, OrderSummaryRealtimeDto order, CancellationToken ct = default)
            => throw new IOException("Injected notifier failure");
        public Task NotifyStockChangedAsync(Guid tenantId, Guid branchId, Guid skuId, decimal available, CancellationToken ct = default)
            => throw new IOException("Injected notifier failure");
        public Task NotifyLowStockAlertAsync(Guid tenantId, Guid branchId, Guid skuId, string skuCode, decimal available, decimal threshold, CancellationToken ct = default)
            => throw new IOException("Injected notifier failure");
    }

    private sealed class ProbeTenant(Guid? value) : ICurrentTenantService
    {
        public Guid? TenantId { get; private set; } = value;
        public void SetTenant(Guid id) => TenantId = id;
    }

    private sealed class RecordingRealtimeNotifier : IRealtimeNotifier
    {
        public ConcurrentBag<OrderSummaryRealtimeDto> OrderCreatedEvents { get; } = new();
        public ConcurrentBag<(Guid BranchId, Guid SkuId, decimal Available)> StockChangedEvents { get; } = new();
        public ConcurrentBag<(Guid BranchId, Guid SkuId, string SkuCode, decimal Available, decimal Threshold)> LowStockEvents { get; } = new();

        public Task NotifyNewOrderAsync(Guid tenantId, OrderSummaryRealtimeDto order, CancellationToken ct = default)
        {
            OrderCreatedEvents.Add(order);
            return Task.CompletedTask;
        }

        public Task NotifyStockChangedAsync(Guid tenantId, Guid branchId, Guid skuId, decimal availableStock, CancellationToken ct = default)
        {
            StockChangedEvents.Add((branchId, skuId, availableStock));
            return Task.CompletedTask;
        }

        public Task NotifyLowStockAlertAsync(Guid tenantId, Guid branchId, Guid skuId, string skuCode, decimal currentAvailable, decimal threshold, CancellationToken ct = default)
        {
            LowStockEvents.Add((branchId, skuId, skuCode, currentAvailable, threshold));
            return Task.CompletedTask;
        }
    }

    private sealed class SqlStateDeadlockObserver : DbCommandInterceptor
    {
        public ConcurrentBag<string> FailedSqlStates { get; } = new();
        public ConcurrentBag<Exception> CaughtExceptions { get; } = new();
        public int Deadlock40P01Count => FailedSqlStates.Count(s => s == "40P01");

        public void Reset()
        {
            while (FailedSqlStates.TryTake(out _)) { }
            while (CaughtExceptions.TryTake(out _)) { }
        }

        public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
        {
            Record(eventData.Exception);
            base.CommandFailed(command, eventData);
        }

        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            Record(eventData.Exception);
            return base.CommandFailedAsync(command, eventData, cancellationToken);
        }

        private void Record(Exception? ex)
        {
            if (ex == null) return;
            CaughtExceptions.Add(ex);
            var curr = ex;
            while (curr != null)
            {
                if (curr is PostgresException pgEx && !string.IsNullOrEmpty(pgEx.SqlState))
                {
                    FailedSqlStates.Add(pgEx.SqlState);
                    break;
                }
                curr = curr.InnerException;
            }
        }
    }
}