using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.RegisterTenant;
using OISM.Application.Features.Branches;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Warehouses;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

public class InventoryAndWarehouseRbacIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public InventoryAndWarehouseRbacIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record RbacTestSetup(
        Guid TenantId,
        Guid BranchId,
        Guid WarehouseId,
        Guid Sku1Id,
        Guid Sku2Id,
        string OwnerToken,
        string StaffToken,
        string CashierToken);

    private async Task<RbacTestSetup> SetupTenantAndSeedBalancesAsync(string suffix)
    {
        var client = _factory.CreateClient();
        var ownerEmail = $"owner_{suffix}@shop.com";
        var staffEmail = $"staff_{suffix}@shop.com";
        var cashierEmail = $"cashier_{suffix}@shop.com";

        var regRes = await client.PostAsJsonAsync("/api/auth/register-tenant", new RegisterTenantRequest
        {
            TenantName = $"Shop RBAC {suffix}",
            TenantCode = $"RBAC_{suffix}",
            FullName = "Chủ Shop",
            Email = ownerEmail,
            Password = "Password@123"
        });
        regRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var regData = await regRes.Content.ReadFromJsonAsync<RegisterTenantResponse>(_jsonOptions);
        var tenantId = regData!.TenantId;

        var ownerToken = await LoginAsync(client, ownerEmail, "Password@123");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // Tạo chi nhánh & kho
        var branchRes = await client.PostAsJsonAsync("/api/branches", new CreateBranchRequest
        {
            Name = $"Chi nhánh {suffix}",
            Address = "123 Nguyễn Huệ"
        });
        branchRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var branch = await branchRes.Content.ReadFromJsonAsync<BranchResponse>(_jsonOptions);

        var whRes = await client.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest
        {
            BranchId = branch!.Id,
            Name = $"Kho Chính {suffix}"
        });
        whRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var warehouse = await whRes.Content.ReadFromJsonAsync<WarehouseResponse>(_jsonOptions);

        // Tạo sản phẩm có 2 SKU qua API
        var prodRes = await client.PostAsJsonAsync("/api/products", new
        {
            Name = $"Áo Khoác {suffix}",
            Variants = new[]
            {
                new { Sku = $"SKU1-{suffix}", VariantName = "Đen", RetailPrice = 300000m, WholesalePrice = 220000m },
                new { Sku = $"SKU2-{suffix}", VariantName = "Trắng", RetailPrice = 280000m, WholesalePrice = 200000m }
            }
        });
        prodRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var prodJson = await prodRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var productId = prodJson.GetProperty("productId").GetGuid();

        var prodDetailRes = await client.GetAsync($"/api/products/{productId}");
        prodDetailRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var prodDetail = await prodDetailRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var sku1Id = prodDetail.GetProperty("variants")[0].GetProperty("id").GetGuid();
        var sku2Id = prodDetail.GetProperty("variants")[1].GetProperty("id").GetGuid();

        // Seed user Staff, Cashier và số dư tồn kho có WAC & ReorderPoint
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using (db.OpenPrivilegedScope())
            {
                db.Users.AddRange(
                    new User
                    {
                        TenantId = tenantId,
                        Email = staffEmail,
                        DisplayName = "Nhân viên Kho",
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password@123"),
                        Role = "Staff",
                        IsActive = true
                    },
                    new User
                    {
                        TenantId = tenantId,
                        Email = cashierEmail,
                        DisplayName = "Thu ngân POS",
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password@123"),
                        Role = "Cashier",
                        IsActive = true
                    });

                db.InventoryBalances.AddRange(
                    new InventoryBalance
                    {
                        TenantId = tenantId,
                        BranchId = branch.Id,
                        SkuId = sku1Id,
                        OnHand = 40m,
                        Reserved = 7m,
                        AverageCost = 135000m,
                        ReorderPoint = 12m,
                        LastSequence = 1
                    },
                    new InventoryBalance
                    {
                        TenantId = tenantId,
                        BranchId = branch.Id,
                        SkuId = sku2Id,
                        OnHand = 25m,
                        Reserved = 5m,
                        AverageCost = 82500m,
                        ReorderPoint = 8m,
                        LastSequence = 1
                    });

                db.WarehouseInventoryBalances.AddRange(
                    new WarehouseInventoryBalance
                    {
                        TenantId = tenantId,
                        BranchId = branch.Id,
                        WarehouseId = warehouse!.Id,
                        SkuId = sku1Id,
                        OnHand = 40m,
                        Reserved = 7m,
                        LastSequence = 1
                    },
                    new WarehouseInventoryBalance
                    {
                        TenantId = tenantId,
                        BranchId = branch.Id,
                        WarehouseId = warehouse.Id,
                        SkuId = sku2Id,
                        OnHand = 25m,
                        Reserved = 5m,
                        LastSequence = 1
                    });

                await db.SaveChangesAsync();
            }
        }

        var staffToken = await LoginAsync(client, staffEmail, "Password@123");
        var cashierToken = await LoginAsync(client, cashierEmail, "Password@123");

        return new RbacTestSetup(tenantId, branch.Id, warehouse.Id, sku1Id, sku2Id, ownerToken, staffToken, cashierToken);
    }

    private async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var loginRes = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = password
        });
        loginRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = await loginRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        return auth!.AccessToken;
    }

    [Fact]
    public async Task AC01_AC05_Cashier_GetBalances_And_GetSkuBalanceAtBranch_ReturnsNullAverageCost_WhileKeepingStockFields()
    {
        var setup = await SetupTenantAndSeedBalancesAsync("G1A_AC01");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", setup.CashierToken);

        // 1. GET /api/inventory/balances (cấp chi nhánh, không truyền warehouseId)
        var branchBalancesRes = await client.GetAsync($"/api/inventory/balances?branchId={setup.BranchId}");
        branchBalancesRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var branchRawJson = await branchBalancesRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var branchItemsJson = branchRawJson.GetProperty("items").EnumerateArray().ToList();
        branchItemsJson.Should().HaveCount(2);
        foreach (var item in branchItemsJson)
        {
            item.TryGetProperty("averageCost", out var avgProp).Should().BeTrue("tên trường JSON averageCost không được đổi");
            avgProp.ValueKind.Should().Be(JsonValueKind.Null, "Cashier không được nhận giá vốn ở mọi dòng");
        }

        var branchBalances = branchRawJson.Deserialize<PagedResult<InventoryBalanceDto>>(_jsonOptions);
        branchBalances!.Items.Should().OnlyContain(b => b.AverageCost == null);
        branchBalances.Items.Should().Contain(b =>
            b.SkuId == setup.Sku1Id && b.OnHand == 40m && b.Reserved == 7m && b.Available == 33m && b.ReorderPoint == 12m);
        branchBalances.Items.Should().Contain(b =>
            b.SkuId == setup.Sku2Id && b.OnHand == 25m && b.Reserved == 5m && b.Available == 20m && b.ReorderPoint == 8m);

        // 2. GET /api/inventory/balances?warehouseId=... (cấp kho)
        var whBalancesRes = await client.GetAsync($"/api/inventory/balances?branchId={setup.BranchId}&warehouseId={setup.WarehouseId}");
        whBalancesRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var whRawJson = await whBalancesRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var whItemsJson = whRawJson.GetProperty("items").EnumerateArray().ToList();
        whItemsJson.Should().HaveCount(2);
        foreach (var item in whItemsJson)
        {
            item.TryGetProperty("averageCost", out var avgProp).Should().BeTrue();
            avgProp.ValueKind.Should().Be(JsonValueKind.Null, "Cashier lọc theo kho vẫn phải bị che averageCost = null");
        }

        var whBalances = whRawJson.Deserialize<PagedResult<InventoryBalanceDto>>(_jsonOptions);
        whBalances!.Items.Should().OnlyContain(b => b.AverageCost == null);
        whBalances.Items.Should().Contain(b =>
            b.SkuId == setup.Sku1Id && b.WarehouseId == setup.WarehouseId &&
            b.OnHand == 40m && b.Reserved == 7m && b.Available == 33m && b.ReorderPoint == 12m);

        // 3. GET /api/inventory/balances/sku/{skuId}/branch/{branchId}
        var skuBranchRes = await client.GetAsync($"/api/inventory/balances/sku/{setup.Sku1Id}/branch/{setup.BranchId}");
        skuBranchRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var skuBranchJson = await skuBranchRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        skuBranchJson.TryGetProperty("averageCost", out var skuAvgProp).Should().BeTrue();
        skuAvgProp.ValueKind.Should().Be(JsonValueKind.Null);

        var skuBalance = skuBranchJson.Deserialize<InventoryBalanceDto>(_jsonOptions);
        skuBalance.Should().NotBeNull();
        skuBalance!.AverageCost.Should().BeNull();
        skuBalance.OnHand.Should().Be(40m);
        skuBalance.Reserved.Should().Be(7m);
        skuBalance.Available.Should().Be(33m);
        skuBalance.ReorderPoint.Should().Be(12m);
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Staff")]
    public async Task AC02_OwnerAndStaff_GetBalances_And_GetSkuBalanceAtBranch_ReturnsStoredAverageCost(string role)
    {
        var setup = await SetupTenantAndSeedBalancesAsync($"G1A_AC02_{role}");
        var token = role == "Owner" ? setup.OwnerToken : setup.StaffToken;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 1. Cấp chi nhánh
        var branchRes = await client.GetAsync($"/api/inventory/balances?branchId={setup.BranchId}");
        branchRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var branchBalances = await branchRes.Content.ReadFromJsonAsync<PagedResult<InventoryBalanceDto>>(_jsonOptions);
        branchBalances!.Items.Should().Contain(b => b.SkuId == setup.Sku1Id && b.AverageCost == 135000m);
        branchBalances.Items.Should().Contain(b => b.SkuId == setup.Sku2Id && b.AverageCost == 82500m);

        // 2. Cấp kho
        var whRes = await client.GetAsync($"/api/inventory/balances?branchId={setup.BranchId}&warehouseId={setup.WarehouseId}");
        whRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var whBalances = await whRes.Content.ReadFromJsonAsync<PagedResult<InventoryBalanceDto>>(_jsonOptions);
        whBalances!.Items.Should().Contain(b => b.SkuId == setup.Sku1Id && b.AverageCost == 135000m);
        whBalances.Items.Should().Contain(b => b.SkuId == setup.Sku2Id && b.AverageCost == 82500m);

        // 3. SKU tại chi nhánh
        var skuRes = await client.GetAsync($"/api/inventory/balances/sku/{setup.Sku1Id}/branch/{setup.BranchId}");
        skuRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var skuBal = await skuRes.Content.ReadFromJsonAsync<InventoryBalanceDto>(_jsonOptions);
        skuBal!.AverageCost.Should().Be(135000m);
        skuBal.OnHand.Should().Be(40m);
        skuBal.Available.Should().Be(33m);
    }

    [Fact]
    public async Task AC03_WarehouseWrites_OnlyOwnerAllowed_StaffAndCashierForbidden_GetAllowedForAll()
    {
        var setup = await SetupTenantAndSeedBalancesAsync("G1A_AC03");

        var ownerClient = _factory.CreateClient();
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", setup.OwnerToken);

        var staffClient = _factory.CreateClient();
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", setup.StaffToken);

        var cashierClient = _factory.CreateClient();
        cashierClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", setup.CashierToken);

        // 1. GET giữ nguyên cho cả 3 vai trò
        foreach (var client in new[] { ownerClient, staffClient, cashierClient })
        {
            var listRes = await client.GetAsync($"/api/warehouses?branchId={setup.BranchId}");
            listRes.StatusCode.Should().Be(HttpStatusCode.OK);

            var detailRes = await client.GetAsync($"/api/warehouses/{setup.WarehouseId}");
            detailRes.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // 2. POST /api/warehouses: Staff -> 403, Cashier -> 403, Owner -> 201
        var staffPost = await staffClient.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest
        {
            BranchId = setup.BranchId,
            Name = "Kho Staff Tạo Trái Phép"
        });
        staffPost.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var cashierPost = await cashierClient.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest
        {
            BranchId = setup.BranchId,
            Name = "Kho Cashier Tạo Trái Phép"
        });
        cashierPost.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var ownerPost = await ownerClient.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest
        {
            BranchId = setup.BranchId,
            Name = "Kho Owner Hợp Lệ"
        });
        ownerPost.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdWh = await ownerPost.Content.ReadFromJsonAsync<WarehouseResponse>(_jsonOptions);

        // 3. PUT /api/warehouses/{id}: Staff -> 403, Cashier -> 403, Owner -> 200
        var staffPut = await staffClient.PutAsJsonAsync($"/api/warehouses/{createdWh!.Id}", new UpdateWarehouseRequest
        {
            BranchId = setup.BranchId,
            Name = "Kho Staff Sửa",
            IsActive = true
        });
        staffPut.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var cashierPut = await cashierClient.PutAsJsonAsync($"/api/warehouses/{createdWh.Id}", new UpdateWarehouseRequest
        {
            BranchId = setup.BranchId,
            Name = "Kho Cashier Sửa",
            IsActive = true
        });
        cashierPut.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var ownerPut = await ownerClient.PutAsJsonAsync($"/api/warehouses/{createdWh.Id}", new UpdateWarehouseRequest
        {
            BranchId = setup.BranchId,
            Name = "Kho Owner Đã Sửa",
            IsActive = true
        });
        ownerPut.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. PUT /api/warehouses/{id}/toggle-status: Staff -> 403, Cashier -> 403, Owner -> 200
        var staffToggle = await staffClient.PutAsync($"/api/warehouses/{createdWh.Id}/toggle-status", null);
        staffToggle.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var cashierToggle = await cashierClient.PutAsync($"/api/warehouses/{createdWh.Id}/toggle-status", null);
        cashierToggle.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var ownerToggle = await ownerClient.PutAsync($"/api/warehouses/{createdWh.Id}/toggle-status", null);
        ownerToggle.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
