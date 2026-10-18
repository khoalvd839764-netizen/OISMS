using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Inventory.Services;

namespace OISM.WebApi.Controllers;

/// <summary>
/// API Quản lý Phiếu nhập kho từ Nhà cung cấp (BE1-04, BE1-05, BE1-07).
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
    /// Lập phiếu nhập kho mới ở trạng thái Nháp (Draft).
    /// Hỗ trợ nhiều dòng mặt hàng (SKU, số lượng, đơn giá nhập).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateGoodsReceiptDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (dto.Items != null && dto.Items.Any(i => i == null))
        {
            return BadRequest(new { message = "Danh sách mặt hàng không được chứa phần tử rỗng (null)." });
        }

        try
        {
            var userId = GetCurrentUserId();
            var result = await _goodsReceiptService.CreateReceiptAsync(dto, userId, ct);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23505")
        {
            return Conflict(new { message = $"Mã phiếu nhập '{dto.Number}' đã tồn tại trong gian hàng." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Lấy danh sách phiếu nhập kho có phân trang và bộ lọc (theo kho, chi nhánh, nhà cung cấp, trạng thái, thời gian).
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
                return NotFound(new { message = $"Không tìm thấy Phiếu nhập kho với ID '{id}'." });
            }
            return Ok(receipt);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Xác nhận nhập kho nguyên tử (Atomic Confirm - BE1-05).
    /// Thực hiện trong 1 transaction READ COMMITTED:
    /// - Khóa tài nguyên (Advisory lock)
    /// - Cập nhật tồn chi tiết tại kho và tổng tồn chi nhánh
    /// - Tự động tính lại Giá vốn bình quân gia quyền (WAC - BE1-07)
    /// - Ghi nhận Sổ cái kho bất biến (Append-Only Inventory Ledger - BE1-06)
    /// - Chuyển trạng thái phiếu sang 'Confirmed'
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
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
