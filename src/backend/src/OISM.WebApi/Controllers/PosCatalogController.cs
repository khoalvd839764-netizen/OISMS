using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Orders.Services;
using OISM.Application.Features.Pos;
using OISM.Infrastructure.Persistence;

namespace OISM.WebApi.Controllers;

/// <summary>
/// API phục vụ Quầy thu ngân POS: Tìm kiếm nhanh sản phẩm, quét mã vạch Barcode, tra cứu tồn khả dụng và Thanh toán nhanh toàn giỏ (FR-POS-01, FR-POS-02, BE2-07, BE1-10).
/// </summary>
[ApiController]
[Route("api/pos")]
[Authorize]
public class PosCatalogController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IPosCheckoutService _posCheckoutService;

    public PosCatalogController(AppDbContext context, IPosCheckoutService posCheckoutService)
    {
        _context = context;
        _posCheckoutService = posCheckoutService;
    }

    /// <summary>
    /// Thanh toán nhanh nguyên tử toàn giỏ hàng tại quầy POS (FR-POS-02, BE1-10).
    /// Tuyến đường chính thức: POST /api/pos/checkout.
    /// Hỗ trợ vai trò Owner, Staff, Cashier.
    /// Tạo đơn Completed, thanh toán Succeeded, trừ tồn hai cấp, ghi ledger OUT, snapshot WAC và sinh hóa đơn trong 1 transaction duy nhất.
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

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirst("userId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var userId) ? userId : null;
    }

    private ObjectResult CreateProblemResult(string errorCode, string title, int statusCode, string detail)
    {
        var problem = new ProblemDetails
        {
            Type = $"https://oisms.local/errors/{errorCode}",
            Title = title,
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

    private static string GetTitleForErrorCode(string errorCode) => errorCode switch
    {
        "INSUFFICIENT_STOCK" => "Tồn kho không đủ",
        "IDEMPOTENCY_PAYLOAD_MISMATCH" => "Xung đột khóa Idempotency",
        "VALIDATION_ERROR" => "Lỗi xác thực dữ liệu",
        "BRANCH_INACTIVE_OR_INVALID" => "Chi nhánh không hợp lệ",
        "SKU_NOT_FOUND" => "Mặt hàng không tồn tại",
        "SKU_INACTIVE" => "Mặt hàng đã ngừng hoạt động",
        "PRICE_MISMATCH" => "Sai lệch đơn giá niêm yết",
        "INSUFFICIENT_CASH_RECEIVED" => "Tiền khách đưa không đủ",
        "INVALID_PAYMENT_METHOD" => "Phương thức thanh toán không hợp lệ",
        "NO_ACTIVE_WAREHOUSE" => "Không tìm thấy kho khả dụng",
        _ => "Lỗi xử lý nghiệp vụ"
    };
}
