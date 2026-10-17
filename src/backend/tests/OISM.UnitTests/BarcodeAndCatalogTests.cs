using FluentAssertions;
using OISM.Application.Common.Helpers;
using OISM.Application.Features.Catalog.Products;
using Xunit;

namespace OISM.UnitTests;

public class BarcodeAndCatalogTests
{
    private readonly CreateVariantValidator _variantValidator = new();

    [Fact]
    public void BarcodeHelper_ShouldGenerate_ValidEan13()
    {
        // Act
        var barcode = BarcodeHelper.GenerateEan13();

        // Assert
        barcode.Should().NotBeNullOrEmpty();
        barcode.Length.Should().Be(13);
        BarcodeHelper.IsValidEan13(barcode).Should().BeTrue();
    }

    [Fact]
    public void BarcodeHelper_ShouldValidate_KnownEan13Barcode()
    {
        // Mã chuẩn Việt Nam kiểm tra hợp lệ
        // 893456789012 + check digit = 8
        var twelveDigits = "893456789012";
        var checkDigit = BarcodeHelper.CalculateEan13CheckDigit(twelveDigits);
        var fullBarcode = twelveDigits + checkDigit;

        // Assert
        BarcodeHelper.IsValidEan13(fullBarcode).Should().BeTrue();
    }

    [Fact]
    public void BarcodeHelper_ShouldReject_WrongCheckDigit()
    {
        // Sai số kiểm tra
        var invalidBarcode = "8934567890129"; // 9 là sai, số đúng phải là khác

        // Assert
        BarcodeHelper.IsValidEan13(invalidBarcode).Should().BeFalse();
    }

    [Fact]
    public void CreateVariantValidator_ShouldPass_WhenInputIsValid()
    {
        // Arrange
        var request = new CreateVariantRequest
        {
            SKU = "AO-THUN-DEN-L",
            VariantName = "Áo thun Đen Size L",
            RetailPrice = 250000,
            WholesalePrice = 180000,
            Color = "Đen",
            Size = "L"
        };

        // Act
        var result = _variantValidator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void CreateVariantValidator_ShouldFail_WhenRetailPriceIsNegative()
    {
        // Arrange
        var request = new CreateVariantRequest
        {
            SKU = "AO-THUN-DEN-L",
            VariantName = "Áo thun Đen Size L",
            RetailPrice = -50000, // Giá âm
            WholesalePrice = 100000
        };

        // Act
        var result = _variantValidator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(request.RetailPrice));
    }

    [Fact]
    public void CreateVariantValidator_ShouldFail_WhenSkuHasSpecialCharacters()
    {
        // Arrange
        var request = new CreateVariantRequest
        {
            SKU = "AO THUN@DEN#L!", // Chứa khoảng trắng và ký tự đặc biệt
            VariantName = "Áo thun Đen Size L",
            RetailPrice = 100000
        };

        // Act
        var result = _variantValidator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(request.SKU));
    }
}
