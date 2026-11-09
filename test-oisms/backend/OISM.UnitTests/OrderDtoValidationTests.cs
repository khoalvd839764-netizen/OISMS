using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using OISM.Application.Common.Attributes;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Realtime.Dtos;
using OISM.Application.Features.StockCounts.Dtos;
using OISM.Application.Features.StockTransfers.Dtos;
using Xunit;

namespace OISM.UnitTests;

public class OrderDtoValidationTests
{
    static OrderDtoValidationTests()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
    }

    private static List<ValidationResult> ValidateModel(object model)
    {
        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(model, serviceProvider: null, items: null);
        Validator.TryValidateObject(model, context, validationResults, validateAllProperties: true);
        return validationResults;
    }

    [Fact]
    public void NotEmptyGuidAttribute_ShouldPass_ForValidGuid()
    {
        var attribute = new NotEmptyGuidAttribute();
        var validGuid = Guid.NewGuid();

        attribute.IsValid(validGuid).Should().BeTrue();
        attribute.IsValid(validGuid.ToString()).Should().BeTrue();
    }

    [Fact]
    public void NotEmptyGuidAttribute_ShouldFail_ForEmptyGuidOrNull()
    {
        var attribute = new NotEmptyGuidAttribute();

        attribute.IsValid(Guid.Empty).Should().BeFalse();
        attribute.IsValid(Guid.Empty.ToString()).Should().BeFalse();
        attribute.IsValid("00000000-0000-0000-0000-000000000000").Should().BeFalse();
        attribute.IsValid(null).Should().BeFalse();
        attribute.IsValid("not-a-guid").Should().BeFalse();
    }

    [Fact]
    public void CreateOrderRequestDto_RequestKey_MustNotDefaultToNewGuid()
    {
        var dto = new CreateOrderRequestDto();
        dto.RequestKey.Should().Be(Guid.Empty, "RequestKey must not be pre-populated by backend to guarantee client-driven idempotency");
    }

    [Fact]
    public void CreateOrderRequestDto_Validation_ShouldFail_WhenRequestKeyOrBranchIdIsEmpty()
    {
        var dto = new CreateOrderRequestDto
        {
            BranchId = Guid.Empty, // Invalid
            Channel = "Admin",
            RequestKey = Guid.Empty, // Invalid
            Items = new List<OrderItemRequestDto>
            {
                new()
                {
                    SkuId = Guid.NewGuid(),
                    SkuCode = "SKU-001",
                    Quantity = 2,
                    UnitPrice = 50000
                }
            }
        };

        var errors = ValidateModel(dto);
        errors.Should().Contain(e => e.MemberNames.Contains("BranchId"));
        errors.Should().Contain(e => e.MemberNames.Contains("RequestKey"));
    }

    [Fact]
    public void PosCheckoutRequestDto_Validation_ShouldFail_WhenRequestKeyOrBranchIdIsEmpty()
    {
        var dto = new PosCheckoutRequestDto
        {
            BranchId = Guid.Empty, // Invalid
            Channel = "POS",
            PaymentMethod = "Cash",
            RequestKey = Guid.Empty, // Invalid
            Items = new List<OrderItemRequestDto>
            {
                new()
                {
                    SkuId = Guid.NewGuid(),
                    SkuCode = "SKU-001",
                    Quantity = 1,
                    UnitPrice = 100000
                }
            }
        };

        var errors = ValidateModel(dto);
        errors.Should().Contain(e => e.MemberNames.Contains("BranchId"));
        errors.Should().Contain(e => e.MemberNames.Contains("RequestKey"));
    }

    [Fact]
    public void OrderItemRequestDto_Validation_ShouldFail_WhenSkuIdIsEmpty()
    {
        var item = new OrderItemRequestDto
        {
            SkuId = Guid.Empty, // Invalid
            SkuCode = "SKU-001",
            Quantity = 1,
            UnitPrice = 25000
        };

        var errors = ValidateModel(item);
        errors.Should().Contain(e => e.MemberNames.Contains("SkuId"));
    }

    [Fact]
    public void PosCheckoutResponseDto_Serialization_MustNeverContainSensitiveCostOrCogsFields()
    {
        // Arrange
        var receipt = new PosReceiptDto
        {
            OrderId = Guid.NewGuid(),
            ReceiptNumber = "HD-20261024-001",
            OrderNumber = "ORD-20261024-001",
            BranchId = Guid.NewGuid(),
            BranchName = "Chi nhánh Quận 1",
            CashierName = "Thu ngân A",
            CustomerName = "Nguyễn Văn B",
            CustomerPhone = "0901234567",
            TotalAmount = 250000,
            PaymentMethod = "Cash",
            AmountGiven = 300000,
            ChangeDue = 50000,
            CreatedAt = DateTimeOffset.UtcNow,
            Items = new List<PosReceiptItemDto>
            {
                new()
                {
                    SkuId = Guid.NewGuid(),
                    SkuCode = "TSHIRT-BLK-L",
                    ProductName = "Áo thun Basic",
                    VariantName = "Đen / L",
                    Quantity = 2,
                    UnitPrice = 125000
                }
            }
        };

        var response = new PosCheckoutResponseDto
        {
            Success = true,
            Message = "Thanh toán thành công.",
            OrderId = receipt.OrderId,
            OrderNumber = receipt.OrderNumber,
            TotalAmount = receipt.TotalAmount,
            AmountGiven = receipt.AmountGiven,
            ChangeDue = receipt.ChangeDue,
            PaymentMethod = receipt.PaymentMethod,
            CheckoutAt = DateTimeOffset.UtcNow,
            Receipt = receipt
        };

        // Act
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        // Assert
        json.ToLowerInvariant().Should().NotContain("costprice", "POS receipt must not leak CostPrice to Cashier role");
        json.ToLowerInvariant().Should().NotContain("totalcost", "POS receipt must not leak TotalCost to Cashier role");
        json.ToLowerInvariant().Should().NotContain("totalcogs", "POS receipt must not leak TotalCogs to Cashier role");
        json.ToLowerInvariant().Should().NotContain("cogs", "POS receipt must not leak COGS to Cashier role");
    }

    [Fact]
    public void OrderSummaryRealtimeDto_Serialization_MustNeverContainSensitiveCostOrCogsFields()
    {
        // Arrange
        var realtimePayload = new OrderSummaryRealtimeDto
        {
            OrderId = Guid.NewGuid(),
            OrderNumber = "ORD-20261024-002",
            BranchId = Guid.NewGuid(),
            BranchName = "Chi nhánh Quận 1",
            Channel = "Shopee",
            Status = "Draft",
            TotalAmount = 500000,
            TotalItems = 3,
            CustomerName = "Trần Thị C",
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act
        var json = JsonSerializer.Serialize(realtimePayload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        // Assert
        json.ToLowerInvariant().Should().NotContain("costprice", "Realtime SignalR broadcast must not leak CostPrice");
        json.ToLowerInvariant().Should().NotContain("totalcost", "Realtime SignalR broadcast must not leak TotalCost");
        json.ToLowerInvariant().Should().NotContain("totalcogs", "Realtime SignalR broadcast must not leak TotalCogs");
    }

    [Fact]
    public void StockTransferItemRequestDto_Validation_ShouldFail_WhenSkuIdIsEmpty()
    {
        var item = new StockTransferItemRequestDto
        {
            SkuId = Guid.Empty,
            Quantity = 10
        };

        var errors = ValidateModel(item);
        errors.Should().Contain(e => e.MemberNames.Contains("SkuId"));
    }

    [Fact]
    public void StockCountItemRequestDto_Validation_ShouldFail_WhenSkuIdIsEmpty()
    {
        var item = new RecordStockCountItemRequestDto
        {
            SkuId = Guid.Empty,
            CountedQuantity = 5
        };

        var errors = ValidateModel(item);
        errors.Should().Contain(e => e.MemberNames.Contains("SkuId"));
    }

    [Fact]
    public void PosCheckoutRequestDto_Validation_ShouldFail_WhenAmountGivenExceedsNumeric20_2()
    {
        var dto = new PosCheckoutRequestDto
        {
            BranchId = Guid.NewGuid(),
            RequestKey = Guid.NewGuid(),
            PaymentMethod = "Cash",
            AmountGiven = 1000000000000000000m, // 10^18 exceeds numeric(20,2) max (999,999,999,999,999,999.99m)
            Items = new List<OrderItemRequestDto>
            {
                new() { SkuId = Guid.NewGuid(), SkuCode = "SKU-001", Quantity = 1m, UnitPrice = 100000m }
            }
        };

        var errors = ValidateModel(dto);
        errors.Should().Contain(e => e.MemberNames.Contains("AmountGiven"));
    }

    [Fact]
    public void OrderItemRequestDto_Validation_ShouldFail_WhenQuantityExceedsNumeric18_3()
    {
        var item = new OrderItemRequestDto
        {
            SkuId = Guid.NewGuid(),
            SkuCode = "SKU-001",
            Quantity = 1000000000000000m, // 10^15 exceeds numeric(18,3) max (999,999,999,999,999.999m)
            UnitPrice = 50000m
        };

        var errors = ValidateModel(item);
        errors.Should().Contain(e => e.MemberNames.Contains("Quantity"));
    }

    [Fact]
    public void OrderItemRequestDto_Validation_ShouldFail_WhenUnitPriceExceedsNumeric20_2()
    {
        var item = new OrderItemRequestDto
        {
            SkuId = Guid.NewGuid(),
            SkuCode = "SKU-001",
            Quantity = 1m,
            UnitPrice = 1000000000000000000m // 10^18 exceeds numeric(20,2) max
        };

        var errors = ValidateModel(item);
        errors.Should().Contain(e => e.MemberNames.Contains("UnitPrice"));
    }

    [Fact]
    public void PosCheckoutRequestDto_Validation_ShouldPass_ForValidBoundaryValues()
    {
        var dto = new PosCheckoutRequestDto
        {
            BranchId = Guid.NewGuid(),
            RequestKey = Guid.NewGuid(),
            PaymentMethod = "Cash",
            AmountGiven = 999999999999999999.99m, // Exact max boundary of numeric(20,2)
            Items = new List<OrderItemRequestDto>
            {
                new()
                {
                    SkuId = Guid.NewGuid(),
                    SkuCode = "SKU-001",
                    Quantity = 999999999999999.999m, // Exact max boundary of numeric(18,3)
                    UnitPrice = 999999999999999999.99m // Exact max boundary of numeric(20,2)
                }
            }
        };

        var errors = ValidateModel(dto);
        errors.Should().BeEmpty();
    }
}
