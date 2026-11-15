using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.RefreshToken;
using OISM.Infrastructure.Persistence;
using OISM.Infrastructure.Services.Email;
using Xunit;

namespace OISM.IntegrationTests;

public class PostgresPasswordResetWebApplicationFactory : WebApplicationFactoryWithPostgres
{
    public CapturingEmailTransport Email { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services => services.AddSingleton<IEmailTransport>(Email));
    }
}

/// <summary>
/// Upgrade 10 trên PostgreSQL thật: khóa dòng users khi đặt lại mật khẩu bằng token,
/// thu hồi refresh token trong cùng transaction, tài khoản bị khóa không đặt lại được.
/// </summary>
public class PostgresPasswordResetIntegrationTests : IAsyncLifetime, IClassFixture<PostgresPasswordResetWebApplicationFactory>
{
    private const string StaffEmail = "staff@shopa.local";
    private readonly PostgresPasswordResetWebApplicationFactory _factory;
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public PostgresPasswordResetIntegrationTests(PostgresPasswordResetWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
        await using var context = PostgresTestEnvironment.CreateRuntimeDbContext(new TestTenantService());
        await OismsDatabaseSeeder.SeedAsync(context, PostgresTestEnvironment.AppPassword);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class TestTenantService : ICurrentTenantService
    {
        public Guid? TenantId { get; set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private async Task<AuthResponse> LoginOkAsync(string identifier, string password)
    {
        using var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new { identifier, password });
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<AuthResponse>(_json))!;
    }

    private async Task<string> RequestTokenAsync(string identifier, string email)
    {
        var skip = _factory.Email.Messages.Count(m => m.To == email);
        using var client = _factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/forgot-password", new { identifier }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        return CapturingEmailTransport.ExtractToken(await _factory.Email.WaitForAsync(email, skip));
    }

    private async Task<HttpResponseMessage> ResetAsync(string token, string newPassword)
    {
        using var client = _factory.CreateClient();
        return await client.PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword });
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage res)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }

    [Fact]
    public async Task Postgres_ConcurrentResetWithSameToken_ExactlyOneSucceeds_AndSessionsRevoked()
    {
        var oldSession = await LoginOkAsync(StaffEmail, PostgresTestEnvironment.AppPassword);
        // SĐT seed của staff là +84901000002: yêu cầu bằng số nội địa.
        var token = await RequestTokenAsync("0901000002", StaffEmail);

        var passwords = Enumerable.Range(1, 6).Select(i => $"DongThoi@{i}").ToArray();
        var responses = await Task.WhenAll(passwords.Select(p => ResetAsync(token, p)));

        var winners = responses.Select((r, i) => (r, i)).Where(x => x.r.StatusCode == HttpStatusCode.OK).ToList();
        winners.Should().HaveCount(1, "token chỉ dùng được một lần kể cả khi gửi đồng thời");
        foreach (var loser in responses.Where(r => r.StatusCode != HttpStatusCode.OK))
        {
            loser.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ErrorCodeAsync(loser)).Should().Be("RESET_TOKEN_INVALID");
        }

        await LoginOkAsync(StaffEmail, passwords[winners[0].i]);

        using var client = _factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest { RefreshToken = oldSession.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await using var db = PostgresTestEnvironment.CreateRuntimeDbContext(new TestTenantService());
        using var scope = db.OpenPrivilegedScope();
        var activeTokens = await db.RefreshTokens.IgnoreQueryFilters()
            .Where(t => t.UserId == oldSession.User.Id && t.RevokedAt == null)
            .CountAsync();
        activeTokens.Should().Be(1, "chỉ còn refresh token của lần đăng nhập bằng mật khẩu mới");
    }

    [Fact]
    public async Task Postgres_SecondRequestToken_StillValidUntilUsed_ThenAllOldLinksExpire()
    {
        var first = await RequestTokenAsync(StaffEmail, StaffEmail);
        var second = await RequestTokenAsync(StaffEmail, StaffEmail);

        (await ResetAsync(second, "LanHai@123")).StatusCode.Should().Be(HttpStatusCode.OK);

        var old = await ResetAsync(first, "LanMot@123");
        old.StatusCode.Should().Be(HttpStatusCode.BadRequest, "đổi mật khẩu xong thì mọi link cũ hết hiệu lực");
        (await ErrorCodeAsync(old)).Should().Be("RESET_TOKEN_INVALID");
        await LoginOkAsync(StaffEmail, "LanHai@123");
    }

    [Fact]
    public async Task Postgres_InactiveUser_GetsNoMail_AndCannotUseEarlierToken()
    {
        var token = await RequestTokenAsync(StaffEmail, StaffEmail);

        await using (var db = PostgresTestEnvironment.CreateRuntimeDbContext(new TestTenantService()))
        using (db.OpenPrivilegedScope())
        {
            var staff = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == StaffEmail);
            staff.IsActive = false;
            await db.SaveChangesAsync();
        }

        var res = await ResetAsync(token, "BiKhoa@123");
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(res)).Should().Be("USER_INACTIVE");

        var before = _factory.Email.Messages.Count(m => m.To == StaffEmail);
        using var client = _factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/forgot-password", new { identifier = StaffEmail }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        await Task.Delay(500);
        _factory.Email.Messages.Count(m => m.To == StaffEmail).Should().Be(before, "tài khoản bị khóa không nhận mail");
    }

    [Fact]
    public async Task Postgres_OwnerReset_UpdatesHash_AndRevokesStaffRefreshTokens()
    {
        var owner = await LoginOkAsync("owner@shopa.local", PostgresTestEnvironment.AppPassword);
        var staff = await LoginOkAsync(StaffEmail, PostgresTestEnvironment.AppPassword);

        using var client = _factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/users/{staff.User.Id}/reset-password")
        {
            Content = JsonContent.Create(new { newPassword = "ChuDat@123" })
        };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var res = await client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());

        (await client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest { RefreshToken = staff.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await LoginOkAsync("+84 901 000 002", "ChuDat@123");
    }
}
