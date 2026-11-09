using OISM.Application.Features.Inventory.Services;
using Xunit;

namespace OISM.UnitTests;

public class WacCalculatorTests
{
    private readonly IWacCalculator _calculator = new WacCalculator();

    /// <summary>
    /// Test bắt buộc theo đặc tả BE1-07:
    /// Tồn 10 × 100.000đ, nhập 20 × 130.000đ → WAC mới = 120.000đ.
    /// NewCost = (10 * 100.000 + 20 * 130.000) / (10 + 20) = (1.000.000 + 2.600.000) / 30 = 120.000.
    /// </summary>
    [Fact]
    public void CalculateWac_MandatorySpecificationCase_Returns120000()
    {
        // Arrange
        decimal oldStock = 10m;
        decimal oldCost = 100000m;
        decimal importQty = 20m;
        decimal importPrice = 130000m;

        // Act
        var newCost = _calculator.CalculateWac(oldStock, oldCost, importQty, importPrice);

        // Assert
        Assert.Equal(120000m, newCost);
    }

    /// <summary>
    /// Kiểm tra trường hợp biên: Tồn ban đầu bằng 0.
    /// WAC mới phải bằng chính đơn giá nhập.
    /// </summary>
    [Fact]
    public void CalculateWac_InitialStockZero_ReturnsImportPrice()
    {
        // Arrange
        decimal oldStock = 0m;
        decimal oldCost = 0m;
        decimal importQty = 50m;
        decimal importPrice = 250000.50m;

        // Act
        var newCost = _calculator.CalculateWac(oldStock, oldCost, importQty, importPrice);

        // Assert
        Assert.Equal(250000.50m, newCost);
    }

    /// <summary>
    /// Kiểm tra số lượng lẻ (3 chữ số thập phân - numeric(18,3)) và đơn giá 6 chữ số thập phân (numeric(20,6)).
    /// </summary>
    [Fact]
    public void CalculateWac_FractionalQuantitiesAndPrices_CalculatesCorrectPrecision()
    {
        // Arrange
        decimal oldStock = 1.250m;
        decimal oldCost = 85000.123456m;
        decimal importQty = 2.750m;
        decimal importPrice = 115000.654321m;

        // Expected: (1.250 * 85000.123456 + 2.750 * 115000.654321) / 4.000
        // = (106250.15432 + 316251.79938275) / 4 = 422501.95370275 / 4 = 105625.4884256875 -> round 6: 105625.488426
        var expected = 105625.488426m;

        // Act
        var newCost = _calculator.CalculateWac(oldStock, oldCost, importQty, importPrice);

        // Assert
        Assert.Equal(expected, newCost);
    }

    /// <summary>
    /// Kiểm tra hàng nhập giá 0đ (quà tặng / khuyến mại / bù hàng).
    /// WAC phải giảm tương ứng theo tổng số lượng tồn.
    /// </summary>
    [Fact]
    public void CalculateWac_ZeroImportPrice_DilutesAverageCost()
    {
        // Arrange: Tồn 10 cái x 100.000đ = 1.000.000đ. Nhập thêm 10 cái x 0đ.
        // Tồn mới: 20 cái. Tổng giá trị: 1.000.000đ. WAC mới = 50.000đ.
        decimal oldStock = 10m;
        decimal oldCost = 100000m;
        decimal importQty = 10m;
        decimal importPrice = 0m;

        // Act
        var newCost = _calculator.CalculateWac(oldStock, oldCost, importQty, importPrice);

        // Assert
        Assert.Equal(50000m, newCost);
    }

    /// <summary>
    /// Kiểm tra validation: Số lượng nhập <= 0 phải quăng ArgumentOutOfRangeException.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.001)]
    public void CalculateWac_InvalidImportQuantity_ThrowsArgumentOutOfRangeException(decimal invalidQty)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _calculator.CalculateWac(10m, 100000m, invalidQty, 50000m));
    }

    /// <summary>
    /// Kiểm tra validation: Đơn giá nhập < 0 phải quăng ArgumentOutOfRangeException.
    /// </summary>
    [Fact]
    public void CalculateWac_NegativeImportPrice_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _calculator.CalculateWac(10m, 100000m, 5m, -100m));
    }
}
