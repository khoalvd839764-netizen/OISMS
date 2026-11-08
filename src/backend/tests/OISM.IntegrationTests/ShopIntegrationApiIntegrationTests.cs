using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.ShopIntegrations;
using OISM.Application.Features.Webhooks.Dtos;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// API quản lý kết nối gian hàng sàn trên PostgreSQL thật: secret được tạo qua API phải dùng được
/// cho webhook, đổi secret làm secret cũ mất hiệu lực, tắt kết nối chặn webhook, và cô lập tenant.
/// </summary>
public class ShopIntegrationApiIntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public ShopIntegrationApiIntegrationTests(WebApplicationFactoryWithPostgres factory)
    {
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
        var context = PostgresTestEnvironment.CreateRuntimeDbContext(new TestTenantService());
        await using (context)
        {
            await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class TestTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private async Task<string> LoginAsync(string email)
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = PostgresTestEnvironment.AppPassword });
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>(_json);
        return auth!.AccessToken;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? token, object? body = null)
    {
        using var req = new HttpRequestMessage(method, url);
        if (token != null)
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        if (body != null)
        {
            req.Content = JsonContent.Create(body);
        }
        return await _client.SendAsync(req);
    }

    private async Task<(Guid BranchId, string SkuCode)> GetTenantABranchAndSkuAsync()
    {
        var context = PostgresTestEnvironment.CreateRuntimeDbContext(new TestTenantService { TenantId = OismsDatabaseSeeder.TenantAId });
        await using (context)
        {
            var branch = await context.Branches.FirstAsync(b => b.IsActive);
            var wh = await context.Warehouses.FirstAsync(w => w.BranchId == branch.Id && w.IsActive);
            var sku = await context.ProductVariants.FirstAsync(v => v.IsActive);
            var supplier = await context.Suppliers.FirstAsync(s => s.IsActive);
            var owner = await context.Users.FirstAsync(u => u.Email == "owner@shopa.local");
            var tenantService = new TestTenantService { TenantId = OismsDatabaseSeeder.TenantAId };
            await PurchaseOrderSeed.ReceiveStockAsync(context, tenantService, branch.Id, wh.Id, supplier.Id, owner.Id, (sku.Id, 20m, 50_000m));
            return (branch.Id, sku.SKU);
        }
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(string secret, string shopId, string skuCode)
    {
        var payload = new CanonicalWebhookPayloadDto
        {
            Channel = "Shopee",
            ExternalShopId = shopId,
            ExternalOrderId = $"ORD_{Guid.NewGuid():N}"[..16],
            CustomerName = "Khách giả lập",
            OrderTimestamp = DateTimeOffset.UtcNow,
            TotalAmount = 100_000m,
            Items = [new() { SkuCode = skuCode, Quantity = 1, UnitPrice = 100_000m }]
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/simulator/shopee");
        req.Headers.Add("X-Webhook-Secret", secret);
        req.Content = JsonContent.Create(payload);
        return await _client.SendAsync(req);
    }

    [Fact]
    public async Task OwnerLifecycle_CreatedSecretWorksForWebhook_RotateAndDeactivateRevokeIt()
    {
        var (branchId, skuCode) = await GetTenantABranchAndSkuAsync();
        var ownerToken = await LoginAsync("owner@shopa.local");
        var shopId = $"SHOP_{Guid.NewGuid():N}"[..16];

        var createRes = await SendAsync(HttpMethod.Post, "/api/shop-integrations", ownerToken,
            new CreateShopIntegrationRequest { Channel = "shopee", ExternalShopId = shopId, BranchId = branchId });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created, await createRes.Content.ReadAsStringAsync());
        var created = (await createRes.Content.ReadFromJsonAsync<ShopIntegrationSecretResponse>(_json))!;
        created.WebhookSecret.Should().StartWith("whsec_");
        created.Integration.Channel.Should().Be("Shopee");

        // Danh sách không lộ secret hay hash
        var listRes = await SendAsync(HttpMethod.Get, "/api/shop-integrations", ownerToken);
        listRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var listJson = await listRes.Content.ReadAsStringAsync();
        listJson.Should().Contain(shopId);
        listJson.Should().NotContain(created.WebhookSecret).And.NotContainEquivalentOf("secretHash");

        // Trùng gian hàng -> 409
        var dupRes = await SendAsync(HttpMethod.Post, "/api/shop-integrations", ownerToken,
            new CreateShopIntegrationRequest { Channel = "Shopee", ExternalShopId = shopId, BranchId = branchId });
        dupRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await dupRes.Content.ReadAsStringAsync()).Should().Contain("SHOP_INTEGRATION_DUPLICATE");

        // whoami: secret cho biết đúng gian hàng, không cần đăng nhập, không lộ id nội bộ
        var whoRes = await GetWhoAmIAsync(created.WebhookSecret);
        whoRes.StatusCode.Should().Be(HttpStatusCode.OK, await whoRes.Content.ReadAsStringAsync());
        var who = (await whoRes.Content.ReadFromJsonAsync<WebhookIdentityDto>(_json))!;
        who.Channel.Should().Be("Shopee");
        who.ExternalShopId.Should().Be(shopId);
        who.BranchName.Should().NotBeNullOrWhiteSpace();
        who.BranchIsActive.Should().BeTrue();
        var whoJson = await (await GetWhoAmIAsync(created.WebhookSecret)).Content.ReadAsStringAsync();
        whoJson.Should().NotContainEquivalentOf("tenantId").And.NotContain(branchId.ToString());
        (await GetWhoAmIAsync("whsec_khong_ton_tai")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _client.GetAsync("/api/webhooks/simulator/whoami")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Secret vừa tạo dùng được cho webhook
        var hook1 = await PostWebhookAsync(created.WebhookSecret, shopId, skuCode);
        hook1.StatusCode.Should().Be(HttpStatusCode.Created, await hook1.Content.ReadAsStringAsync());
        (await hook1.Content.ReadFromJsonAsync<WebhookSimulationResultDto>(_json))!.Status.Should().Be("Reserved");

        // Đổi secret: secret cũ 401, secret mới 201
        var rotateRes = await SendAsync(HttpMethod.Post, $"/api/shop-integrations/{created.Integration.Id}/rotate-secret", ownerToken);
        rotateRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = (await rotateRes.Content.ReadFromJsonAsync<ShopIntegrationSecretResponse>(_json))!;
        rotated.WebhookSecret.Should().NotBe(created.WebhookSecret);
        (await PostWebhookAsync(created.WebhookSecret, shopId, skuCode)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PostWebhookAsync(rotated.WebhookSecret, shopId, skuCode)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await GetWhoAmIAsync(created.WebhookSecret)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await GetWhoAmIAsync(rotated.WebhookSecret)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Tắt kết nối: webhook 401
        var updateRes = await SendAsync(HttpMethod.Put, $"/api/shop-integrations/{created.Integration.Id}", ownerToken,
            new UpdateShopIntegrationRequest { BranchId = branchId, IsActive = false });
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostWebhookAsync(rotated.WebhookSecret, shopId, skuCode)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await GetWhoAmIAsync(rotated.WebhookSecret)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpResponseMessage> GetWhoAmIAsync(string secret)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/webhooks/simulator/whoami");
        req.Headers.Add("X-Webhook-Secret", secret);
        return await _client.SendAsync(req);
    }

    [Fact]
    public async Task NonOwnerAndOtherTenant_AreBlocked()
    {
        var (branchId, _) = await GetTenantABranchAndSkuAsync();
        var ownerA = await LoginAsync("owner@shopa.local");
        var createRes = await SendAsync(HttpMethod.Post, "/api/shop-integrations", ownerA,
            new CreateShopIntegrationRequest { Channel = "Lazada", ExternalShopId = "LZ_A_1", BranchId = branchId });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await createRes.Content.ReadFromJsonAsync<ShopIntegrationSecretResponse>(_json))!;

        (await SendAsync(HttpMethod.Get, "/api/shop-integrations", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        foreach (var email in new[] { "staff@shopa.local", "cashier@shopa.local" })
        {
            var token = await LoginAsync(email);
            (await SendAsync(HttpMethod.Get, "/api/shop-integrations", token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var ownerB = await LoginAsync("owner@shopb.local");
        var listB = await SendAsync(HttpMethod.Get, "/api/shop-integrations", ownerB);
        listB.StatusCode.Should().Be(HttpStatusCode.OK);
        (await listB.Content.ReadAsStringAsync()).Should().NotContain("LZ_A_1");
        (await SendAsync(HttpMethod.Get, $"/api/shop-integrations/{created.Integration.Id}", ownerB)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Post, $"/api/shop-integrations/{created.Integration.Id}/rotate-secret", ownerB)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Owner B không gán được chi nhánh của Tenant A
        var crossRes = await SendAsync(HttpMethod.Post, "/api/shop-integrations", ownerB,
            new CreateShopIntegrationRequest { Channel = "Shopee", ExternalShopId = "SP_B_1", BranchId = branchId });
        crossRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await crossRes.Content.ReadAsStringAsync()).Should().Contain("BRANCH_INACTIVE_OR_INVALID");
    }
}
