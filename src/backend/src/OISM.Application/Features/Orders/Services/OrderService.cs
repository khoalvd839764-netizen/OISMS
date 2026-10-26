using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Orders.Dtos;
using OISM.Domain.Entities;

namespace OISM.Application.Features.Orders.Services;

public class OrderService : IOrderService
{
    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _currentTenantService;

    public OrderService(IAppDbContext context, ICurrentTenantService currentTenantService)
    {
        _context = context;
        _currentTenantService = currentTenantService;
    }

    private Guid RequireTenantId()
    {
        var tenantId = _currentTenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Yêu cầu xác thực gian hàng (TenantId hợp lệ).");
        }
        return tenantId.Value;
    }

    public async Task<PagedResult<OrderSummaryDto>> GetOrdersAsync(OrderFilterDto filter, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var query = _context.Orders
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var statusTrim = filter.Status.Trim();
            query = query.Where(o => o.Status == statusTrim);
        }

        if (!string.IsNullOrWhiteSpace(filter.Channel))
        {
            var channelTrim = filter.Channel.Trim();
            query = query.Where(o => o.Channel == channelTrim);
        }

        if (filter.BranchId.HasValue && filter.BranchId.Value != Guid.Empty)
        {
            query = query.Where(o => o.BranchId == filter.BranchId.Value);
        }

        if (filter.FromDate.HasValue)
        {
            query = query.Where(o => o.CreatedAt >= filter.FromDate.Value);
        }

        if (filter.ToDate.HasValue)
        {
            query = query.Where(o => o.CreatedAt <= filter.ToDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLower();
            query = query.Where(o =>
                o.Number.ToLower().Contains(search) ||
                (o.CustomerName != null && o.CustomerName.ToLower().Contains(search)) ||
                (o.CustomerPhone != null && o.CustomerPhone.Contains(search)) ||
                (o.ExternalOrderId != null && o.ExternalOrderId.ToLower().Contains(search)));
        }

        var totalCount = await query.CountAsync(ct);

        var pageIndex = Math.Max(1, filter.PageIndex);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new OrderSummaryDto
            {
                Id = o.Id,
                Number = o.Number,
                BranchId = o.BranchId,
                BranchName = o.Branch != null ? o.Branch.Name : null,
                Channel = o.Channel,
                ExternalShopId = o.ExternalShopId,
                ExternalOrderId = o.ExternalOrderId,
                Status = o.Status,
                CustomerName = o.CustomerName,
                CustomerPhone = o.CustomerPhone,
                TotalAmount = o.TotalAmount,
                ItemCount = o.Items.Count,
                CreatedAt = o.CreatedAt,
                ConfirmedAt = o.ConfirmedAt,
                CompletedAt = o.CompletedAt,
                CancelledAt = o.CancelledAt
            })
            .ToListAsync(ct);

        return new PagedResult<OrderSummaryDto>
        {
            Items = orders,
            PageIndex = pageIndex,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<OrderDetailDto?> GetOrderByIdAsync(Guid id, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var order = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Branch)
            .Include(o => o.Items)
                .ThenInclude(i => i.Sku)
                    .ThenInclude(s => s!.Product)
            .FirstOrDefaultAsync(o => o.Id == id && o.TenantId == tenantId, ct);

        if (order == null)
        {
            return null;
        }

        return MapToDetailDto(order);
    }

    public async Task<OrderDetailDto> CreateOrderAsync(CreateOrderDto dto, Guid? userId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        if (dto.Items == null || dto.Items.Count == 0)
        {
            throw new ArgumentException("Đơn hàng phải chứa ít nhất một mặt hàng.");
        }

        var branch = await _context.Branches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == dto.BranchId && b.TenantId == tenantId, ct);

        if (branch == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy Chi nhánh với ID '{dto.BranchId}'.");
        }

        var skuIds = dto.Items.Select(i => i.SkuId).Distinct().ToList();
        var skus = await _context.Skus
            .Include(s => s.Product)
            .Where(s => skuIds.Contains(s.Id) && s.TenantId == tenantId)
            .ToDictionaryAsync(s => s.Id, ct);

        foreach (var item in dto.Items)
        {
            if (!skus.ContainsKey(item.SkuId))
            {
                throw new KeyNotFoundException($"Không tìm thấy SKU với ID '{item.SkuId}' trong gian hàng.");
            }
        }

        var number = string.IsNullOrWhiteSpace(dto.Number)
            ? $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}"
            : dto.Number.Trim();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Number = number,
            BranchId = dto.BranchId,
            Channel = string.IsNullOrWhiteSpace(dto.Channel) ? "Admin" : dto.Channel.Trim(),
            ExternalShopId = dto.ExternalShopId?.Trim(),
            ExternalOrderId = dto.ExternalOrderId?.Trim(),
            CustomerName = dto.CustomerName?.Trim(),
            CustomerPhone = dto.CustomerPhone?.Trim(),
            ShippingAddress = dto.ShippingAddress?.Trim(),
            Status = dto.InitialStatus == "Reserved" ? "Reserved" : "Draft",
            RequestKey = dto.RequestKey ?? Guid.NewGuid(),
            CreatedBy = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        decimal totalAmount = 0;

        foreach (var itemReq in dto.Items)
        {
            var sku = skus[itemReq.SkuId];
            var lineTotal = itemReq.Quantity * itemReq.UnitPrice;
            totalAmount += lineTotal;

            // Đọc snapshot giá vốn WAC hiện thời nếu có
            var balance = await _context.InventoryBalances
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.BranchId == dto.BranchId && b.SkuId == sku.Id && b.TenantId == tenantId, ct);

            var orderItem = new OrderItem
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                OrderId = order.Id,
                SkuId = sku.Id,
                SkuCodeSnapshot = sku.SKU,
                SkuNameSnapshot = sku.Product?.Name ?? sku.SKU,
                Quantity = itemReq.Quantity,
                UnitPrice = itemReq.UnitPrice,
                CostPrice = balance?.AverageCost ?? 0,
                CreatedAt = DateTimeOffset.UtcNow
            };

            order.Items.Add(orderItem);
        }

        order.TotalAmount = totalAmount;

        _context.Orders.Add(order);
        await _context.SaveChangesAsync(ct);

        return MapToDetailDto(order, branch.Name);
    }

    public async Task<OrderDetailDto> ConfirmOrderAsync(Guid id, Guid? userId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var order = await _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.Items)
            .Include(o => o.Reservations)
            .FirstOrDefaultAsync(o => o.Id == id && o.TenantId == tenantId, ct);

        if (order == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy Đơn hàng với ID '{id}'.");
        }

        if (order.Status == "Confirmed" || order.Status == "Completed")
        {
            return MapToDetailDto(order);
        }

        if (order.Status == "Cancelled")
        {
            throw new InvalidOperationException("Không thể duyệt đơn hàng đã bị hủy.");
        }

        var wasReserved = order.Status == "Reserved";

        // Thực hiện cập nhật kho cho từng mặt hàng trong đơn
        foreach (var item in order.Items)
        {
            var balance = await _context.InventoryBalances
                .FirstOrDefaultAsync(b => b.BranchId == order.BranchId && b.SkuId == item.SkuId && b.TenantId == tenantId, ct);

            if (balance != null)
            {
                // Trừ OnHand
                balance.OnHand = Math.Max(0, balance.OnHand - item.Quantity);

                // Nếu trước đó đơn đã được giữ chỗ thì giải phóng số lượng giữ chỗ tương ứng
                if (wasReserved)
                {
                    balance.Reserved = Math.Max(0, balance.Reserved - item.Quantity);
                }

                balance.LastSequence++;
                item.CostPrice = balance.AverageCost;

                // Ghi sổ cái bất biến (Append-Only Inventory Ledger)
                var transaction = new InventoryTransaction
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    BranchId = order.BranchId,
                    WarehouseId = Guid.Empty, // Theo branch
                    SkuId = item.SkuId,
                    Type = "OUT",
                    Quantity = item.Quantity,
                    UnitCost = balance.AverageCost,
                    BalanceAfter = balance.OnHand,
                    SequenceNo = balance.LastSequence,
                    OrderItemId = item.Id,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                _context.InventoryTransactions.Add(transaction);
            }
        }

        // Đánh dấu các bản ghi giữ chỗ liên quan sang 'Consumed'
        foreach (var res in order.Reservations.Where(r => r.Status == "Active"))
        {
            res.Status = "Consumed";
        }

        order.Status = "Confirmed";
        order.ConfirmedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(ct);

        return MapToDetailDto(order);
    }

    public async Task<OrderDetailDto> ProcessOrderAsync(Guid id, Guid? userId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var order = await _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.Items)
            .Include(o => o.Reservations)
            .FirstOrDefaultAsync(o => o.Id == id && o.TenantId == tenantId, ct);

        if (order == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy Đơn hàng với ID '{id}'.");
        }

        if (order.Status == "Processing")
        {
            return MapToDetailDto(order);
        }

        if (order.Status == "Draft" || order.Status == "Reserved")
        {
            // Tự động confirm trước khi chuyển sang Processing
            await ConfirmOrderAsync(id, userId, ct);
            order = await _context.Orders
                .Include(o => o.Branch)
                .Include(o => o.Items)
                .Include(o => o.Reservations)
                .FirstAsync(o => o.Id == id && o.TenantId == tenantId, ct);
        }

        if (order.Status != "Confirmed")
        {
            throw new InvalidOperationException($"Không thể chuyển sang xử lý đóng gói đối với đơn hàng ở trạng thái '{order.Status}'.");
        }

        order.Status = "Processing";
        order.UpdatedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(ct);

        return MapToDetailDto(order);
    }

    public async Task<OrderDetailDto> CompleteOrderAsync(Guid id, Guid? userId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var order = await _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.Items)
            .Include(o => o.Reservations)
            .FirstOrDefaultAsync(o => o.Id == id && o.TenantId == tenantId, ct);

        if (order == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy Đơn hàng với ID '{id}'.");
        }

        if (order.Status == "Completed")
        {
            return MapToDetailDto(order);
        }

        if (order.Status != "Confirmed" && order.Status != "Processing")
        {
            throw new InvalidOperationException($"Chỉ có thể hoàn tất giao hàng đối với đơn đã xác nhận hoặc đang đóng gói (Trạng thái hiện tại: '{order.Status}').");
        }

        order.Status = "Completed";
        order.CompletedAt = DateTimeOffset.UtcNow;
        order.UpdatedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(ct);

        return MapToDetailDto(order);
    }

    public async Task<OrderDetailDto> CancelOrderAsync(Guid id, Guid? userId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var order = await _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.Items)
            .Include(o => o.Reservations)
            .FirstOrDefaultAsync(o => o.Id == id && o.TenantId == tenantId, ct);

        if (order == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy Đơn hàng với ID '{id}'.");
        }

        if (order.Status == "Cancelled")
        {
            return MapToDetailDto(order);
        }

        if (order.Status == "Confirmed" || order.Status == "Completed")
        {
            throw new InvalidOperationException("Không thể hủy đơn hàng đã xác nhận hoặc hoàn tất giao hàng.");
        }

        // Giải phóng giữ chỗ nếu có
        if (order.Status == "Reserved")
        {
            foreach (var res in order.Reservations.Where(r => r.Status == "Active"))
            {
                res.Status = "Released";

                var balance = await _context.InventoryBalances
                    .FirstOrDefaultAsync(b => b.BranchId == res.BranchId && b.SkuId == res.SkuId && b.TenantId == tenantId, ct);

                if (balance != null)
                {
                    balance.Reserved = Math.Max(0, balance.Reserved - res.Quantity);
                }
            }
        }

        order.Status = "Cancelled";
        order.CancelledAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(ct);

        return MapToDetailDto(order);
    }

    private static OrderDetailDto MapToDetailDto(Order order, string? branchName = null)
    {
        return new OrderDetailDto
        {
            Id = order.Id,
            Number = order.Number,
            BranchId = order.BranchId,
            BranchName = branchName ?? order.Branch?.Name,
            Channel = order.Channel,
            ExternalShopId = order.ExternalShopId,
            ExternalOrderId = order.ExternalOrderId,
            Status = order.Status,
            CustomerName = order.CustomerName,
            CustomerPhone = order.CustomerPhone,
            ShippingAddress = order.ShippingAddress,
            TotalAmount = order.TotalAmount,
            RequestKey = order.RequestKey,
            ItemCount = order.Items.Count,
            CreatedAt = order.CreatedAt,
            ConfirmedAt = order.ConfirmedAt,
            CompletedAt = order.CompletedAt,
            CancelledAt = order.CancelledAt,
            Items = order.Items.Select(i => new OrderItemDto
            {
                Id = i.Id,
                SkuId = i.SkuId,
                SkuCode = i.SkuCodeSnapshot ?? i.Sku?.SKU ?? string.Empty,
                SkuName = i.SkuNameSnapshot ?? i.Sku?.Product?.Name ?? string.Empty,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                CostPrice = i.CostPrice
            }).ToList()
        };
    }
}
