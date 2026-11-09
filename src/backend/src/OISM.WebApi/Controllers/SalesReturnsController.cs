using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Features.SalesReturns.Dtos;
using OISM.Application.Features.SalesReturns.Services;
using OISM.WebApi.Http;

namespace OISM.WebApi.Controllers;

/// <summary>
/// Controller điều phối quy trình Lập phiếu trả hàng, xác nhận nhập kho và hoàn tiền (CS-01).
/// </summary>
[ApiController]
[Route("api/sales-returns")]
[Authorize]
public class SalesReturnsController : ControllerBase
{
    private readonly ISalesReturnService _salesReturnService;

    public SalesReturnsController(ISalesReturnService salesReturnService)
    {
        _salesReturnService = salesReturnService;
    }

    /// <summary>
    /// Lấy danh sách các dòng hàng có thể trả từ đơn hàng gốc (đã bán, đã trả, còn được trả).
    /// </summary>
    [HttpGet("/api/orders/{id:guid}/returnable-items")]
    [Authorize(Roles = "Owner,Staff")]
    [ApiErrorContract(ApiErrorProfile.SalesReturns)]
    public async Task<IActionResult> GetReturnableItems(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.SalesReturns, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã đơn hàng không được để trống.");
        }

        var result = await _salesReturnService.GetReturnableItemsAsync(id, ct);
        return Ok(result);
    }

    /// <summary>
    /// Lập phiếu trả hàng ở trạng thái Nháp (Draft).
    /// Bắt buộc Idempotency-Key hoặc X-Request-Key dạng UUID.
    /// Replay cùng RequestKey và cùng payload trả 200 OK; tạo mới trả 201 Created.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Staff")]
    [ApiErrorContract(ApiErrorProfile.SalesReturns)]
    public async Task<IActionResult> Create(
        [FromBody] CreateSalesReturnDto dto,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader,
        [FromHeader(Name = "X-Request-Key")] string? xRequestKeyHeader,
        CancellationToken ct)
    {
        if (dto == null)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.SalesReturns, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Dữ liệu yêu cầu không được để trống.");
        }

        var rawHeaderKey = !string.IsNullOrWhiteSpace(idempotencyKeyHeader) ? idempotencyKeyHeader : xRequestKeyHeader;
        if (string.IsNullOrWhiteSpace(rawHeaderKey) && !dto.RequestKey.HasValue)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.SalesReturns, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Bắt buộc cung cấp Idempotency-Key hoặc X-Request-Key hợp lệ.");
        }

        Guid parsedKey;
        if (!string.IsNullOrWhiteSpace(rawHeaderKey))
        {
            if (!Guid.TryParse(rawHeaderKey.Trim(), out parsedKey) || parsedKey == Guid.Empty)
            {
                return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.SalesReturns, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Giá trị Idempotency-Key hoặc X-Request-Key trong header không phải là GUID hợp lệ.");
            }

            if (dto.RequestKey.HasValue && dto.RequestKey.Value != Guid.Empty && dto.RequestKey.Value != parsedKey)
            {
                return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.SalesReturns, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "RequestKey trong payload không khớp với Idempotency-Key trong header.");
            }
        }
        else
        {
            parsedKey = dto.RequestKey!.Value;
        }

        var userId = User.GetUserId() ?? throw new UnauthorizedAccessException("Không xác định được danh tính người dùng từ Token.");
        var (result, isReplay) = await _salesReturnService.CreateAsync(dto, parsedKey, userId, ct);

        if (isReplay)
        {
            return Ok(result);
        }

        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>
    /// Danh sách phiếu trả hàng có phân trang và bộ lọc.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> GetList([FromQuery] SalesReturnFilterDto filter, CancellationToken ct)
    {
        var result = await _salesReturnService.GetListAsync(filter, ct);
        return Ok(result);
    }

    /// <summary>
    /// Chi tiết một phiếu trả hàng theo ID (bao gồm giá vốn gốc snapshot cho Owner và Staff).
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Owner,Staff")]
    [ApiErrorContract(ApiErrorProfile.SalesReturns)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.SalesReturns, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã phiếu trả không được để trống.");
        }

        var result = await _salesReturnService.GetByIdAsync(id, ct);
        return Ok(result);
    }

    /// <summary>
    /// Xác nhận phiếu trả hàng: nhập kho dòng sổ IN, tính lại WAC, ghi nhận hoàn tiền.
    /// </summary>
    [HttpPost("{id:guid}/confirm")]
    [Authorize(Roles = "Owner,Staff")]
    [ApiErrorContract(ApiErrorProfile.SalesReturns)]
    public async Task<IActionResult> Confirm(
        Guid id,
        [FromBody] ConfirmSalesReturnRequestDto? request,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.SalesReturns, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã phiếu trả không được để trống.");
        }

        var userId = User.GetUserId() ?? throw new UnauthorizedAccessException("Không xác định được danh tính người dùng từ Token.");
        var result = await _salesReturnService.ConfirmAsync(id, request?.RefundMethod, userId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Hủy phiếu trả hàng ở trạng thái Nháp (Draft).
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Owner,Staff")]
    [ApiErrorContract(ApiErrorProfile.SalesReturns)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.SalesReturns, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Mã phiếu trả không được để trống.");
        }

        var userId = User.GetUserId() ?? throw new UnauthorizedAccessException("Không xác định được danh tính người dùng từ Token.");
        var result = await _salesReturnService.CancelAsync(id, userId, ct);
        return Ok(result);
    }
}
