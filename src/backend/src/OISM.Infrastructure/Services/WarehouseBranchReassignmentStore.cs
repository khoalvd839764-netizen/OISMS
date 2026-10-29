using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;

namespace OISM.Infrastructure.Services;

/// <summary>
/// Hiện thực hóa thao tác đổi chi nhánh kho hàng cho PostgreSQL và fallback InMemory.
/// Sử dụng điều kiện kiểm tra provider chính xác: Database.IsNpgsql().
/// </summary>
public class WarehouseBranchReassignmentStore : IWarehouseBranchReassignmentStore
{
    private readonly AppDbContext _context;

    public WarehouseBranchReassignmentStore(AppDbContext context)
    {
        _context = context;
    }

    public async Task<bool> ReassignBranchAsync(
        Guid warehouseId,
        Guid tenantId,
        Guid newBranchId,
        string name,
        bool isActive,
        DateTimeOffset updatedAt,
        CancellationToken ct = default)
    {
        if (_context.Database.IsNpgsql())
        {
            var affectedRows = await _context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE warehouses SET branch_id = {newBranchId}, name = {name}, is_active = {isActive}, updated_at = {updatedAt} WHERE id = {warehouseId} AND tenant_id = {tenantId}",
                ct);

            if (affectedRows == 0)
            {
                return false;
            }

            _context.ChangeTracker.Clear();
            return true;
        }
        else
        {
            var warehouse = await _context.Warehouses
                .FirstOrDefaultAsync(w => w.Id == warehouseId && w.TenantId == tenantId, ct);

            if (warehouse == null)
            {
                return false;
            }

            _context.Warehouses.Remove(warehouse);
            await _context.SaveChangesAsync(ct);

            var replacement = new Warehouse
            {
                Id = warehouse.Id,
                TenantId = warehouse.TenantId,
                BranchId = newBranchId,
                Name = name,
                IsActive = isActive,
                CreatedAt = warehouse.CreatedAt,
                UpdatedAt = updatedAt
            };

            _context.Warehouses.Add(replacement);
            await _context.SaveChangesAsync(ct);
            return true;
        }
    }
}
