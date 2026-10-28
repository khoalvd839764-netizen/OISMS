using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.StockTransfers.Dtos;
using OISM.Application.Features.StockTransfers.Services;

namespace OISM.WebApi.Controllers;

/// <summary>
/// Controller điều phối quy trình Chuyển kho nội bộ 2 bước (FR-INV-03).
/// Quản lý vòng đời Draft -> InTransit -> Received, và Hủy (chỉ khi còn Draft).
/// </summary>
[ApiController]
[Route("api/stock-transfers")]
[Authorize]
public class StockTransfersController : ControllerBase
{
    private readonly IStockTransferService _stockTransferService;

    public StockTransfersController(IStockTransferService stockTransferService)
    {
        _stockTransferService = stockTransferService;
    }

    /// <summary>
    /// Tra cứu danh sách phiếu chuyển kho nội bộ có phân trang và lọc theo trạng thái, chi nhánh, kho.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> GetTransfers([FromQuery] StockTransferFilterRequestDto filter, CancellationToken ct)
    {
        var result = await _stockTransferService.GetTransfersAsync(filter, ct);
        return Ok(result);
    }

    /// <summary>
    /// Xem chi tiết một phiếu chuyển kho theo ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> GetTransferById(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã phiếu chuyển không được để trống (Guid.Empty).");
        }

        var transfer = await _stockTransferService.GetTransferByIdAsync(id, ct);
        if (transfer == null)
        {
            return CreateProblemResult("NOT_FOUND", "Không tìm thấy dữ liệu", StatusCodes.Status404NotFound, $"Không tìm thấy phiếu chuyển kho với ID '{id}'.");
        }

        return Ok(transfer);
    }

    /// <summary>
    /// Tạo phiếu chuyển kho nội bộ ở trạng thái nháp (Draft).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> CreateTransfer(
        [FromBody] CreateStockTransferRequestDto dto,
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
            var result = await _stockTransferService.CreateTransferAsync(dto, userId, ct);

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
    /// Bước 1 - Xuất gửi hàng (Draft -> InTransit).
    /// Trừ OnHand kho/chi nhánh xuất, ghi Sổ cái OUT và snapshot WAC.
    /// </summary>
    [HttpPost("{id:guid}/dispatch")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> DispatchTransfer(
        [FromRoute] Guid id,
        [FromBody] DispatchStockTransferRequestDto? dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã phiếu chuyển không được để trống (Guid.Empty).");
        }

        dto ??= new DispatchStockTransferRequestDto();

        try
        {
            var userId = GetCurrentUserId() ?? Guid.Empty;
            var result = await _stockTransferService.DispatchTransferAsync(id, dto, userId, ct);
            return Ok(result);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, GetTitleForErrorCode(ex.ErrorCode), ex.StatusCode, ex.Message);
        }
    }

    /// <summary>
    /// Bước 2 - Tiếp nhận nhập kho đích (InTransit -> Received).
    /// Tăng OnHand kho/chi nhánh đích, ghi Sổ cái IN và cập nhật lại WAC đích.
    /// </summary>
    [HttpPost("{id:guid}/receive")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> ReceiveTransfer(
        [FromRoute] Guid id,
        [FromBody] ReceiveStockTransferRequestDto? dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã phiếu chuyển không được để trống (Guid.Empty).");
        }

        dto ??= new ReceiveStockTransferRequestDto();

        try
        {
            var userId = GetCurrentUserId() ?? Guid.Empty;
            var result = await _stockTransferService.ReceiveTransferAsync(id, dto, userId, ct);
            return Ok(result);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, GetTitleForErrorCode(ex.ErrorCode), ex.StatusCode, ex.Message);
        }
    }

    /// <summary>
    /// Hủy phiếu chuyển kho khi còn ở trạng thái Draft (Draft -> Cancelled).
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> CancelTransfer(
        [FromRoute] Guid id,
        [FromBody] CancelStockTransferRequestDto? dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã phiếu chuyển không được để trống (Guid.Empty).");
        }

        try
        {
            var userId = GetCurrentUserId() ?? Guid.Empty;
            var result = await _stockTransferService.CancelTransferAsync(id, dto, userId, ct);
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
        "NOT_FOUND" => "Không tìm thấy dữ liệu",
        "INVALID_STATE_TRANSITION" => "Chuyển trạng thái không hợp lệ",
        "UNAUTHORIZED" => "Chưa xác thực",
        _ => "Lỗi xử lý nghiệp vụ"
    };
}
