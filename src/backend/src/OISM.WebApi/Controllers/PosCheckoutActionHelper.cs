using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Orders.Services;

namespace OISM.WebApi.Controllers;

/// <summary>
/// Helper dùng chung cho cả canonical route (POST /api/pos/checkout) và alias route (POST /api/orders/checkout).
/// Đảm bảo tính nhất quán tuyệt đối về action logic, authorization context, validation, error formats và RFC 7807 problem details.
/// </summary>
public static class PosCheckoutActionHelper
{
    public static async Task<IActionResult> ExecuteCheckoutAsync(
        ControllerBase controller,
        IPosCheckoutService posCheckoutService,
        PosCheckoutRequestDto dto,
        string? idempotencyKeyHeader,
        string? xRequestKeyHeader,
        CancellationToken ct)
    {
        if (dto == null)
        {
            return CreateProblemResult(controller, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Dữ liệu yêu cầu không được để trống.");
        }

        // Kiểm tra đối chiếu header Idempotency-Key / X-Request-Key với RequestKey trong payload (nếu có header gửi kèm)
        var rawHeaderKey = !string.IsNullOrWhiteSpace(idempotencyKeyHeader) ? idempotencyKeyHeader : xRequestKeyHeader;
        if (!string.IsNullOrWhiteSpace(rawHeaderKey))
        {
            if (Guid.TryParse(rawHeaderKey.Trim(), out var parsedHeaderGuid))
            {
                if (dto.RequestKey != Guid.Empty && dto.RequestKey != parsedHeaderGuid)
                {
                    return CreateProblemResult(controller, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "RequestKey trong payload không khớp với Idempotency-Key trong header.");
                }
            }
            else
            {
                return CreateProblemResult(controller, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Giá trị Idempotency-Key trong header không phải là GUID hợp lệ.");
            }
        }

        if (!controller.ModelState.IsValid)
        {
            var firstError = controller.ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Dữ liệu yêu cầu không hợp lệ.";
            return CreateProblemResult(controller, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, firstError);
        }

        try
        {
            var cashierUserId = GetCurrentUserId(controller.User);
            var result = await posCheckoutService.CheckoutPosAsync(dto, cashierUserId, ct);
            return controller.Ok(result);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(controller, ex.ErrorCode, GetTitleForErrorCode(ex.ErrorCode), ex.StatusCode, ex.Message);
        }
    }

    public static Guid? GetCurrentUserId(ClaimsPrincipal user)
    {
        var claim = user.FindFirst("userId")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var userId) ? userId : null;
    }

    public static ObjectResult CreateProblemResult(ControllerBase controller, string errorCode, string title, int statusCode, string detail)
    {
        var problem = new ProblemDetails
        {
            Type = $"https://oisms.local/errors/{errorCode}",
            Title = title,
            Status = statusCode,
            Detail = detail,
            Instance = controller.HttpContext?.Request.Path.Value ?? string.Empty,
            Extensions =
            {
                ["errorCode"] = errorCode,
                ["timestamp"] = DateTimeOffset.UtcNow
            }
        };
        return controller.StatusCode(statusCode, problem);
    }

    public static string GetTitleForErrorCode(string errorCode) => errorCode switch
    {
        "INSUFFICIENT_STOCK" => "Tồn kho không đủ",
        "IDEMPOTENCY_PAYLOAD_MISMATCH" => "Xung đột khóa Idempotency",
        "VALIDATION_ERROR" => "Lỗi xác thực dữ liệu",
        "BRANCH_INACTIVE_OR_INVALID" => "Chi nhánh không hợp lệ",
        "SKU_NOT_FOUND" => "Mặt hàng không tồn tại",
        "SKU_INACTIVE" => "Mặt hàng đã ngừng hoạt động",
        "PRICE_MISMATCH" => "Sai lệch đơn giá niêm yết",
        "INSUFFICIENT_CASH_RECEIVED" => "Tiền khách đưa không đủ",
        "INVALID_PAYMENT_METHOD" => "Phương thức thanh toán không hợp lệ",
        "NO_ACTIVE_WAREHOUSE" => "Không tìm thấy kho khả dụng",
        "ORDER_NOT_FOUND" => "Không tìm thấy đơn hàng",
        "INVALID_STATE_TRANSITION" => "Chuyển trạng thái không hợp lệ",
        "RESERVATION_EXPIRED" => "Phiếu giữ chỗ đã hết hạn",
        _ => "Lỗi xử lý nghiệp vụ"
    };
}
