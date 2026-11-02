using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Features.PurchaseOrders.Dtos;
using OISM.Application.Features.PurchaseOrders.Services;

namespace OISM.WebApi.Controllers;

/// <summary>
/// API Quản lý Đơn đặt mua hàng từ Nhà cung cấp (Upgrade 03 - Mua hàng Purchase Orders).
/// </summary>
[ApiController]
[Route("api/purchase-orders")]
[Authorize]
public class PurchaseOrdersController : ControllerBase
{
    private readonly IPurchaseOrderService _poService;

    public PurchaseOrdersController(IPurchaseOrderService poService)
    {
        _poService = poService;
    }

    [HttpGet]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> GetPurchaseOrders(CancellationToken ct)
    {
        var result = await _poService.GetPurchaseOrdersAsync(ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> GetPurchaseOrderById([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _poService.GetPurchaseOrderByIdAsync(id, ct);
        if (result == null) return NotFound(new { message = "Không tìm thấy đơn đặt hàng." });
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> CreatePurchaseOrder([FromBody] CreatePurchaseOrderDto dto, CancellationToken ct)
    {
        var result = await _poService.CreatePurchaseOrderAsync(dto, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> UpdateStatus([FromRoute] Guid id, [FromBody] UpdatePurchaseOrderStatusDto dto, CancellationToken ct)
    {
        var result = await _poService.UpdateStatusAsync(id, dto, ct);
        if (result == null) return NotFound(new { message = "Không tìm thấy đơn đặt hàng để cập nhật trạng thái." });
        return Ok(result);
    }
}
