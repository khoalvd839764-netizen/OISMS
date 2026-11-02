using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.Catalog.Units;
using OISM.Application.Features.Catalog.Units.Services;

namespace OISM.WebApi.Controllers;

[ApiController]
[Authorize]
public class UnitsController : ControllerBase
{
    private readonly IUnitService _unitService;

    public UnitsController(IUnitService unitService)
    {
        _unitService = unitService;
    }

    /// <summary>
    /// Lấy danh sách đơn vị tính của Tenant (tự động seed nếu tenant mới chưa có).
    /// </summary>
    [HttpGet("api/units")]
    [Authorize(Roles = "Owner,Staff,Cashier")]
    public async Task<IActionResult> GetUnits(CancellationToken ct)
    {
        try
        {
            var units = await _unitService.GetUnitsAsync(ct);
            return Ok(units);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, ex.StatusCode, ex.Message);
        }
    }

    /// <summary>
    /// Lấy chi tiết đơn vị tính theo Id.
    /// </summary>
    [HttpGet("api/units/{id:guid}")]
    [Authorize(Roles = "Owner,Staff,Cashier")]
    public async Task<IActionResult> GetUnitById(Guid id, CancellationToken ct)
    {
        try
        {
            var unit = await _unitService.GetUnitByIdAsync(id, ct);
            if (unit == null)
            {
                return CreateProblemResult("UNIT_NOT_FOUND", 404, "Không tìm thấy đơn vị tính.");
            }
            return Ok(unit);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, ex.StatusCode, ex.Message);
        }
    }

    /// <summary>
    /// Tạo mới đơn vị tính.
    /// </summary>
    [HttpPost("api/units")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> CreateUnit([FromBody] CreateUnitRequest request, CancellationToken ct)
    {
        try
        {
            var unit = await _unitService.CreateUnitAsync(request, ct);
            return CreatedAtAction(nameof(GetUnitById), new { id = unit.Id }, unit);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, ex.StatusCode, ex.Message);
        }
    }

    /// <summary>
    /// Cập nhật thông tin đơn vị tính.
    /// </summary>
    [HttpPut("api/units/{id:guid}")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> UpdateUnit(Guid id, [FromBody] UpdateUnitRequest request, CancellationToken ct)
    {
        try
        {
            var unit = await _unitService.UpdateUnitAsync(id, request, ct);
            return Ok(unit);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, ex.StatusCode, ex.Message);
        }
    }

    /// <summary>
    /// Lấy danh sách cấu hình đơn vị tính cho biến thể SKU.
    /// </summary>
    [HttpGet("api/products/{productId:guid}/variants/{variantId:guid}/units")]
    [Authorize(Roles = "Owner,Staff,Cashier")]
    public async Task<IActionResult> GetSkuUnits(Guid productId, Guid variantId, CancellationToken ct)
    {
        try
        {
            var units = await _unitService.GetSkuUnitsAsync(productId, variantId, ct);
            return Ok(units);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, ex.StatusCode, ex.Message);
        }
    }

    /// <summary>
    /// Thêm cấu hình đơn vị tính giao dịch cho SKU.
    /// </summary>
    [HttpPost("api/products/{productId:guid}/variants/{variantId:guid}/units")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> CreateSkuUnit(Guid productId, Guid variantId, [FromBody] CreateSkuUnitRequest request, CancellationToken ct)
    {
        try
        {
            var userId = GetCurrentUserId();
            var unit = await _unitService.CreateSkuUnitAsync(productId, variantId, request, userId, ct);
            return Ok(unit);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, ex.StatusCode, ex.Message);
        }
    }

    /// <summary>
    /// Cập nhật cấu hình đơn vị tính cho SKU.
    /// </summary>
    [HttpPut("api/products/{productId:guid}/variants/{variantId:guid}/units/{skuUnitId:guid}")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> UpdateSkuUnit(Guid productId, Guid variantId, Guid skuUnitId, [FromBody] UpdateSkuUnitRequest request, CancellationToken ct)
    {
        try
        {
            var userId = GetCurrentUserId();
            var unit = await _unitService.UpdateSkuUnitAsync(productId, variantId, skuUnitId, request, userId, ct);
            return Ok(unit);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, ex.StatusCode, ex.Message);
        }
    }

    /// <summary>
    /// Tắt mềm cấu hình đơn vị tính cho SKU.
    /// </summary>
    [HttpDelete("api/products/{productId:guid}/variants/{variantId:guid}/units/{skuUnitId:guid}")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> DeleteSkuUnit(Guid productId, Guid variantId, Guid skuUnitId, CancellationToken ct)
    {
        try
        {
            var userId = GetCurrentUserId();
            await _unitService.DeleteSkuUnitAsync(productId, variantId, skuUnitId, userId, ct);
            return NoContent();
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, ex.StatusCode, ex.Message);
        }
    }

    /// <summary>
    /// Lấy lịch sử biến động hệ số quy đổi của đơn vị tính SKU.
    /// </summary>
    [HttpGet("api/products/{productId:guid}/variants/{variantId:guid}/units/{skuUnitId:guid}/history")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> GetSkuUnitHistory(Guid productId, Guid variantId, Guid skuUnitId, CancellationToken ct)
    {
        try
        {
            var history = await _unitService.GetSkuUnitHistoryAsync(productId, variantId, skuUnitId, ct);
            return Ok(history);
        }
        catch (OismException ex)
        {
            return CreateProblemResult(ex.ErrorCode, ex.StatusCode, ex.Message);
        }
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirst("userId")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(claim, out var userId))
        {
            return userId;
        }
        return null;
    }

    private ObjectResult CreateProblemResult(string errorCode, int statusCode, string detail)
    {
        var problem = new ProblemDetails
        {
            Type = $"https://oisms.local/errors/{errorCode}",
            Title = errorCode,
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
}
