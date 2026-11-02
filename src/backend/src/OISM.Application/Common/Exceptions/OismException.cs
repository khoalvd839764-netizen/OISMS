namespace OISM.Application.Common.Exceptions;

/// <summary>
/// Ngoại lệ nghiệp vụ cơ sở của hệ thống OISMS tuân thủ cấu trúc lỗi RFC 7807 (Problem Details).
/// </summary>
public class OismException : Exception
{
    public string ErrorCode { get; }
    public int StatusCode { get; }

    public OismException(string errorCode, string message, int statusCode = 400) : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }
}

/// <summary>
/// Ngoại lệ không đủ tồn kho khả dụng tại chi nhánh hoặc kho hàng (HTTP 409 Conflict).
/// </summary>
public class InsufficientStockException : OismException
{
    public string SkuCode { get; }
    public decimal AvailableStock { get; }
    public decimal RequestedStock { get; }

    public InsufficientStockException(string skuCode, decimal availableStock, decimal requestedStock)
        : base("INSUFFICIENT_STOCK",
               $"SKU '{skuCode}' chỉ còn {availableStock:G29} sản phẩm khả dụng, không đủ đáp ứng số lượng yêu cầu {requestedStock:G29}.",
               409)
    {
        SkuCode = skuCode;
        AvailableStock = availableStock;
        RequestedStock = requestedStock;
    }
}

/// <summary>
/// Ngoại lệ xung đột RequestKey khi cùng key nhưng khác payload (HTTP 409 Conflict).
/// </summary>
public class IdempotencyPayloadMismatchException : OismException
{
    public IdempotencyPayloadMismatchException(string message = "RequestKey đã được sử dụng cho một yêu cầu thanh toán khác với nội dung giỏ hàng khác.")
        : base("IDEMPOTENCY_PAYLOAD_MISMATCH", message, 409)
    {
    }
}

public class PriceValidFromInPastException : OismException
{
    public PriceValidFromInPastException(string message = "Thời điểm hiệu lực của phiếu đổi giá phải lớn hơn hoặc bằng thời điểm hiện tại.")
        : base("PRICE_VALID_FROM_IN_PAST", message, 400)
    {
    }
}

public class PriceValidFromConflictException : OismException
{
    public PriceValidFromConflictException(string message = "Đã tồn tại giá có hiệu lực cùng mốc thời gian cho mặt hàng và đơn vị này.")
        : base("PRICE_VALID_FROM_CONFLICT", message, 409)
    {
    }
}

public class PriceAlreadyEffectiveException : OismException
{
    public PriceAlreadyEffectiveException(string message = "Không thể hủy giá đã đến thời điểm hoặc đang có hiệu lực.")
        : base("PRICE_ALREADY_EFFECTIVE", message, 409)
    {
    }
}

public class PriceListNotFoundException : OismException
{
    public PriceListNotFoundException(string code)
        : base("PRICE_LIST_NOT_FOUND", $"Không tìm thấy bảng giá '{code}' trong cửa hàng.", 404)
    {
    }
}

public class DirectPriceUpdateNotAllowedException : OismException
{
    public DirectPriceUpdateNotAllowedException(string message = "Hệ thống đã nâng cấp sang bảng giá theo thời gian. Mọi thay đổi giá phải thông qua phiếu đổi giá.")
        : base("DIRECT_PRICE_UPDATE_NOT_ALLOWED", message, 409)
    {
    }
}

public sealed record PriceMismatchItem(Guid SkuId, string SkuCode, Guid? UnitId, decimal CurrentPrice);

public class PriceMismatchException : OismException
{
    public IReadOnlyList<PriceMismatchItem> CurrentPrices { get; }

    public PriceMismatchException(string message, IReadOnlyList<PriceMismatchItem> currentPrices)
        : base("PRICE_MISMATCH", message, 400)
    {
        CurrentPrices = currentPrices;
    }
}
