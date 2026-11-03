using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.Pricing.Dtos;
using OISM.Application.Features.Pricing.Services;

namespace OISM.WebApi.Controllers;

/// <summary>
/// Controller điều phối nghiệp vụ Bảng giá theo thời gian và Phiếu đổi giá (OISMS-47-U04).
/// </summary>
[ApiController]
[Authorize]
public class PricingController : ControllerBase
{
    private readonly IPriceChangeService _priceChangeService;
    private readonly IPriceQueryService _priceQueryService;

    public PricingController(
        IPriceChangeService priceChangeService,
        IPriceQueryService priceQueryService)
    {
        _priceChangeService = priceChangeService;
        _priceQueryService = priceQueryService;
    }

    /// <summary>
    /// Danh sách các bảng giá của cửa hàng (retail, wholesale).
    /// </summary>
    [HttpGet("api/price-lists")]
    [Authorize(Roles = "Owner,Staff,Cashier")]
    public async Task<IActionResult> GetPriceLists(CancellationToken ct)
    {
        var result = await _priceQueryService.GetPriceListsAsync(ct);
        return Ok(result);
    }

    /// <summary>
    /// Khởi tạo phiếu đổi giá mới (Owner duyệt ngay, Staff chuyển trạng thái Pending chờ duyệt).
    /// </summary>
    [HttpPost("api/price-change-requests")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> CreatePriceChangeRequest(
        [FromBody] CreatePriceChangeRequest dto,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader,
        [FromHeader(Name = "X-Request-Key")] string? xRequestKeyHeader,
        CancellationToken ct)
    {
        if (dto == null)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Dữ liệu yêu cầu không được để trống.");
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
            var isOwner = User.IsInRole("Owner");
            var actor = new PriceActor(userId, isOwner);

            var result = await _priceChangeService.CreateAsync(dto, requestKey, actor, ct);
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
    /// Tra cứu danh sách phiếu đổi giá có phân trang và lọc theo trạng thái.
    /// </summary>
    [HttpGet("api/price-change-requests")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> GetPriceChangeRequests(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var (items, totalCount) = await _priceQueryService.GetRequestsAsync(status, page, pageSize, ct);
        return Ok(new
        {
            items,
            totalCount,
            page,
            pageSize
        });
    }

    /// <summary>
    /// Xem chi tiết một phiếu đổi giá theo ID.
    /// </summary>
    [HttpGet("api/price-change-requests/{id:guid}")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> GetPriceChangeRequestById(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã phiếu không được để trống.");
        }

        var request = await _priceQueryService.GetRequestByIdAsync(id, ct);
        if (request == null)
        {
            return CreateProblemResult("NOT_FOUND", "Không tìm thấy dữ liệu", StatusCodes.Status404NotFound, $"Không tìm thấy phiếu đổi giá với ID '{id}'.");
        }

        return Ok(request);
    }

    /// <summary>
    /// Chủ cửa hàng (Owner) phê duyệt phiếu đổi giá Pending.
    /// </summary>
    [HttpPost("api/price-change-requests/{id:guid}/approve")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> ApprovePriceChangeRequest(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã phiếu không được để trống.");
        }

        try
        {
            var reviewerId = GetCurrentUserId();
            var result = await _priceChangeService.ApproveAsync(id, reviewerId, ct);
            return Ok(result);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, GetTitleForErrorCode(ex.ErrorCode), ex.StatusCode, ex.Message);
        }
    }

    /// <summary>
    /// Chủ cửa hàng (Owner) từ chối phiếu đổi giá Pending.
    /// </summary>
    [HttpPost("api/price-change-requests/{id:guid}/reject")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> RejectPriceChangeRequest(
        Guid id,
        [FromBody] RejectPriceChangeRequestDto? dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã phiếu không được để trống.");
        }

        try
        {
            var reviewerId = GetCurrentUserId();
            var result = await _priceChangeService.RejectAsync(id, dto?.Note ?? string.Empty, reviewerId, ct);
            return Ok(result);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, GetTitleForErrorCode(ex.ErrorCode), ex.StatusCode, ex.Message);
        }
    }

    /// <summary>
    /// Chủ cửa hàng (Owner) hủy một dòng giá đã lên lịch trong tương lai chưa đến thời điểm hiệu lực.
    /// </summary>
    [HttpPost("api/sku-unit-prices/{id:guid}/cancel")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> CancelScheduledPrice(
        Guid id,
        [FromBody] CancelSkuUnitPriceRequestDto? dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã dòng giá không được để trống.");
        }

        try
        {
            var userId = GetCurrentUserId();
            await _priceChangeService.CancelScheduledPriceAsync(id, dto?.Reason ?? string.Empty, userId, ct);
            return Ok(new { message = "Hủy giá đã lên lịch thành công." });
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, GetTitleForErrorCode(ex.ErrorCode), ex.StatusCode, ex.Message);
        }
    }

