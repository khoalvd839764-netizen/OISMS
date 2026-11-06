using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Features.StockTransfers.Dtos;
using OISM.Application.Features.StockTransfers.Services;
using OISM.WebApi.Http;

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
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockTransfers, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã phiếu chuyển không được để trống (Guid.Empty).");
        }

        var transfer = await _stockTransferService.GetTransferByIdAsync(id, ct);
        if (transfer == null)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockTransfers, "NOT_FOUND", "Không tìm thấy dữ liệu", StatusCodes.Status404NotFound, $"Không tìm thấy phiếu chuyển kho với ID '{id}'.");
        }

        return Ok(transfer);
    }

    /// <summary>
    /// Tạo phiếu chuyển kho nội bộ ở trạng thái nháp (Draft).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Staff")]
    [ApiErrorContract(ApiErrorProfile.StockTransfers)]
    public async Task<IActionResult> CreateTransfer(
        [FromBody] CreateStockTransferRequestDto dto,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader,
        [FromHeader(Name = "X-Request-Key")] string? xRequestKeyHeader,
        CancellationToken ct)
    {
        if (dto == null)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockTransfers, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Dữ liệu yêu cầu không được để trống.");
        }

        var rawHeaderKey = !string.IsNullOrWhiteSpace(idempotencyKeyHeader) ? idempotencyKeyHeader : xRequestKeyHeader;
        if (!string.IsNullOrWhiteSpace(rawHeaderKey))
        {
            if (Guid.TryParse(rawHeaderKey.Trim(), out var parsedHeaderGuid))
            {
                if (dto.RequestKey != Guid.Empty && dto.RequestKey != parsedHeaderGuid)
                {
                    return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockTransfers, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "RequestKey trong payload không khớp với Idempotency-Key trong header.");
                }

                if (dto.RequestKey == Guid.Empty)
                {
                    dto.RequestKey = parsedHeaderGuid;
                }
            }
            else
            {
                return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockTransfers, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Giá trị Idempotency-Key trong header không phải là GUID hợp lệ.");
            }
        }

        if (dto.Items != null && dto.Items.Any(i => i == null))
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockTransfers, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Danh sách mặt hàng chứa phần tử rỗng (null).");
        }

        var userId = User.GetUserId() ?? Guid.Empty;
        var result = await _stockTransferService.CreateTransferAsync(dto, userId, ct);

        if (result.IsReplay)
        {
            return Ok(result);
        }

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>
    /// Bước 1 - Xuất gửi hàng (Draft -> InTransit).
    /// Trừ OnHand kho/chi nhánh xuất, ghi Sổ cái OUT và snapshot WAC.
    /// </summary>
    [HttpPost("{id:guid}/dispatch")]
    [Authorize(Roles = "Owner,Staff")]
    [ApiErrorContract(ApiErrorProfile.StockTransfers)]
    public async Task<IActionResult> DispatchTransfer(
        [FromRoute] Guid id,
        [FromBody] DispatchStockTransferRequestDto? dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockTransfers, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã phiếu chuyển không được để trống (Guid.Empty).");
        }

        dto ??= new DispatchStockTransferRequestDto();

        var userId = User.GetUserId() ?? Guid.Empty;
        var result = await _stockTransferService.DispatchTransferAsync(id, dto, userId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Bước 2 - Tiếp nhận nhập kho đích (InTransit -> Received).
    /// Tăng OnHand kho/chi nhánh đích, ghi Sổ cái IN và cập nhật lại WAC đích.
    /// </summary>
    [HttpPost("{id:guid}/receive")]
    [Authorize(Roles = "Owner,Staff")]
    [ApiErrorContract(ApiErrorProfile.StockTransfers)]
    public async Task<IActionResult> ReceiveTransfer(
        [FromRoute] Guid id,
        [FromBody] ReceiveStockTransferRequestDto? dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockTransfers, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã phiếu chuyển không được để trống (Guid.Empty).");
        }

        dto ??= new ReceiveStockTransferRequestDto();

        var userId = User.GetUserId() ?? Guid.Empty;
        var result = await _stockTransferService.ReceiveTransferAsync(id, dto, userId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Hủy phiếu chuyển kho khi còn ở trạng thái Draft (Draft -> Cancelled).
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Owner,Staff")]
    [ApiErrorContract(ApiErrorProfile.StockTransfers)]
    public async Task<IActionResult> CancelTransfer(
        [FromRoute] Guid id,
        [FromBody] CancelStockTransferRequestDto? dto,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.StockTransfers, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã phiếu chuyển không được để trống (Guid.Empty).");
        }

        var userId = User.GetUserId() ?? Guid.Empty;
        var result = await _stockTransferService.CancelTransferAsync(id, dto, userId, ct);
        return Ok(result);
    }
}
