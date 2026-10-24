using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Orders.Services;

namespace OISM.WebApi.Controllers;

/// <summary>
/// API Quản lý Trung tâm Đơn hàng Đa Kênh (FR-ORD-01, FR-ORD-02, FR-ORD-03).
/// Phục vụ cho Web Admin và Quầy POS. Hỗ trợ xem danh sách, lọc đa tiêu chí, tạo đơn, duyệt và hủy đơn.
/// </summary>
[ApiController]
[Route("api/orders")]
[Authorize(Roles = "Owner,Staff")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirst("userId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(claim, out var userId))
        {
            return userId;
        }
        return null;
    }

    /// <summary>
    /// Tra cứu danh sách đơn hàng đa kênh với bộ lọc (Trạng thái, Kênh bán, Chi nhánh, Thời gian, Từ khóa) và phân trang.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] OrderFilterDto filter, CancellationToken ct)
    {
        var result = await _orderService.GetOrdersAsync(filter, ct);
        return Ok(result);
    }

    /// <summary>
    /// Xem chi tiết một đơn hàng cụ thể kèm danh sách mặt hàng SKU, giá bán, giá vốn và thông tin khách hàng.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var order = await _orderService.GetOrderByIdAsync(id, ct);
        if (order == null)
        {
            return NotFound(new { message = $"Không tìm thấy Đơn hàng với ID '{id}'." });
        }
        return Ok(order);
    }

    /// <summary>
    /// Lập đơn hàng mới từ Web Admin hoặc kênh bán thủ công.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrderDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var userId = GetCurrentUserId();
            var result = await _orderService.CreateOrderAsync(dto, userId, ct);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Duyệt xác nhận đơn hàng (Draft / Reserved -> Confirmed).
    /// Trừ tồn OnHand, giảm Reserved tương ứng, chụp giá vốn WAC vào OrderItem và ghi Sổ cái kho xuất (OUT).
    /// </summary>
    [HttpPost("{id}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken ct)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _orderService.ConfirmOrderAsync(id, userId, ct);
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
    }

    /// <summary>
    /// Hủy đơn hàng (Draft / Reserved -> Cancelled).
    /// Giải phóng số lượng tồn kho giữ chỗ (Reserved) nếu có.
    /// </summary>
    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        try
        {
            var userId = GetCurrentUserId();
            var result = await _orderService.CancelOrderAsync(id, userId, ct);
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
    }
}
