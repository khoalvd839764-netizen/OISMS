using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.RegisterTenant;
using OISM.Application.Features.Users;
using Xunit;

namespace OISM.IntegrationTests;

public class ExceptionHandlingMiddlewareTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ExceptionHandlingMiddlewareTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task LoginAsOwnerAsync()
    {
        await _client.PostAsJsonAsync("/api/auth/register-tenant", new RegisterTenantRequest
        {
            TenantName = "Shop Middleware",
            TenantCode = "MIDDLEWARE_SHOP",
            FullName = "Chủ Shop",
            Email = "owner_middleware@shop.com",
            Password = "Password@123"
        });

        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "owner_middleware@shop.com",
            Password = "Password@123"
        });
        var authData = await loginRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authData!.AccessToken);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string errorCode)
    {
        response.StatusCode.Should().Be(status);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("status").GetInt32().Should().Be((int)status);
        body.RootElement.GetProperty("errorCode").GetString().Should().Be(errorCode);
    }

    [Fact]
    public async Task UnknownUser_ReturnsProblemDetailsWithErrorCode()
    {
        await LoginAsOwnerAsync();

        var response = await _client.GetAsync($"/api/users/{Guid.NewGuid()}");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "USER_NOT_FOUND");
    }

    [Fact]
    public async Task InvalidCreateUser_ReturnsValidationProblem()
    {
        await LoginAsOwnerAsync();

        var response = await _client.PostAsJsonAsync("/api/users", new CreateUserRequest
        {
            DisplayName = "Nhân viên",
            Email = "staff_middleware@shop.com",
            Phone = "abc",
            Password = "Password@123",
            Role = "Staff"
        });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task ReorderPointOfUnknownSku_ReturnsProblemDetailsWithErrorCode()
    {
        await LoginAsOwnerAsync();

        var response = await _client.PutAsJsonAsync("/api/inventory/reorder-point", new
        {
            branchId = Guid.NewGuid(),
            skuId = Guid.NewGuid(),
            reorderPoint = 5
        });

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "SKU_NOT_FOUND");
    }
}
