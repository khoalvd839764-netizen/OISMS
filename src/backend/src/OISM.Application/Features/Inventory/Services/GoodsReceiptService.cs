using System.Data;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Domain.Entities;

namespace OISM.Application.Features.Inventory.Services;

public interface IGoodsReceiptService
{
    Task<GoodsReceiptResponseDto> CreateReceiptAsync(CreateGoodsReceiptDto dto, Guid userId, CancellationToken ct = default);
    Task<PagedResult<GoodsReceiptResponseDto>> GetReceiptsAsync(GoodsReceiptFilterDto filter, CancellationToken ct = default);
    Task<GoodsReceiptResponseDto?> GetReceiptByIdAsync(Guid id, CancellationToken ct = default);
    Task<GoodsReceiptResponseDto> ConfirmReceiptAsync(Guid id, Guid userId, CancellationToken ct = default);
}

public class GoodsReceiptService : IGoodsReceiptService
{
    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _currentTenantService;
    private readonly IWacCalculator _wacCalculator;

    public GoodsReceiptService(
        IAppDbContext context,
        ICurrentTenantService currentTenantService,
        IWacCalculator wacCalculator)
    {
        _context = context;
        _currentTenantService = currentTenantService;
        _wacCalculator = wacCalculator;
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

    public async Task<GoodsReceiptResponseDto> CreateReceiptAsync(CreateGoodsReceiptDto dto, Guid userId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        // 1. Kiểm tra Chi nhánh
        var branch = await _context.Branches.FirstOrDefaultAsync(b => b.Id == dto.BranchId, ct);
        if (branch == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy Chi nhánh với ID '{dto.BranchId}'.");
        }

        // 2. Kiểm tra Kho hàng thuộc đúng Chi nhánh
        var warehouse = await _context.Warehouses.FirstOrDefaultAsync(w => w.Id == dto.WarehouseId && w.BranchId == dto.BranchId, ct);
        if (warehouse == null)
        {
            throw new InvalidOperationException($"Kho hàng ID '{dto.WarehouseId}' không tồn tại hoặc không thuộc Chi nhánh '{branch.Name}'.");
        }

        // 3. Kiểm tra Nhà cung cấp
        var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.Id == dto.SupplierId, ct);
        if (supplier == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy Nhà cung cấp với ID '{dto.SupplierId}'.");
        }

        // 4. Kiểm tra các dòng SKU
        if (dto.Items == null || dto.Items.Count == 0)
        {
            throw new InvalidOperationException("Phiếu nhập phải có ít nhất 1 dòng mặt hàng.");
        }

        var skuIds = dto.Items.Select(i => i.SkuId).Distinct().ToList();
        var existingSkus = await _context.ProductVariants
            .Include(v => v.Product)
            .Where(v => skuIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, ct);

        if (existingSkus.Count != skuIds.Count)
        {
            var missing = skuIds.Except(existingSkus.Keys).First();
            throw new KeyNotFoundException($"Mặt hàng SKU với ID '{missing}' không tồn tại trong gian hàng.");
        }

        // Kiểm tra không trùng SKU trên cùng 1 phiếu
        if (dto.Items.Count != skuIds.Count)
        {
            throw new InvalidOperationException("Phiếu nhập không được chứa các dòng SKU trùng lặp.");
        }

        // 5. Sinh mã phiếu nhập nếu chưa có
        var number = string.IsNullOrWhiteSpace(dto.Number)
            ? $"GR-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}"
            : dto.Number.Trim();

        // Kiểm tra mã phiếu đã tồn tại chưa
        var exists = await _context.GoodsReceipts.AnyAsync(r => r.Number == number, ct);
        if (exists)
        {
            throw new InvalidOperationException($"Mã phiếu nhập '{number}' đã tồn tại trong gian hàng.");
        }

        var receipt = new GoodsReceipt
        {
            TenantId = tenantId,
            Number = number,
            BranchId = dto.BranchId,
            WarehouseId = dto.WarehouseId,
            SupplierId = dto.SupplierId,
            Status = "Draft",
            Note = dto.Note,
            CreatedBy = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        foreach (var itemDto in dto.Items)
        {
            if (itemDto.Quantity <= 0)
            {
                throw new ArgumentException($"Số lượng SKU '{existingSkus[itemDto.SkuId].SKU}' phải lớn hơn 0.");
            }
            if (itemDto.UnitPrice < 0)
            {
                throw new ArgumentException($"Đơn giá SKU '{existingSkus[itemDto.SkuId].SKU}' không được âm.");
            }

            receipt.Items.Add(new GoodsReceiptItem
            {
                TenantId = tenantId,
                SkuId = itemDto.SkuId,
                Quantity = itemDto.Quantity,
                UnitPrice = itemDto.UnitPrice,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        _context.GoodsReceipts.Add(receipt);
        await _context.SaveChangesAsync(ct);

        return await MapToResponseDtoAsync(receipt.Id, ct);
    }

    public async Task<PagedResult<GoodsReceiptResponseDto>> GetReceiptsAsync(GoodsReceiptFilterDto filter, CancellationToken ct = default)
    {
        RequireTenantId();

        var query = _context.GoodsReceipts
            .Include(r => r.Branch)
            .Include(r => r.Warehouse)
            .Include(r => r.Supplier)
            .Include(r => r.CreatedByUser)
            .Include(r => r.ConfirmedByUser)
            .Include(r => r.Items)
                .ThenInclude(i => i.Sku)
                    .ThenInclude(s => s!.Product)
            .AsNoTracking();

        if (filter.BranchId.HasValue)
            query = query.Where(r => r.BranchId == filter.BranchId.Value);

        if (filter.WarehouseId.HasValue)
            query = query.Where(r => r.WarehouseId == filter.WarehouseId.Value);

        if (filter.SupplierId.HasValue)
            query = query.Where(r => r.SupplierId == filter.SupplierId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Status))
            query = query.Where(r => r.Status == filter.Status);

        if (filter.FromDate.HasValue)
            query = query.Where(r => r.CreatedAt >= filter.FromDate.Value);

        if (filter.ToDate.HasValue)
            query = query.Where(r => r.CreatedAt <= filter.ToDate.Value);

        var totalCount = await query.CountAsync(ct);

        var pageIndex = Math.Max(1, filter.PageIndex);
        var pageSize = Math.Max(1, Math.Min(100, filter.PageSize));

        var receipts = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = receipts.Select(r => MapReceiptToDto(r)).ToList();

        return new PagedResult<GoodsReceiptResponseDto>
        {
            Items = items,
            PageIndex = pageIndex,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<GoodsReceiptResponseDto?> GetReceiptByIdAsync(Guid id, CancellationToken ct = default)
    {
        RequireTenantId();
        return await MapToResponseDtoAsync(id, ct);
    }

    public async Task<GoodsReceiptResponseDto> ConfirmReceiptAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        // BE1-05: Toàn bộ quá trình xác nhận nhập kho diễn ra trong một Transaction READ COMMITTED nguyên tử
        // và được bao bọc bởi IExecutionStrategy tương thích với NpgsqlRetryingExecutionStrategy
        var strategy = _context.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _context.ClearChangeTracker();
            await using var transaction = await _context.BeginTransactionAsync(ct);

            try
            {
                // 1. BE1-05: Khóa chứng từ phiếu nhập bằng PostgreSQL Advisory Lock để tuần tự hóa các yêu cầu xác nhận cùng 1 phiếu
                if (_context.IsRelationalDatabase)
                {
                    await _context.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/goods_receipt/' || {1}::text, 0));",
                        new object[] { tenantId.ToString(), id.ToString() },
                        ct);
                }

                // 2. Tải phiếu nhập tươi mới từ DB sau khi đã có khóa chứng từ
                var receipt = await _context.GoodsReceipts
                    .Include(r => r.Items)
                    .FirstOrDefaultAsync(r => r.Id == id, ct);

                if (receipt == null)
                {
                    throw new KeyNotFoundException($"Không tìm thấy Phiếu nhập kho với ID '{id}'.");
                }

                if (receipt.Status == "Confirmed")
                {
                    throw new InvalidOperationException($"Phiếu nhập '{receipt.Number}' đã được xác nhận trước đó.");
                }

                if (receipt.Status == "Cancelled")
                {
                    throw new InvalidOperationException($"Không thể xác nhận phiếu nhập '{receipt.Number}' đã bị hủy.");
                }

                if (receipt.Items.Count == 0)
                {
                    throw new InvalidOperationException("Phiếu nhập không có dòng hàng nào để xác nhận.");
                }

                // 3. Sắp xếp SKU theo thứ tự tăng dần cố định để phòng ngừa Deadlock giữa nhiều phiếu nhập cùng SKU
                var sortedItems = receipt.Items.OrderBy(i => i.SkuId).ToList();
                var skuIds = sortedItems.Select(i => i.SkuId).Distinct().ToList();

                // Lấy pg_advisory_xact_lock theo từng SKU
                if (_context.IsRelationalDatabase)
                {
                    foreach (var skuId in skuIds)
                    {
                        await _context.ExecuteSqlRawAsync(
                            "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/' || {1}::text || '/' || {2}::text, 0));",
                            new object[] { tenantId.ToString(), receipt.BranchId.ToString(), skuId.ToString() },
                            ct);
                    }
                }

            // Tải tồn kho hiện tại cấp Chi nhánh và cấp Kho
            var branchBalances = await _context.InventoryBalances
                .Where(b => b.BranchId == receipt.BranchId && skuIds.Contains(b.SkuId))
                .ToDictionaryAsync(b => b.SkuId, ct);

            var warehouseBalances = await _context.WarehouseInventoryBalances
                .Where(b => b.WarehouseId == receipt.WarehouseId && skuIds.Contains(b.SkuId))
                .ToDictionaryAsync(b => b.SkuId, ct);

            var now = DateTimeOffset.UtcNow;

            foreach (var item in sortedItems)
            {
                // 1. Tồn cấp Chi nhánh (Tổng tồn + WAC)
                if (!branchBalances.TryGetValue(item.SkuId, out var branchBalance))
                {
                    branchBalance = new InventoryBalance
                    {
                        TenantId = tenantId,
                        BranchId = receipt.BranchId,
                        SkuId = item.SkuId,
                        OnHand = 0,
                        Reserved = 0,
                        AverageCost = 0,
                        LastSequence = 0,
                        CreatedAt = now
                    };
                    _context.InventoryBalances.Add(branchBalance);
                    branchBalances[item.SkuId] = branchBalance;
                }

                // 2. Tồn cấp Kho
                if (!warehouseBalances.TryGetValue(item.SkuId, out var warehouseBalance))
                {
                    warehouseBalance = new WarehouseInventoryBalance
                    {
                        TenantId = tenantId,
                        BranchId = receipt.BranchId,
                        WarehouseId = receipt.WarehouseId,
                        SkuId = item.SkuId,
                        OnHand = 0,
                        Reserved = 0,
                        LastSequence = 0,
                        CreatedAt = now
                    };
                    _context.WarehouseInventoryBalances.Add(warehouseBalance);
                    warehouseBalances[item.SkuId] = warehouseBalance;
                }

                // BE1-07: Tính lại Giá vốn WAC cho Chi nhánh
                var oldOnHand = branchBalance.OnHand;
                var oldWac = branchBalance.AverageCost;
                var newWac = _wacCalculator.CalculateWac(oldOnHand, oldWac, item.Quantity, item.UnitPrice);

                // Tăng sequence
                var newSequence = branchBalance.LastSequence + 1;
                var newBalanceAfter = oldOnHand + item.Quantity;

                // Cập nhật số dư chi nhánh
                branchBalance.OnHand += item.Quantity;
                branchBalance.AverageCost = newWac;
                branchBalance.LastSequence = newSequence;
                branchBalance.UpdatedAt = now;

                // Cập nhật số dư kho
                warehouseBalance.OnHand += item.Quantity;
                warehouseBalance.LastSequence = newSequence;
                warehouseBalance.UpdatedAt = now;

                // BE1-06: Ghi Sổ cái kho (Inventory Transactions - Append-Only)
                var ledgerEntry = new InventoryTransaction
                {
                    TenantId = tenantId,
                    BranchId = receipt.BranchId,
                    WarehouseId = receipt.WarehouseId,
                    SkuId = item.SkuId,
                    Type = "IN",
                    Quantity = item.Quantity,
                    UnitCost = item.UnitPrice,
                    BalanceAfter = newBalanceAfter,
                    SequenceNo = newSequence,
                    ReceiptItemId = item.Id,
                    CreatedAt = now
                };
                _context.InventoryTransactions.Add(ledgerEntry);
            }

            // Cập nhật trạng thái chứng từ
            receipt.Status = "Confirmed";
            receipt.ConfirmedAt = now;
            receipt.ConfirmedBy = userId;
            receipt.UpdatedAt = now;

            // Lưu thay đổi vào DbContext
            await _context.SaveChangesAsync(ct);

            // Chốt kiểm tra Deferred Constraints của PostgreSQL trước khi COMMIT
            if (_context.IsRelationalDatabase)
            {
                await _context.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE;", Array.Empty<object>(), ct);
            }

            await transaction.CommitAsync(ct);

            return await MapToResponseDtoAsync(receipt.Id, ct);
        }
        catch (DbUpdateException dbEx) when (dbEx.InnerException?.Message.Contains("23505") == true || dbEx.InnerException?.Message.Contains("uq_inventory_transactions") == true)
        {
            await transaction.RollbackAsync(ct);
            _context.ClearChangeTracker();
            var isConfirmed = await _context.GoodsReceipts.AnyAsync(r => r.Id == id && r.Status == "Confirmed", ct);
            if (isConfirmed)
            {
                throw new InvalidOperationException($"Phiếu nhập '{id}' đã được xác nhận trước đó.", dbEx);
            }
            throw;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
        });
    }

    private async Task<GoodsReceiptResponseDto> MapToResponseDtoAsync(Guid id, CancellationToken ct)
    {
        var receipt = await _context.GoodsReceipts
            .Include(r => r.Branch)
            .Include(r => r.Warehouse)
            .Include(r => r.Supplier)
            .Include(r => r.CreatedByUser)
            .Include(r => r.ConfirmedByUser)
            .Include(r => r.Items)
                .ThenInclude(i => i.Sku)
                    .ThenInclude(s => s!.Product)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (receipt == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy Phiếu nhập kho với ID '{id}'.");
        }

        return MapReceiptToDto(receipt);
    }

    private static GoodsReceiptResponseDto MapReceiptToDto(GoodsReceipt receipt)
    {
        return new GoodsReceiptResponseDto
        {
            Id = receipt.Id,
            Number = receipt.Number,
            BranchId = receipt.BranchId,
            BranchName = receipt.Branch?.Name ?? string.Empty,
            WarehouseId = receipt.WarehouseId,
            WarehouseName = receipt.Warehouse?.Name ?? string.Empty,
            SupplierId = receipt.SupplierId,
            SupplierName = receipt.Supplier?.Name ?? string.Empty,
            Status = receipt.Status,
            Note = receipt.Note,
            TotalAmount = receipt.Items.Sum(i => i.Quantity * i.UnitPrice),
            CreatedBy = receipt.CreatedBy,
            CreatedByName = receipt.CreatedByUser?.FullName,
            ConfirmedAt = receipt.ConfirmedAt,
            ConfirmedBy = receipt.ConfirmedBy,
            ConfirmedByName = receipt.ConfirmedByUser?.FullName,
            CreatedAt = receipt.CreatedAt,
            UpdatedAt = receipt.UpdatedAt,
            Items = receipt.Items.Select(i => new GoodsReceiptItemResponseDto
            {
                Id = i.Id,
                SkuId = i.SkuId,
                SkuCode = i.Sku?.SKU ?? string.Empty,
                ProductName = i.Sku?.Product?.Name ?? string.Empty,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList()
        };
    }
}
