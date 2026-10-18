using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Features.Pos;
using OISM.Infrastructure.Persistence;

namespace OISM.WebApi.Controllers;

/// <summary>
/// API phục vụ Quầy thu ngân POS: Tìm kiếm nhanh sản phẩm và quét mã vạch Barcode (FR-POS-01, NFR-PERF-01).
/// </summary>
[ApiController]
[Route("api/pos")]
[Authorize]
public class PosCatalogController : ControllerBase
{
    private readonly AppDbContext _context;

    public PosCatalogController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Tìm kiếm nhanh sản phẩm tại quầy POS theo Tên / SKU / Mã vạch (Độ trễ p95 < 200ms).
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 1)
        {
            return Ok(new List<PosProductSearchResponse>());
        }

        var keyword = query.Trim().ToLower();

        var variants = await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .Where(v => v.IsActive && v.Product != null && v.Product.IsActive &&
                       (v.SKU.ToLower().Contains(keyword) ||
                        v.Product.Name.ToLower().Contains(keyword) ||
                        (v.Barcode != null && v.Barcode.Contains(keyword))))
            .Take(20)
            .ToListAsync();

        var results = variants.Select(v => new PosProductSearchResponse
        {
            VariantId = v.Id,
            ProductId = v.ProductId,
            SKU = v.SKU,
            ProductName = v.Product!.Name,
            VariantName = v.VariantName,
            Barcode = v.Barcode,
            RetailPrice = v.RetailPrice,
            Color = v.Color,
            Size = v.Size
        }).ToList();

        return Ok(results);
    }

    /// <summary>
    /// Quét mã vạch (Barcode Scanner) tìm chính xác một SKU (FR-POS-01, FR-PROD-03).
    /// </summary>
    [HttpGet("barcode/{barcode}")]
    public async Task<IActionResult> GetByBarcode(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return BadRequest(new { message = "Mã vạch không được để trống." });
        }

        var v = await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .Where(v => v.IsActive && v.Product != null && v.Product.IsActive && v.Barcode == barcode.Trim())
            .FirstOrDefaultAsync();

        if (v == null)
        {
            return NotFound(new { message = $"Không tìm thấy sản phẩm nào khớp với mã vạch '{barcode}'." });
        }

        var variant = new PosProductSearchResponse
        {
            VariantId = v.Id,
            ProductId = v.ProductId,
            SKU = v.SKU,
            ProductName = v.Product?.Name ?? string.Empty,
            VariantName = v.VariantName,
            Barcode = v.Barcode,
            RetailPrice = v.RetailPrice,
            Color = v.Color,
            Size = v.Size
        };

        return Ok(variant);
    }
}
