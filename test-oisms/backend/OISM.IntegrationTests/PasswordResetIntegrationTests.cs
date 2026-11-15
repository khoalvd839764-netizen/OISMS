using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.RefreshToken;
using OISM.Application.Features.Auth.RegisterTenant;
using OISM.Infrastructure.Services.Email;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>Kênh gửi mail giả cho test: giữ thư trong bộ nhớ thay vì gửi.</summary>
public sealed class CapturingEmailTransport : IEmailTransport
{
    private static readonly Regex TokenRegex = new("#token=([A-Za-z0-9_-]+)", RegexOptions.Compiled);

    public ConcurrentQueue<EmailMessage> Messages { get; } = new();

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        Messages.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>Chờ thư gửi tới địa chỉ <paramref name="to"/> (thư đi qua hàng đợi nền nên không đến ngay).</summary>
    public async Task<EmailMessage> WaitForAsync(string to, int skip = 0)
    {
        for (var i = 0; i < 100; i++)
        {
            var match = Messages.Where(m => m.To == to).Skip(skip).FirstOrDefault();
            if (match != null)
            {
                return match;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Không nhận được email gửi tới {to}.");
    }

    public static string ExtractLink(EmailMessage message) =>
        Regex.Match(message.TextBody, @"https?://\S+").Value;

    public static string ExtractToken(EmailMessage message) =>
        TokenRegex.Match(message.TextBody).Groups[1].Value;
}

public class PasswordResetWebApplicationFactory : CustomWebApplicationFactory
{
    public CapturingEmailTransport Email { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services => services.AddSingleton<IEmailTransport>(Email));
    }
}

/// <summary>
/// Upgrade 10 trên EF InMemory: đăng nhập email/SĐT, quên mật khẩu qua email, Owner đặt lại hộ, rate limit.
/// Phần đồng thời và transaction kiểm chứng trên PostgreSQL ở <see cref="PostgresPasswordResetIntegrationTests"/>.
/// </summary>
public class PasswordResetIntegrationTests : IClassFixture<PasswordResetWebApplicationFactory>
{
    private const string Password = "Password@123";
    private readonly PasswordResetWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public PasswordResetIntegrationTests(PasswordResetWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<(string Email, string Phone)> RegisterTenantAsync(string code)
    {
        var suffix = Math.Abs(code.GetHashCode() % 10_000_000).ToString("D7");
        var email = $"owner@{code.ToLowerInvariant()}.test";
        var phone = "091" + suffix;
        var res = await _client.PostAsJsonAsync("/api/auth/register-tenant", new RegisterTenantRequest
        {
            TenantName = "Cửa hàng " + code,
            TenantCode = code,
            FullName = "Chủ " + code,
            Email = email,
            Password = Password,
            PhoneNumber = phone
        });
        res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        return (email, phone);
    }

    private Task<HttpResponseMessage> LoginAsync(string identifier, string password) =>
        _client.PostAsJsonAsync("/api/auth/login", new { identifier, password });

    private async Task<AuthResponse> LoginOkAsync(string identifier, string password)
    {
        var res = await LoginAsync(identifier, password);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<AuthResponse>(_json))!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null)
        {
            req.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(req);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage res)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }

    private async Task<Guid> CreateStaffAsync(string ownerToken, string email, string role = "Staff")
    {
        var res = await SendAsync(HttpMethod.Post, "/api/users", ownerToken, new
        {
            email,
            displayName = "Nhân viên " + email,
            password = Password,
            role
        });
        res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Login_ByPhone_InDomesticOrSpacedFormat_Succeeds_AndLegacyEmailFieldStillWorks()
    {
        var (email, phone) = await RegisterTenantAsync("U10PHONE");

        var byPhone = await LoginOkAsync(phone, Password);
        byPhone.User.Email.Should().Be(email);

        var spaced = $"+84 {phone[1..4]} {phone[4..7]} {phone[7..]}";
        (await LoginOkAsync(spaced, Password)).User.Id.Should().Be(byPhone.User.Id);

        var legacy = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email.ToUpperInvariant(), Password = Password });
        legacy.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_UnknownPhone_And_WrongPassword_ReturnSameError_InvalidIdentifierReturns400()
    {
        var (_, phone) = await RegisterTenantAsync("U10WRONG");

        var unknown = await LoginAsync("0999999999", Password);
        var wrong = await LoginAsync(phone, "sai-mat-khau");

        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await unknown.Content.ReadAsStringAsync()).Should().Be(await wrong.Content.ReadAsStringAsync());

        (await LoginAsync("khong-phai-sdt", Password)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ForgotPassword_UnknownAccount_Returns202_AndSendsNoMail()
    {
        var statusRes = await _client.GetFromJsonAsync<JsonElement>("/api/auth/password-reset/status");
        statusRes.GetProperty("enabled").GetBoolean().Should().BeTrue();

        var known = await RegisterTenantAsync("U10UNKNOWN");
        var before = _factory.Email.Messages.Count;

        var res = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { identifier = "khongtontai@u10.test" });
        var knownRes = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { identifier = known.Email });

