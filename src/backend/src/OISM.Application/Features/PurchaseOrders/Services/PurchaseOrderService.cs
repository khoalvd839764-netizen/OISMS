using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.PurchaseOrders.Dtos;

namespace OISM.Application.Features.PurchaseOrders.Services;

public class PurchaseOrderService : IPurchaseOrderService
{
    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _currentTenantService;

    private static readonly ConcurrentDictionary<Guid, List<PurchaseOrderDto>> _tenantOrders = new();
    private static int _sequence = 1;

    public PurchaseOrderService(IAppDbContext context, ICurrentTenantService currentTenantService)
    {
        _context = context;
        _currentTenantService = currentTenantService;
    }

    private Guid RequireTenantId()
    {
        var tenantId = _currentTenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Yêu cầu xác thực gian hàng hợp lệ.");
        }
        return tenantId.Value;
    }

    public Task<List<PurchaseOrderDto>> GetPurchaseOrdersAsync(CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();
        var orders = _tenantOrders.GetOrAdd(tenantId, _ => new List<PurchaseOrderDto>());

        lock (orders)
        {
            return Task.FromResult(orders.OrderByDescending(o => o.OrderedAt).ToList());
        }
    }

    public Task<PurchaseOrderDto?> GetPurchaseOrderByIdAsync(Guid id, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();
        var orders = _tenantOrders.GetOrAdd(tenantId, _ => new List<PurchaseOrderDto>());

        lock (orders)
        {
            return Task.FromResult(orders.FirstOrDefault(o => o.Id == id));
        }
    }

    public async Task<PurchaseOrderDto> CreatePurchaseOrderAsync(CreatePurchaseOrderDto dto, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        // Tra cứu thông tin NCC và Chi nhánh từ DB thật
        var supplier = await _context.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == dto.SupplierId, ct);
        var branch = await _context.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == dto.BranchId, ct);

        var skuIds = dto.Items.Select(i => i.SkuId).Distinct().ToList();
        var skus = await _context.ProductVariants.AsNoTracking().Where(s => s.TenantId == tenantId && skuIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);

        var poItems = dto.Items.Select(item =>
        {
            skus.TryGetValue(item.SkuId, out var skuInfo);
            return new PurchaseOrderItemDto
            {
                SkuId = item.SkuId,
                SkuCode = skuInfo?.Code ?? "SKU",
                SkuName = skuInfo?.VariantName ?? skuInfo?.Code ?? "Sản phẩm",
                QuantityOrdered = item.Quantity,
                QuantityReceived = 0,
                UnitCost = item.UnitCost
            };
        }).ToList();

        var seq = Interlocked.Increment(ref _sequence);
        var poNumber = $"PO-{DateTime.UtcNow:yyyyMMdd}-{seq:D5}";

        var newPo = new PurchaseOrderDto
        {
            Id = Guid.NewGuid(),
            PoNumber = poNumber,
            SupplierId = dto.SupplierId,
            SupplierName = supplier?.Name ?? "Nhà cung cấp",
            BranchId = dto.BranchId,
            BranchName = branch?.Name ?? "Chi nhánh",
            Status = "Draft",
            TotalAmount = poItems.Sum(i => i.Subtotal),
            Note = dto.Note,
            OrderedAt = DateTime.UtcNow,
            ExpectedDeliveryDate = dto.ExpectedDeliveryDate,
            Items = poItems
        };

        var orders = _tenantOrders.GetOrAdd(tenantId, _ => new List<PurchaseOrderDto>());
        lock (orders)
        {
            orders.Add(newPo);
        }

        return newPo;
    }

    public Task<PurchaseOrderDto?> UpdateStatusAsync(Guid id, UpdatePurchaseOrderStatusDto dto, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();
        var orders = _tenantOrders.GetOrAdd(tenantId, _ => new List<PurchaseOrderDto>());

        lock (orders)
        {
            var po = orders.FirstOrDefault(o => o.Id == id);
            if (po != null)
            {
                po.Status = dto.Status;
                if (!string.IsNullOrWhiteSpace(dto.Note))
                {
                    po.Note = dto.Note;
                }
            }
            return Task.FromResult(po);
        }
    }
}
