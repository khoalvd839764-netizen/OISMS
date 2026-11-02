using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Features.PriceLists.Dtos;
using OISM.Application.Features.PriceLists.Services;

namespace OISM.WebApi.Controllers;

/// <summary>
/// API Quản lý Bảng giá và Phiên bản giá theo kênh / thời gian (Upgrade 04 - Bảng giá).
/// </summary>
[ApiController]
[Route("api/price-lists")]
[Authorize]
public class PriceListsController : ControllerBase
{
    private readonly IPriceListService _priceListService;

    public PriceListsController(IPriceListService priceListService)
    {
        _priceListService = priceListService;
    }

    [HttpGet]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> GetPriceLists(CancellationToken ct)
    {
        var result = await _priceListService.GetPriceListsAsync(ct);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> CreatePriceList([FromBody] CreatePriceListDto dto, CancellationToken ct)
    {
        var result = await _priceListService.CreatePriceListAsync(dto, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet("{id:guid}/versions")]
    [Authorize(Roles = "Owner,Staff")]
    public async Task<IActionResult> GetVersions([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _priceListService.GetVersionsAsync(id, ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/versions")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> CreateVersion([FromRoute] Guid id, [FromBody] CreatePriceVersionDto dto, CancellationToken ct)
    {
        var result = await _priceListService.CreateVersionAsync(id, dto, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }
}
