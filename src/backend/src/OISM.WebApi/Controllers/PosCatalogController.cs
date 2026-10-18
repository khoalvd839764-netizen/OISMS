using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Features.Pos;
using OISM.Infrastructure.Persistence;

namespace OISM.WebApi.Controllers;

/// <summary>
/// API phục vụ Quầy thu ngân POS: Tìm kiếm nhanh sản phẩm, quét mã vạch Barcode và tra cứu tồn khả dụng (FR-POS-01, BE2-07).
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
    /// Hỗ trợ tra cứu tồn khả dụng theo Chi nhánh (branchId) tái sử dụng công thức Available = OnHand - Reserved.
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string query, [FromQuery] Guid? branchId)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 1)
        {
            return Ok(new List<PosProductSearchResponse>());
        }

        if (branchId.HasValue)
        {
            var branchExists = await _context.Branches.AnyAsync(b => b.Id == branchId.Value);
            if (!branchExists)
            {
                return BadRequest(new { message = "Chi nhánh được chọn không tồn tại hoặc không thuộc cửa hàng của bạn." });
            }
        }

        var keyword = query.Trim().ToLower();

        var variants = await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .Where(v => v.IsActive && v.Product != null && v.Product.IsActive &&
                       (v.SKU.ToLower().Contains(keyword) ||
                        v.Product.Name.ToLower().Contains(keyword) ||
                        (v.Barcode != null && v.Barcode.ToLower().Contains(keyword))))
            .Take(20)
            .ToListAsync();

        var variantIds = variants.Select(v => v.Id).ToList();

        // Tra cứu số dư tồn kho theo branchId nếu có
        var stockMap = new Dictionary<Guid, (decimal OnHand, decimal Reserved)>();
        if (branchId.HasValue && variantIds.Count > 0)
        {
            var balances = await _context.InventoryBalances
                .AsNoTracking()
                .Where(b => b.BranchId == branchId.Value && variantIds.Contains(b.SkuId))
                .Select(b => new { b.SkuId, b.OnHand, b.Reserved })
                .ToListAsync();

            stockMap = balances.ToDictionary(b => b.SkuId, b => (b.OnHand, b.Reserved));
        }

        var results = variants.Select(v =>
        {
            decimal? onHand = null;
            decimal? reserved = null;
            decimal? available = null;

            if (branchId.HasValue)
            {
                if (stockMap.TryGetValue(v.Id, out var stock))
                {
                    onHand = stock.OnHand;
                    reserved = stock.Reserved;
                    available = Math.Max(0, stock.OnHand - stock.Reserved);
                }
                else
                {
                    onHand = 0;
                    reserved = 0;
                    available = 0;
                }
            }

            return new PosProductSearchResponse
            {
                VariantId = v.Id,
                ProductId = v.ProductId,
                SKU = v.SKU,
                ProductName = v.Product!.Name,
                VariantName = v.VariantName,
                Barcode = v.Barcode,
                RetailPrice = v.RetailPrice,
                Color = v.Color,
                Size = v.Size,
                Attributes = v.GetAttributes(),
                OnHand = onHand,
                Reserved = reserved,
                Available = available
            };
        }).ToList();

        return Ok(results);
    }

    /// <summary>
    /// Quét mã vạch (Barcode Scanner) tìm chính xác một SKU kèm tồn khả dụng (FR-POS-01, FR-PROD-03, BE2-07).
    /// </summary>
    [HttpGet("barcode/{barcode}")]
    public async Task<IActionResult> GetByBarcode(string barcode, [FromQuery] Guid? branchId)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return BadRequest(new { message = "Mã vạch không được để trống." });
        }

        if (branchId.HasValue)
        {
            var branchExists = await _context.Branches.AnyAsync(b => b.Id == branchId.Value);
            if (!branchExists)
            {
                return BadRequest(new { message = "Chi nhánh được chọn không tồn tại hoặc không thuộc cửa hàng của bạn." });
            }
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

        decimal? onHand = null;
        decimal? reserved = null;
        decimal? available = null;

        if (branchId.HasValue)
        {
            var bal = await _context.InventoryBalances
                .AsNoTracking()
                .Where(b => b.BranchId == branchId.Value && b.SkuId == v.Id)
                .Select(b => new { b.OnHand, b.Reserved })
                .FirstOrDefaultAsync();

            onHand = bal?.OnHand ?? 0;
            reserved = bal?.Reserved ?? 0;
            available = Math.Max(0, onHand.Value - reserved.Value);
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
            Size = v.Size,
            Attributes = v.GetAttributes(),
            OnHand = onHand,
            Reserved = reserved,
            Available = available
        };

        return Ok(variant);
    }
}
