using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Orders.Services;
using OISM.Application.Features.Pos;

namespace OISM.WebApi.Controllers;

/// <summary>
/// API phục vụ Quầy thu ngân POS: Tìm kiếm nhanh sản phẩm, quét mã vạch Barcode, tra cứu tồn khả dụng và Thanh toán nhanh toàn giỏ (FR-POS-01, FR-POS-02, BE2-07, BE1-10).
/// </summary>
[ApiController]
[Route("api/pos")]
[Authorize]
public class PosCatalogController : ControllerBase
{
    private readonly IPosCatalogService _posCatalogService;
    private readonly IPosCheckoutService _posCheckoutService;

    public PosCatalogController(IPosCatalogService posCatalogService, IPosCheckoutService posCheckoutService)
    {
        _posCatalogService = posCatalogService;
        _posCheckoutService = posCheckoutService;
    }

    /// <summary>
    /// Thanh toán nhanh nguyên tử toàn giỏ hàng tại quầy POS (FR-POS-02, BE1-10).
    /// Tuyến đường chính thức: POST /api/pos/checkout.
    /// Hỗ trợ vai trò Owner, Staff, Cashier.
    /// </summary>
    [HttpPost("checkout")]
    [Authorize(Roles = "Owner,Staff,Cashier")]
    public async Task<IActionResult> Checkout(
        [FromBody] PosCheckoutRequestDto dto,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader,
        [FromHeader(Name = "X-Request-Key")] string? xRequestKeyHeader,
        CancellationToken ct)
    {
        return await PosCheckoutActionHelper.ExecuteCheckoutAsync(this, _posCheckoutService, dto, idempotencyKeyHeader, xRequestKeyHeader, ct);
    }

    /// <summary>
    /// Tìm kiếm nhanh sản phẩm tại quầy POS theo Tên / SKU / Mã vạch (Độ trễ p95 < 200ms).
    /// Hỗ trợ tra cứu tồn khả dụng theo Chi nhánh (branchId) tái sử dụng công thức Available = OnHand - Reserved.
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string query, [FromQuery] Guid? branchId, CancellationToken ct)
    {
        try
        {
            var results = await _posCatalogService.SearchAsync(query, branchId, ct);
            return Ok(results);
        }
        catch (OismException ex) when (ex.StatusCode == 400)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Quét mã vạch (Barcode Scanner) tìm chính xác một SKU kèm tồn khả dụng (FR-POS-01, FR-PROD-03, BE2-07).
    /// </summary>
    [HttpGet("barcode/{barcode}")]
    public async Task<IActionResult> GetByBarcode(string barcode, [FromQuery] Guid? branchId, CancellationToken ct)
    {
        try
        {
            var result = await _posCatalogService.GetByBarcodeAsync(barcode, branchId, ct);
            if (result == null)
            {
                return NotFound(new { message = $"Không tìm thấy sản phẩm nào khớp với mã vạch '{barcode}'." });
            }

            return Ok(result);
        }
        catch (OismException ex) when (ex.StatusCode == 400)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
