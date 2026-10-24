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