        res.StatusCode.Should().Be(HttpStatusCode.Accepted);
        knownRes.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await res.Content.ReadAsStringAsync()).Should().Be(await knownRes.Content.ReadAsStringAsync(),
            "phản hồi không được cho biết tài khoản có tồn tại hay không");
        await _factory.Email.WaitForAsync(known.Email);
        _factory.Email.Messages.Skip(before).Should().OnlyContain(m => m.To == known.Email);
    }

    [Fact]
    public async Task ForgotPassword_ByPhoneFromPos_ResetsPassword_RevokesSessions_AndTokenIsSingleUse()
    {
        var (email, phone) = await RegisterTenantAsync("U10FLOW");
        var oldSession = await LoginOkAsync(email, Password);

        var forgot = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { identifier = phone, app = "pos" });
        forgot.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var mail = await _factory.Email.WaitForAsync(email);
        CapturingEmailTransport.ExtractLink(mail).Should().StartWith("http://localhost:5174/reset-password#token=");
        var token = CapturingEmailTransport.ExtractToken(mail);

        var reset = await _client.PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = "MatKhauMoi@1" });
        reset.StatusCode.Should().Be(HttpStatusCode.OK, await reset.Content.ReadAsStringAsync());

        var refresh = await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest { RefreshToken = oldSession.RefreshToken });
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "đặt lại mật khẩu phải thu hồi refresh token cũ");

        (await LoginAsync(email, Password)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await LoginOkAsync(email, "MatKhauMoi@1");

        var reuse = await _client.PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = "MatKhauKhac@2" });
        reuse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(reuse)).Should().Be("RESET_TOKEN_INVALID");
    }

    [Fact]
    public async Task ResetPassword_TamperedToken_Returns400_AndShortPassword_Returns400()
    {
        var (email, _) = await RegisterTenantAsync("U10TAMPER");
        await _client.PostAsJsonAsync("/api/auth/forgot-password", new { identifier = email });
        var token = CapturingEmailTransport.ExtractToken(await _factory.Email.WaitForAsync(email));

        var tampered = token[..10] + (token[10] == 'A' ? 'B' : 'A') + token[11..];
        var res = await _client.PostAsJsonAsync("/api/auth/reset-password", new { token = tampered, newPassword = "MatKhauMoi@1" });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(res)).Should().Be("RESET_TOKEN_INVALID");

        var shortPw = await _client.PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = "123" });
        shortPw.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(shortPw)).Should().Be("VALIDATION_ERROR");

        await LoginOkAsync(email, Password);
    }

    [Fact]
    public async Task OwnerReset_StaffAndCashierOnly_WithinOwnTenant()
    {
        var (ownerEmail, _) = await RegisterTenantAsync("U10OWNA");
        var owner = await LoginOkAsync(ownerEmail, Password);
        var staffId = await CreateStaffAsync(owner.AccessToken, "staff@u10owna.test");
        var cashierId = await CreateStaffAsync(owner.AccessToken, "cashier@u10owna.test", "Cashier");
        var staffSession = await LoginOkAsync("staff@u10owna.test", Password);

        var reset = await SendAsync(HttpMethod.Post, $"/api/users/{staffId}/reset-password", owner.AccessToken, new { newPassword = "NhanVien@2" });
        reset.StatusCode.Should().Be(HttpStatusCode.OK, await reset.Content.ReadAsStringAsync());
        (await _client.PostAsJsonAsync("/api/auth/refresh-token", new RefreshTokenRequest { RefreshToken = staffSession.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await LoginOkAsync("staff@u10owna.test", "NhanVien@2");

        (await SendAsync(HttpMethod.Post, $"/api/users/{cashierId}/reset-password", owner.AccessToken, new { newPassword = "ThuNgan@2" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var self = await SendAsync(HttpMethod.Post, $"/api/users/{owner.User.Id}/reset-password", owner.AccessToken, new { newPassword = "ChuShop@2" });
        self.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(self)).Should().Be("CANNOT_RESET_OWNER_PASSWORD");

        var staffCalls = await SendAsync(HttpMethod.Post, $"/api/users/{cashierId}/reset-password",
            (await LoginOkAsync("staff@u10owna.test", "NhanVien@2")).AccessToken, new { newPassword = "ThuNgan@3" });
        staffCalls.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var (otherOwnerEmail, _) = await RegisterTenantAsync("U10OWNB");
        var otherOwner = await LoginOkAsync(otherOwnerEmail, Password);
        var crossTenant = await SendAsync(HttpMethod.Post, $"/api/users/{staffId}/reset-password", otherOwner.AccessToken, new { newPassword = "ChiemQuyen@1" });
        crossTenant.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await LoginOkAsync("staff@u10owna.test", "NhanVien@2");
    }

    [Fact]
    public async Task ForgotPassword_WhenNotConfigured_Returns503_AndStatusDisabled()
    {
        using var disabled = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["PasswordReset:AdminBaseUrl"] = "" })));
        using var client = disabled.CreateClient();

        var status = await client.GetFromJsonAsync<JsonElement>("/api/auth/password-reset/status");
        status.GetProperty("enabled").GetBoolean().Should().BeFalse();

        var res = await client.PostAsJsonAsync("/api/auth/forgot-password", new { identifier = "ai@u10.test" });
        res.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCodeAsync(res)).Should().Be("PASSWORD_RESET_DISABLED");
    }

    [Fact]
    public async Task Login_MoreThanLimitFromSameIp_Returns429()
    {
        using var limited = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:AuthLoginPerMinute"] = "3" }));
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, FixedRemoteIpStartupFilter>());
        });
        using var client = limited.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            (await client.PostAsJsonAsync("/api/auth/login", new { identifier = "ai@u10.test", password = "x" }))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var blocked = await client.PostAsJsonAsync("/api/auth/login", new { identifier = "ai@u10.test", password = "x" });
        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await ErrorCodeAsync(blocked)).Should().Be("TOO_MANY_REQUESTS");
    }

    /// <summary>TestServer không có IP client; gán IP cố định để policy rate limit theo IP có hiệu lực.</summary>
    private sealed class FixedRemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
