using System.Text.Json;
using FluentAssertions;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.Realtime.Dtos;
using OISM.Application.Features.Webhooks.Dtos;
using OISM.Application.Features.Webhooks.Services;
using Xunit;

namespace OISM.UnitTests;

/// <summary>
/// Unit tests cho Webhook Signature, Hashing và Realtime Event Payloads (Task S3-BE1-T05-WEBHOOK-REALTIME).
/// </summary>
public class WebhookAndRealtimeUnitTests
{
    #region 1. WebhookCanonicalSignatureHelper Tests

    [Fact]
    public void CanonicalSignature_ItemOrderInvariance_ProducesIdenticalHash()
    {
        var timestamp = new DateTimeOffset(2026, 10, 25, 14, 30, 0, 123, TimeSpan.Zero);

        var payload1 = new CanonicalWebhookPayloadDto
        {
            Channel = "Shopee",
            ExternalShopId = "SHOP_001",
            ExternalOrderId = "ORD_999",
            CustomerName = "Nguyễn Văn A",
            OrderTimestamp = timestamp,
            TotalAmount = 300_000m,
            Items = new List<CanonicalWebhookItemDto>
            {
                new() { SkuCode = "SKU-B", Quantity = 1m, UnitPrice = 100_000m },
                new() { SkuCode = "SKU-A", Quantity = 2m, UnitPrice = 100_000m }
            }
        };

        var payload2 = new CanonicalWebhookPayloadDto
        {
            Channel = "shopee", // Khác case
            ExternalShopId = " SHOP_001 ", // Khoảng trắng
            ExternalOrderId = "ORD_999",
            CustomerName = "Nguyễn Văn A",
            OrderTimestamp = timestamp,
            TotalAmount = 300_000m,
            Items = new List<CanonicalWebhookItemDto>
            {
                new() { SkuCode = "sku-a", Quantity = 2m, UnitPrice = 100_000m },
                new() { SkuCode = "SKU-B", Quantity = 1m, UnitPrice = 100_000m }
            }
        };

        var hash1 = WebhookCanonicalSignatureHelper.ComputeCanonicalSignature(payload1);
        var hash2 = WebhookCanonicalSignatureHelper.ComputeCanonicalSignature(payload2);

        hash1.Should().NotBeNullOrEmpty();
        hash1.Length.Should().Be(64); // SHA-256 hex string
        hash1.Should().Be(hash2, "Chữ ký Canonical phải bất biến đối với thứ tự dòng hàng và chuẩn hóa ký tự.");
    }

    [Fact]
    public void CanonicalSignature_PayloadDifference_ProducesDifferentHash()
    {
        var timestamp = DateTimeOffset.UtcNow;

        var basePayload = new CanonicalWebhookPayloadDto
        {
            Channel = "TikTok",
            ExternalShopId = "SHOP_TK",
            ExternalOrderId = "ORD_TK_1",
            CustomerName = "Trần Thị B",
            OrderTimestamp = timestamp,
            TotalAmount = 200_000m,
            Items = new List<CanonicalWebhookItemDto>
            {
                new() { SkuCode = "SKU-TK-1", Quantity = 2m, UnitPrice = 100_000m }
            }
        };

        var modifiedQty = new CanonicalWebhookPayloadDto
        {
            Channel = basePayload.Channel,
            ExternalShopId = basePayload.ExternalShopId,
            ExternalOrderId = basePayload.ExternalOrderId,
            CustomerName = basePayload.CustomerName,
            OrderTimestamp = basePayload.OrderTimestamp,
            TotalAmount = 300_000m,
            Items = new List<CanonicalWebhookItemDto>
            {
                new() { SkuCode = "SKU-TK-1", Quantity = 3m, UnitPrice = 100_000m }
            }
        };

        var hashBase = WebhookCanonicalSignatureHelper.ComputeCanonicalSignature(basePayload);
        var hashModified = WebhookCanonicalSignatureHelper.ComputeCanonicalSignature(modifiedQty);

        hashBase.Should().NotBe(hashModified, "Thay đổi số lượng hoặc tổng tiền phải làm thay đổi chữ ký Canonical.");
    }

    [Fact]
    public void CanonicalSignature_EmptyItems_ComputesCanonicalSignatureDeterministically()
    {
        var payload = new CanonicalWebhookPayloadDto
        {
            Channel = "Lazada",
            ExternalShopId = "SHOP_LZ",
            ExternalOrderId = "ORD_LZ_1",
            Items = new List<CanonicalWebhookItemDto>()
        };

        var hash = WebhookCanonicalSignatureHelper.ComputeCanonicalSignature(payload);
        hash.Should().NotBeNullOrEmpty();
        hash.Length.Should().Be(64);
    }

