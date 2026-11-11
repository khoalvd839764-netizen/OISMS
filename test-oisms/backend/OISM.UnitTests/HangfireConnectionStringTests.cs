using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Npgsql;
using OISM.Infrastructure.Jobs;
using Xunit;

namespace OISM.UnitTests;

/// <summary>
/// Kiểm thử chọn chuỗi kết nối cho storage Hangfire:
/// 1. HangfireConnection đặt riêng thì dùng nguyên chuỗi đó.
/// 2. Không có HangfireConnection thì dùng cùng nguồn với AppDbContext: Database:* được ưu tiên hơn DefaultConnection mẫu.
/// 3. Chỉ có DefaultConnection thì dùng DefaultConnection.
/// 4. Thiếu hết hoặc trỏ tài khoản quản trị thì ném InvalidOperationException, không fallback.
/// </summary>
public class HangfireConnectionStringTests
{
    private const string SampleDefaultConnection =
        "Host=localhost;Port=5432;Database=oisms;Username=oisms_app;Password=your_oisms_runtime_password_here";

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void DedicatedHangfireConnection_IsUsedAsIs()
    {
        const string dedicated = "Host=jobs-db;Port=5433;Database=oisms_jobs;Username=oisms_app;Password=secret";
        var config = Config(new()
        {
            ["ConnectionStrings:HangfireConnection"] = dedicated,
            ["Database:Host"] = "postgres",
            ["ConnectionStrings:DefaultConnection"] = SampleDefaultConnection
        });

        HangfireConfigurationExtensions.ResolveConnectionString(config).Should().Be(dedicated);
    }

    [Fact]
    public void DatabaseSection_WinsOverSampleDefaultConnection_LikeDockerCompose()
    {
        var config = Config(new()
        {
            ["Database:Host"] = "postgres",
            ["Database:Port"] = "5432",
            ["Database:Database"] = "oisms",
            ["Database:Username"] = "oisms_app",
            ["Database:Password"] = "p@ss;word=1",
            ["ConnectionStrings:DefaultConnection"] = SampleDefaultConnection
        });

        var builder = new NpgsqlConnectionStringBuilder(HangfireConfigurationExtensions.ResolveConnectionString(config));

        builder.Host.Should().Be("postgres", "container chỉ cấu hình Database__*, không được rơi về localhost của appsettings.json");
        builder.Database.Should().Be("oisms");
        builder.Username.Should().Be("oisms_app");
        builder.Password.Should().Be("p@ss;word=1", "mật khẩu có ký tự đặc biệt phải được giữ nguyên");
    }

    [Fact]
    public void OnlyDefaultConnection_IsUsed()
    {
        var config = Config(new() { ["ConnectionStrings:DefaultConnection"] = SampleDefaultConnection });

        var builder = new NpgsqlConnectionStringBuilder(HangfireConfigurationExtensions.ResolveConnectionString(config));

        builder.Host.Should().Be("localhost");
        builder.Username.Should().Be("oisms_app");
    }

    [Fact]
    public void MissingAllSettings_ThrowsInvalidOperation()
    {
        var act = () => HangfireConfigurationExtensions.ResolveConnectionString(Config(new()));

        act.Should().Throw<InvalidOperationException>().WithMessage("*missing or unconfigured*");
    }

    [Fact]
    public void AdminAccountWithoutDedicatedConnection_IsRejected()
    {
        var config = Config(new()
        {
            ["Database:Host"] = "postgres",
            ["Database:Username"] = "postgres",
            ["Database:Password"] = "x"
        });

        var act = () => HangfireConfigurationExtensions.ResolveConnectionString(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Security violation*");
    }
}
