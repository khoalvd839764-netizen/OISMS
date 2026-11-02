using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Inventory.Services;

namespace OISM.WebApi.Controllers;

/// <summary>
/// API Quản lý Phiếu nhập kho từ Nhà cung cấp (BE1-04, BE1-05, BE1-07, U01-A).
/// Áp dụng cho vai trò Chủ shop (Owner) và Nhân viên kho (Staff).
/// Nhân viên thu ngân (Cashier) bị từ chối truy cập (403 Forbidden).
/// </summary>
[ApiController]
[Route("api/goods-receipts")]
[Authorize(Roles = "Owner,Staff")]
public class GoodsReceiptsController : ControllerBase
{
    private readonly IGoodsReceiptService _goodsReceiptService;

    public GoodsReceiptsController(IGoodsReceiptService goodsReceiptService)
    {
        _goodsReceiptService = goodsReceiptService;
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirst("userId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(claim) || !Guid.TryParse(claim, out var userId))
        {
            throw new UnauthorizedAccessException("Không xác định được danh tính người dùng từ Token.");
        }
        return userId;
    }

    /// <summary>
    /// Lập phiếu nhập kho mới ở trạng thái Nháp (Draft) từ Phiếu mua hàng đã duyệt.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateGoodsReceiptDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            var firstError = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "Dữ liệu yêu cầu không hợp lệ.";
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, firstError);
        }

        if (dto.Items != null && dto.Items.Any(i => i == null))
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Danh sách mặt hàng không được chứa phần tử rỗng (null).");
        }

        try
        {
            var userId = GetCurrentUserId();
            var result = await _goodsReceiptService.CreateReceiptAsync(dto, userId, ct);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, GetTitleForErrorCode(ex.ErrorCode), ex.StatusCode, ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return CreateProblemResult("NOT_FOUND", "Không tìm thấy dữ liệu", StatusCodes.Status404NotFound, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Xung đột dữ liệu", StatusCodes.Status409Conflict, ex.Message);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23505")
        {
            return CreateProblemResult("VALIDATION_ERROR", "Xung đột dữ liệu", StatusCodes.Status409Conflict, $"Mã phiếu nhập '{dto.Number}' đã tồn tại trong gian hàng.");
        }
        catch (ArgumentException ex)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, ex.Message);
        }
    }

    /// <summary>
    /// Lấy danh sách phiếu nhập kho có phân trang và bộ lọc.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] GoodsReceiptFilterDto filter, CancellationToken ct)
    {
        var result = await _goodsReceiptService.GetReceiptsAsync(filter, ct);
        return Ok(result);
    }

    /// <summary>
    /// Xem chi tiết một phiếu nhập kho kèm danh sách mặt hàng SKU.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        try
        {
            var receipt = await _goodsReceiptService.GetReceiptByIdAsync(id, ct);
            if (receipt == null)
            {
                return CreateProblemResult("NOT_FOUND", "Không tìm thấy dữ liệu", StatusCodes.Status404NotFound, $"Không tìm thấy Phiếu nhập kho với ID '{id}'.");
            }
            return Ok(receipt);
        }
        catch (KeyNotFoundException ex)
        {
            return CreateProblemResult("NOT_FOUND", "Không tìm thấy dữ liệu", StatusCodes.Status404NotFound, ex.Message);
        }
    }

    /// <summary>
    /// Xác nhận nhập kho nguyên tử (Atomic Confirm - BE1-05, U01-A).
    /// </summary>
    [HttpPost("{id}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken ct)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _goodsReceiptService.ConfirmReceiptAsync(id, userId, ct);
            return Ok(result);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, GetTitleForErrorCode(ex.ErrorCode), ex.StatusCode, ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return CreateProblemResult("NOT_FOUND", "Không tìm thấy dữ liệu", StatusCodes.Status404NotFound, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return CreateProblemResult("INVALID_STATE_TRANSITION", "Chuyển trạng thái không hợp lệ", StatusCodes.Status409Conflict, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, ex.Message);
        }
    }

    /// <summary>
    /// Hủy phiếu nhập kho ở trạng thái Nháp (Draft -> Cancelled).
    /// </summary>
    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _goodsReceiptService.CancelReceiptAsync(id, userId, ct);
            return Ok(result);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, GetTitleForErrorCode(ex.ErrorCode), ex.StatusCode, ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return CreateProblemResult("NOT_FOUND", "Không tìm thấy dữ liệu", StatusCodes.Status404NotFound, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return CreateProblemResult("INVALID_STATE_TRANSITION", "Chuyển trạng thái không hợp lệ", StatusCodes.Status409Conflict, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return CreateProblemResult("VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, ex.Message);
        }
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
                ["timestamp"] = DateTimeOffset.UtcNow,
                ["message"] = detail
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
