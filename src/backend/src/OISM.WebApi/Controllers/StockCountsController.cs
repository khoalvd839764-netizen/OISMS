using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Features.StockCounts.Dtos;
using OISM.Application.Features.StockCounts.Services;
using OISM.WebApi.Http;

namespace OISM.WebApi.Controllers;

/// <summary>
/// Controller điều phối quy trình Kiểm kê kho và Cân bằng tồn (FR-INV-04).
/// Quản lý vòng đời Draft -> Counting -> Confirmed, và Hủy (khi còn Draft hoặc Counting).
/// </summary>
[ApiController]
[Route("api/stock-counts")]
[Authorize]
public class StockCountsController : ControllerBase
{
    private readonly IStockCountService _stockCountService;

    public StockCountsController(IStockCountService stockCountService)
    {
        _stockCountService = stockCountService;
    }

    /// <summary>
    /// Tra cứu danh sách đợt kiểm kê có phân trang và lọc theo trạng thái, chi nhánh, kho.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> GetStockCounts([FromQuery] StockCountFilterRequestDto filter, CancellationToken ct)
    {
        var result = await _stockCountService.GetStockCountsAsync(filter, ct);
        return Ok(result);
    }

    /// <summary>
    /// Xem chi tiết một đợt kiểm kê theo ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> GetStockCountById(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockCounts, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã đợt kiểm kê không được để trống (Guid.Empty).");
        }

        var count = await _stockCountService.GetStockCountByIdAsync(id, ct);
        if (count == null)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockCounts, "NOT_FOUND", "Không tìm thấy dữ liệu", StatusCodes.Status404NotFound, $"Không tìm thấy đợt kiểm kê với ID '{id}'.");
        }

        return Ok(count);
    }

    /// <summary>
    /// Khởi tạo đợt kiểm kê mới ở trạng thái Draft và chụp snapshot số dư sổ sách.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Staff")]
    [ApiErrorContract(ApiErrorProfile.StockCounts)]
    public async Task<IActionResult> CreateStockCount(
        [FromBody] CreateStockCountRequestDto dto,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader,
        [FromHeader(Name = "X-Request-Key")] string? xRequestKeyHeader,
        CancellationToken ct)
    {
        if (dto == null)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockCounts, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Dữ liệu yêu cầu không được để trống.");
        }

        var rawHeaderKey = !string.IsNullOrWhiteSpace(idempotencyKeyHeader) ? idempotencyKeyHeader : xRequestKeyHeader;
        if (!string.IsNullOrWhiteSpace(rawHeaderKey))
        {
            if (Guid.TryParse(rawHeaderKey.Trim(), out var parsedHeaderGuid))
            {
                if (dto.RequestKey != Guid.Empty && dto.RequestKey != parsedHeaderGuid)
                {
                    return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockCounts, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "RequestKey trong payload không khớp với Idempotency-Key trong header.");
                }

                if (dto.RequestKey == Guid.Empty)
                {
                    dto.RequestKey = parsedHeaderGuid;
                }
            }
            else
            {
                return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockCounts, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Giá trị Idempotency-Key trong header không phải là GUID hợp lệ.");
            }
        }

        var userId = User.GetUserId() ?? Guid.Empty;
        var result = await _stockCountService.CreateStockCountAsync(dto, userId, ct);

        if (result.IsReplay)
        {
            return Ok(result);
        }

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// Ghi nhận số lượng đếm thực tế của nhân viên kiểm kho (Draft -> Counting).
    /// </summary>
    [HttpPost("{id:guid}/record")]
    [Authorize(Roles = "Owner,Staff")]
    [ApiErrorContract(ApiErrorProfile.StockCounts)]
    public async Task<IActionResult> RecordCounts(
        [FromRoute] Guid id,
        [FromBody] RecordStockCountBatchRequestDto dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockCounts, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã đợt kiểm kê không được để trống (Guid.Empty).");
        }

        if (dto == null)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockCounts, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Dữ liệu yêu cầu không được để trống.");
        }

        if (dto.Items != null && dto.Items.Any(i => i == null))
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockCounts, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Danh sách mặt hàng chứa phần tử rỗng (null).");
        }

        var userId = User.GetUserId() ?? Guid.Empty;
        var result = await _stockCountService.RecordCountsAsync(id, dto, userId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Chốt đợt kiểm kê, đối soát sequence drift, tạo chứng từ điều chỉnh và hạch toán Sổ cái (PendingApproval -> Confirmed).
    /// Chỉ Owner mới có quyền phê duyệt chốt kiểm kê.
    /// </summary>
    [HttpPost("{id:guid}/confirm")]
    [Authorize(Roles = "Owner")]
    [ApiErrorContract(ApiErrorProfile.StockCounts)]
    public async Task<IActionResult> ConfirmStockCount(
        [FromRoute] Guid id,
        [FromBody] ConfirmStockCountRequestDto? dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockCounts, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã đợt kiểm kê không được để trống (Guid.Empty).");
        }

        var userId = User.GetUserId() ?? Guid.Empty;
        var result = await _stockCountService.ConfirmStockCountAsync(id, dto ?? new ConfirmStockCountRequestDto(), userId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Nộp đợt kiểm kê để trình duyệt (Counting -> PendingApproval).
    /// </summary>
    [HttpPost("{id:guid}/submit")]
    [Authorize(Roles = "Owner,Staff")]
    [ApiErrorContract(ApiErrorProfile.StockCounts)]
    public async Task<IActionResult> SubmitStockCount(
        [FromRoute] Guid id,
        [FromBody] SubmitStockCountRequestDto? dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockCounts, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã đợt kiểm kê không được để trống (Guid.Empty).");
        }

        var userId = User.GetUserId() ?? Guid.Empty;
        var result = await _stockCountService.SubmitStockCountAsync(id, dto ?? new SubmitStockCountRequestDto(), userId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Nhân viên cập nhật số lượng đếm lại cho các SKU bị lệch sequence (PendingApproval -> Counting).
    /// </summary>
    [HttpPost("{id:guid}/recount")]
    [Authorize(Roles = "Owner,Staff")]
    [ApiErrorContract(ApiErrorProfile.StockCounts)]
    public async Task<IActionResult> RecountDrifted(
        [FromRoute] Guid id,
        [FromBody] RecountStockCountRequestDto dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockCounts, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã đợt kiểm kê không được để trống (Guid.Empty).");
        }

        if (dto == null)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockCounts, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Dữ liệu yêu cầu không được để trống.");
        }

        if (dto.SkuIds == null || dto.SkuIds.Count == 0)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockCounts, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Danh sách SKU kiểm đếm lại không được rỗng.");
        }

        var userId = User.GetUserId() ?? Guid.Empty;
        var result = await _stockCountService.RecountDriftedAsync(id, dto, userId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Owner yêu cầu đếm lại các SKU bị lệch sequence (PendingApproval -> PendingApproval).
    /// </summary>
    [HttpPost("{id:guid}/request-recount")]
    [Authorize(Roles = "Owner")]
    [ApiErrorContract(ApiErrorProfile.StockCounts)]
    public async Task<IActionResult> RequestRecount(
        [FromRoute] Guid id,
        [FromBody] RequestRecountRequestDto dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockCounts, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã đợt kiểm kê không được để trống (Guid.Empty).");
        }

        if (dto == null)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockCounts, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Dữ liệu yêu cầu không được để trống.");
        }

        var userId = User.GetUserId() ?? Guid.Empty;
        var result = await _stockCountService.RequestRecountAsync(id, dto, userId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Hủy đợt kiểm kê khi chưa chốt (Draft/Counting -> Cancelled).
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Owner,Staff")]
    [ApiErrorContract(ApiErrorProfile.StockCounts)]
    public async Task<IActionResult> CancelStockCount(
        [FromRoute] Guid id,
        [FromBody] CancelStockCountRequestDto? dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockCounts, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã đợt kiểm kê không được để trống (Guid.Empty).");
        }

        var userId = User.GetUserId() ?? Guid.Empty;
        var result = await _stockCountService.CancelStockCountAsync(id, dto?.Reason, userId, ct);
        return Ok(result);
    }
}
