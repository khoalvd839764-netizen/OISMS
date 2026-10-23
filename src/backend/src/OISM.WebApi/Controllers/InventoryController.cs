using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Inventory.Services;

namespace OISM.WebApi.Controllers;

/// <summary>
/// API Quản lý Tồn kho, Sổ cái kho và Giữ chỗ (BE1-06, BE1-08, BE1-09).
/// Phục vụ cả Web Admin (Quản trị) và Quầy POS (Bán lẻ).
/// </summary>
[ApiController]
[Route("api/inventory")]
[Authorize]
public class InventoryController : ControllerBase
{
    private readonly IInventoryService _inventoryService;
    private readonly IReservationService _reservationService;

    public InventoryController(
        IInventoryService inventoryService,
        IReservationService reservationService)
    {
        _inventoryService = inventoryService;
        _reservationService = reservationService;
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
    /// Tra cứu danh sách tồn kho theo Chi nhánh hoặc Kho (BE1-08).
    /// Hỗ trợ cả 3 vai trò: Owner, Staff, Cashier (POS tra cứu tồn khả dụng).
    /// </summary>
    [HttpGet("balances")]
    [Authorize(Roles = "Owner,Staff,Cashier")]
    public async Task<IActionResult> GetBalances([FromQuery] InventoryBalanceFilterDto filter, CancellationToken ct)
    {
        var result = await _inventoryService.GetBalancesAsync(filter, ct);
        return Ok(result);
    }

    /// <summary>
    /// Tra cứu tồn kho chi tiết của một SKU tại một Chi nhánh cụ thể (BE1-08, phục vụ POS).
    /// </summary>
    [HttpGet("balances/sku/{skuId}/branch/{branchId}")]
    [Authorize(Roles = "Owner,Staff,Cashier")]
    public async Task<IActionResult> GetSkuBalanceAtBranch(Guid skuId, Guid branchId, CancellationToken ct)
    {
        var balance = await _inventoryService.GetSkuBalanceAtBranchAsync(branchId, skuId, ct);
        if (balance == null)
        {
            return NotFound(new { message = $"Không tìm thấy số dư cho SKU '{skuId}' tại chi nhánh '{branchId}'." });
        }
        return Ok(balance);
    }

    /// <summary>
    /// Tra cứu lịch sử Sổ cái kho bất biến (Inventory Ledger - BE1-06).
    /// Chỉ dành cho vai trò Quản lý: Owner và Staff. Thu ngân (Cashier) bị từ chối (403 Forbidden).
    /// Không hỗ trợ API Sửa hoặc Xóa (Append-Only).
    /// </summary>
    [HttpGet("ledger")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> GetLedger([FromQuery] InventoryLedgerFilterDto filter, CancellationToken ct)
    {
        var result = await _inventoryService.GetLedgerAsync(filter, ct);
        return Ok(result);
    }

    /// <summary>
    /// Giữ chỗ tồn kho chống bán âm / Overselling (BE1-09).
    /// Khóa giao dịch đồng thời (Advisory Lock), kiểm tra Available, cập nhật Reserved ở 2 cấp và sinh Reservation Active.
    /// Hỗ trợ Owner, Staff, Cashier.
    /// Hỗ trợ Idempotency chống giữ chỗ lặp khi thử lại qua thuộc tính RequestKey trong Body hoặc Header 'Idempotency-Key' / 'X-Request-Key'.
    /// </summary>
    [HttpPost("reserve")]
    [Authorize(Roles = "Owner,Staff,Cashier")]
    public async Task<IActionResult> Reserve(
        [FromBody] ReserveStockRequestDto dto,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader,
        [FromHeader(Name = "X-Request-Key")] string? xRequestKeyHeader,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        // Ưu tiên RequestKey từ Body; nếu chưa có thì đọc từ Header Idempotency-Key hoặc X-Request-Key
        if (!dto.RequestKey.HasValue)
        {
            var rawHeaderKey = !string.IsNullOrWhiteSpace(idempotencyKeyHeader) ? idempotencyKeyHeader : xRequestKeyHeader;
            if (!string.IsNullOrWhiteSpace(rawHeaderKey) && Guid.TryParse(rawHeaderKey.Trim(), out var parsedHeaderGuid))
            {
                dto.RequestKey = parsedHeaderGuid;
            }
        }

        try
        {
            var userId = GetCurrentUserId();
            var result = await _reservationService.ReserveStockAsync(dto, userId, ct);
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
