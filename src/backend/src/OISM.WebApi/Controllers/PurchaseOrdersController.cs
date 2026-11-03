using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.PurchaseOrders.Dtos;
using OISM.Application.Features.PurchaseOrders.Services;

namespace OISM.WebApi.Controllers;

/// <summary>
/// Controller điều phối nghiệp vụ Phiếu mua hàng từ Nhà cung cấp (OISMS-47-U01A).
/// </summary>
[ApiController]
[Route("api/purchase-orders")]
[Authorize]
public class PurchaseOrdersController : ControllerBase
{
    private readonly IPurchaseOrderService _purchaseOrderService;

    public PurchaseOrdersController(IPurchaseOrderService purchaseOrderService)
    {
        _purchaseOrderService = purchaseOrderService;
    }

    /// <summary>
    /// Danh sách phiếu mua hàng có phân trang và lọc theo trạng thái, nhà cung cấp, chi nhánh.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> GetList([FromQuery] PurchaseOrderFilterDto filter, CancellationToken ct)
    {
        var result = await _purchaseOrderService.GetPurchaseOrdersAsync(filter, ct);
        return Ok(result);
    }

    /// <summary>
    /// Xem chi tiết một phiếu mua hàng theo ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã phiếu mua không được để trống.");
        }

        var po = await _purchaseOrderService.GetPurchaseOrderByIdAsync(id, ct);
        if (po == null)
        {
            return CreateProblemResult("NOT_FOUND", "Không tìm thấy dữ liệu", StatusCodes.Status404NotFound, $"Không tìm thấy phiếu mua hàng với ID '{id}'.");
        }

        return Ok(po);
    }

    /// <summary>
    /// Khởi tạo phiếu mua hàng mới ở trạng thái Draft (idempotent theo Idempotency-Key / X-Request-Key).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> Create(
        [FromBody] CreatePurchaseOrderDto? dto,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader,
        [FromHeader(Name = "X-Request-Key")] string? xRequestKeyHeader,
        CancellationToken ct)
    {
        if (dto == null)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Dữ liệu phiếu mua không được để trống.");
        }

        var rawHeader = !string.IsNullOrWhiteSpace(idempotencyKeyHeader) ? idempotencyKeyHeader : xRequestKeyHeader;
        if (string.IsNullOrWhiteSpace(rawHeader))
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Bắt buộc cung cấp Idempotency-Key hoặc X-Request-Key header.");
        }

        if (!Guid.TryParse(rawHeader.Trim(), out var requestKey) || requestKey == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Giá trị Idempotency-Key header không phải là GUID hợp lệ.");
        }

        try
        {
            var userId = GetCurrentUserId();
            var result = await _purchaseOrderService.CreateAsync(dto, requestKey, userId, ct);
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
    /// Cập nhật bản nháp phiếu mua hàng (chỉ khi ở trạng thái Draft).
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> UpdateDraft(
        Guid id,
        [FromBody] UpdatePurchaseOrderDraftDto? dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã phiếu mua không được để trống.");
        }

        if (dto == null)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Dữ liệu phiếu mua không được để trống.");
        }

        try
        {
            var userId = GetCurrentUserId();
            var result = await _purchaseOrderService.UpdateDraftAsync(id, dto, userId, ct);
            return Ok(result);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, GetTitleForErrorCode(ex.ErrorCode), ex.StatusCode, ex.Message);
        }
    }

    /// <summary>
    /// Phê duyệt phiếu mua hàng (chỉ Owner).
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> Approve(
        Guid id,
        [FromBody] ApprovePurchaseOrderRequestDto? dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã phiếu mua không được để trống.");
        }

        try
        {
            var userId = GetCurrentUserId();
            var confirmZeroPrice = dto?.ConfirmZeroPrice ?? false;
            var result = await _purchaseOrderService.ApproveAsync(id, confirmZeroPrice, userId, ct);
            return Ok(result);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, GetTitleForErrorCode(ex.ErrorCode), ex.StatusCode, ex.Message);
        }
    }

    /// <summary>
    /// Đóng phần chưa nhận của phiếu mua hàng (chỉ Owner, bắt buộc lý do).
    /// </summary>
    [HttpPost("{id:guid}/close")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> Close(
        Guid id,
        [FromBody] ClosePurchaseOrderRequestDto? dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã phiếu mua không được để trống.");
        }

        try
        {
            var userId = GetCurrentUserId();
            var result = await _purchaseOrderService.CloseAsync(id, dto?.Reason, userId, ct);
            return Ok(result);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, GetTitleForErrorCode(ex.ErrorCode), ex.StatusCode, ex.Message);
        }
    }

    /// <summary>
    /// Hủy phiếu mua hàng khi chưa phát sinh hàng nhận (chỉ Owner).
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã phiếu mua không được để trống.");
        }

        try
        {
            var userId = GetCurrentUserId();
            var result = await _purchaseOrderService.CancelAsync(id, userId, ct);
            return Ok(result);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, GetTitleForErrorCode(ex.ErrorCode), ex.StatusCode, ex.Message);
        }
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirst("userId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(claim) || !Guid.TryParse(claim, out var userId))
        {
            throw new OismException("UNAUTHORIZED", "Không xác định được danh tính người dùng từ Token.", 401);
        }
        return userId;
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
        "PURCHASE_ORDER_REQUIRED" => "Yêu cầu phiếu mua hàng",
        "PURCHASE_ORDER_NOT_RECEIVABLE" => "Phiếu mua không ở trạng thái nhận hàng",
        "PURCHASE_ORDER_OVER_RECEIPT" => "Vượt số lượng đặt mua",
        "PURCHASE_ORDER_ITEM_MISMATCH" => "Dòng hàng không khớp phiếu mua",
        "PURCHASE_ORDER_ZERO_PRICE_CONFIRM_REQUIRED" => "Yêu cầu xác nhận đơn giá 0",
        "UNIT_NOT_ALLOWED" => "Đơn vị tính không hợp lệ",
        "INVALID_UNIT_QUANTITY" => "Số lượng đơn vị không hợp lệ",
        "IDEMPOTENCY_PAYLOAD_MISMATCH" => "Xung đột khóa Idempotency",
        "INVALID_STATE_TRANSITION" => "Chuyển trạng thái không hợp lệ",
        "VALIDATION_ERROR" => "Lỗi xác thực dữ liệu",
        "NOT_FOUND" => "Không tìm thấy dữ liệu",
        "UNAUTHORIZED" => "Chưa xác thực",
        _ => "Lỗi xử lý nghiệp vụ"
    };
}
