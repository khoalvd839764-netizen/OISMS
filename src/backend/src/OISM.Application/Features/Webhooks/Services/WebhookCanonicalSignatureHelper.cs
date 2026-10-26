using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OISM.Application.Features.Webhooks.Dtos;

namespace OISM.Application.Features.Webhooks.Services;

/// <summary>
/// Helper tạo chữ ký Canonical Payload SHA-256 bền vững cho Webhook nhận đơn từ các sàn TMĐT.
/// Đảm bảo tính nhất quán (deterministic), bất biến trước thứ tự thuộc tính JSON, thứ tự danh sách items,
/// múi giờ biểu diễn và định dạng số thập phân tương đương.
/// </summary>
public static class WebhookCanonicalSignatureHelper
{
    public static string ComputeCanonicalSignature(CanonicalWebhookPayloadDto dto)
    {
        if (dto == null)
        {
            throw new ArgumentNullException(nameof(dto));
        }

        var normChannel = (dto.Channel ?? string.Empty).Trim().ToUpperInvariant();
        var normShopId = (dto.ExternalShopId ?? string.Empty).Trim();
        var normOrderId = (dto.ExternalOrderId ?? string.Empty).Trim();
        var normCustName = string.IsNullOrWhiteSpace(dto.CustomerName) ? string.Empty : dto.CustomerName.Trim();
        var normCustPhone = string.IsNullOrWhiteSpace(dto.CustomerPhone) ? string.Empty : dto.CustomerPhone.Trim();
        var normAddress = string.IsNullOrWhiteSpace(dto.ShippingAddress) ? string.Empty : dto.ShippingAddress.Trim();
        var total = dto.TotalAmount ?? 0m;
        var normTotal = Math.Round(total, 2, MidpointRounding.AwayFromZero)
            .ToString("0.00", CultureInfo.InvariantCulture);
        var timestamp = dto.OrderTimestamp ?? DateTimeOffset.UtcNow;
        var normTimestamp = timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);

        // Gom nhóm các dòng theo SKU Code chuẩn hóa (nếu cùng giá) và sắp xếp tăng dần theo SKU Code
        var groupedItems = (dto.Items ?? new List<CanonicalWebhookItemDto>())
            .Where(i => i != null && !string.IsNullOrWhiteSpace(i.SkuCode))
            .GroupBy(i => i.SkuCode.Trim().ToUpperInvariant())
            .Select(g => new
            {
                SkuCode = g.Key,
                Quantity = g.Sum(x => x.Quantity ?? 0m).ToString("0.000", CultureInfo.InvariantCulture),
                UnitPrice = Math.Round(g.First().UnitPrice ?? 0m, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture)
            })
            .OrderBy(i => i.SkuCode, StringComparer.Ordinal)
            .ToList();

        var canonicalModel = new
        {
            channel = normChannel,
            external_shop_id = normShopId,
            external_order_id = normOrderId,
            customer_name = normCustName,
            customer_phone = normCustPhone,
            shipping_address = normAddress,
            order_timestamp = normTimestamp,
            total_amount = normTotal,
            items = groupedItems.Select(i => new
            {
                sku = i.SkuCode,
                qty = i.Quantity,
                price = i.UnitPrice
            }).ToList()
        };

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            WriteIndented = false
        };

        var json = JsonSerializer.Serialize(canonicalModel, jsonOptions);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
