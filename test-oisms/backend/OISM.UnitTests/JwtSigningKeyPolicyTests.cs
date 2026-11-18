using System.Text;
using FluentAssertions;
using OISM.Infrastructure.Services;
using Xunit;

namespace OISM.UnitTests;

public class JwtSigningKeyPolicyTests
{
    private const string ValidKey = "0123456789abcdef0123456789abcdef";

    // Ghép chuỗi để khóa đã lộ không xuất hiện nguyên vẹn khi quét source.
    private const string LeakedSampleKey = "SuperSecretKeyForOISM" + "OmnichannelRetailSystem2026!#";

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Development")]
    public void ValidKey_IsUsedAsIs_InEveryEnvironment(string environment)
    {
        var key = JwtSigningKeyPolicy.Resolve(ValidKey, environment);

        key.Bytes.Should().Equal(Encoding.UTF8.GetBytes(ValidKey));
        key.Warning.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CHANGE_ME")]
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData(LeakedSampleKey)]
    public void InvalidKey_StopsStartup_OutsideDevelopmentAndTest(string? configuredKey)
    {
        foreach (var environment in new[] { "Production", "Staging", "Demo", "", null })
        {
            var act = () => JwtSigningKeyPolicy.Resolve(configuredKey, environment);

            act.Should().Throw<InvalidOperationException>().WithMessage("Jwt:Secret*");
        }
    }

    [Theory]
    [InlineData(null, "Development")]
    [InlineData("CHANGE_ME", "Test")]
    [InlineData(LeakedSampleKey, "Testing")]
    [InlineData(LeakedSampleKey, "development")]
    public void InvalidKey_UsesRandomKey_InDevelopmentAndTest(string? configuredKey, string environment)
    {
        var key = JwtSigningKeyPolicy.Resolve(configuredKey, environment);

        key.Bytes.Should().HaveCount(JwtSigningKeyPolicy.MinimumKeyBytes);
        key.Bytes.Should().NotEqual(Encoding.UTF8.GetBytes(LeakedSampleKey));
        key.Warning.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ErrorMessage_DoesNotExposeConfiguredKey()
    {
        var act = () => JwtSigningKeyPolicy.Resolve(LeakedSampleKey, "Production");

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().NotContain(LeakedSampleKey);
    }
}
