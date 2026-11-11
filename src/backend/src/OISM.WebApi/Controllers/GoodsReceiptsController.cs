using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Inventory.Services;
using OISM.WebApi.Http;

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

    /// <summary>
    /// Lập phiếu nhập kho mới ở trạng thái Nháp (Draft) từ Phiếu mua hàng đã duyệt.
    /// </summary>
    [HttpPost]
    [ApiErrorContract(ApiErrorProfile.GoodsReceiptCreate)]
    public async Task<IActionResult> Create([FromBody] CreateGoodsReceiptDto dto, CancellationToken ct)
    {
        if (dto.Items != null && dto.Items.Any(i => i == null))
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.GoodsReceipts, "VALIDATION_ERROR", "Lỗi xác thực dữ liệu", StatusCodes.Status400BadRequest, "Danh sách mặt hàng không được chứa phần tử rỗng (null).");
        }

        var userId = RequireUserId();
        var result = await _goodsReceiptService.CreateReceiptAsync(dto, userId, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
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
    [ApiErrorContract(ApiErrorProfile.GoodsReceiptRead)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var receipt = await _goodsReceiptService.GetReceiptByIdAsync(id, ct);
        if (receipt == null)
        {
            return ApiErrorResponse.Problem(HttpContext, ApiErrorProfile.GoodsReceipts, "NOT_FOUND", "Không tìm thấy dữ liệu", StatusCodes.Status404NotFound, $"Không tìm thấy Phiếu nhập kho với ID '{id}'.");
        }
        return Ok(receipt);
    }

    /// <summary>
    /// Xác nhận nhập kho nguyên tử (Atomic Confirm - BE1-05, U01-A).
    /// </summary>
    [HttpPost("{id}/confirm")]
    [ApiErrorContract(ApiErrorProfile.GoodsReceipts)]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken ct)
    {
        var userId = RequireUserId();
        var result = await _goodsReceiptService.ConfirmReceiptAsync(id, userId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Hủy phiếu nhập kho ở trạng thái Nháp (Draft -> Cancelled).
    /// </summary>
    [HttpPost("{id}/cancel")]
    [ApiErrorContract(ApiErrorProfile.GoodsReceipts)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var userId = RequireUserId();
        var result = await _goodsReceiptService.CancelReceiptAsync(id, userId, ct);
        return Ok(result);
    }

    private Guid RequireUserId() =>
        User.GetUserId() ?? throw new UnauthorizedAccessException("Không xác định được danh tính người dùng từ Token.");
}
