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
}
