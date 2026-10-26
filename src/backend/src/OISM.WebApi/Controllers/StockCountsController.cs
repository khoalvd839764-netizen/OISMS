using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.StockCounts.Dtos;
using OISM.Application.Features.StockCounts.Services;

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
    /// Khởi tạo đợt kiểm kê mới ở trạng thái Draft và chụp snapshot số dư sổ sách.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> CreateStockCount(
        [FromBody] CreateStockCountRequestDto dto,
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

                if (dto.RequestKey == Guid.Empty)
                {
                    dto.RequestKey = parsedHeaderGuid;
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
            var userId = GetCurrentUserId() ?? Guid.Empty;
            var result = await _stockCountService.CreateStockCountAsync(dto, userId, ct);

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
    /// Ghi nhận số lượng đếm thực tế của nhân viên kiểm kho (Draft -> Counting).
    /// </summary>
    [HttpPost("{id:guid}/record")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> RecordCounts(
        [FromRoute] Guid id,
        [FromBody] RecordStockCountBatchRequestDto dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã đợt kiểm kê không được để trống (Guid.Empty).");
        }

        if (dto == null)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Dữ liệu yêu cầu không được để trống.");
        }

        if (dto.Items != null && dto.Items.Any(i => i == null))
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Danh sách mặt hàng chứa phần tử rỗng (null).");
        }

        if (!ModelState.IsValid)
        {
            var firstError = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Dữ liệu yêu cầu không hợp lệ.";
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, firstError);
        }

        try
        {
            var userId = GetCurrentUserId() ?? Guid.Empty;
            var result = await _stockCountService.RecordCountsAsync(id, dto, userId, ct);
            return Ok(result);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, GetTitleForErrorCode(ex.ErrorCode), ex.StatusCode, ex.Message);
        }
    }

    /// <summary>
    /// Chốt đợt kiểm kê, đối soát sequence drift, tạo chứng từ điều chỉnh và hạch toán Sổ cái (Counting -> Confirmed).
    /// </summary>
    [HttpPost("{id:guid}/confirm")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> ConfirmStockCount(
        [FromRoute] Guid id,
        [FromBody] ConfirmStockCountRequestDto? dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã đợt kiểm kê không được để trống (Guid.Empty).");
        }

        if (!ModelState.IsValid)
        {
            var firstError = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Dữ liệu yêu cầu không hợp lệ.";
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, firstError);
        }

        try
        {
            var userId = GetCurrentUserId() ?? Guid.Empty;
            var result = await _stockCountService.ConfirmStockCountAsync(id, dto ?? new ConfirmStockCountRequestDto(), userId, ct);
            return Ok(result);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, GetTitleForErrorCode(ex.ErrorCode), ex.StatusCode, ex.Message);
        }
    }

    /// <summary>
    /// Hủy đợt kiểm kê khi chưa chốt (Draft/Counting -> Cancelled).
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> CancelStockCount(
        [FromRoute] Guid id,
        [FromBody] CancelStockCountRequestDto? dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã đợt kiểm kê không được để trống (Guid.Empty).");
        }

        try
        {
            var userId = GetCurrentUserId() ?? Guid.Empty;
            var result = await _stockCountService.CancelStockCountAsync(id, dto?.Reason, userId, ct);
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
        "INSUFFICIENT_STOCK_FOR_RESERVATION" => "Tồn kho không đủ đáp ứng hàng giữ chỗ",
        "IDEMPOTENCY_CONFLICT" => "Xung đột khóa Idempotency",
        "IDEMPOTENCY_PAYLOAD_MISMATCH" => "Xung đột khóa Idempotency",
        "STOCK_COUNT_DRIFT_DETECTED" => "Sai lệch sequence kiểm kê (drift)",
        "STOCK_COUNT_SEQUENCE_DRIFT" => "Sai lệch sequence kiểm kê",
        "EMPTY_WAREHOUSE_STOCK_COUNT" => "Kho không có sản phẩm để kiểm kê",
        "INVALID_COUNT_ITEM" => "Sản phẩm kiểm đếm không hợp lệ",
        "INCOMPLETE_STOCK_COUNT" => "Chưa kiểm đếm đủ sản phẩm",
        "ADJUSTMENT_COST_REQUIRED" => "Bắt buộc cung cấp đơn giá điều chỉnh",
        "SKU_INACTIVE" => "Sản phẩm hoặc biến thể bị khóa",
        "VALIDATION_ERROR" => "Lỗi xác thực dữ liệu",
        "NOT_FOUND" => "Không tìm thấy dữ liệu",
        "INVALID_STATE_TRANSITION" => "Chuyển trạng thái không hợp lệ",
        "UNAUTHORIZED" => "Chưa xác thực",
        _ => "Lỗi xử lý nghiệp vụ"
    };
}
