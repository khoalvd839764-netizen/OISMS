using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Features.Units.Dtos;
using OISM.Application.Features.Units.Services;

namespace OISM.WebApi.Controllers;

/// <summary>
/// API Quản lý Đa đơn vị tính và Hệ số quy đổi theo SKU (Upgrade 01 - Đơn vị tính).
/// </summary>
[ApiController]
[Route("api/units")]
[Authorize]
public class UnitsController : ControllerBase
{
    private readonly IUnitService _unitService;

    public UnitsController(IUnitService unitService)
    {
        _unitService = unitService;
    }

    [HttpGet]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> GetUnits(CancellationToken ct)
    {
        var result = await _unitService.GetUnitsAsync(ct);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> CreateUnit([FromBody] CreateUnitDto dto, CancellationToken ct)
    {
        var result = await _unitService.CreateUnitAsync(dto, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet("skus/{skuId:guid}")]
    [Authorize(Roles = "Owner,Staff,Cashier")]
    public async Task<IActionResult> GetSkuUnits([FromRoute] Guid skuId, CancellationToken ct)
    {
        var result = await _unitService.GetSkuUnitsAsync(skuId, ct);
        return Ok(result);
    }

    [HttpPost("skus/{skuId:guid}")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> AddSkuUnit([FromRoute] Guid skuId, [FromBody] CreateSkuUnitDto dto, CancellationToken ct)
    {
        var result = await _unitService.AddSkuUnitAsync(skuId, dto, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }
}
