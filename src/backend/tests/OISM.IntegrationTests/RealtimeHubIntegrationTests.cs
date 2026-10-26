using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.RegisterTenant;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Orders.Services;
using OISM.Application.Features.Realtime;
using OISM.Application.Features.Realtime.Dtos;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Bộ kiểm thử tích hợp SignalR NotificationHub thời gian thực (/hubs/notifications).
/// Kiểm chứng toàn diện các tiêu chí AC-07, AC-08, AC-09, AC-10 (Task S3-BE1-T05-WEBHOOK-REALTIME / v1.0).
/// </summary>
public class RealtimeHubIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public RealtimeHubIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private HubConnection CreateHubConnection(string? accessToken = null)
    {
        var hubUrl = accessToken != null
            ? new Uri(_factory.Server.BaseAddress, $"/hubs/notifications?access_token={accessToken}")
            : new Uri(_factory.Server.BaseAddress, "/hubs/notifications");

        return new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
    }

    private record RegisteredTenantInfo(
        Guid TenantId,
        string OwnerToken,
        string CashierToken,
        Guid BranchId,
        Guid WarehouseId,
        Guid SkuId,
        string SkuCode,
        decimal RetailPrice);

    private async Task<RegisteredTenantInfo> RegisterTestTenantAsync(string prefix)
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

        // 3. Khởi tạo dữ liệu chi nhánh, kho, sản phẩm và user Cashier trong DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantService = scope.ServiceProvider.GetRequiredService<ICurrentTenantService>();

        var tenantId = regData!.TenantId;
        tenantService.SetTenant(tenantId);
        using (db.OpenPrivilegedScope())
        {
            // Tạo Cashier User
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

            // Tạo Chi nhánh & Kho
            var branch = new Branch
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = $"Chi nhánh {tenantCode}",
                Address = "123 Đường Test",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Branches.Add(branch);

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

            // Tạo Danh mục, Thương hiệu, Sản phẩm, SKU
            var category = new Category { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Áo Quần", CreatedAt = DateTimeOffset.UtcNow };
            var brand = new Brand { Id = Guid.NewGuid(), TenantId = tenantId, Name = "LocalBrand", CreatedAt = DateTimeOffset.UtcNow };
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
                RetailPrice = 100_000m,
                WholesalePrice = 70_000m,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.ProductVariants.Add(sku);

            // Khởi tạo số dư tồn kho
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

            await db.SaveChangesAsync();

            // Đăng nhập Cashier
            var cashierLoginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
            {
                Email = cashierEmail,
                Password = password
            });
            cashierLoginRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var cashierAuth = await cashierLoginRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);

            return new RegisteredTenantInfo(
                TenantId: tenantId,
                OwnerToken: ownerAuth!.AccessToken,
                CashierToken: cashierAuth!.AccessToken,
                BranchId: branch.Id,
                WarehouseId: warehouse.Id,
                SkuId: sku.Id,
                SkuCode: sku.SKU,
                RetailPrice: sku.RetailPrice);
        }
    }

    #region AC-07: Hub JWT & Method Authorization

    [Fact]
    public async Task AC07_NotificationHub_RealConnectionAndJwtAuthorization()
    {
        var tenantA = await RegisterTestTenantAsync("HA");
        var tenantB = await RegisterTestTenantAsync("HB");

        // 1. Kết nối với Token Owner qua Query string -> Thành công
        await using var hubOwner = CreateHubConnection(tenantA.OwnerToken);
        var startOwner = async () => await hubOwner.StartAsync();
        await startOwner.Should().NotThrowAsync("Owner được phép kết nối SignalR Hub.");
        hubOwner.State.Should().Be(HubConnectionState.Connected);

        // 2. Kết nối với Token Cashier qua Query string -> Thành công (Cashier được phép nhận thông báo)
        await using var hubCashier = CreateHubConnection(tenantA.CashierToken);
        var startCashier = async () => await hubCashier.StartAsync();
        await startCashier.Should().NotThrowAsync("Cashier được phép kết nối SignalR Hub.");
        hubCashier.State.Should().Be(HubConnectionState.Connected);

        // 3. Kết nối không có Token -> Thất bại 401 Unauthorized
        await using var hubNoAuth = CreateHubConnection(null);
        var startNoAuth = async () => await hubNoAuth.StartAsync();
        await startNoAuth.Should().ThrowAsync<Exception>("Thiếu token phải bị từ chối kết nối Hub.");

        // 4. Token qua Query string tuyệt đối KHÔNG được chấp nhận trên REST endpoint
        var restResWithQueryToken = await _client.GetAsync($"/api/auth/me?access_token={tenantA.OwnerToken}");
        restResWithQueryToken.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "REST endpoint không được chấp nhận token qua query string.");

        // 5. Tham gia nhóm chi nhánh hợp lệ của Tenant mình -> Thành công
        var joinValidBranch = async () => await hubOwner.InvokeAsync("JoinBranchGroup", tenantA.BranchId);
        await joinValidBranch.Should().NotThrowAsync();

        // 6. Cố tình tham gia chi nhánh của Tenant khác (Cross-tenant branch) -> Bị từ chối bằng HubException
        var joinCrossTenantBranch = async () => await hubOwner.InvokeAsync("JoinBranchGroup", tenantB.BranchId);
        await joinCrossTenantBranch.Should().ThrowAsync<HubException>()
            .WithMessage("*không tồn tại, đã ngừng hoạt động hoặc không thuộc Tenant của bạn*");

        // 7. Cố tình tham gia chi nhánh không tồn tại -> Bị từ chối bằng HubException
        var joinNonExistentBranch = async () => await hubOwner.InvokeAsync("JoinBranchGroup", Guid.NewGuid());
        await joinNonExistentBranch.Should().ThrowAsync<HubException>();
    }

    #endregion

    #region AC-08: Group Isolation & Cost Privacy

    [Fact]
    public async Task AC08_GroupIsolationAndCostPrivacy()
    {
        var tenantA = await RegisterTestTenantAsync("GA");
        var tenantB = await RegisterTestTenantAsync("GB");

        await using var hubA = CreateHubConnection(tenantA.OwnerToken);
        await using var hubB = CreateHubConnection(tenantB.OwnerToken);

        await hubA.StartAsync();
        await hubB.StartAsync();

        await hubA.InvokeAsync("JoinBranchGroup", tenantA.BranchId);
        await hubB.InvokeAsync("JoinBranchGroup", tenantB.BranchId);

        var receivedOrdersA = new List<OrderSummaryRealtimeDto>();
        var receivedOrdersB = new List<OrderSummaryRealtimeDto>();
        var receivedStocksA = new List<StockChangedRealtimeDto>();
        var receivedStocksB = new List<StockChangedRealtimeDto>();

        hubA.On<OrderSummaryRealtimeDto>("OrderCreated", order => receivedOrdersA.Add(order));
        hubB.On<OrderSummaryRealtimeDto>("OrderCreated", order => receivedOrdersB.Add(order));
        hubA.On<StockChangedRealtimeDto>("StockLevelChanged", stock => receivedStocksA.Add(stock));
        hubB.On<StockChangedRealtimeDto>("StockLevelChanged", stock => receivedStocksB.Add(stock));

        // Lấy IRealtimeNotifier phát sự kiện cho Tenant A
        using (var scope = _factory.Services.CreateScope())
        {
            var notifier = scope.ServiceProvider.GetRequiredService<IRealtimeNotifier>();

            var orderDto = new OrderSummaryRealtimeDto
            {
                OrderId = Guid.NewGuid(),
                OrderNumber = "ORD-TEST-ISO-01",
                BranchId = tenantA.BranchId,
                BranchName = "Chi nhánh GA",
                Channel = "Admin",
                Status = "Reserved",
                TotalAmount = 200_000m,
                TotalItems = 2,
                CustomerName = "Khách Hàng A",
                CreatedAt = DateTimeOffset.UtcNow
            };

            await notifier.NotifyNewOrderAsync(tenantA.TenantId, orderDto);
            await notifier.NotifyStockChangedAsync(tenantA.TenantId, tenantA.BranchId, tenantA.SkuId, 85m);
        }

        // Chờ nhận sự kiện
        await Task.Delay(1000);

        // 1. Tenant Isolation: Client A nhận đúng sự kiện của Tenant A
        receivedOrdersA.Should().ContainSingle(o => o.OrderNumber == "ORD-TEST-ISO-01");
        receivedStocksA.Should().ContainSingle(s => s.SkuId == tenantA.SkuId && s.NewAvailableStock == 85m);

        // 2. Client B tuyệt đối KHÔNG nhận được sự kiện của Tenant A
        receivedOrdersB.Should().BeEmpty("Client B thuộc Tenant khác không được nhận OrderCreated của Tenant A.");
        receivedStocksB.Should().BeEmpty("Client B thuộc Tenant khác không được nhận StockLevelChanged của Tenant A.");

        // 3. Privacy: Kiểm tra payload không rò rỉ bất kỳ thông tin giá vốn hay thực thể bí mật
        var serializedOrder = JsonSerializer.Serialize(receivedOrdersA[0]);
        serializedOrder.Should().NotContain("CostPrice");
        serializedOrder.Should().NotContain("AverageCost");
        serializedOrder.Should().NotContain("UnitCost");
        serializedOrder.Should().NotContain("TotalCost");
        serializedOrder.Should().NotContain("TotalCogs");
        serializedOrder.Should().NotContain("SecretHash");
    }

    #endregion

    #region AC-09: Event Coverage & No-Op Verification

    [Fact]
    public async Task AC09_EventCoverage_OrderCreated_StockLevelChanged_LowStockWarning()
    {
        var tenant = await RegisterTestTenantAsync("EV");

        await using var hub = CreateHubConnection(tenant.OwnerToken);
        await hub.StartAsync();
        await hub.InvokeAsync("JoinBranchGroup", tenant.BranchId);

        var ordersReceived = new List<OrderSummaryRealtimeDto>();
        var stockChangesReceived = new List<StockChangedRealtimeDto>();
        var warningsReceived = new List<LowStockAlertRealtimeDto>();

        hub.On<OrderSummaryRealtimeDto>("OrderCreated", o => ordersReceived.Add(o));
        hub.On<StockChangedRealtimeDto>("StockLevelChanged", s => stockChangesReceived.Add(s));
        hub.On<LowStockAlertRealtimeDto>("LowStockWarning", w => warningsReceived.Add(w));

        // Tạo đơn hàng mới qua API Admin POST /api/orders
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tenant.OwnerToken);

        var requestKey = Guid.NewGuid();
        var createOrderDto = new CreateOrderRequestDto
        {
            BranchId = tenant.BranchId,
            Channel = "Admin",
            RequestKey = requestKey,
            CustomerName = "Khách Hàng Event Test",
            Items = new List<OrderItemRequestDto>
            {
                // Mua 92 sản phẩm (OnHand ban đầu 100, mua 92 -> Available còn 8 <= ReorderPoint 10 -> Kích hoạt LowStockWarning)
                new() { SkuId = tenant.SkuId, SkuCode = tenant.SkuCode, Quantity = 92m, UnitPrice = tenant.RetailPrice }
            }
        };

        var res = await _client.PostAsJsonAsync("/api/orders", createOrderDto);
        res.StatusCode.Should().Be(HttpStatusCode.Created);

        await Task.Delay(1500);

        // 1. Phải nhận sự kiện OrderCreated cho đơn hàng Reserved mới
        ordersReceived.Should().ContainSingle(o => o.TotalAmount == 92 * tenant.RetailPrice);
        ordersReceived[0].Channel.Should().Be("Admin");
        ordersReceived[0].Status.Should().Be("Reserved");

        // 2. Phải nhận sự kiện StockLevelChanged (Tồn khả dụng mới = 100 - 92 = 8)
        stockChangesReceived.Should().Contain(s => s.SkuId == tenant.SkuId && s.NewAvailableStock == 8m);

        // 3. Phải nhận cảnh báo LowStockWarning vì tồn khả dụng giảm từ 100 xuống 8 (<= ReorderPoint 10)
        warningsReceived.Should().ContainSingle(w => w.SkuId == tenant.SkuId && w.CurrentAvailable == 8m && w.Threshold == 10m);

        // 4. Idempotent Replay cùng RequestKey -> Tuyệt đối không phát lại sự kiện OrderCreated hay StockLevelChanged
        var preOrderCount = ordersReceived.Count;
        var preStockCount = stockChangesReceived.Count;
        var preWarnCount = warningsReceived.Count;

        var replayRes = await _client.PostAsJsonAsync("/api/orders", createOrderDto);
        replayRes.StatusCode.Should().Be(HttpStatusCode.OK);

        await Task.Delay(1000);

        ordersReceived.Count.Should().Be(preOrderCount, "Replay không bao giờ phát lại sự kiện OrderCreated.");
        stockChangesReceived.Count.Should().Be(preStockCount, "Replay không bao giờ phát lại sự kiện StockLevelChanged.");
        warningsReceived.Count.Should().Be(preWarnCount, "Replay không bao giờ phát lại cảnh báo LowStockWarning.");
    }

    #endregion

    #region AC-10: Commit-Before-Send & Notifier Failure Tolerance

    public class ThrowingNotifier : IRealtimeNotifier
    {
        public Task NotifyNewOrderAsync(Guid tenantId, OrderSummaryRealtimeDto order, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Mô phỏng lỗi SignalR connection drop / network timeout khi phát sự kiện.");
        }

        public Task NotifyStockChangedAsync(Guid tenantId, Guid branchId, Guid skuId, decimal newAvailableStock, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Mô phỏng lỗi SignalR khi phát biến động tồn.");
        }

        public Task NotifyLowStockAlertAsync(Guid tenantId, Guid branchId, Guid skuId, string skuCode, decimal currentAvailable, decimal threshold, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Mô phỏng lỗi SignalR khi phát cảnh báo tồn thấp.");
        }
    }

    [Fact]
    public async Task AC10_CommitBeforeSendAndNotifierFailureTolerance()
    {
        var tenant = await RegisterTestTenantAsync("FL");

        // Kiểm chứng tính chịu lỗi (Fault Tolerance):
        // Khi IRealtimeNotifier ném Exception sau khi Transaction đã commit,
        // Dịch vụ OrderService / WebhookService phải nuốt Exception đó, ghi log,
        // và HTTP response vẫn thành công trả về kết quả, Database vẫn được lưu toàn vẹn.

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantService = scope.ServiceProvider.GetRequiredService<ICurrentTenantService>();
        tenantService.SetTenant(tenant.TenantId);

        var throwingNotifier = new ThrowingNotifier();
        var orderService = new OrderService(
            db,
            tenantService,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OrderService>.Instance,
            throwingNotifier);

        var requestKey = Guid.NewGuid();
        var orderReq = new CreateOrderRequestDto
        {
            BranchId = tenant.BranchId,
            Channel = "Admin",
            RequestKey = requestKey,
            CustomerName = "Khách Hàng Test Notifier Thất Bại",
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = tenant.SkuId, SkuCode = tenant.SkuCode, Quantity = 2m, UnitPrice = tenant.RetailPrice }
            }
        };

        // Hàm gọi nghiệp vụ không được ném Exception ra ngoài
        Func<Task<OrderResponseDto>> act = async () => await orderService.CreateOrderAsync(orderReq, null);
        var result = await act.Should().NotThrowAsync("Lỗi phát Realtime sau commit không được làm hỏng giao dịch đã hoàn tất.");

        result.Subject.Should().NotBeNull();
        result.Subject.Status.Should().Be("Reserved");

        // Đối soát Database: Đơn hàng và giữ chỗ đã được lưu thành công
        var orderInDb = await db.Orders.FirstOrDefaultAsync(o => o.TenantId == tenant.TenantId && o.RequestKey == requestKey);
        orderInDb.Should().NotBeNull();
        orderInDb!.Status.Should().Be("Reserved");

        var bBal = await db.InventoryBalances.FirstAsync(b => b.TenantId == tenant.TenantId && b.BranchId == tenant.BranchId && b.SkuId == tenant.SkuId);
        bBal.Reserved.Should().Be(2m, "Giữ chỗ tồn kho phải được commit thành công vào DB dù Notifier lỗi.");
    }

    #endregion
}