    /// <summary>
    /// Tra cứu giá bán có hiệu lực của SKU tại một thời điểm (mặc định là hiện tại).
    /// </summary>
    [HttpGet("api/sku-unit-prices/effective")]
    [Authorize(Roles = "Owner,Staff,Cashier")]
    public async Task<IActionResult> GetEffectivePrice(
        [FromQuery] Guid skuId,
        [FromQuery] string? priceListCode,
        [FromQuery] DateTimeOffset? at,
        CancellationToken ct)
    {
        if (skuId == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã SKU không được để trống.");
        }

        // Cashier chỉ được dùng at trống (mặc định thời điểm hiện tại)
        if (User.IsInRole("Cashier") && !User.IsInRole("Owner") && !User.IsInRole("Staff") && at.HasValue)
        {
            return CreateProblemResult("FORBIDDEN_ACCESS", "Không có quyền truy cập", StatusCodes.Status403Forbidden, "Thu ngân chỉ được tra cứu giá tại thời điểm hiện tại.");
        }

        var result = await _priceQueryService.GetEffectivePriceAsync(skuId, priceListCode, at, ct);
        return Ok(result);
    }

    /// <summary>
    /// Xem lịch sử thay đổi giá của SKU và đơn vị tính (gồm dòng đã hủy và dòng tương lai).
    /// </summary>
    [HttpGet("api/sku-unit-prices/history")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> GetPriceHistory(
        [FromQuery] Guid skuId,
        [FromQuery] Guid? unitId,
        [FromQuery] string? priceListCode,
        CancellationToken ct)
    {
        if (skuId == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã SKU không được để trống.");
        }

        var result = await _priceQueryService.GetPriceHistoryAsync(skuId, unitId, priceListCode, ct);
        return Ok(result);
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
        "PRICE_VALID_FROM_IN_PAST" => "Mốc thời gian hiệu lực ở quá khứ",
        "PRICE_VALID_FROM_CONFLICT" => "Trùng mốc thời gian hiệu lực",
        "PRICE_ALREADY_EFFECTIVE" => "Giá đã có hiệu lực",
        "PRICE_LIST_NOT_FOUND" => "Bảng giá không tồn tại",
        "DIRECT_PRICE_UPDATE_NOT_ALLOWED" => "Cấm cập nhật giá trực tiếp",
        "IDEMPOTENCY_PAYLOAD_MISMATCH" => "Xung đột khóa Idempotency",
        "INVALID_STATE_TRANSITION" => "Chuyển trạng thái không hợp lệ",
        "UNIT_NOT_ALLOWED" => "Đơn vị không được phép",
        "VALIDATION_ERROR" => "Lỗi xác thực dữ liệu",
        "NOT_FOUND" => "Không tìm thấy dữ liệu",
        "FORBIDDEN_ACCESS" => "Không có quyền truy cập",
        "UNAUTHORIZED" => "Chưa xác thực",
        _ => "Lỗi xử lý nghiệp vụ"
    };
}
