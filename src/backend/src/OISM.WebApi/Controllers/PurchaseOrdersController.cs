using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.PurchaseOrders.Dtos;
using OISM.Application.Features.PurchaseOrders.Services;
using OISM.WebApi.Http;

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
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.PurchaseOrders, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã phiếu mua không được để trống.");
        }

        var po = await _purchaseOrderService.GetPurchaseOrderByIdAsync(id, ct);
        if (po == null)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.PurchaseOrders, "NOT_FOUND", "Không tìm thấy dữ liệu", StatusCodes.Status404NotFound, $"Không tìm thấy phiếu mua hàng với ID '{id}'.");
        }

        return Ok(po);
    }

    /// <summary>
    /// Khởi tạo phiếu mua hàng mới ở trạng thái Draft (idempotent theo Idempotency-Key / X-Request-Key).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Staff")]
    [ApiErrorContract(ApiErrorProfile.PurchaseOrders)]
    public async Task<IActionResult> Create(
        [FromBody] CreatePurchaseOrderDto? dto,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader,
        [FromHeader(Name = "X-Request-Key")] string? xRequestKeyHeader,
        CancellationToken ct)
    {
        if (dto == null)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.PurchaseOrders, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Dữ liệu phiếu mua không được để trống.");
        }

        var rawHeader = !string.IsNullOrWhiteSpace(idempotencyKeyHeader) ? idempotencyKeyHeader : xRequestKeyHeader;
        if (string.IsNullOrWhiteSpace(rawHeader))
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.PurchaseOrders, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Bắt buộc cung cấp Idempotency-Key hoặc X-Request-Key header.");
        }

        if (!Guid.TryParse(rawHeader.Trim(), out var requestKey) || requestKey == Guid.Empty)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.PurchaseOrders, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Giá trị Idempotency-Key header không phải là GUID hợp lệ.");
        }

        var userId = RequireUserId();
        var result = await _purchaseOrderService.CreateAsync(dto, requestKey, userId, ct);
        if (result.IsReplay)
        {
            return Ok(result);
        }

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// Cập nhật bản nháp phiếu mua hàng (chỉ khi ở trạng thái Draft).
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Owner,Staff")]
    [ApiErrorContract(ApiErrorProfile.PurchaseOrders)]
    public async Task<IActionResult> UpdateDraft(
        Guid id,
        [FromBody] UpdatePurchaseOrderDraftDto? dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.PurchaseOrders, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã phiếu mua không được để trống.");
        }

        if (dto == null)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.PurchaseOrders, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Dữ liệu phiếu mua không được để trống.");
        }

        var userId = RequireUserId();
        var result = await _purchaseOrderService.UpdateDraftAsync(id, dto, userId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Phê duyệt phiếu mua hàng (chỉ Owner).
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = "Owner")]
    [ApiErrorContract(ApiErrorProfile.PurchaseOrders)]
    public async Task<IActionResult> Approve(
        Guid id,
        [FromBody] ApprovePurchaseOrderRequestDto? dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.PurchaseOrders, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã phiếu mua không được để trống.");
        }

        var userId = RequireUserId();
        var confirmZeroPrice = dto?.ConfirmZeroPrice ?? false;
        var result = await _purchaseOrderService.ApproveAsync(id, confirmZeroPrice, userId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Đóng phần chưa nhận của phiếu mua hàng (chỉ Owner, bắt buộc lý do).
    /// </summary>
    [HttpPost("{id:guid}/close")]
    [Authorize(Roles = "Owner")]
    [ApiErrorContract(ApiErrorProfile.PurchaseOrders)]
    public async Task<IActionResult> Close(
        Guid id,
        [FromBody] ClosePurchaseOrderRequestDto? dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.PurchaseOrders, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã phiếu mua không được để trống.");
        }

        var userId = RequireUserId();
        var result = await _purchaseOrderService.CloseAsync(id, dto?.Reason, userId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Hủy phiếu mua hàng khi chưa phát sinh hàng nhận (chỉ Owner).
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Owner")]
    [ApiErrorContract(ApiErrorProfile.PurchaseOrders)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.PurchaseOrders, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã phiếu mua không được để trống.");
        }

        var userId = RequireUserId();
        var result = await _purchaseOrderService.CancelAsync(id, userId, ct);
        return Ok(result);
    }

    private Guid RequireUserId() =>
        User.GetUserId() ?? throw new OismException("UNAUTHORIZED", "Không xác định được danh tính người dùng từ Token.", 401);
}