    #endregion

    #region 2. WebhookSecretHasher Tests

    [Fact]
    public void SecretHasher_HashAndVerify_Success()
    {
        var rawSecret = "SuperSecretWebhookKey_2026!@#";
        var hash = WebhookSecretHasher.HashSecret(rawSecret);

        hash.Should().NotBeNullOrEmpty();
        hash.Length.Should().Be(64);
        WebhookSecretHasher.VerifySecret(rawSecret, hash).Should().BeTrue();
    }

    [Fact]
    public void SecretHasher_WrongSecret_ReturnsFalse()
    {
        var rawSecret = "CorrectSecret_123";
        var hash = WebhookSecretHasher.HashSecret(rawSecret);

        WebhookSecretHasher.VerifySecret("WrongSecret_456", hash).Should().BeFalse();
    }

    [Theory]
    [InlineData("", "hash")]
    [InlineData("   ", "hash")]
    [InlineData("secret", "")]
    [InlineData("secret", "   ")]
    [InlineData("secret", "invalid_bcrypt_format")]
    public void SecretHasher_InvalidInputs_ReturnsFalseSafely(string raw, string hash)
    {
        WebhookSecretHasher.VerifySecret(raw, hash).Should().BeFalse();
    }

    #endregion

    #region 3. Realtime Event DTO Cost & Privacy Protection

    [Fact]
    public void RealtimeEventDtos_DoNotExposeCostOrSecrets()
    {
        var forbiddenTerms = new[] { "cost", "cogs", "secret", "purchase", "profit", "margin", "password" };

        // 1. OrderSummaryRealtimeDto
        var orderProps = typeof(OrderSummaryRealtimeDto).GetProperties();
        foreach (var prop in orderProps)
        {
            var propNameLower = prop.Name.ToLowerInvariant();
            forbiddenTerms.Should().NotContain(term => propNameLower.Contains(term),
                $"Sự kiện OrderSummaryRealtimeDto không được chứa thông tin nhạy cảm: {prop.Name}");
        }

        // 2. StockChangedRealtimeDto
        var stockProps = typeof(StockChangedRealtimeDto).GetProperties();
        foreach (var prop in stockProps)
        {
            var propNameLower = prop.Name.ToLowerInvariant();
            forbiddenTerms.Should().NotContain(term => propNameLower.Contains(term),
                $"Sự kiện StockChangedRealtimeDto không được chứa thông tin nhạy cảm: {prop.Name}");
        }

        // 3. LowStockAlertRealtimeDto
        var lowProps = typeof(LowStockAlertRealtimeDto).GetProperties();
        foreach (var prop in lowProps)
        {
            var propNameLower = prop.Name.ToLowerInvariant();
            forbiddenTerms.Should().NotContain(term => propNameLower.Contains(term),
                $"Sự kiện LowStockAlertRealtimeDto không được chứa thông tin nhạy cảm: {prop.Name}");
        }
    }

    [Fact]
    public void RealtimeEventDtos_SerializesCleanPublicPayload()
    {
        var orderEvent = new OrderSummaryRealtimeDto
        {
            OrderId = Guid.NewGuid(),
            OrderNumber = "ORD-ADM-12345",
            Channel = "Shopee",
            BranchId = Guid.NewGuid(),
            Status = "Reserved",
            TotalAmount = 500_000m,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var json = JsonSerializer.Serialize(orderEvent);
        json.ToLowerInvariant().Should().NotContain("cost");
        json.ToLowerInvariant().Should().NotContain("cogs");
        json.ToLowerInvariant().Should().NotContain("average");

        var stockEvent = new StockChangedRealtimeDto
        {
            BranchId = Guid.NewGuid(),
            SkuId = Guid.NewGuid(),
            NewAvailableStock = 15m,
            Timestamp = DateTimeOffset.UtcNow
        };

        var stockJson = JsonSerializer.Serialize(stockEvent);
        stockJson.ToLowerInvariant().Should().NotContain("cost");
        stockJson.ToLowerInvariant().Should().NotContain("cogs");

        var warningEvent = new LowStockAlertRealtimeDto
        {
            BranchId = Guid.NewGuid(),
            SkuId = Guid.NewGuid(),
            SkuCode = "SKU-001",
            CurrentAvailable = 5m,
            Threshold = 10m,
            AlertedAt = DateTimeOffset.UtcNow
        };

        var warningJson = JsonSerializer.Serialize(warningEvent);
        warningJson.ToLowerInvariant().Should().NotContain("cost");
        warningJson.ToLowerInvariant().Should().NotContain("cogs");
    }

    #endregion
}
