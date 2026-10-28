using OISM.Application.Common.Exceptions;
using OISM.Application.Features.Orders.Services;
using OISM.Application.Features.Webhooks.Dtos;

namespace OISM.Application.Features.Webhooks.Services;

/// <summary>
/// Bộ kiểm tra xác thực dữ liệu chuẩn hóa Canonical Webhook (BE2-F06, FR-SIM-01).
/// Đảm bảo tính nhất quán 100% giữa Ingress Webhook thật (/api/webhooks/simulator/{channel})
/// và Simulator Adapter Service (ISimulatorService.IngestWebhookOrderAsync).
/// </summary>
public static class WebhookCanonicalValidator
{
    public static string CanonicalizeChannel(string rawChannel)
    {
        if (string.IsNullOrWhiteSpace(rawChannel))
        {
            throw new OismException("VALIDATION_ERROR", "Kênh bán trên đường dẫn URL không được để trống.", 400);
        }

        var trimmed = rawChannel.Trim();
        if (string.Equals(trimmed, "shopee", StringComparison.OrdinalIgnoreCase)) return "Shopee";
        if (string.Equals(trimmed, "tiktok", StringComparison.OrdinalIgnoreCase)) return "TikTok";
        if (string.Equals(trimmed, "lazada", StringComparison.OrdinalIgnoreCase)) return "Lazada";

        throw new OismException("VALIDATION_ERROR", $"Kênh bán '{rawChannel}' không được hỗ trợ. Chỉ hỗ trợ 'Shopee', 'TikTok', 'Lazada'.", 400);
    }

