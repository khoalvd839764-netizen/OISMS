using System.Data;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Domain.Entities;

namespace OISM.Application.Features.Inventory.Services;

public interface IReservationService
{
    Task<ReserveStockResponseDto> ReserveStockAsync(ReserveStockRequestDto dto, Guid? userId, CancellationToken ct = default);
}

public class ReservationService : IReservationService
{
    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _currentTenantService;

    public ReservationService(IAppDbContext context, ICurrentTenantService currentTenantService)
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

    public async Task<ReserveStockResponseDto> ReserveStockAsync(ReserveStockRequestDto dto, Guid? userId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        if (dto.Quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dto.Quantity), "Số lượng giữ chỗ phải lớn hơn 0.");
        }

        // Bắt đầu một transaction READ COMMITTED bao bọc trong ExecutionStrategy
        var strategy = _context.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.BeginTransactionAsync(ct);

            try
            {
            // BE1-09: Lấy advisory lock theo (Tenant, Branch, SKU) trước khi đọc tồn kho để tránh Race Condition
            if (_context.IsRelationalDatabase)
            {
                await _context.ExecuteSqlRawAsync(
                    "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/' || {1}::text || '/' || {2}::text, 0));",
                    new object[] { tenantId.ToString(), dto.BranchId.ToString(), dto.SkuId.ToString() },
                    ct);
            }

            // Kiểm tra tồn kho tại Kho cụ thể
            var warehouseBalance = await _context.WarehouseInventoryBalances
                .FirstOrDefaultAsync(w => w.WarehouseId == dto.WarehouseId && w.SkuId == dto.SkuId, ct);

            if (warehouseBalance == null)
            {
                throw new InvalidOperationException($"Không tìm thấy tồn kho cho SKU '{dto.SkuId}' tại kho '{dto.WarehouseId}'. Tồn khả dụng = 0.");
            }

            var warehouseAvailable = warehouseBalance.OnHand - warehouseBalance.Reserved;
            if (warehouseAvailable < dto.Quantity)
            {
                throw new InvalidOperationException($"Tồn kho khả dụng tại kho không đủ. Cần {dto.Quantity}, khả dụng hiện tại: {warehouseAvailable}.");
            }

            // Kiểm tra tồn kho tại Chi nhánh
            var branchBalance = await _context.InventoryBalances
                .FirstOrDefaultAsync(b => b.BranchId == dto.BranchId && b.SkuId == dto.SkuId, ct);

            if (branchBalance == null)
            {
                throw new InvalidOperationException($"Không tìm thấy số dư chi nhánh cho SKU '{dto.SkuId}'.");
            }

            var branchAvailable = branchBalance.OnHand - branchBalance.Reserved;
            if (branchAvailable < dto.Quantity)
            {
                throw new InvalidOperationException($"Tồn kho khả dụng tại chi nhánh không đủ. Cần {dto.Quantity}, khả dụng hiện tại: {branchAvailable}.");
            }

            // Lấy thông tin SKU
            var sku = await _context.ProductVariants
                .Include(s => s.Product)
                .FirstOrDefaultAsync(s => s.Id == dto.SkuId, ct);

            if (sku == null)
            {
                throw new KeyNotFoundException($"Không tìm thấy SKU '{dto.SkuId}'.");
            }

            // Đảm bảo có Order và OrderItem tương ứng theo schema v0.5 FK constraints
            Order order;
            OrderItem orderItem;

            if (dto.OrderId.HasValue && dto.OrderItemId.HasValue)
            {
                order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == dto.OrderId.Value, ct)
                    ?? throw new KeyNotFoundException($"Không tìm thấy Order '{dto.OrderId.Value}'.");

                orderItem = await _context.OrderItems.FirstOrDefaultAsync(i => i.Id == dto.OrderItemId.Value, ct)
                    ?? throw new KeyNotFoundException($"Không tìm thấy OrderItem '{dto.OrderItemId.Value}'.");
            }
            else
            {
                // Tự sinh đơn hàng nháp tối thiểu cho luồng giữ chỗ (POS hoặc Test harness)
                var orderNumber = $"ORD-RES-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}";
                order = new Order
                {
                    TenantId = tenantId,
                    Number = orderNumber,
                    BranchId = dto.BranchId,
                    Channel = "POS",
                    Status = "Draft",
                    TotalAmount = dto.Quantity * sku.RetailPrice,
                    RequestKey = Guid.NewGuid(),
                    CreatedBy = userId,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                _context.Orders.Add(order);

                orderItem = new OrderItem
                {
                    TenantId = tenantId,
                    Order = order,
                    SkuId = dto.SkuId,
                    SkuCodeSnapshot = sku.SKU,
                    SkuNameSnapshot = sku.Product?.Name ?? sku.SKU,
                    Quantity = dto.Quantity,
                    UnitPrice = sku.RetailPrice,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                _context.OrderItems.Add(orderItem);
            }

            var duration = Math.Max(1, dto.DurationMinutes);
            var now = DateTimeOffset.UtcNow;
            var expiresAt = now.AddMinutes(duration);

            // Cập nhật Reserved ở cả 2 cấp đồng thời để vượt qua Deferred Trigger stock_consistency
            warehouseBalance.Reserved += dto.Quantity;
            warehouseBalance.UpdatedAt = now;

            branchBalance.Reserved += dto.Quantity;
            branchBalance.UpdatedAt = now;

            // Tạo Reservation mới
            var reservation = new Reservation
            {
                TenantId = tenantId,
                Order = order,
                OrderItem = orderItem,
                BranchId = dto.BranchId,
                WarehouseId = dto.WarehouseId,
                SkuId = dto.SkuId,
                Quantity = dto.Quantity,
                Status = "Active",
                ExpiresAt = expiresAt,
                CreatedAt = now
            };
            _context.Reservations.Add(reservation);

            await _context.SaveChangesAsync(ct);

            // Kiểm tra Deferred Constraints trước khi COMMIT
            if (_context.IsRelationalDatabase)
            {
                await _context.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE;", Array.Empty<object>(), ct);
            }

            await transaction.CommitAsync(ct);

            return new ReserveStockResponseDto
            {
                ReservationId = reservation.Id,
                OrderId = order.Id,
                OrderItemId = orderItem.Id,
                BranchId = dto.BranchId,
                WarehouseId = dto.WarehouseId,
                SkuId = dto.SkuId,
                Quantity = dto.Quantity,
                Status = reservation.Status,
                ExpiresAt = reservation.ExpiresAt,
                RemainingAvailableStock = branchBalance.OnHand - branchBalance.Reserved
            };
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
        });
    }
}
