using FluentAssertions;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.RegisterTenant;
using Xunit;

namespace OISM.UnitTests;

public class AuthValidatorTests
{
    private readonly RegisterTenantValidator _registerValidator = new();
    private readonly LoginValidator _loginValidator = new();

    [Fact]
    public void RegisterTenantValidator_ShouldPass_WhenInputIsValid()
    {
        // Arrange
        var request = new RegisterTenantRequest
        {
            TenantName = "Cửa Hàng Thời Trang ABC",
            TenantCode = "THOITRANG_ABC",
            FullName = "Nguyễn Văn Chủ Shop",
            Email = "chushop@thoitrangabc.com",
            PhoneNumber = "0987654321",
            Password = "Password@123"
        };

        // Act
        var result = _registerValidator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("test@")]
    public void RegisterTenantValidator_ShouldFail_WhenEmailIsInvalid(string invalidEmail)
    {
        // Arrange
        var request = new RegisterTenantRequest
        {
            TenantName = "Shop Test",
            TenantCode = "SHOP_TEST",
            FullName = "Test User",
            Email = invalidEmail,
            Password = "Password@123"
        };

        // Act
        var result = _registerValidator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(request.Email));
    }

    [Fact]
    public void RegisterTenantValidator_ShouldFail_WhenPasswordTooShort()
    {
        // Arrange
        var request = new RegisterTenantRequest
        {
            TenantName = "Shop Test",
            TenantCode = "SHOP_TEST",
            FullName = "Test User",
            Email = "test@shop.com",
            Password = "123" // Dưới 6 ký tự
        };

        // Act
        var result = _registerValidator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(request.Password));
    }

    [Fact]
    public void LoginValidator_ShouldPass_WhenValidEmailAndPassword()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "owner@oism.vn",
            Password = "Password@123"
        };

        // Act
        var result = _loginValidator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void LoginValidator_ShouldFail_WhenPasswordIsEmpty()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "owner@oism.vn",
            Password = ""
        };

        // Act
        var result = _loginValidator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(request.Password));
    }

    [Theory]
    [InlineData("090 123 4567", "+84901234567")]
    [InlineData("0901234567", "+84901234567")]
    [InlineData("090-123-4567", "+84901234567")]
    [InlineData("(028) 3822 1234", "+842838221234")]
    [InlineData("+84 90 123 4567", "+84901234567")]
    [InlineData("+84901234567", "+84901234567")]
    [InlineData("+1 (202) 555-0123", "+12025550123")]
    [InlineData("84901234567", "+84901234567")]
    public void PhoneHelper_ShouldNormalize_ValidPhoneFormats(string raw, string expected)
    {
        var isValid = OISM.Application.Common.Helpers.PhoneHelper.TryNormalizePhoneNumber(raw, out var normalized);
        isValid.Should().BeTrue();
        normalized.Should().Be(expected);
        OISM.Application.Common.Helpers.PhoneHelper.IsValidPhoneNumber(raw).Should().BeTrue();
    }

    [Theory]
    [InlineData("090abc123")]
    [InlineData("123")]
    [InlineData("+0901234567")]
    [InlineData("++84901234567")]
    [InlineData("090!@#1234")]
    [InlineData("12345678901234567")] // Quá 15 chữ số
    public void PhoneHelper_ShouldReject_InvalidPhoneFormats(string invalidPhone)
    {
        var isValid = OISM.Application.Common.Helpers.PhoneHelper.TryNormalizePhoneNumber(invalidPhone, out var normalized);
        isValid.Should().BeFalse();
        normalized.Should().BeNull();
        OISM.Application.Common.Helpers.PhoneHelper.IsValidPhoneNumber(invalidPhone).Should().BeFalse();
    }

    [Theory]
    [InlineData("090 123 4567", true)]
    [InlineData("0901234567", true)]
    [InlineData("+84901234567", true)]
    [InlineData(null, true)] // Optional
    [InlineData("", true)]   // Optional
    [InlineData("   ", true)]// Optional
    [InlineData("090abc", false)]
    [InlineData("123", false)]
    [InlineData("+0901234567", false)]
    public void RegisterTenantValidator_ShouldValidate_PhoneNumber(string? phone, bool shouldBeValid)
    {
        var request = new RegisterTenantRequest
        {
            TenantName = "Shop Phone Test",
            TenantCode = "PHONE_TEST",
            FullName = "Chủ Shop Test",
            Email = "phonetest@shop.com",
            Password = "Password@123",
            PhoneNumber = phone
        };

        var result = _registerValidator.Validate(request);
        result.IsValid.Should().Be(shouldBeValid);
        if (!shouldBeValid)
        {
            result.Errors.Should().Contain(e => e.PropertyName == nameof(request.PhoneNumber));
        }
    }
}