    public static decimal ValidateAndCalculateTotal(string normalizedPathChannel, CanonicalWebhookPayloadDto dto)
    {
        if (dto == null)
        {
            throw new OismException("VALIDATION_ERROR", "Nội dung Webhook payload không được để trống.", 400);
        }

        if (string.IsNullOrWhiteSpace(dto.Channel))
        {
            throw new OismException("VALIDATION_ERROR", "Trường 'channel' trong payload không được để trống.", 400);
        }

        if (!string.Equals(normalizedPathChannel, dto.Channel.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new OismException("CHANNEL_MISMATCH", $"Kênh bán trên đường dẫn URL ('{normalizedPathChannel}') không khớp với kênh bán trong nội dung payload ('{dto.Channel}').", 400);
        }

        if (string.IsNullOrWhiteSpace(dto.ExternalShopId))
        {
            throw new OismException("VALIDATION_ERROR", "Mã Shop trên sàn ('externalShopId') là bắt buộc.", 400);
        }

        if (dto.ExternalShopId.Trim().Length > 100)
        {
            throw new OismException("VALIDATION_ERROR", "Mã Shop trên sàn không được vượt quá 100 ký tự.", 400);
        }

        if (string.IsNullOrWhiteSpace(dto.ExternalOrderId))
        {
            throw new OismException("VALIDATION_ERROR", "Mã Đơn hàng trên sàn ('externalOrderId') là bắt buộc.", 400);
        }

        if (dto.ExternalOrderId.Trim().Length > 100)
        {
            throw new OismException("VALIDATION_ERROR", "Mã Đơn hàng trên sàn không được vượt quá 100 ký tự.", 400);
        }

        // T5-F03 / R2-AC-04: OrderTimestamp bắt buộc có giá trị xác định (không null, không default, không MinValue)
        if (!dto.OrderTimestamp.HasValue || dto.OrderTimestamp.Value == default || dto.OrderTimestamp.Value == DateTimeOffset.MinValue)
        {
            throw new OismException("VALIDATION_ERROR", "Thời gian đặt hàng ('orderTimestamp') là bắt buộc và phải có giá trị xác định.", 400);
        }

        // T5-F03: TotalAmount bắt buộc, không âm, scale <= 2, magnitude
        if (!dto.TotalAmount.HasValue)
        {
            throw new OismException("VALIDATION_ERROR", "Tổng tiền đơn hàng ('totalAmount') là bắt buộc.", 400);
        }

        if (dto.TotalAmount.Value < 0)
        {
            throw new OismException("VALIDATION_ERROR", "Tổng tiền đơn hàng không được âm.", 400);
        }

        if (decimal.Round(dto.TotalAmount.Value, 2) != dto.TotalAmount.Value)
        {
            throw new OismException("VALIDATION_ERROR", "Tổng tiền đơn hàng không được vượt quá 2 chữ số thập phân.", 400);
        }

        if (dto.TotalAmount.Value > OrderService.MaxMoneyAmount)
        {
            throw new OismException("VALIDATION_ERROR", "Tổng tiền đơn hàng vượt quá giới hạn tối đa cho phép.", 400);
        }

        // Customer fields length validation
        if (dto.CustomerName != null && dto.CustomerName.Trim().Length > 200)
        {
            throw new OismException("VALIDATION_ERROR", "Tên khách hàng không được vượt quá 200 ký tự.", 400);
        }

        if (dto.CustomerPhone != null && dto.CustomerPhone.Trim().Length > 32)
        {
            throw new OismException("VALIDATION_ERROR", "Số điện thoại không được vượt quá 32 ký tự.", 400);
        }

        if (dto.ShippingAddress != null && dto.ShippingAddress.Trim().Length > 500)
        {
            throw new OismException("VALIDATION_ERROR", "Địa chỉ giao hàng không được vượt quá 500 ký tự.", 400);
        }

        if (dto.Items == null || dto.Items.Count == 0)
        {
            throw new OismException("VALIDATION_ERROR", "Đơn hàng phải có ít nhất 1 sản phẩm.", 400);
        }

        if (dto.Items.Any(i => i == null))
        {
            throw new OismException("VALIDATION_ERROR", "Dòng sản phẩm trong đơn hàng không được mang giá trị null.", 400);
        }

        foreach (var item in dto.Items)
        {
            if (string.IsNullOrWhiteSpace(item.SkuCode))
            {
                throw new OismException("VALIDATION_ERROR", "Mã SKU mặt hàng trong đơn không được để trống.", 400);
            }

            if (item.SkuCode.Trim().Length > 80)
            {
                throw new OismException("VALIDATION_ERROR", $"Mã SKU '{item.SkuCode}' vượt quá 80 ký tự cho phép.", 400);
            }

            if (!item.Quantity.HasValue)
            {
                throw new OismException("VALIDATION_ERROR", $"Số lượng mặt hàng SKU '{item.SkuCode}' là bắt buộc.", 400);
            }

            if (item.Quantity.Value <= 0)
            {
                throw new OismException("VALIDATION_ERROR", $"Số lượng mặt hàng SKU '{item.SkuCode}' phải lớn hơn 0.", 400);
            }

            if (item.Quantity.Value > OrderService.MaxQuantityAmount)
            {
                throw new OismException("VALIDATION_ERROR", $"Số lượng mặt hàng SKU '{item.SkuCode}' vượt quá giới hạn tối đa cho phép.", 400);
            }

            if (decimal.Round(item.Quantity.Value, 3) != item.Quantity.Value)
            {
                throw new OismException("VALIDATION_ERROR", $"Số lượng mặt hàng SKU '{item.SkuCode}' không được vượt quá 3 chữ số thập phân.", 400);
            }

            if (!item.UnitPrice.HasValue)
            {
                throw new OismException("VALIDATION_ERROR", $"Đơn giá của SKU '{item.SkuCode}' là bắt buộc.", 400);
            }

            if (item.UnitPrice.Value < 0)
            {
                throw new OismException("VALIDATION_ERROR", $"Đơn giá của SKU '{item.SkuCode}' không được âm.", 400);
            }

            if (item.UnitPrice.Value > OrderService.MaxMoneyAmount)
            {
                throw new OismException("VALIDATION_ERROR", $"Đơn giá của SKU '{item.SkuCode}' vượt quá giới hạn tối đa cho phép.", 400);
            }

            if (decimal.Round(item.UnitPrice.Value, 2) != item.UnitPrice.Value)
            {
                throw new OismException("VALIDATION_ERROR", $"Đơn giá của SKU '{item.SkuCode}' không được vượt quá 2 chữ số thập phân.", 400);
            }
        }

        // Kiểm tra tính nhất quán đơn giá nếu có dòng trùng SKU
        foreach (var g in dto.Items.GroupBy(i => i.SkuCode.Trim().ToUpperInvariant()))
        {
            if (g.Select(x => Math.Round(x.UnitPrice!.Value, 2)).Distinct().Count() > 1)
            {
                throw new OismException("VALIDATION_ERROR", "Các dòng cùng SKU trong đơn hàng phải có cùng đơn giá.", 400);
            }
        }

        // Tính toán tổng tiền theo cấu trúc dòng hàng phía Server
        decimal calculatedTotal = 0;
        foreach (var item in dto.Items)
        {
            try
            {
                checked
                {
                    var lineTotal = item.Quantity!.Value * item.UnitPrice!.Value;
                    if (lineTotal > OrderService.MaxMoneyAmount)
                    {
                        throw new OismException("VALIDATION_ERROR", "Tổng tiền của dòng hàng vượt quá giới hạn tối đa cho phép.", 400);
                    }
                    calculatedTotal += lineTotal;
                    if (calculatedTotal > OrderService.MaxMoneyAmount)
                    {
                        throw new OismException("VALIDATION_ERROR", "Tổng tiền đơn hàng vượt quá giới hạn tối đa cho phép.", 400);
                    }
                }
            }
            catch (OverflowException)
            {
                throw new OismException("VALIDATION_ERROR", "Tổng tiền đơn hàng vượt quá giới hạn tối đa cho phép.", 400);
            }
        }
        calculatedTotal = Math.Round(calculatedTotal, 2, MidpointRounding.AwayFromZero);

        return calculatedTotal;
    }
}
