using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.RegisterTenant;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

public class StockDocumentApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public StockDocumentApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<(string ownerToken, string cashierToken, Guid tenantId)> SetupTenantAndUsersAsync(string suffix)
    {
        var tenantCode = $"DOC_{suffix.ToUpperInvariant()}";
        var ownerEmail = $"owner_{suffix}@docshop.com";
        var cashierEmail = $"cashier_{suffix}@docshop.com";
        var password = "Password@123";

        // 1. Đăng ký Tenant và Owner
        var regRequest = new RegisterTenantRequest
        {
            TenantName = $"Doc Shop {suffix}",
            TenantCode = tenantCode,
            FullName = $"Chủ Shop {suffix}",
            Email = ownerEmail,
            Password = password
        };
        var regRes = await _client.PostAsJsonAsync("/api/auth/register-tenant", regRequest);
        var regData = await regRes.Content.ReadFromJsonAsync<RegisterTenantResponse>(_jsonOptions);
        var tenantId = regData!.TenantId;

        // Login lấy Owner token
        var ownerLoginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = ownerEmail,
            Password = password
        });
        var ownerAuth = await ownerLoginRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        var ownerToken = ownerAuth!.AccessToken;

        // 2. Thêm Cashier user vào tenant
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cashierUser = new User
            {
                TenantId = tenantId,
                Email = cashierEmail,
                DisplayName = $"Thu ngân {suffix}",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                Role = "Cashier",
                IsActive = true
            };
            using (db.OpenPrivilegedScope())
            {
                db.Users.Add(cashierUser);
                await db.SaveChangesAsync();
            }
        }

        // Login lấy Cashier token
        var cashierLoginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = cashierEmail,
            Password = password
        });
        var cashierAuth = await cashierLoginRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        var cashierToken = cashierAuth!.AccessToken;

        return (ownerToken, cashierToken, tenantId);
    }

    [Fact]
    public async Task StockTransfers_Endpoints_RoleAndAuthGuards()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (ownerToken, cashierToken, _) = await SetupTenantAndUsersAsync(suffix);

        // 1. Unauthenticated -> 401 Unauthorized
        _client.DefaultRequestHeaders.Authorization = null;
        var unauthListRes = await _client.GetAsync("/api/stock-transfers");
        unauthListRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var unauthDetailRes = await _client.GetAsync($"/api/stock-transfers/{Guid.NewGuid()}");
        unauthDetailRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 2. Cashier -> 403 Forbidden
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        var cashierListRes = await _client.GetAsync("/api/stock-transfers");
        cashierListRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var cashierDetailRes = await _client.GetAsync($"/api/stock-transfers/{Guid.NewGuid()}");
        cashierDetailRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 3. Owner -> 200 OK for list
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var ownerListRes = await _client.GetAsync("/api/stock-transfers");
        ownerListRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Owner -> 404 Not Found ProblemDetails for non-existent ID
        var missingId = Guid.NewGuid();
        var ownerDetailRes = await _client.GetAsync($"/api/stock-transfers/{missingId}");
        ownerDetailRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var notFoundBody = await ownerDetailRes.Content.ReadAsStringAsync();
        notFoundBody.Should().Contain("NOT_FOUND");
    }

    [Fact]
    public async Task StockCounts_Endpoints_RoleAndAuthGuards()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (ownerToken, cashierToken, _) = await SetupTenantAndUsersAsync(suffix);

        // 1. Unauthenticated -> 401 Unauthorized
        _client.DefaultRequestHeaders.Authorization = null;
        var unauthListRes = await _client.GetAsync("/api/stock-counts");
        unauthListRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var unauthDetailRes = await _client.GetAsync($"/api/stock-counts/{Guid.NewGuid()}");
        unauthDetailRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 2. Cashier -> 403 Forbidden
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        var cashierListRes = await _client.GetAsync("/api/stock-counts");
        cashierListRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var cashierDetailRes = await _client.GetAsync($"/api/stock-counts/{Guid.NewGuid()}");
        cashierDetailRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 3. Owner -> 200 OK for list
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var ownerListRes = await _client.GetAsync("/api/stock-counts");
        ownerListRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Owner -> 404 Not Found ProblemDetails for non-existent ID
        var missingId = Guid.NewGuid();
        var ownerDetailRes = await _client.GetAsync($"/api/stock-counts/{missingId}");
        ownerDetailRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var notFoundBody = await ownerDetailRes.Content.ReadAsStringAsync();
        notFoundBody.Should().Contain("NOT_FOUND");
    }
}
