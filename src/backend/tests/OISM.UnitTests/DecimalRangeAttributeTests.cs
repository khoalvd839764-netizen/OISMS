using System.ComponentModel.DataAnnotations;
using System.Reflection;
using FluentAssertions;
using OISM.Application.Common.Attributes;
using OISM.Application.Features.Webhooks.Dtos;
using Xunit;

namespace OISM.UnitTests;

public class DecimalRangeAttributeTests
{
    [Theory]
    [InlineData("0.001", true)]
    [InlineData("999999999999999.999", true)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    public void IsValid_ShouldIncludeBothBounds(string value, bool expected)
    {
        var attribute = new DecimalRangeAttribute("0.001", "999999999999999.999");

        attribute.IsValid(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture))
            .Should().Be(expected);
    }

    [Fact]
    public void IsValid_ShouldAcceptNull_SoRequiredDecidesMissingValues()
    {
        new DecimalRangeAttribute("0", "1").IsValid(null).Should().BeTrue();
    }

    [Fact]
    public void IsValid_ShouldRejectValueThatCannotBeConverted()
    {
        new DecimalRangeAttribute("0", "1").IsValid("abc").Should().BeFalse();
    }

    [Fact]
    public void GetValidationResult_ShouldReturnConfiguredMessage()
    {
        var attribute = new DecimalRangeAttribute("0", "1") { ErrorMessage = "Giá trị không hợp lệ." };

        var result = attribute.GetValidationResult(2m, new ValidationContext(new object()));

        result!.ErrorMessage.Should().Be("Giá trị không hợp lệ.");
    }

    [Fact]
    public async Task IsValid_ShouldNotThrow_WhenSharedInstanceIsUsedConcurrentlyFromFirstCall()
    {
        for (var round = 0; round < 2000; round++)
        {
            var attribute = new DecimalRangeAttribute("0.001", "999999999999999.999");
            using var barrier = new Barrier(8);

            var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            {
                barrier.SignalAndWait();
                return attribute.IsValid(1m);
            }));

            (await Task.WhenAll(tasks)).Should().OnlyContain(valid => valid);
        }
    }

    [Fact]
    public void ApplicationDtos_ShouldNotUseTypedRangeAttribute()
    {
        var offenders = typeof(CanonicalWebhookItemDto).Assembly.GetTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(property => property.GetCustomAttributes<RangeAttribute>()
                .Any(range => range is not DecimalRangeAttribute && range.OperandType != typeof(int) && range.OperandType != typeof(double)))
            .Select(property => $"{property.DeclaringType!.Name}.{property.Name}")
            .ToList();

        offenders.Should().BeEmpty("RangeAttribute theo kiểu không an toàn luồng ở lần dùng đầu, hãy dùng DecimalRangeAttribute");
    }
}
