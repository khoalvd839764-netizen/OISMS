using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Npgsql;
using OISM.Application.Features.Auth.CurrentUser;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.RefreshToken;
using OISM.Application.Features.Auth.RegisterTenant;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// BF-036: đăng nhập, đăng ký và làm mới token khi request còn mang access token hợp lệ của tenant khác
/// (phiên cũ trong trình duyệt). Trước khi sửa, CurrentTenantService lấy tenant từ token cũ và guard
/// chéo tenant của AppDbContext ném SecurityException, API trả 500. Chạy trên PostgreSQL thật.
/// </summary>
public class AuthStaleTokenIntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private const string Password = "Password@123";

    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public AuthStaleTokenIntegrationTests(WebApplicationFactoryWithPostgres factory) => _factory = factory;

    public async Task InitializeAsync() => await PostgresTestEnvironment.ResetTestDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<RegisterTenantResponse> RegisterAsync(HttpClient client, string code, string? bearer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register-tenant")
        {
            Content = JsonContent.Create(new RegisterTenantRequest
            {
                TenantName = $"Shop {code}",
                TenantCode = code,
                FullName = $"Owner {code}",
                Email = $"{code.ToLowerInvariant()}@stale.test",
                Password = Password
            })
        };
        if (bearer != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RegisterTenantResponse>(_json))!;
    }

    private async Task<HttpResponseMessage> PostWithBearerAsync<T>(HttpClient client, string url, T body, string? bearer)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (bearer != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }
        return await client.SendAsync(request);
    }

    private static Guid TenantClaim(string accessToken)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        return Guid.Parse(jwt.Claims.Single(c => c.Type == "tenantId").Value);
    }

    private async Task<string> GetMeEmailAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<CurrentUserResponse>(_json))!.Email;
    }

    private static async Task<List<(Guid TokenTenant, Guid UserTenant)>> ReadRefreshTokenTenantsAsync()
    {
        var rows = new List<(Guid, Guid)>();
        await using var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT rt.tenant_id, u.tenant_id FROM refresh_tokens rt JOIN users u ON u.id = rt.user_id", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetGuid(0), reader.GetGuid(1)));
        }
        return rows;
    }

    [Fact]
    public async Task Login_WithStaleBearerOfOtherTenant_ShouldReturn200_AndIssueTokenOfLoggingInTenant()
    {
        var client = _factory.CreateClient();
        var tenantA = await RegisterAsync(client, "STALEA");
        var tenantB = await RegisterAsync(client, "STALEB");

        // Phiên cũ trong trình duyệt: access token còn hạn của tenant B
        var loginB = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = tenantB.Email, Password = Password });
        loginB.StatusCode.Should().Be(HttpStatusCode.OK);
        var authB = (await loginB.Content.ReadFromJsonAsync<AuthResponse>(_json))!;
        TenantClaim(authB.AccessToken).Should().Be(tenantB.TenantId);

        // Đăng nhập tài khoản tenant A nhưng request vẫn gửi kèm token của B
        var loginA = await PostWithBearerAsync(client, "/api/auth/login",
            new LoginRequest { Email = tenantA.Email, Password = Password }, authB.AccessToken);
        loginA.StatusCode.Should().Be(HttpStatusCode.OK, await loginA.Content.ReadAsStringAsync());
        var authA = (await loginA.Content.ReadFromJsonAsync<AuthResponse>(_json))!;

        TenantClaim(authA.AccessToken).Should().Be(tenantA.TenantId);
        authA.User.TenantId.Should().Be(tenantA.TenantId);
        authA.User.Email.Should().Be(tenantA.Email);
        (await GetMeEmailAsync(client, authA.AccessToken)).Should().Be(tenantA.Email);

        // Token B vẫn chỉ thấy tenant B
        (await GetMeEmailAsync(client, authB.AccessToken)).Should().Be(tenantB.Email);

        // Refresh token mới được ghi đúng tenant của user, không có dòng nào lệch tenant
        var rows = await ReadRefreshTokenTenantsAsync();
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.TokenTenant == r.UserTenant);
        rows.Count(r => r.TokenTenant == tenantA.TenantId).Should().Be(1);
        rows.Count(r => r.TokenTenant == tenantB.TenantId).Should().Be(1);
    }

    [Fact]
    public async Task Login_WithWrongPassword_AndStaleBearer_ShouldStillReturn401()
    {
        var client = _factory.CreateClient();
        var tenantA = await RegisterAsync(client, "STALEC");
        var tenantB = await RegisterAsync(client, "STALED");
        var loginB = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = tenantB.Email, Password = Password });
        var authB = (await loginB.Content.ReadFromJsonAsync<AuthResponse>(_json))!;

        var response = await PostWithBearerAsync(client, "/api/auth/login",
            new LoginRequest { Email = tenantA.Email, Password = "WrongPassword@999" }, authB.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadRefreshTokenTenantsAsync()).Should().ContainSingle(r => r.TokenTenant == tenantB.TenantId);
    }

    [Fact]
    public async Task RefreshToken_AndRegister_WithStaleBearerOfOtherTenant_ShouldUseOwnTenant()
    {
        var client = _factory.CreateClient();
        var tenantA = await RegisterAsync(client, "STALEE");
        var tenantB = await RegisterAsync(client, "STALEF");

        var authA = (await (await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = tenantA.Email, Password = Password })).Content.ReadFromJsonAsync<AuthResponse>(_json))!;
        var authB = (await (await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = tenantB.Email, Password = Password })).Content.ReadFromJsonAsync<AuthResponse>(_json))!;

        // Làm mới token của A trong khi request mang token của B
        var refresh = await PostWithBearerAsync(client, "/api/auth/refresh-token",
            new RefreshTokenRequest { RefreshToken = authA.RefreshToken }, authB.AccessToken);
        refresh.StatusCode.Should().Be(HttpStatusCode.OK, await refresh.Content.ReadAsStringAsync());
        var refreshedA = (await refresh.Content.ReadFromJsonAsync<AuthResponse>(_json))!;
        TenantClaim(refreshedA.AccessToken).Should().Be(tenantA.TenantId);
        refreshedA.User.TenantId.Should().Be(tenantA.TenantId);

        // Đăng ký tenant mới trong khi request mang token của B: dữ liệu mới thuộc tenant mới
        var tenantG = await RegisterAsync(client, "STALEG", authB.AccessToken);
        tenantG.TenantId.Should().NotBe(tenantB.TenantId);

        await using (var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT tenant_id FROM users WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", tenantG.UserId);
            ((Guid)(await cmd.ExecuteScalarAsync())!).Should().Be(tenantG.TenantId);
        }

        // Đăng xuất bằng refresh token của A khi còn mang token B
        var logout = await PostWithBearerAsync(client, "/api/auth/logout",
            new RefreshTokenRequest { RefreshToken = refreshedA.RefreshToken }, authB.AccessToken);
        logout.StatusCode.Should().Be(HttpStatusCode.OK);

        var rows = await ReadRefreshTokenTenantsAsync();
        rows.Should().OnlyContain(r => r.TokenTenant == r.UserTenant);
        rows.Count(r => r.TokenTenant == tenantB.TenantId).Should().Be(1);
    }

    [Fact]
    public async Task BusinessEndpoint_WithBearer_ShouldStillUseTenantFromJwt()
    {
        // Endpoint nghiệp vụ không bị ảnh hưởng: vẫn lấy tenant từ JWT đã xác thực
        var client = _factory.CreateClient();
        var tenantA = await RegisterAsync(client, "STALEH");
        var authA = (await (await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = tenantA.Email, Password = Password })).Content.ReadFromJsonAsync<AuthResponse>(_json))!;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/branches");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authA.AccessToken);
        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var anonymous = new HttpRequestMessage(HttpMethod.Get, "/api/branches");
        (await client.SendAsync(anonymous)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
