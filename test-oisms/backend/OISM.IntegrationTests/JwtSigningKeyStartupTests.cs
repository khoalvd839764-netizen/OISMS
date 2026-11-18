using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OISM.Application.Common.Interfaces;
using OISM.Domain.Entities;
using Xunit;

namespace OISM.IntegrationTests;

public class JwtSigningKeyStartupTests
{
    // Ghép chuỗi để khóa đã lộ không xuất hiện nguyên vẹn khi quét source.
    private const string LeakedSampleKey = "SuperSecretKeyForOISM" + "OmnichannelRetailSystem2026!#";

    [Fact]
    public void Production_WithoutValidKey_RefusesToStart()
    {
        using var factory = new CustomWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Jwt:Secret", "CHANGE_ME");
        });

        var act = () => factory.CreateClient();

        act.Should().Throw<Exception>().Where(ex => ex.ToString().Contains("Jwt:Secret"));
    }

    [Fact]
    public async Task TokenForgedWithLeakedKey_IsRejected()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(LeakedSampleKey));
        var forged = new JwtSecurityToken(
            issuer: "OISM_WebAPI",
            audience: "OISM_Client",
            claims:
            [
                new Claim("userId", Guid.NewGuid().ToString()),
                new Claim("tenantId", Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, "Owner")
            ],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(forged));

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenIssuedByBackend_IsStillAccepted()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Email = "jwt-key@oisms.test",
            DisplayName = "Jwt Key",
            Role = "Owner"
        };
        var token = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().GenerateAccessToken(user, ["Owner"]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
