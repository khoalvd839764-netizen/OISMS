using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.PasswordReset;
using OISM.Infrastructure.Services;
using Xunit;

namespace OISM.UnitTests;

/// <summary>
/// Upgrade 10: định danh đăng nhập email/SĐT, validator và token đặt lại mật khẩu tự ký.
/// </summary>
public class PasswordResetUnitTests
{
    private const string SigningKey = "unit-test-signing-key-0123456789-abcdef";

    [Theory]
    [InlineData("0901234567", LoginIdentifierKind.Phone, "+84901234567")]
    [InlineData("+84 90 123 4567", LoginIdentifierKind.Phone, "+84901234567")]
    [InlineData("84901234567", LoginIdentifierKind.Phone, "+84901234567")]
    [InlineData("090-123-4567", LoginIdentifierKind.Phone, "+84901234567")]
    [InlineData("  Owner@ShopA.Local ", LoginIdentifierKind.Email, "owner@shopa.local")]
    public void LoginIdentifier_Resolve_NormalizesEmailAndPhone(string raw, LoginIdentifierKind kind, string normalized)
    {
        var result = LoginIdentifier.Resolve(raw);

        result.Kind.Should().Be(kind);
        result.Normalized.Should().Be(normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("090abc4567")]
    [InlineData("a@")]
    [InlineData("@b.com")]
    [InlineData("12")]
    public void LoginIdentifier_Resolve_RejectsGarbage(string raw)
    {
        LoginIdentifier.Resolve(raw).IsValid.Should().BeFalse();
    }

    [Fact]
    public void LoginValidator_AcceptsPhoneIdentifier_AndLegacyEmailField()
    {
        var validator = new LoginValidator();

        validator.Validate(new LoginRequest { Identifier = "0901234567", Password = "x" }).IsValid.Should().BeTrue();
        validator.Validate(new LoginRequest { Email = "owner@shopa.local", Password = "x" }).IsValid.Should().BeTrue();
        validator.Validate(new LoginRequest { Identifier = "khong-hop-le", Password = "x" }).IsValid.Should().BeFalse();
        validator.Validate(new LoginRequest { Password = "x" }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validators_EnforcePasswordLengthAndAppValues()
    {
        new ResetPasswordValidator().Validate(new ResetPasswordRequest { Token = "t", NewPassword = "12345" }).IsValid.Should().BeFalse();
        new ResetPasswordValidator().Validate(new ResetPasswordRequest { Token = "t", NewPassword = "123456" }).IsValid.Should().BeTrue();
        new AdminResetPasswordValidator().Validate(new AdminResetPasswordRequest { NewPassword = new string('a', 101) }).IsValid.Should().BeFalse();
        new ForgotPasswordValidator().Validate(new ForgotPasswordRequest { Identifier = "0901234567", App = "pos" }).IsValid.Should().BeTrue();
        new ForgotPasswordValidator().Validate(new ForgotPasswordRequest { Identifier = "0901234567", App = "evil" }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Token_RoundTrip_ReturnsUserExpiryAndMatchesSamePasswordHash()
    {
        var service = CreateService(SigningKey, "Production");
        var userId = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);

        var token = service.CreateToken(userId, "$2a$11$hash-cu", expiresAt);
        var payload = service.ReadToken(token);

        token.Should().MatchRegex("^[A-Za-z0-9_-]+$", "token phải an toàn khi đặt trong URL");
        payload.Should().NotBeNull();
        payload!.UserId.Should().Be(userId);
        payload.ExpiresAt.ToUnixTimeSeconds().Should().Be(expiresAt.ToUnixTimeSeconds());
        service.MatchesPasswordHash(payload, "$2a$11$hash-cu").Should().BeTrue();
        service.MatchesPasswordHash(payload, "$2a$11$hash-moi").Should().BeFalse("đổi mật khẩu xong thì token cũ hết hiệu lực");
    }

    [Fact]
    public void Token_TamperedOrSignedWithOtherKey_IsRejected()
    {
        var service = CreateService(SigningKey, "Production");
        var token = service.CreateToken(Guid.NewGuid(), "hash", DateTimeOffset.UtcNow.AddMinutes(30));

        var tampered = (token[5] == 'A' ? 'B' : 'A') + "";
        service.ReadToken(token[..5] + tampered + token[6..]).Should().BeNull();
        service.ReadToken(token + "A").Should().BeNull();
        service.ReadToken("khong-phai-token").Should().BeNull();
        CreateService(SigningKey + "-khac", "Production").ReadToken(token).Should().BeNull();
    }

    [Fact]
    public void Token_DisabledOutsideDevelopment_WhenKeyMissingOrShort()
    {
        CreateService(null, "Production").IsConfigured.Should().BeFalse();
        CreateService("CHANGE_ME", "Production").IsConfigured.Should().BeFalse();
        CreateService(null, "Development").IsConfigured.Should().BeTrue("máy dev tự sinh khóa tạm");
        CreateService(SigningKey, "Production").IsConfigured.Should().BeTrue();
    }

    private static PasswordResetTokenService CreateService(string? key, string environment) =>
        new(
            Options.Create(new PasswordResetOptions { SigningKey = key, TokenLifetimeMinutes = 30 }),
            new FakeHostEnvironment(environment),
            NullLogger<PasswordResetTokenService>.Instance);

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "OISM.UnitTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
