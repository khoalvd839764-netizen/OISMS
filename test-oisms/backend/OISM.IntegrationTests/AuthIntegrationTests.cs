using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using OISM.Application.Features.Auth.CurrentUser;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.RefreshToken;
using OISM.Application.Features.Auth.RegisterTenant;
using Xunit;

namespace OISM.IntegrationTests;

public class AuthIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public AuthIntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task TC_AUTH_01_RegisterTenant_ShouldSucceed_AndAllowLogin()
    {
        // 1. Đăng ký Tenant mới (FR-AUTH-01)
        var registerRequest = new RegisterTenantRequest
        {
            TenantName = "Fashion Store 1",
            TenantCode = "FASHION01",
            FullName = "Nguyễn Văn Chủ Shop",
            Email = "owner@fashion01.com",
            Password = "Password@123",
            PhoneNumber = "0901234567"
        };

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register-tenant", registerRequest);
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var regContent = await registerResponse.Content.ReadFromJsonAsync<RegisterTenantResponse>(_jsonOptions);
        regContent.Should().NotBeNull();
        regContent!.TenantCode.Should().Be("FASHION01");
        regContent.Email.Should().Be("owner@fashion01.com");

        // 2. Đăng nhập hợp lệ (FR-AUTH-02)
        var loginRequest = new LoginRequest
        {
            Email = "owner@fashion01.com",
            Password = "Password@123"
        };

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var authData = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        authData.Should().NotBeNull();
        authData!.AccessToken.Should().NotBeNullOrEmpty();
        authData.RefreshToken.Should().NotBeNullOrEmpty();
        authData.User.Roles.Should().Contain("Owner");

        // 3. Gọi endpoint /api/auth/me với Bearer Token
        var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authData.AccessToken);

        var meResponse = await _client.SendAsync(meRequest);
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var meData = await meResponse.Content.ReadFromJsonAsync<CurrentUserResponse>(_jsonOptions);
        meData.Should().NotBeNull();
        meData!.Email.Should().Be("owner@fashion01.com");
        meData.Roles.Should().Contain("Owner");
    }

    [Fact]
    public async Task RegisterTenant_ShouldReturnConflict_WhenEmailAlreadyExists()
    {
        // Arrange
        var initialRequest = new RegisterTenantRequest
        {
            TenantName = "Shop A",
            TenantCode = "SHOPA",
            FullName = "Chủ Shop A",
            Email = "duplicate@shop.com",
            Password = "Password@123"
        };
        await _client.PostAsJsonAsync("/api/auth/register-tenant", initialRequest);

        // Act: Đăng ký shop khác nhưng trùng Email
        var duplicateRequest = new RegisterTenantRequest
        {
            TenantName = "Shop B",
            TenantCode = "SHOPB",
            FullName = "Chủ Shop B",
            Email = "duplicate@shop.com",
            Password = "Password@123"
        };
        var response = await _client.PostAsJsonAsync("/api/auth/register-tenant", duplicateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Login_ShouldReturnUnauthorized_WhenPasswordIsWrong()
    {
        // Arrange: Đăng ký trước
        var regRequest = new RegisterTenantRequest
        {
            TenantName = "Shop Pass Test",
            TenantCode = "SHOPPASS",
            FullName = "Chủ Shop",
            Email = "userpass@shop.com",
            Password = "CorrectPassword@123"
        };
        await _client.PostAsJsonAsync("/api/auth/register-tenant", regRequest);

        // Act: Đăng nhập sai mật khẩu
        var loginRequest = new LoginRequest
        {
            Email = "userpass@shop.com",
            Password = "WrongPassword@999"
        };
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RefreshToken_ShouldRotateToken_WhenValid()
    {
        // Arrange
        var regRequest = new RegisterTenantRequest
        {
            TenantName = "Shop Token Test",
            TenantCode = "SHOPTOKEN",
            FullName = "Chủ Shop",
            Email = "token@shop.com",
            Password = "Password@123"
        };
        await _client.PostAsJsonAsync("/api/auth/register-tenant", regRequest);

        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "token@shop.com",
            Password = "Password@123"
        });
        var authData = await loginRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);

        // Act: Làm mới token
        var refreshRes = await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest
        {
            RefreshToken = authData!.RefreshToken
        });

        // Assert
        refreshRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var newAuthData = await refreshRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        newAuthData.Should().NotBeNull();
        newAuthData!.AccessToken.Should().NotBeNullOrEmpty();
        newAuthData.RefreshToken.Should().NotBe(authData.RefreshToken);
    }
}
