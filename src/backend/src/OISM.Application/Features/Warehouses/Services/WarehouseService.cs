using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Warehouses;
using OISM.Domain.Entities;

namespace OISM.Application.Features.Warehouses.Services;

/// <summary>
/// Hiện thực hóa nghiệp vụ Quản lý Kho hàng thuộc Chi nhánh (BE2-08, FR-AUTH-04, S3-B01-FIX).
/// </summary>
public class WarehouseService : IWarehouseService
{
    private readonly IAppDbContext _context;
    private readonly ISqlLockService _lockService;
    private readonly IWarehouseBranchReassignmentStore _reassignmentStore;

    public WarehouseService(
        IAppDbContext context,
        ISqlLockService lockService,
        IWarehouseBranchReassignmentStore reassignmentStore)
    {
        _context = context;
        _lockService = lockService;
        _reassignmentStore = reassignmentStore;
    }

    public async Task<List<WarehouseResponse>> GetWarehousesAsync(Guid? branchId, CancellationToken ct = default)
    {
        var query = _context.Warehouses
            .Include(w => w.Branch)
            .AsQueryable();

        if (branchId.HasValue)
        {
            query = query.Where(w => w.BranchId == branchId.Value);
        }

        return await query
            .OrderBy(w => w.Name)
            .Select(w => new WarehouseResponse
            {
                Id = w.Id,
                BranchId = w.BranchId,
                BranchName = w.Branch != null ? w.Branch.Name : string.Empty,
                Name = w.Name,
                IsActive = w.IsActive,
                CreatedAt = w.CreatedAt
            })
            .ToListAsync(ct);
    }

    public async Task<WarehouseResponse?> GetWarehouseByIdAsync(Guid id, CancellationToken ct = default)
    {
        var warehouse = await _context.Warehouses
            .Include(w => w.Branch)
            .FirstOrDefaultAsync(w => w.Id == id, ct);

        if (warehouse == null)
        {
            return null;
        }

        return new WarehouseResponse
        {
            Id = warehouse.Id,
            BranchId = warehouse.BranchId,
            BranchName = warehouse.Branch != null ? warehouse.Branch.Name : string.Empty,
            Name = warehouse.Name,
            IsActive = warehouse.IsActive,
            CreatedAt = warehouse.CreatedAt
        };
    }

    public async Task<WarehouseResponse> CreateWarehouseAsync(CreateWarehouseRequest request, CancellationToken ct = default)
    {
        var branch = await _context.Branches.FirstOrDefaultAsync(b => b.Id == request.BranchId, ct);
        if (branch == null)
        {
            throw new OismException("BRANCH_NOT_FOUND", "Chi nhánh được chọn không tồn tại hoặc không thuộc cửa hàng của bạn.", 400);
        }

        var warehouse = new Warehouse
        {
            BranchId = request.BranchId,
            Name = request.Name.Trim(),
            IsActive = true
        };

        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync(ct);

        return new WarehouseResponse
        {
            Id = warehouse.Id,
            BranchId = warehouse.BranchId,
            BranchName = branch.Name,
            Name = warehouse.Name,
            IsActive = warehouse.IsActive,
            CreatedAt = warehouse.CreatedAt
        };
    }

    public async Task<WarehouseResponse?> UpdateWarehouseAsync(Guid id, UpdateWarehouseRequest request, Guid? tenantIdHint, CancellationToken ct = default)
    {
        var tenantId = tenantIdHint;
        if (!tenantId.HasValue)
        {
            var whStub = await _context.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);
            if (whStub == null)
            {
                return null;
            }
            tenantId = whStub.TenantId;
        }

