using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Orders.Services;
using OISM.WebApi.Http;

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
            return ApiErrorResponse.Problem(controller.HttpContext, ApiErrorProfile.PosCheckout, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Dữ liệu yêu cầu không được để trống.");
        }

        // Kiểm tra đối chiếu header Idempotency-Key / X-Request-Key với RequestKey trong payload (nếu có header gửi kèm)
        var rawHeaderKey = !string.IsNullOrWhiteSpace(idempotencyKeyHeader) ? idempotencyKeyHeader : xRequestKeyHeader;
        if (!string.IsNullOrWhiteSpace(rawHeaderKey))
        {
            if (Guid.TryParse(rawHeaderKey.Trim(), out var parsedHeaderGuid))
            {
                if (dto.RequestKey != Guid.Empty && dto.RequestKey != parsedHeaderGuid)
                {
                    return ApiErrorResponse.Problem(controller.HttpContext, ApiErrorProfile.PosCheckout, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "RequestKey trong payload không khớp với Idempotency-Key trong header.");
                }
            }
            else
            {
                return ApiErrorResponse.Problem(controller.HttpContext, ApiErrorProfile.PosCheckout, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Giá trị Idempotency-Key trong header không phải là GUID hợp lệ.");
            }
        }

        var cashierUserId = controller.User.GetUserId();
        var result = await posCheckoutService.CheckoutPosAsync(dto, cashierUserId, ct);
        return controller.Ok(result);
    }

}
