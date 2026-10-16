using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.RegisterTenant;
using OISM.Application.Features.Branches;
using OISM.Application.Features.Suppliers;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

public class BranchAndSupplierIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public BranchAndSupplierIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Branch_CreateAndToggleStatus_ShouldWork_ForOwner()
    {
        // 1. Đăng ký Tenant và lấy Owner Token
        var regRequest = new RegisterTenantRequest
        {
            TenantName = "Shop Chi Nhánh",
            TenantCode = "BRANCH_SHOP",
            FullName = "Chủ Shop",
            Email = "owner_branch@shop.com",
            Password = "Password@123"
        };
        await _client.PostAsJsonAsync("/api/auth/register-tenant", regRequest);

        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "owner_branch@shop.com",
            Password = "Password@123"
        });
        var authData = await loginRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        var ownerToken = authData!.AccessToken;

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        // 2. Tạo Chi nhánh mới
        var createBranchReq = new CreateBranchRequest
        {
            Name = "Chi nhánh Quận 1",
            Address = "123 Lê Lợi, P. Bến Nghé, Q.1, TP.HCM",
            Phone = "02838123456"
        };
        var createRes = await _client.PostAsJsonAsync("/api/branches", createBranchReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var branch = await createRes.Content.ReadFromJsonAsync<BranchResponse>(_jsonOptions);
        branch.Should().NotBeNull();
        branch!.Name.Should().Be("Chi nhánh Quận 1");
        branch.IsActive.Should().BeTrue();

        // 3. Toggle trạng thái chi nhánh
        var toggleRes = await _client.PutAsync($"/api/branches/{branch.Id}/toggle-status", null);
        toggleRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Lấy danh sách chi nhánh
        var listRes = await _client.GetAsync("/api/branches");
        listRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var branches = await listRes.Content.ReadFromJsonAsync<List<BranchResponse>>(_jsonOptions);
        branches.Should().Contain(b => b.Id == branch.Id && !b.IsActive);
    }

    [Fact]
    public async Task TC_AUTH_02_CashierRole_ShouldBeForbidden_WhenAccessingOwnerEndpoints()
    {
        // 1. Tạo Tenant và tạo tài khoản Cashier trực tiếp vào database
        var regRequest = new RegisterTenantRequest
        {
            TenantName = "Shop Phân Quyền",
            TenantCode = "RBAC_SHOP",
            FullName = "Chủ Shop",
            Email = "owner_rbac@shop.com",
            Password = "Password@123"
        };
        var regRes = await _client.PostAsJsonAsync("/api/auth/register-tenant", regRequest);
        var regData = await regRes.Content.ReadFromJsonAsync<RegisterTenantResponse>(_jsonOptions);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cashierRole = await db.Roles.FirstOrDefaultAsync(r => r.Code == "Cashier");

            var cashierUser = new User
            {
                TenantId = regData!.TenantId,
                Email = "cashier@shop.com",
                FullName = "Nhân viên Thu ngân",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Cashier@123"),
                IsActive = true
            };
            db.Users.Add(cashierUser);
            await db.SaveChangesAsync();

            db.UserRoles.Add(new UserRole
            {
                UserId = cashierUser.Id,
                RoleId = cashierRole!.Id,
                AssignedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // 2. Đăng nhập bằng tài khoản Cashier
        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "cashier@shop.com",
            Password = "Cashier@123"
        });
        var authData = await loginRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        var cashierToken = authData!.AccessToken;

        // 3. Thử gọi API tạo chi nhánh (yêu cầu quyền Owner) -> Bắt buộc nhận 403 Forbidden (TC-AUTH-02)
        var forbiddenClient = _factory.CreateClient();
        forbiddenClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);

        var response = await forbiddenClient.PostAsJsonAsync("/api/branches", new CreateBranchRequest
        {
            Name = "Chi nhánh trái phép"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Supplier_CreateAndSearch_ShouldWorkProperly()
    {
        // 1. Đăng ký Tenant và lấy token
        var regRequest = new RegisterTenantRequest
        {
            TenantName = "Shop Nhà Cung Cấp",
            TenantCode = "SUPPLIER_SHOP",
            FullName = "Chủ Shop",
            Email = "owner_supplier@shop.com",
            Password = "Password@123"
        };
        await _client.PostAsJsonAsync("/api/auth/register-tenant", regRequest);

        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "owner_supplier@shop.com",
            Password = "Password@123"
        });
        var authData = await loginRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authData!.AccessToken);

        // 2. Tạo Nhà cung cấp
        var createSupplierReq = new CreateSupplierRequest
        {
            Name = "Công ty Dệt May Việt Tiến",
            Code = "NCC-VIETTIEN",
            ContactName = "Nguyễn Văn Đại Diện",
            Phone = "0987654321",
            Email = "contact@viettien.vn",
            Address = "Quận Tân Bình, TP.HCM"
        };
        var createRes = await _client.PostAsJsonAsync("/api/suppliers", createSupplierReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // 3. Tra cứu theo từ khóa
        var searchRes = await _client.GetAsync("/api/suppliers?search=viettien");
        searchRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await searchRes.Content.ReadFromJsonAsync<List<SupplierResponse>>(_jsonOptions);
        list.Should().NotBeNull();
        list!.Should().Contain(s => s.Code == "NCC-VIETTIEN");
    }
}
