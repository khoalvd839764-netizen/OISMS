using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.RegisterTenant;
using OISM.Application.Features.Webhooks.Dtos;
using OISM.Application.Features.Webhooks.Services;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Bộ kiểm thử tích hợp cho Webhook tiếp nhận đơn hàng sàn TMĐT (/api/webhooks/simulator/{channel}).
/// Sử dụng CustomWebApplicationFactory (Hermetic In-Memory) kiểm chứng đầy đủ AC-02, AC-03, AC-04, AC-05, AC-06, AC-11, AC-13.
/// </summary>
public class WebhookSimulatorControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public WebhookSimulatorControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private record TestShopContext(
        Guid TenantId,
        string OwnerToken,
        string CashierToken,
        Guid BranchId,
        Guid WarehouseId,
        Guid SkuId,
        string SkuCode,
        decimal RetailPrice,
        Guid InactiveBranchId,
        string SecretKey,
        string ExternalShopId);

    private async Task<TestShopContext> SetupShopAsync(string prefix = "WHK")
    {
        var randomSuffix = Random.Shared.Next(10000000, 99999999).ToString();
        var tenantCode = $"{prefix}{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var ownerEmail = $"owner_{tenantCode.ToLowerInvariant()}@test.local";
        var cashierEmail = $"cashier_{tenantCode.ToLowerInvariant()}@test.local";
        var password = "Password@123";

        // 1. Đăng ký Tenant mới
        var regRes = await _client.PostAsJsonAsync("/api/auth/register-tenant", new RegisterTenantRequest
        {
            TenantName = $"Shop {tenantCode}",
            TenantCode = tenantCode,
            FullName = $"Owner {tenantCode}",
            Email = ownerEmail,
            Password = password,
            PhoneNumber = $"09{randomSuffix}"
        });
        regRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var regData = await regRes.Content.ReadFromJsonAsync<RegisterTenantResponse>(_jsonOptions);

        // 2. Đăng nhập Owner
        var ownerLoginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = ownerEmail,
            Password = password
        });
        ownerLoginRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var ownerAuth = await ownerLoginRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);

        // 3. Khởi tạo dữ liệu cơ sở
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantService = scope.ServiceProvider.GetRequiredService<ICurrentTenantService>();

        var tenantId = regData!.TenantId;
        tenantService.SetTenant(tenantId);

        using (db.OpenPrivilegedScope())
        {
            var cashierUser = new User
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Email = cashierEmail,
                DisplayName = $"Cashier {tenantCode}",
                Role = "Cashier",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Users.Add(cashierUser);

            var branch = new Branch
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = $"Chi nhánh Hoạt động {tenantCode}",
                Address = "123 Đường Test",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Branches.Add(branch);

            var inactiveBranch = new Branch
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = $"Chi nhánh Đóng cửa {tenantCode}",
                Address = "456 Đường Test",
                IsActive = false,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Branches.Add(inactiveBranch);

            var warehouse = new Warehouse
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BranchId = branch.Id,
                Name = $"Kho {tenantCode}",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Warehouses.Add(warehouse);

            var category = new Category { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Thời trang", CreatedAt = DateTimeOffset.UtcNow };
            var brand = new Brand { Id = Guid.NewGuid(), TenantId = tenantId, Name = "BrandTest", CreatedAt = DateTimeOffset.UtcNow };
            db.Categories.Add(category);
            db.Brands.Add(brand);

            var product = new Product
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CategoryId = category.Id,
                BrandId = brand.Id,
                Name = $"Sản phẩm {tenantCode}",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Products.Add(product);

            var sku = new ProductVariant
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProductId = product.Id,
                SKU = $"SKU-{tenantCode}",
                RetailPrice = 120_000m,
                WholesalePrice = 80_000m,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.ProductVariants.Add(sku);

            var bBal = new InventoryBalance
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BranchId = branch.Id,
                SkuId = sku.Id,
                OnHand = 100m,
                Reserved = 0m,
                AverageCost = 50_000m,
                ReorderPoint = 10m,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.InventoryBalances.Add(bBal);

            var wBal = new WarehouseInventoryBalance
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                BranchId = branch.Id,
                WarehouseId = warehouse.Id,
                SkuId = sku.Id,
                OnHand = 100m,
                Reserved = 0m,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.WarehouseInventoryBalances.Add(wBal);

            // Cấu hình tích hợp gian hàng Shopee
            var secretKey = $"shopee_secret_{tenantCode}_{Guid.NewGuid():N}";
            var externalShopId = $"SHOP_{tenantCode}";

            var integration = new ShopIntegration
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Channel = "Shopee",
                ExternalShopId = externalShopId,
                BranchId = branch.Id,
                IsActive = true,
                SecretHash = WebhookSecretHasher.HashSecret(secretKey),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.ShopIntegrations.Add(integration);

            await db.SaveChangesAsync();

            // Đăng nhập Cashier
            var cashierLoginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
            {
                Email = cashierEmail,
                Password = password
            });
            cashierLoginRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var cashierAuth = await cashierLoginRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);

            return new TestShopContext(
                TenantId: tenantId,
                OwnerToken: ownerAuth!.AccessToken,
                CashierToken: cashierAuth!.AccessToken,
                BranchId: branch.Id,
                WarehouseId: warehouse.Id,
                SkuId: sku.Id,
                SkuCode: sku.SKU,
                RetailPrice: sku.RetailPrice,
                InactiveBranchId: inactiveBranch.Id,
                SecretKey: secretKey,
                ExternalShopId: externalShopId);
        }
    }

    #region AC-02 & AC-03: Path Validation, Authentication & Shop Mapping

    [Fact]
    public async Task Webhook_ChannelMismatch_Returns400ChannelMismatch()
    {
        var ctx = await SetupShopAsync("CMIS");

        var payload = new CanonicalWebhookPayloadDto
        {
            Channel = "TikTok", // Body là TikTok nhưng URL là shopee
            ExternalShopId = ctx.ExternalShopId,
            ExternalOrderId = "ORD_001",
            OrderTimestamp = DateTimeOffset.UtcNow,
            TotalAmount = 120_000m,
            Items = new List<CanonicalWebhookItemDto>
            {
                new() { SkuCode = ctx.SkuCode, Quantity = 1m, UnitPrice = 120_000m }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/simulator/shopee")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.OwnerToken);

        var res = await _client.SendAsync(request);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("CHANNEL_MISMATCH");
    }

    [Fact]
    public async Task Webhook_WrongSecret_Returns401Unauthorized()
    {
        var ctx = await SetupShopAsync("WSEC");

        var payload = new CanonicalWebhookPayloadDto
        {
            Channel = "Shopee",
            ExternalShopId = ctx.ExternalShopId,
            ExternalOrderId = "ORD_002",
            OrderTimestamp = DateTimeOffset.UtcNow,
            TotalAmount = 120_000m,
            Items = new List<CanonicalWebhookItemDto>
            {
                new() { SkuCode = ctx.SkuCode, Quantity = 1m, UnitPrice = 120_000m }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/simulator/shopee")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("X-Webhook-Secret", "wrong_secret_key");

        var res = await _client.SendAsync(request);
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Webhook_CashierBearerToken_Returns403Forbidden()
    {
        var ctx = await SetupShopAsync("CSH");

        var payload = new CanonicalWebhookPayloadDto
        {
            Channel = "Shopee",
            ExternalShopId = ctx.ExternalShopId,
            ExternalOrderId = "ORD_003",
            OrderTimestamp = DateTimeOffset.UtcNow,
            TotalAmount = 120_000m,
            Items = new List<CanonicalWebhookItemDto>
            {
                new() { SkuCode = ctx.SkuCode, Quantity = 1m, UnitPrice = 120_000m }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/simulator/shopee")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.CashierToken);

        var res = await _client.SendAsync(request);
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Vai trò Cashier tuyệt đối không có quyền gọi Webhook simulator.");
    }

    [Fact]
    public async Task Webhook_ShopNotIntegrated_Returns404NotFound()
    {
        var ctx = await SetupShopAsync("UNREG");

        var payload = new CanonicalWebhookPayloadDto
        {
            Channel = "Shopee",
            ExternalShopId = "UNKNOWN_SHOP_9999",
            ExternalOrderId = "ORD_004",
            OrderTimestamp = DateTimeOffset.UtcNow,
            TotalAmount = 120_000m,
            Items = new List<CanonicalWebhookItemDto>
            {
                new() { SkuCode = ctx.SkuCode, Quantity = 1m, UnitPrice = 120_000m }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/simulator/shopee")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.OwnerToken);

        var res = await _client.SendAsync(request);
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("INTEGRATION_NOT_FOUND");
    }

    #endregion

    #region AC-04 & AC-06: Order Creation, Reservation & Market Pricing

    [Fact]
    public async Task Webhook_ValidOrder_CreatesReservedOrderAndReservation_KeepsOnHandUnchanged()
    {
        var ctx = await SetupShopAsync("SUCC");

        // Đơn giá sàn TMĐT khác Catalog niêm yết (vd sàn áp mã giảm giá còn 110,000đ thay vì 120,000đ)
        var marketplacePrice = 110_000m;
        var quantity = 5m;
        var expectedTotal = marketplacePrice * quantity;

        var payload = new CanonicalWebhookPayloadDto
        {
            Channel = "Shopee",
            ExternalShopId = ctx.ExternalShopId,
            ExternalOrderId = "ORD_SHOPEE_SUCCESS_1",
            CustomerName = "Người mua Shopee",
            CustomerPhone = "0987654321",
            ShippingAddress = "Tầng 5, Tòa nhà Bitexco, Q1, TP.HCM",
            OrderTimestamp = DateTimeOffset.UtcNow,
            TotalAmount = expectedTotal,
            Items = new List<CanonicalWebhookItemDto>
            {
                new() { SkuCode = ctx.SkuCode, Quantity = quantity, UnitPrice = marketplacePrice }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/simulator/shopee")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.OwnerToken);

        var res = await _client.SendAsync(request);
        var body = await res.Content.ReadAsStringAsync();
        res.StatusCode.Should().Be(HttpStatusCode.Created, $"Body: {body}");

        var simResult = JsonSerializer.Deserialize<WebhookSimulationResultDto>(body, _jsonOptions);
        simResult.Should().NotBeNull();
        simResult!.Success.Should().BeTrue();
        simResult.OrderId.Should().NotBeNull();

        // Đối soát Database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantService = scope.ServiceProvider.GetRequiredService<ICurrentTenantService>();
        tenantService.SetTenant(ctx.TenantId);

        using (db.OpenPrivilegedScope())
        {
            var order = await db.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == simResult.OrderId!.Value);

            order.Should().NotBeNull();
            order!.Status.Should().Be("Reserved");
            order.Channel.Should().Be("Shopee");
            order.ExternalShopId.Should().Be(ctx.ExternalShopId);
            order.ExternalOrderId.Should().Be("ORD_SHOPEE_SUCCESS_1");
            order.TotalAmount.Should().Be(expectedTotal);
            order.WebhookPayloadSignature.Should().NotBeNullOrEmpty();
            order.WebhookPayloadSignature!.Length.Should().Be(64);

            // Kiểm tra Reserved tăng, OnHand KHÔNG đổi
            var bBal = await db.InventoryBalances.FirstAsync(b => b.TenantId == ctx.TenantId && b.BranchId == ctx.BranchId && b.SkuId == ctx.SkuId);
            bBal.OnHand.Should().Be(100m, "Tồn OnHand tuyệt đối không bị trừ ở giai đoạn tiếp nhận Reserved");
            bBal.Reserved.Should().Be(quantity, "Số lượng giữ chỗ Reserved phải tăng đúng 5");

            // Kiểm tra Reservation
            var reservation = await db.Reservations.FirstOrDefaultAsync(r => r.TenantId == ctx.TenantId && r.OrderId == order.Id);
            reservation.Should().NotBeNull();
            reservation!.Status.Should().Be("Active");
            reservation.Quantity.Should().Be(quantity);
            reservation.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(14));
        }
    }

    [Fact]
    public async Task Webhook_TotalAmountMismatch_Returns400TotalAmountMismatch()
    {
        var ctx = await SetupShopAsync("TOTMIS");

        var payload = new CanonicalWebhookPayloadDto
        {
            Channel = "Shopee",
            ExternalShopId = ctx.ExternalShopId,
            ExternalOrderId = "ORD_MISMATCH_TOTAL",
            OrderTimestamp = DateTimeOffset.UtcNow,
            TotalAmount = 500_000m, // Sai lệch: 2 * 100_000 = 200_000 != 500_000
            Items = new List<CanonicalWebhookItemDto>
            {
                new() { SkuCode = ctx.SkuCode, Quantity = 2m, UnitPrice = 100_000m }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/simulator/shopee")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.OwnerToken);

        var res = await _client.SendAsync(request);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("TOTAL_AMOUNT_MISMATCH");
    }

    [Fact]
    public async Task Webhook_InsufficientStock_Returns409InsufficientStock()
    {
        var ctx = await SetupShopAsync("OOS");

        var payload = new CanonicalWebhookPayloadDto
        {
            Channel = "Shopee",
            ExternalShopId = ctx.ExternalShopId,
            ExternalOrderId = "ORD_INSUFFICIENT",
            OrderTimestamp = DateTimeOffset.UtcNow,
            TotalAmount = 200 * 100_000m,
            Items = new List<CanonicalWebhookItemDto>
            {
                new() { SkuCode = ctx.SkuCode, Quantity = 200m, UnitPrice = 100_000m } // Tồn OnHand ban đầu chỉ có 100
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/simulator/shopee")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.OwnerToken);

        var res = await _client.SendAsync(request);
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("INSUFFICIENT_STOCK");
    }

    #endregion

    #region AC-05: Deduplication, Canonical Signature & Replay

    [Fact]
    public async Task Webhook_IdempotentReplay_SamePayloadReturns200_DifferentPayloadReturns409Conflict()
    {
        var ctx = await SetupShopAsync("IDEM");

        var externalOrderId = "ORD_IDEMPOTENT_TEST_99";
        var timestamp = new DateTimeOffset(2026, 10, 25, 15, 0, 0, TimeSpan.Zero);

        var initialPayload = new CanonicalWebhookPayloadDto
        {
            Channel = "Shopee",
            ExternalShopId = ctx.ExternalShopId,
            ExternalOrderId = externalOrderId,
            CustomerName = "Khách Hàng Idempotent",
            OrderTimestamp = timestamp,
            TotalAmount = 240_000m,
            Items = new List<CanonicalWebhookItemDto>
            {
                new() { SkuCode = ctx.SkuCode, Quantity = 2m, UnitPrice = 120_000m }
            }
        };

        // 1. Gửi lần 1 -> Thành công 201 Created
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/simulator/shopee")
        {
            Content = JsonContent.Create(initialPayload)
        };
        req1.Headers.Add("X-Webhook-Secret", ctx.SecretKey);

        var res1 = await _client.SendAsync(req1);
        res1.StatusCode.Should().Be(HttpStatusCode.Created);
        var body1 = await res1.Content.ReadAsStringAsync();
        var sim1 = JsonSerializer.Deserialize<WebhookSimulationResultDto>(body1, _jsonOptions);

        // 2. Gửi lần 2: Cùng Deduplication Key và CÙNG Payload -> Trả HTTP 200 OK (Idempotent Replay)
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/simulator/shopee")
        {
            Content = JsonContent.Create(initialPayload)
        };
        req2.Headers.Add("X-Webhook-Secret", ctx.SecretKey);

        var res2 = await _client.SendAsync(req2);
        res2.StatusCode.Should().Be(HttpStatusCode.OK, "Replay cùng khóa và cùng nội dung phải trả 200 OK.");

        var body2 = await res2.Content.ReadAsStringAsync();
        var sim2 = JsonSerializer.Deserialize<WebhookSimulationResultDto>(body2, _jsonOptions);
        sim2!.OrderId.Should().Be(sim1!.OrderId, "Mã đơn hàng trả về phải là đơn hàng cũ.");

        // 3. Gửi lần 3: Cùng Deduplication Key nhưng KHÁC Payload (đổi số lượng và tổng tiền) -> Trả HTTP 409 Conflict
        var modifiedPayload = new CanonicalWebhookPayloadDto
        {
            Channel = "Shopee",
            ExternalShopId = ctx.ExternalShopId,
            ExternalOrderId = externalOrderId,
            CustomerName = "Khách Hàng Idempotent",
            OrderTimestamp = timestamp,
            TotalAmount = 360_000m,
            Items = new List<CanonicalWebhookItemDto>
            {
                new() { SkuCode = ctx.SkuCode, Quantity = 3m, UnitPrice = 120_000m }
            }
        };

        var req3 = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/simulator/shopee")
        {
            Content = JsonContent.Create(modifiedPayload)
        };
        req3.Headers.Add("X-Webhook-Secret", ctx.SecretKey);

        var res3 = await _client.SendAsync(req3);
        res3.StatusCode.Should().Be(HttpStatusCode.Conflict, "Cùng external key nhưng khác nội dung payload phải trả 409 Conflict.");

        var body3 = await res3.Content.ReadAsStringAsync();
        body3.Should().Contain("WEBHOOK_PAYLOAD_MISMATCH");
    }

    #endregion
}
