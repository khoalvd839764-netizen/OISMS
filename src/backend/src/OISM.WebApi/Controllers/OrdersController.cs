using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Orders.Services;

namespace OISM.WebApi.Controllers;

/// <summary>
/// API Quản lý Đơn hàng đa kênh và Điểm bán hàng (Order Management & POS Controller).
/// </summary>
[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IPosCheckoutService _posCheckoutService;
    private readonly IOrderService _orderService;

    public OrdersController(
        IPosCheckoutService posCheckoutService,
        IOrderService orderService)
    {
        _posCheckoutService = posCheckoutService;
        _orderService = orderService;
    }

    /// <summary>
    /// Tuyến đường alias thanh toán POS: POST /api/orders/checkout.
    /// Chia sẻ cùng lõi nghiệp vụ nguyên tử với POST /api/pos/checkout.
    /// Hỗ trợ vai trò Owner, Staff, Cashier.
    /// </summary>
    [HttpPost("checkout")]
    [Authorize(Roles = "Owner,Staff,Cashier")]
    public async Task<IActionResult> Checkout(
        [FromBody] PosCheckoutRequestDto dto,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader,
        [FromHeader(Name = "X-Request-Key")] string? xRequestKeyHeader,
        CancellationToken ct)
    {
        return await PosCheckoutActionHelper.ExecuteCheckoutAsync(this, _posCheckoutService, dto, idempotencyKeyHeader, xRequestKeyHeader, ct);
    }

    /// <summary>
    /// Tạo đơn hàng thủ công trên Admin Portal (POST /api/orders).
    /// Khởi tạo trạng thái Reserved và giữ chỗ tồn kho 15 phút.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> CreateOrder(
        [FromBody] CreateOrderRequestDto dto,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader,
        [FromHeader(Name = "X-Request-Key")] string? xRequestKeyHeader,
        CancellationToken ct)
    {
        if (dto == null)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Dữ liệu yêu cầu không được để trống.");
        }

        var rawHeaderKey = !string.IsNullOrWhiteSpace(idempotencyKeyHeader) ? idempotencyKeyHeader : xRequestKeyHeader;
        if (!string.IsNullOrWhiteSpace(rawHeaderKey))
        {
            if (Guid.TryParse(rawHeaderKey.Trim(), out var parsedHeaderGuid))
            {
                if (dto.RequestKey != Guid.Empty && dto.RequestKey != parsedHeaderGuid)
                {
                    return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "RequestKey trong payload không khớp với Idempotency-Key trong header.");
                }
            }
            else
            {
                return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Giá trị Idempotency-Key trong header không phải là GUID hợp lệ.");
            }
        }

        if (!ModelState.IsValid)
        {
            var firstError = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Dữ liệu yêu cầu không hợp lệ.";
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, firstError);
        }

        try
        {
            var userId = GetCurrentUserId();
            var result = await _orderService.CreateOrderAsync(dto, userId, ct);

            if (result.IsReplay)
            {
                return Ok(result);
            }

            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, GetTitleForErrorCode(ex.ErrorCode), ex.StatusCode, ex.Message);
        }
    }

    /// <summary>
    /// Duyệt đơn hàng trực tuyến (POST /api/orders/{id}/confirm).
    /// Chuyển trạng thái Reserved -> Confirmed: trừ OnHand, giảm Reserved, ghi Ledger OUT và snapshot WAC CostPrice.
    /// </summary>
    [HttpPost("{id:guid}/confirm")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> ConfirmOrder(
        [FromRoute] Guid id,
        [FromBody] ConfirmOrderRequestDto? dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã đơn hàng không được để trống (Guid.Empty).");
        }

        dto ??= new ConfirmOrderRequestDto();

        try
        {
            var userId = GetCurrentUserId();
            var result = await _orderService.ConfirmOrderAsync(id, dto, userId, ct);
            return Ok(result);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, GetTitleForErrorCode(ex.ErrorCode), ex.StatusCode, ex.Message);
        }
    }

    /// <summary>
    /// Hoàn tất đơn hàng trực tuyến (POST /api/orders/{id}/complete).
    /// Chuyển trạng thái Confirmed -> Completed, ghi nhận CompletedAt = now().
    /// Không đổi tồn kho, không ghi ledger OUT. Replay trên Completed là idempotent (200 OK).
    /// </summary>
    [HttpPost("{id:guid}/complete")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> CompleteOrder(
        [FromRoute] Guid id,
        [FromBody] CompleteOrderRequestDto? dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã đơn hàng không được để trống (Guid.Empty).");
        }

        dto ??= new CompleteOrderRequestDto();

        try
        {
            var userId = GetCurrentUserId();
            var result = await _orderService.CompleteOrderAsync(id, dto, userId, ct);
            return Ok(result);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, GetTitleForErrorCode(ex.ErrorCode), ex.StatusCode, ex.Message);
        }
    }

    /// <summary>
    /// Hủy đơn hàng trực tuyến (POST /api/orders/{id}/cancel).
    /// Chuyển trạng thái Draft/Reserved -> Cancelled, giải phóng toàn bộ giữ chỗ Active.
    /// Không đổi OnHand, không ghi ledger OUT.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> CancelOrder(
        [FromRoute] Guid id,
        [FromBody] CancelOrderRequestDto dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã đơn hàng không được để trống (Guid.Empty).");
        }

        if (dto == null || string.IsNullOrWhiteSpace(dto.Reason))
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Lý do hủy đơn không được để trống.");
        }

        try
        {
            var userId = GetCurrentUserId();
            var result = await _orderService.CancelOrderAsync(id, dto, userId, ct);
            return Ok(result);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, GetTitleForErrorCode(ex.ErrorCode), ex.StatusCode, ex.Message);
        }
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirst("userId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var userId) ? userId : null;
    }

    private ObjectResult CreateProblemResult(string errorCode, string title, int statusCode, string detail)
    {
        var problem = new ProblemDetails
        {
            Type = $"https://oisms.local/errors/{errorCode}",
            Title = title,
            Status = statusCode,
            Detail = detail,
            Instance = HttpContext.Request.Path,
            Extensions =
            {
                ["errorCode"] = errorCode,
                ["timestamp"] = DateTimeOffset.UtcNow
            }
        };
        return StatusCode(statusCode, problem);
    }

    private static string GetTitleForErrorCode(string errorCode) => errorCode switch
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
