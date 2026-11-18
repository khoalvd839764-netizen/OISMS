using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Npgsql;
using OISM.Application.Features.Auth.PasswordReset;
using OISM.Application.Features.Auth.Welcome;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Upgrade 11 trên PostgreSQL thật: nhiều request đăng ký cùng email chạy đồng thời,
/// transaction thua ràng buộc unique bị rollback nên không phát sinh mail chào mừng.
/// </summary>
public class PostgresOwnerWelcomeIntegrationTests : IAsyncLifetime, IClassFixture<PostgresPasswordResetWebApplicationFactory>
{
    private readonly PostgresPasswordResetWebApplicationFactory _factory;

    public PostgresOwnerWelcomeIntegrationTests(PostgresPasswordResetWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public Task InitializeAsync() => PostgresTestEnvironment.ResetTestDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<long> CountAsync(string sql, string value)
    {
        await using var connection = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("value", value);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<HttpResponseMessage> RegisterAsync(int index, string email)
    {
        using var client = _factory.CreateClient();
        return await client.PostAsJsonAsync("/api/auth/register-tenant", new
        {
            tenantName = $"Cửa hàng đồng thời {index}",
            tenantCode = $"U11PG{index}",
            fullName = "Chủ cửa hàng",
            email,
            password = "Password@123"
        });
    }

    [Fact]
    public async Task Postgres_ConcurrentRegisterSameEmail_OneWins_AndExactlyOneWelcomeMail()
    {
        const string email = "owner@u11concurrent.test";

        var responses = await Task.WhenAll(Enumerable.Range(1, 8).Select(i => RegisterAsync(i, email)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.Created)
            .Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);

        (await CountAsync("SELECT count(*) FROM users WHERE email = @value", email)).Should().Be(1);
        (await CountAsync("SELECT count(*) FROM tenants WHERE name LIKE @value", "Cửa hàng đồng thời %"))
            .Should().Be(1, "transaction thua phải rollback cả tenant vừa tạo");

        // Hàng đợi gửi tuần tự: thư đặt lại đến nơi nghĩa là mọi thư xếp trước nó đã được xử lý.
        using var client = _factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/forgot-password", new { identifier = email }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        await _factory.Email.WaitForAsync(email, subject: PasswordResetEmail.Subject);

        _factory.Email.Messages.Count(m => m.To == email && m.Subject == OwnerWelcomeEmail.Subject).Should().Be(1);
    }
}
