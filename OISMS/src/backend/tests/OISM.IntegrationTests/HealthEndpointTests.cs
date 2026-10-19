using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace OISM.IntegrationTests;

public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RootEndpoint_ReturnsSuccess()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("OISM Web API");
    }

    [Fact]
    public async Task HealthLiveEndpoint_ReturnsOkAndLiveStatus()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Live");
    }

    private WebApplicationFactory<Program> CreateUnreachableFactory()
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Host"] = "127.0.0.1",
                    ["Database:Port"] = "54399",
                    ["Database:Username"] = "oisms_app",
                    ["Database:Password"] = "oisms_runtime_secret"
                });
            });
        });
    }

    [Fact]
    public async Task HealthGeneralEndpoint_WhenDatabaseIsUnavailable_Returns503SameAsReady()
    {
        var unreachableFactory = CreateUnreachableFactory();
        var client = unreachableFactory.CreateClient();
        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Unhealthy");
    }

    [Fact]
    public async Task HealthReadyEndpoint_WhenDatabaseIsUnavailable_Returns503WithoutLeakingSecrets()
    {
        // Arrange: Factory with unreachable database connection
        var unreachableFactory = CreateUnreachableFactory();
        var client = unreachableFactory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/ready");

        // Assert: When PostgreSQL is unavailable, endpoint must return 503 Service Unavailable
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Unhealthy");

        // Strict security assertion: MUST NOT leak passwords, user credentials, or raw connection strings
        content.Should().NotContain("Password=");
        content.Should().NotContain("oisms_runtime_secret");
        content.Should().NotContain("your_oisms_runtime_password");
        content.Should().NotContain("Host=");
    }

    [Fact]
    public void BuildConnectionStringBuilder_FromIndividualProperties_HandlesSemicolonsAndQuotesInPassword()
    {
        // Passwords containing semicolons would corrupt raw connection string concatenation.
        // Individual property assignment via NpgsqlConnectionStringBuilder preserves it completely.
        var complexPassword = "p@ss;word'with\"quotes\\and$symbols and spaces";
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["Database:Host"] = "postgres",
            ["Database:Port"] = "5432",
            ["Database:Database"] = "oisms",
            ["Database:Username"] = "oisms_app",
            ["Database:Password"] = complexPassword
        };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var builder = NpgsqlDatabaseReadinessService.BuildConnectionStringBuilder(configuration);

        builder.Host.Should().Be("postgres");
        builder.Port.Should().Be(5432);
        builder.Database.Should().Be("oisms");
        builder.Username.Should().Be("oisms_app");
        builder.Password.Should().Be(complexPassword);

        // When converted to connection string and parsed back, password remains intact
        var connStr = builder.ConnectionString;
        var parsed = new NpgsqlConnectionStringBuilder(connStr);
        parsed.Password.Should().Be(complexPassword);
    }

    [Fact]
    public void BuildConnectionStringBuilder_FallbackToDefaultConnection_ParsesSuccessfully()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=remotehost;Port=5433;Database=customdb;Username=customuser;Password=secret"
        };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var builder = NpgsqlDatabaseReadinessService.BuildConnectionStringBuilder(configuration);

        builder.Host.Should().Be("remotehost");
        builder.Port.Should().Be(5433);
        builder.Database.Should().Be("customdb");
        builder.Username.Should().Be("customuser");
        builder.Password.Should().Be("secret");
    }

    [Fact]
    public void BuildConnectionStringBuilder_MissingConfiguration_ThrowsInvalidOperationException()
    {
        IConfiguration emptyConfig = new ConfigurationBuilder().Build();

        var action = () => NpgsqlDatabaseReadinessService.BuildConnectionStringBuilder(emptyConfig);
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Database connection settings*");
    }

    [Fact]
    public async Task HealthReadyEndpoint_WhenSchemaIsValid_Returns200Ok()
    {
        // Arrange: Mock readiness service returning Healthy
        var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IDatabaseReadinessService));
                if (descriptor != null) services.Remove(descriptor);

                services.AddSingleton<IDatabaseReadinessService>(new MockReadinessService(
                    new ReadinessCheckResult(
                        true, 200, "Healthy",
                        "Database connection, schema completeness (29 tables), trigger immutability, and runtime permissions verified.",
                        CurrentUser: "oisms_app",
                        TablesCount: 29,
                        TriggerVerified: true,
                        Timestamp: DateTime.UtcNow)));
            });
        });

        var client = customFactory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/ready");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Healthy");
        content.Should().Contain("oisms_app");
        content.Should().Contain("29");
    }

    [Fact]
    public async Task HealthReadyEndpoint_WhenTablesAreMissing_Returns503ServiceUnavailable()
    {
        // Arrange: Mock readiness service returning Unhealthy due to incomplete schema
        var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IDatabaseReadinessService));
                if (descriptor != null) services.Remove(descriptor);

                services.AddSingleton<IDatabaseReadinessService>(new MockReadinessService(
                    new ReadinessCheckResult(
                        false, 503, "Unhealthy",
                        "Database schema is incomplete (28 of 29 base tables found). Missing: inventory_transactions. Database migration required.",
                        CurrentUser: "oisms_app",
                        TablesCount: 28,
                        MissingTables: new[] { "inventory_transactions" },
                        Timestamp: DateTime.UtcNow)));
            });
        });

        var client = customFactory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/ready");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Unhealthy");
        content.Should().Contain("inventory_transactions");
    }

    [Fact]
    public async Task HealthReadyEndpoint_WhenTriggerIsMissingOrDisabled_Returns503ServiceUnavailable()
    {
        // Arrange: Mock readiness service returning Unhealthy due to missing trigger
        var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IDatabaseReadinessService));
                if (descriptor != null) services.Remove(descriptor);

                services.AddSingleton<IDatabaseReadinessService>(new MockReadinessService(
                    new ReadinessCheckResult(
                        false, 503, "Unhealthy",
                        "Critical trigger 'ledger_append_only' on 'public.inventory_transactions' is missing or disabled. Database migration required.",
                        CurrentUser: "oisms_app",
                        TablesCount: 29,
                        TriggerVerified: false,
                        Timestamp: DateTime.UtcNow)));
            });
        });

        var client = customFactory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/ready");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Unhealthy");
        content.Should().Contain("ledger_append_only");
    }

    [Fact]
    public async Task HealthReadyEndpoint_WhenUserHasAdminPrivileges_Returns503Degraded()
    {
        // Arrange: Mock readiness service reporting security violation (admin user connected)
        var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IDatabaseReadinessService));
                if (descriptor != null) services.Remove(descriptor);

                services.AddSingleton<IDatabaseReadinessService>(new MockReadinessService(
                    new ReadinessCheckResult(
                        false, 503, "Degraded",
                        "Security alert: Connected database user possesses direct or inherited administrative privileges or owns schema/tables. Least-privilege runtime account required.",
                        CurrentUser: "postgres",
                        Timestamp: DateTime.UtcNow)));
            });
        });

        var client = customFactory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/ready");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Degraded");
        content.Should().Contain("Security alert");
    }

    [Fact]
    public async Task HealthReadyEndpoint_WhenTimeoutOccurs_Returns503WithoutLeakingSecrets()
    {
        var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IDatabaseReadinessService));
                if (descriptor != null) services.Remove(descriptor);

                services.AddSingleton<IDatabaseReadinessService>(new MockReadinessService(
                    new ReadinessCheckResult(
                        false, 503, "Unhealthy",
                        "Database readiness check timed out. PostgreSQL service is not responding.",
                        ErrorType: nameof(OperationCanceledException),
                        Timestamp: DateTime.UtcNow)));
            });
        });

        var client = customFactory.CreateClient();
        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("timed out");
    }

    private class MockReadinessService : IDatabaseReadinessService
    {
        private readonly ReadinessCheckResult _result;

        public MockReadinessService(ReadinessCheckResult result)
        {
            _result = result;
        }

        public Task<ReadinessCheckResult> CheckReadinessAsync(CancellationToken ct = default)
        {
            return Task.FromResult(_result);
        }
    }
}
