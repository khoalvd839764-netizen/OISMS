using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Warehouses;
using OISM.Application.Features.Warehouses.Services;

namespace OISM.WebApi.Controllers;

/// <summary>
/// API Quản lý Kho hàng thuộc Chi nhánh (BE2-08, FR-AUTH-04, S3-B01-FIX).
/// </summary>
[ApiController]
[Route("api/warehouses")]
[Authorize]
public class WarehousesController : ControllerBase
{
    private readonly IWarehouseService _warehouseService;
    private readonly ICurrentTenantService? _currentTenantService;

    public WarehousesController(IWarehouseService warehouseService, ICurrentTenantService? currentTenantService = null)
    {
        _warehouseService = warehouseService;
        _currentTenantService = currentTenantService;
    }

    private Guid? GetCurrentTenantId()
    {
        var claim = User.FindFirst("tenantId")?.Value;
        if (!string.IsNullOrEmpty(claim) && Guid.TryParse(claim, out var tid))
        {
            return tid;
        }
        return _currentTenantService?.TenantId;
    }

    /// <summary>
    /// Lấy danh sách kho hàng thuộc về Cửa hàng của phiên đăng nhập (Hỗ trợ lọc theo Chi nhánh - branchId).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetWarehouses([FromQuery] Guid? branchId, CancellationToken ct)
    {
        var warehouses = await _warehouseService.GetWarehousesAsync(branchId, ct);
        return Ok(warehouses);
    }

    /// <summary>
    /// Lấy chi tiết thông tin một kho hàng theo Id.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetWarehouseById(Guid id, CancellationToken ct)
    {
        var warehouse = await _warehouseService.GetWarehouseByIdAsync(id, ct);
        if (warehouse == null)
        {
            return NotFound(new { message = "Không tìm thấy kho hàng." });
        }

        return Ok(warehouse);
    }

    /// <summary>
    /// Tạo kho hàng mới gắn với Chi nhánh (Yêu cầu quyền Owner hoặc Staff).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> CreateWarehouse([FromBody] CreateWarehouseRequest request, CancellationToken ct)
    {
        var validator = new CreateWarehouseValidator();
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        try
        {
            var response = await _warehouseService.CreateWarehouseAsync(request, ct);
            return StatusCode(201, response);
        }
        catch (OismException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Cập nhật thông tin kho hàng (Yêu cầu quyền Owner hoặc Staff - BE2-08, S3-B01-FIX).
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> UpdateWarehouse(Guid id, [FromBody] UpdateWarehouseRequest request, CancellationToken ct)
    {
        var validator = new UpdateWarehouseValidator();
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        try
        {
            var tenantId = GetCurrentTenantId();
            var response = await _warehouseService.UpdateWarehouseAsync(id, request, tenantId, ct);
            if (response == null)
            {
                return NotFound(new { message = "Không tìm thấy kho hàng." });
            }

            return Ok(response);
        }
        catch (OismException ex) when (ex.StatusCode == 409)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (OismException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Bật/Tắt trạng thái hoạt động của kho hàng (Yêu cầu quyền Owner hoặc Staff).
    /// </summary>
    [HttpPut("{id}/toggle-status")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> ToggleStatus(Guid id, CancellationToken ct)
    {
        var tenantId = GetCurrentTenantId();
        var result = await _warehouseService.ToggleStatusAsync(id, tenantId, ct);
        if (result == null)
        {
            return NotFound(new { message = "Không tìm thấy kho hàng." });
        }

        return Ok(new
        {
            message = result.Message,
            id = result.Id,
            isActive = result.IsActive
        });
    }
}
