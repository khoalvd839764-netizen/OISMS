using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;

namespace OISM.Application.Features.Pos;

/// <summary>
/// Hiện thực hóa nghiệp vụ tra cứu sản phẩm và tồn kho tại quầy POS (FR-POS-01, BE2-07).
/// </summary>
public class PosCatalogService : IPosCatalogService
{
    private readonly IAppDbContext _context;

    public PosCatalogService(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<List<PosProductSearchResponse>> SearchAsync(string query, Guid? branchId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 1)
        {
            return new List<PosProductSearchResponse>();
        }

        if (branchId.HasValue)
        {
            var branchExists = await _context.Branches.AnyAsync(b => b.Id == branchId.Value, ct);
            if (!branchExists)
            {
                throw new OismException("BRANCH_NOT_FOUND", "Chi nhánh được chọn không tồn tại hoặc không thuộc cửa hàng của bạn.", 400);
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
            .ToListAsync(ct);

        var variantIds = variants.Select(v => v.Id).ToList();

        var stockMap = new Dictionary<Guid, (decimal OnHand, decimal Reserved)>();
        if (branchId.HasValue && variantIds.Count > 0)
        {
            var balances = await _context.InventoryBalances
                .AsNoTracking()
                .Where(b => b.BranchId == branchId.Value && variantIds.Contains(b.SkuId))
                .Select(b => new { b.SkuId, b.OnHand, b.Reserved })
                .ToListAsync(ct);

            stockMap = balances.ToDictionary(b => b.SkuId, b => (b.OnHand, b.Reserved));
        }

        return variants.Select(v =>
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
    }

    public async Task<PosProductSearchResponse?> GetByBarcodeAsync(string barcode, Guid? branchId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            throw new OismException("EMPTY_BARCODE", "Mã vạch không được để trống.", 400);
        }

        if (branchId.HasValue)
        {
            var branchExists = await _context.Branches.AnyAsync(b => b.Id == branchId.Value, ct);
            if (!branchExists)
            {
                throw new OismException("BRANCH_NOT_FOUND", "Chi nhánh được chọn không tồn tại hoặc không thuộc cửa hàng của bạn.", 400);
            }
        }

        var v = await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .Where(v => v.IsActive && v.Product != null && v.Product.IsActive && v.Barcode == barcode.Trim())
            .FirstOrDefaultAsync(ct);

        if (v == null)
        {
            return null;
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
                .FirstOrDefaultAsync(ct);

            onHand = bal?.OnHand ?? 0;
            reserved = bal?.Reserved ?? 0;
            available = Math.Max(0, onHand.Value - reserved.Value);
        }

        return new PosProductSearchResponse
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
    }
}