        var strategy = _context.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<WarehouseResponse?>(async () =>
        {
            await using var tx = await _context.BeginTransactionAsync(ct);

            await _lockService.LockWarehouseAsync(id, tenantId.Value, ct);

            var warehouse = await _context.Warehouses
                .Include(w => w.Branch)
                .FirstOrDefaultAsync(w => w.Id == id && w.TenantId == tenantId.Value, ct);

            if (warehouse == null)
            {
                await tx.RollbackAsync(ct);
                return null;
            }

            var branch = warehouse.Branch;
            var trimmedName = request.Name.Trim();
            var isActive = request.IsActive;
            var now = DateTimeOffset.UtcNow;

            if (request.BranchId != warehouse.BranchId)
            {
                var hasReceipts = await _context.GoodsReceipts.AnyAsync(g => g.WarehouseId == id, ct);
                var hasBalances = await _context.WarehouseInventoryBalances.AnyAsync(b => b.WarehouseId == id, ct);
                var hasTransactions = await _context.InventoryTransactions.AnyAsync(t => t.WarehouseId == id, ct);
                var hasReservations = await _context.Reservations.AnyAsync(r => r.WarehouseId == id, ct);
                var hasStockTransfers = await _context.StockTransfers.AnyAsync(st => st.FromWarehouseId == id || st.ToWarehouseId == id, ct);
                var hasStockCounts = await _context.StockCounts.AnyAsync(sc => sc.WarehouseId == id, ct);
                var hasAdjustments = await _context.InventoryAdjustments.AnyAsync(ia => ia.WarehouseId == id, ct);

                if (hasReceipts || hasBalances || hasTransactions || hasReservations || hasStockTransfers || hasStockCounts || hasAdjustments)
                {
                    await tx.RollbackAsync(ct);
                    throw new OismException("WAREHOUSE_HAS_DEPENDENCIES",
                        "Không thể thay đổi chi nhánh của kho hàng đã có lịch sử chứng từ, tồn kho hoặc giao dịch liên kết.", 409);
                }

                var targetBranch = await _context.Branches.FirstOrDefaultAsync(b => b.Id == request.BranchId && b.TenantId == tenantId.Value, ct);
                if (targetBranch == null)
                {
                    await tx.RollbackAsync(ct);
                    throw new OismException("TARGET_BRANCH_INVALID",
                        "Chi nhánh được chọn không tồn tại hoặc không thuộc cửa hàng của bạn.", 400);
                }

                try
                {
                    var success = await _reassignmentStore.ReassignBranchAsync(
                        id, tenantId.Value, request.BranchId, trimmedName, isActive, now, ct);

                    if (!success)
                    {
                        await tx.RollbackAsync(ct);
                        return null;
                    }

                    await tx.CommitAsync(ct);
                }
                catch (Exception ex) when (IsForeignKeyViolation(ex))
                {
                    await tx.RollbackAsync(ct);
                    throw new OismException("FK_VIOLATION",
                        "Không thể thay đổi chi nhánh của kho hàng do vi phạm ràng buộc dữ liệu liên kết.", 409);
                }

                return new WarehouseResponse
                {
                    Id = warehouse.Id,
                    BranchId = request.BranchId,
                    BranchName = targetBranch.Name,
                    Name = trimmedName,
                    IsActive = isActive,
                    CreatedAt = warehouse.CreatedAt
                };
            }

            warehouse.Name = trimmedName;
            warehouse.IsActive = isActive;

            try
            {
                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (Exception ex) when (IsForeignKeyViolation(ex))
            {
                await tx.RollbackAsync(ct);
                throw new OismException("FK_VIOLATION",
                    "Không thể thay đổi chi nhánh của kho hàng do vi phạm ràng buộc dữ liệu liên kết.", 409);
            }

            return new WarehouseResponse
            {
                Id = warehouse.Id,
                BranchId = warehouse.BranchId,
                BranchName = branch?.Name ?? string.Empty,
                Name = warehouse.Name,
                IsActive = warehouse.IsActive,
                CreatedAt = warehouse.CreatedAt
            };
        });
    }

    public async Task<WarehouseToggleResult?> ToggleStatusAsync(Guid id, Guid? tenantIdHint, CancellationToken ct = default)
    {
        var tenantId = tenantIdHint;
        var query = _context.Warehouses.AsQueryable();
        if (tenantId.HasValue)
        {
            query = query.Where(w => w.TenantId == tenantId.Value);
        }

        var warehouse = await query.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (warehouse == null)
        {
            return null;
        }

        warehouse.IsActive = !warehouse.IsActive;
        await _context.SaveChangesAsync(ct);

        return new WarehouseToggleResult(warehouse.Id, warehouse.IsActive,
            $"Đã {(warehouse.IsActive ? "kích hoạt" : "ngưng hoạt động")} kho hàng thành công.");
    }

    private static bool IsForeignKeyViolation(Exception ex)
    {
        if (ex is DbUpdateException dbEx && dbEx.InnerException != null)
        {
            return dbEx.InnerException.Message.Contains("23503") ||
                   dbEx.InnerException.GetType().Name == "PostgresException";
        }

        return ex.Message.Contains("23503") || ex.GetType().Name == "PostgresException";
    }
}
