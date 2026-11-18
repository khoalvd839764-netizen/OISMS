using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.PasswordReset;
using OISM.Application.Features.Auth.Welcome;
using OISM.Infrastructure.Services.Email;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Upgrade 11 trên EF InMemory: chỉ Owner tự đăng ký nhận mail chào mừng, lỗi gửi mail không ảnh hưởng đăng ký.
/// Đăng ký đồng thời kiểm chứng trên PostgreSQL ở <see cref="PostgresOwnerWelcomeIntegrationTests"/>.
/// </summary>
public class OwnerWelcomeIntegrationTests : IClassFixture<PasswordResetWebApplicationFactory>
{
    private const string Password = "Password@123";
    private readonly PasswordResetWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public OwnerWelcomeIntegrationTests(PasswordResetWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static object RegisterBody(string code, string email) => new
    {
        tenantName = "Cửa hàng " + code,
        tenantCode = code,
        fullName = "Chủ " + code,
        email,
        password = Password
    };

    /// <summary>Hàng đợi gửi tuần tự: thư đặt lại đến nơi nghĩa là mọi thư xếp trước nó đã được xử lý.</summary>
    private async Task DrainQueueAsync(string email)
    {
        var skip = _factory.Email.Messages.Count(m => m.To == email && m.Subject == PasswordResetEmail.Subject);
        (await _client.PostAsJsonAsync("/api/auth/forgot-password", new { identifier = email }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        await _factory.Email.WaitForAsync(email, skip, PasswordResetEmail.Subject);
    }

    private int WelcomeCount(string email) =>
        _factory.Email.Messages.Count(m => m.To == email && m.Subject == OwnerWelcomeEmail.Subject);

    [Fact]
    public async Task Register_SendsExactlyOneWelcomeMail_WithoutPassword()
    {
        const string email = "owner@u11once.test";

        var res = await _client.PostAsJsonAsync("/api/auth/register-tenant", RegisterBody("U11ONCE", email));

        res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        var mail = await _factory.Email.WaitForAsync(email, subject: OwnerWelcomeEmail.Subject);
        mail.TextBody.Should().Contain("Cửa hàng U11ONCE").And.Contain("http://localhost:5173/login");
        mail.HtmlBody.Should().NotContain(Password);
        mail.TextBody.Should().NotContain(Password);

        await DrainQueueAsync(email);
        WelcomeCount(email).Should().Be(1);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409_AndSendsNoSecondMail()
    {
        const string email = "owner@u11dup.test";
        (await _client.PostAsJsonAsync("/api/auth/register-tenant", RegisterBody("U11DUP", email)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicate = await _client.PostAsJsonAsync("/api/auth/register-tenant", RegisterBody("U11DUP2", email));

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await DrainQueueAsync(email);
        WelcomeCount(email).Should().Be(1);
    }

    [Theory]
    [InlineData("Staff")]
    [InlineData("Cashier")]
    public async Task OwnerCreatesEmployee_EmployeeReceivesNoWelcomeMail(string role)
    {
        var code = "U11" + role.ToUpperInvariant();
        var ownerEmail = $"owner@{code.ToLowerInvariant()}.test";
        var employeeEmail = $"employee@{code.ToLowerInvariant()}.test";
        (await _client.PostAsJsonAsync("/api/auth/register-tenant", RegisterBody(code, ownerEmail)))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { identifier = ownerEmail, password = Password });
        using var loginDoc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = loginDoc.RootElement.GetProperty("accessToken").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users")
        {
            Content = JsonContent.Create(new { email = employeeEmail, displayName = "Nhân viên", password = Password, role })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var created = await _client.SendAsync(request);

        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        await DrainQueueAsync(employeeEmail);
        WelcomeCount(employeeEmail).Should().Be(0);
    }

    [Fact]
    public async Task Register_WhenTransportFails_StillReturns201()
    {
        using var failing = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IEmailTransport, ThrowingEmailTransport>()));
        using var client = failing.CreateClient();

        var res = await client.PostAsJsonAsync("/api/auth/register-tenant", RegisterBody("U11FAIL", "owner@u11fail.test"));

        res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Register_MoreThanLimitFromSameIp_Returns429()
    {
        using var limited = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:RegisterTenantPer15Minutes"] = "2" }));
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, FixedRemoteIpStartupFilter>());
        });
        using var client = limited.CreateClient();

        for (var i = 0; i < 2; i++)
        {
            (await client.PostAsJsonAsync("/api/auth/register-tenant", RegisterBody($"U11RATE{i}", $"owner{i}@u11rate.test")))
                .StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var blocked = await client.PostAsJsonAsync("/api/auth/register-tenant", RegisterBody("U11RATE9", "owner9@u11rate.test"));

        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        using var doc = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("TOO_MANY_REQUESTS");
    }

    private sealed class ThrowingEmailTransport : IEmailTransport
    {
        public Task SendAsync(EmailMessage message, CancellationToken ct) =>
            throw new InvalidOperationException("SMTP không phản hồi.");
    }

    /// <summary>TestServer không có IP client; gán IP cố định để policy rate limit theo IP có hiệu lực.</summary>
    private sealed class FixedRemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.11");
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
