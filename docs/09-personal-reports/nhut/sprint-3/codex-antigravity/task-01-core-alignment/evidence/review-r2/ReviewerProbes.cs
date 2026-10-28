using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Inventory.Services;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Orders.Services;
using OISM.Application.Features.Simulator.Dtos;
using OISM.Application.Features.Simulator.Services;
using OISM.Application.Features.Webhooks.Dtos;
using OISM.Application.Features.Webhooks.Services;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using OISM.IntegrationTests;
using Xunit;
using Xunit.Abstractions;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryContentRoot(
    "OISM.WebApi, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
    "D:/OISMS/src/backend/src/OISM.WebApi", "OISM.WebApi.csproj", "0")]

public sealed class ReviewRunCleanup : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => PostgresTestEnvironment.CleanupCurrentRunAsync();
}

public sealed class ReviewerProbes : IAsyncLifetime, IClassFixture<ReviewRunCleanup>
{
    private readonly ITestOutputHelper _output;
    private WebApplicationFactoryWithPostgres _factory = null!;
    private HttpClient _client = null!;
    private Guid _branchId, _skuId;
    private string _skuCode = "";
    private decimal _retailPrice;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private sealed class Tenant : ICurrentTenantService
    {
        public Guid? TenantId { get; set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    public ReviewerProbes(ITestOutputHelper output) => _output = output;

    private static AppDbContext Context() => PostgresTestEnvironment.CreateRuntimeDbContext(
        new Tenant { TenantId = OismsDatabaseSeeder.TenantAId });

    public async Task InitializeAsync()
    {
        Assert.StartsWith("oisms_test_", PostgresTestEnvironment.DatabaseName);
        await PostgresTestEnvironment.EnsureInitializedAsync();
        await PostgresTestEnvironment.ResetTestDataAsync();
        await using (var seed = PostgresTestEnvironment.CreateRuntimeDbContext(new Tenant()))
        {
            await OismsDatabaseSeeder.SeedAsync(seed, PostgresTestEnvironment.AppPassword);
        }
        await using (var db = Context())
        {
            var branch = await db.Branches.FirstAsync();
            _branchId = branch.Id;
            var warehouse = await db.Warehouses.FirstAsync(x => x.BranchId == _branchId);
            var supplier = await db.Suppliers.FirstAsync();
            var user = await db.Users.FirstAsync(x => x.Email == "owner@shopa.local");
            var sku = await db.ProductVariants.FirstAsync(x => x.IsActive);
            _skuId = sku.Id; _skuCode = sku.SKU; _retailPrice = sku.RetailPrice;
            var tenant = new Tenant { TenantId = OismsDatabaseSeeder.TenantAId };
            var receipts = new GoodsReceiptService(db, tenant, new WacCalculator());
            var receipt = await receipts.CreateReceiptAsync(new CreateGoodsReceiptDto {
                BranchId = _branchId, WarehouseId = warehouse.Id, SupplierId = supplier.Id,
                Items = new() { new() { SkuId = _skuId, Quantity = 10, UnitPrice = 100 } }
            }, user.Id);
            await receipts.ConfirmReceiptAsync(receipt.Id, user.Id);
        }
        _factory = new WebApplicationFactoryWithPostgres();
        _client = _factory.CreateClient();
        var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest {
            Email = "owner@shopa.local", Password = PostgresTestEnvironment.AppPassword
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var auth = await login.Content.ReadFromJsonAsync<AuthResponse>(Json);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory != null) await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ProcessEndpoint_OnRealPostgres_MustNotReturn500OrPartiallyConfirm()
    {
        var create = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto {
            BranchId = _branchId, Channel = "Admin", RequestKey = Guid.NewGuid(),
            Items = new() { new() { SkuId = _skuId, SkuCode = _skuCode, Quantity = 2, UnitPrice = _retailPrice } }
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<OrderResponseDto>(Json);
        var process = await _client.PostAsync($"/api/orders/{created!.Id}/process", null);
        var body = await process.Content.ReadAsStringAsync();
        await using var db = Context();
        var order = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == created.Id);
        var balance = await db.InventoryBalances.AsNoTracking().SingleAsync(x => x.BranchId == _branchId && x.SkuId == _skuId);
        var outRows = await db.InventoryTransactions.CountAsync(x => x.Type == "OUT");
        _output.WriteLine(JsonSerializer.Serialize(new {
            httpStatus = (int)process.StatusCode, persistedStatus = order.Status,
            balance.OnHand, balance.Reserved, outRows,
            schemaConstraintInBody = body.Contains("ck_orders_status_values"),
            bodyStart = body[..Math.Min(800, body.Length)]
        }));
        Assert.Equal(HttpStatusCode.OK, process.StatusCode);
    }

    [Fact]
    public async Task SimulatorAdapter_SameTotalChangedItems_MustRejectPayloadMismatch()
    {
        await using var db = Context();
        var tenant = new Tenant { TenantId = OismsDatabaseSeeder.TenantAId };
        var simulator = new SimulatorService(db, tenant, new OrderService(db, tenant));
        var payload = new WebhookOrderPayloadDto {
            ExternalShopId = "SHOP-SIGNATURE-PROBE", ExternalOrderId = "ORDER-SAME-TOTAL",
            BranchId = _branchId,
            Items = new() { new() { SkuCode = _skuCode, Quantity = 1, UnitPrice = 100 } }
        };
        var configuredShop = await db.ShopIntegrations.AnyAsync(x => x.ExternalShopId == payload.ExternalShopId);
        if (!configuredShop)
        {
            db.ShopIntegrations.Add(new ShopIntegration
            {
                Id = Guid.NewGuid(),
                TenantId = OismsDatabaseSeeder.TenantAId,
                BranchId = _branchId,
                Channel = "Shopee",
                ExternalShopId = payload.ExternalShopId,
                SecretHash = "test_secret_hash",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            configuredShop = true;
        }
        var first = await simulator.IngestWebhookOrderAsync("Shopee", payload, null);
        payload.Items[0].Quantity = 2;
        payload.Items[0].UnitPrice = 50;
        OrderDetailDto? replay = null;
        var error = await Record.ExceptionAsync(async () => replay = await simulator.IngestWebhookOrderAsync("Shopee", payload, null));
        _output.WriteLine(JsonSerializer.Serialize(new {
            provider = "PostgreSQL", configuredShop,
            sameOrderReturned = replay?.Id == first.Id, requestedQuantity = 2,
            returnedQuantity = replay?.Items.Single().Quantity,
            requestedUnitPrice = 50, returnedUnitPrice = replay?.Items.Single().UnitPrice,
            actualErrorType = error?.GetType().Name
        }));
        Assert.NotNull(error);
        var mismatch = Assert.IsType<OismException>(error);
        Assert.Equal("WEBHOOK_PAYLOAD_MISMATCH", mismatch.ErrorCode);
        Assert.Equal(409, mismatch.StatusCode);
    }

    [Fact]
    public async Task Batch_MustNotProvisionIntegrationWithPredictableAnonymousCredential()
    {
        await using var db = Context();
        var before = await db.ShopIntegrations.CountAsync(x => x.Channel == "Shopee" && x.BranchId == _branchId);
        Assert.Equal(0, before);
        var batch = await _client.PostAsJsonAsync("/api/webhooks/simulator/batch", new SimulationBatchRequestDto {
            Channel = "Shopee", BranchId = _branchId, SkuCode = _skuCode,
            ConcurrencyCount = 1, QuantityPerOrder = 1, UnitPrice = 100
        });
        Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
        var batchResult = await batch.Content.ReadFromJsonAsync<SimulationBatchResultDto>(Json);
        var createdIntegration = await db.ShopIntegrations.AsNoTracking().SingleAsync(x => x.Channel == "Shopee" && x.BranchId == _branchId);
        _client.DefaultRequestHeaders.Authorization = null;
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/simulator/Shopee") {
            Content = JsonContent.Create(new CanonicalWebhookPayloadDto {
                Channel = "Shopee", ExternalShopId = createdIntegration.ExternalShopId,
                ExternalOrderId = "ANON-AFTER-BATCH", OrderTimestamp = DateTimeOffset.UtcNow,
                TotalAmount = 100,
                Items = new() { new() { SkuCode = _skuCode, Quantity = 1, UnitPrice = 100 } }
            })
        };
        request.Headers.Add("X-Webhook-Secret", "sim-batch-secret-key");
        var anonymousWebhook = await _client.SendAsync(request);
        var count = await db.Orders.AsNoTracking().CountAsync(x => x.ExternalOrderId == "ANON-AFTER-BATCH");
        _output.WriteLine(JsonSerializer.Serialize(new {
            beforeIntegrations = before, afterIntegrations = before + 1,
            batchHttpStatus = (int)batch.StatusCode, batchResult!.SuccessCount,
            anonymousWebhookStatus = (int)anonymousWebhook.StatusCode,
            anonymousOrderCount = count,
            authorizationHeaderSent = false, credentialWasKnownSourceConstant = true
        }));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousWebhook.StatusCode);
    }

    [Fact]
    public async Task SimulatorAdapter_DefaultTimestamp_MustApplyCanonicalValidation()
    {
        await using var db = Context();
        var tenant = new Tenant { TenantId = OismsDatabaseSeeder.TenantAId };
        db.ShopIntegrations.Add(new ShopIntegration {
            Id = Guid.NewGuid(), TenantId = tenant.TenantId!.Value, BranchId = _branchId,
            Channel = "Shopee", ExternalShopId = "SHOP-TIME-PROBE", IsActive = true,
            SecretHash = WebhookSecretHasher.HashSecret(Guid.NewGuid().ToString("N"))
        });
        await db.SaveChangesAsync();
        var payload = new WebhookOrderPayloadDto {
            ExternalShopId = "SHOP-TIME-PROBE", ExternalOrderId = "DEFAULT-TIMESTAMP-ADAPTER",
            BranchId = _branchId, OrderTimestamp = default(DateTimeOffset),
            Items = new() { new() { SkuCode = _skuCode, Quantity = 1, UnitPrice = 100 } }
        };
        var adapter = new SimulatorService(db, tenant, new OrderService(db, tenant));
        var adapterError = await Record.ExceptionAsync(async () => { await adapter.IngestWebhookOrderAsync("Shopee", payload, null); });
        var canonical = await _client.PostAsJsonAsync("/api/webhooks/simulator/Shopee", new CanonicalWebhookPayloadDto {
            Channel = "Shopee", ExternalShopId = payload.ExternalShopId, ExternalOrderId = "DEFAULT-TIMESTAMP-CANONICAL",
            OrderTimestamp = default(DateTimeOffset), TotalAmount = 100,
            Items = new() { new() { SkuCode = _skuCode, Quantity = 1, UnitPrice = 100 } }
        });
        var persisted = await db.Orders.AsNoTracking().CountAsync(x => x.ExternalOrderId == payload.ExternalOrderId);
        _output.WriteLine(JsonSerializer.Serialize(new {
            adapterErrorType = adapterError?.GetType().Name,
            canonicalHttpStatus = (int)canonical.StatusCode,
            adapterPersistedOrders = persisted
        }));
        Assert.Equal(HttpStatusCode.BadRequest, canonical.StatusCode);
        var validation = Assert.IsType<OismException>(adapterError);
        Assert.Equal("VALIDATION_ERROR", validation.ErrorCode);
    }
}
