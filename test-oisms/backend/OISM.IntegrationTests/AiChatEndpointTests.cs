using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Ai.Dtos;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;

namespace OISM.IntegrationTests;

public sealed class AiChatEndpointTests
{
    private const string Path = "/api/ai/chat";
    private static AiChatRequest Request(string message = "Cách tạo phiếu nhập hàng?") => new([new("user", message)]);

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Cashier", HttpStatusCode.Forbidden)]
    [InlineData("Owner", HttpStatusCode.OK)]
    [InlineData("Staff", HttpStatusCode.OK)]
    public async Task Chat_UsesJwtRoles(string? role, HttpStatusCode expected)
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory, role);
        using var response = await client.PostAsJsonAsync(Path, Request());

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            var result = await response.Content.ReadFromJsonAsync<AiChatResponse>();
            Assert.Equal("Mở Kho & sổ kho.", result?.Reply);
            Assert.Empty(result!.UsedTools);
            Assert.Null(result.DateRange);
            Assert.Null(result.Scope);
            Assert.Empty(result.BranchChoices);
            Assert.NotNull(result.Sources);
            Assert.NotEmpty(result.Sources);
        }
    }

    [Fact]
    public async Task Chat_ReturnsProblemDetailsWhenDisabled()
    {
        await using var factory = CreateFactory(useFake: false);
        using var client = CreateClient(factory, "Owner");
        using var response = await client.PostAsJsonAsync(Path, Request());

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "AI_DISABLED");
    }

    [Fact]
    public async Task Chat_RejectsLongMessagesAndMalformedJson()
    {
        await using var factory = CreateFactory();
        using var client = CreateClient(factory, "Staff");
        using var longResponse = await client.PostAsJsonAsync(Path, Request(new string('a', 1001)));
        await AssertProblemAsync(longResponse, HttpStatusCode.BadRequest, "AI_INVALID_REQUEST");

        using var malformed = new StringContent("""{"branchId":"not-a-guid"}""", Encoding.UTF8, "application/json");
        using var malformedResponse = await client.PostAsync(Path, malformed);
        await AssertProblemAsync(malformedResponse, HttpStatusCode.BadRequest, "AI_INVALID_REQUEST");
    }

    [Fact]
    public async Task Chat_LimitsRequestsPerJwtUser()
    {
        await using var factory = CreateFactory();
        using var owner = CreateClient(factory, "Owner");
        using var staff = CreateClient(factory, "Staff");
        for (var i = 0; i < 10; i++)
        {
            using var response = await owner.PostAsJsonAsync(Path, Request());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var limited = await owner.PostAsJsonAsync(Path, Request());
        await AssertProblemAsync(limited, HttpStatusCode.TooManyRequests, "AI_RATE_LIMITED");
        using var otherUser = await staff.PostAsJsonAsync(Path, Request());
        Assert.Equal(HttpStatusCode.OK, otherUser.StatusCode);
    }

    [Theory]
    [InlineData("quan 1", "Quận 1")]
    [InlineData("Q.1", null)]
    public async Task Chat_ReturnsScopeOrBranchChoices(string branchName, string? expectedScope)
    {
        var tenantId = Guid.NewGuid();
        var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Quận 1", IsActive = true };
        await using var factory = CreateFactory(branchName: branchName);
        using var client = CreateClient(factory, "Staff", tenantId);
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ICurrentTenantService>().SetTenant(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Branches.Add(branch);
            await db.SaveChangesAsync();
        }

        using var response = await client.PostAsJsonAsync(Path, Request("Tồn kho thấp ở chi nhánh?"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedScope, json.RootElement.GetProperty("scope").GetString());
        var choices = json.RootElement.GetProperty("branchChoices");
        if (expectedScope is null)
        {
            var choice = Assert.Single(choices.EnumerateArray());
            Assert.Equal(branch.Id, choice.GetProperty("id").GetGuid());
            Assert.Equal(branch.Name, choice.GetProperty("name").GetString());
        }
        else
        {
            Assert.Equal(0, choices.GetArrayLength());
        }
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Cashier", HttpStatusCode.Forbidden)]
    [InlineData("Staff", HttpStatusCode.OK)]
    [InlineData("Owner", HttpStatusCode.OK)]
    public async Task Models_ListsOnlyConfiguredProvidersForOwnerAndStaff(string? role, HttpStatusCode expected)
    {
        await using var factory = new CustomWebApplicationFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:Enabled"] = "true",
                ["Ai:ApiKey"] = "",
                ["Ai:Providers:gemini:Label"] = "Google Gemini 3.5 Flash",
                ["Ai:Providers:gemini:BaseUrl"] = "https://gemini.invalid/openai",
                ["Ai:Providers:gemini:Model"] = "gemini-3.5-flash",
                ["Ai:Providers:gemini:ApiKey"] = "integration-placeholder",
                ["Database:SeedDemoData"] = "false"
            })));
        using var client = CreateClient(factory, role);

        var response = await client.GetAsync("/api/ai/models");

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            var body = await response.Content.ReadAsStringAsync();
            var model = Assert.Single(JsonDocument.Parse(body).RootElement.EnumerateArray());
            Assert.Equal("gemini", model.GetProperty("id").GetString());
            Assert.Equal("Google Gemini 3.5 Flash", model.GetProperty("label").GetString());
            Assert.True(model.GetProperty("isDefault").GetBoolean());
            Assert.DoesNotContain("integration-placeholder", body);
            Assert.DoesNotContain("gemini.invalid", body);
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(bool useFake = true, string? branchName = null) =>
        new CustomWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:Enabled"] = "false",
                ["Database:SeedDemoData"] = "false"
            }));
            if (useFake)
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ILlmClient>();
                    services.AddSingleton<ILlmClient>(new FakeLlmClient(branchName));
                });
            }
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, string? role, Guid? tenantId = null)
    {
        var client = factory.CreateClient();
        if (role is not null)
        {
            var user = new User { Id = Guid.NewGuid(), TenantId = tenantId ?? Guid.NewGuid(), Role = role };
            using var scope = factory.Services.CreateScope();
            var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().GenerateAccessToken(user, [role]);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        }
        return client;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string errorCode)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(errorCode, problem.GetProperty("errorCode").GetString());
    }

    private sealed class FakeLlmClient(string? branchName) : ILlmClient
    {
        public Task<LlmResponse> ChatAsync(LlmRequest request, CancellationToken ct = default) =>
            Task.FromResult(branchName is not null && request.Messages[^1].Role == "user"
                ? new LlmResponse(null, [new("branch", "get_low_stock", JsonSerializer.Serialize(new { branchName }))], "tool_calls", null)
                : new LlmResponse("Mở Kho & sổ kho.", [], "stop", null));
    }
}
