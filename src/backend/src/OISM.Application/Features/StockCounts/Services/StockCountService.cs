using OISM.Application.Features.Realtime;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Inventory.Services;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.StockCounts.Dtos;
using OISM.Domain.Entities;

namespace OISM.Application.Features.StockCounts.Services;

/// <summary>
/// Triển khai nghiệp vụ Kiểm kê kho và Cân bằng sai lệch tồn (FR-INV-04).
/// Vòng đời đợt kiểm kê: Draft -> Counting -> Confirmed; cho phép hủy khi còn Draft/Counting.
/// Chụp snapshot tồn kho sổ sách, đối soát sequence chống giao dịch xen giữa (drift),
/// sinh chứng từ InventoryAdjustment và ghi sổ cái Ledger nguyên tử trong một Transaction.
/// </summary>
public class StockCountService : IStockCountService
{
    private const decimal MaxQuantityAmount = 999_999_999_999_999.999m;
    private const decimal MaxUnitCostAmount = 999_999_999_999.999999m;

    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IWacCalculator _wacCalculator;
    private readonly IRealtimeNotifier? _realtimeNotifier;
    private readonly ILogger<StockCountService> _logger;

    public StockCountService(
        IAppDbContext context,
        ICurrentTenantService tenantService,
        IWacCalculator wacCalculator,
        ILogger<StockCountService> logger,
        IRealtimeNotifier? realtimeNotifier = null)
    {
        _context = context;
        _tenantService = tenantService;
        _wacCalculator = wacCalculator;
        _logger = logger;
        _realtimeNotifier = realtimeNotifier;
    }

    public async Task<StockCountResponseDto> CreateStockCountAsync(
        CreateStockCountRequestDto dto,
        Guid createdByUserId,
        CancellationToken ct = default)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new OismException("UNAUTHORIZED", "Không xác định được danh tính Tenant.", 401);
        }

        if (dto == null)
        {
            throw new OismException("VALIDATION_ERROR", "Dữ liệu yêu cầu không được để trống.", 400);
        }

        if (dto.RequestKey == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "RequestKey không được rỗng (Guid.Empty).", 400);
        }

        if (dto.BranchId == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "Chi nhánh kiểm kê không được rỗng.", 400);
        }

        if (dto.WarehouseId == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "Kho kiểm kê không được rỗng.", 400);
        }

        if (dto.SkuIds != null && dto.SkuIds.Count > 0)
        {
            if (dto.SkuIds.Any(id => id == Guid.Empty))
            {
                throw new OismException("VALIDATION_ERROR", "Mã biến thể SKU trong danh sách kiểm kê không được rỗng.", 400);
            }

            if (dto.SkuIds.GroupBy(x => x).Any(g => g.Count() > 1))
            {
                throw new OismException("VALIDATION_ERROR", "Danh sách SKU kiểm kê không được chứa phần tử trùng lặp.", 400);
            }
        }

        // Chuẩn hóa canonical string để băm SHA-256 payload signature
        var skuModePart = (dto.SkuIds != null && dto.SkuIds.Count > 0)
            ? "SKUS:" + string.Join(",", dto.SkuIds.OrderBy(x => x).Select(x => x.ToString("D")))
            : "ALL";
        var canonical = $"{dto.BranchId:D}|{dto.WarehouseId:D}|{dto.Note?.Trim()}|{skuModePart}";
        var payloadSignature = ComputeSha256Hash(canonical);

        var isAllMode = dto.SkuIds == null || dto.SkuIds.Count == 0;
        List<Guid> candidateSkuIds;
        if (!isAllMode)
        {
            candidateSkuIds = dto.SkuIds!.Distinct().OrderBy(x => x).ToList();
        }
        else
        {
            // Discovery/hint: danh sách SKU ban đầu trong kho để xác định tập SKU cần khóa
            candidateSkuIds = await _context.WarehouseInventoryBalances
                .Where(w => w.TenantId == tenantId.Value && w.WarehouseId == dto.WarehouseId)
                .Select(w => w.SkuId)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(ct);
        }

        while (true)
        {
            var strategy = _context.CreateExecutionStrategy();
            var (shouldRetry, response, newCandidates) = await strategy.ExecuteAsync(async () =>
            {
                _context.ClearChangeTracker();
                var tx = await _context.BeginTransactionAsync(ct);
                try
                {

                // 1. Phân cấp khóa nghiêm ngặt: SKU khóa trước (ascending) -> RequestKey khóa sau
                if (_context.IsRelationalDatabase)
                {
                    foreach (var skuId in candidateSkuIds)
                    {
                        await _context.ExecuteSqlRawAsync(
                            "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/' || {1}::text || '/' || {2}::text, 0));",
                            new object[] { tenantId.Value.ToString(), dto.BranchId.ToString(), skuId.ToString() },
                            ct);
                    }

                    await _context.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/request_key/' || {1}::text, 0));",
                        new object[] { tenantId.Value.ToString(), dto.RequestKey.ToString() },
                        ct);
                }

                // 2. Kiểm tra Replay dưới khóa RequestKey TRƯỚC KHI kiểm tra tài nguyên hoặc ném empty guard
                var existingCount = await _context.StockCounts
                    .Include(c => c.Items)
                    .ThenInclude(i => i.Sku)
                    .Include(c => c.Branch)
                    .Include(c => c.Warehouse)
                    .FirstOrDefaultAsync(c => c.TenantId == tenantId.Value && c.RequestKey == dto.RequestKey, ct);

                if (existingCount != null)
                {
                    if (existingCount.PayloadSignature == payloadSignature)
                    {
                        _logger.LogInformation("Replay đợt kiểm kê {Number} thành công theo RequestKey {Key}.", existingCount.Number, dto.RequestKey);
                        await tx.CommitAsync(ct);
                        return (false, MapToResponseDto(existingCount, isReplay: true), candidateSkuIds);
                    }

                    throw new OismException("IDEMPOTENCY_PAYLOAD_MISMATCH", "Yêu cầu trùng RequestKey nhưng nội dung payload không khớp.", 409);
                }

                // 3. Kiểm tra tài nguyên chi nhánh và kho (trả 404 NOT_FOUND nếu không thuộc tenant)
                var branch = await _context.Branches
                    .FirstOrDefaultAsync(b => b.TenantId == tenantId.Value && b.Id == dto.BranchId, ct);
                if (branch == null)
                {
                    throw new OismException("NOT_FOUND", "Không tìm thấy chi nhánh kiểm kê trong phạm vi doanh nghiệp.", 404);
                }

                if (!branch.IsActive)
                {
                    throw new OismException("VALIDATION_ERROR", "Chi nhánh kiểm kê đang bị khóa hoặc ngưng hoạt động.", 400);
                }

                var warehouse = await _context.Warehouses
                    .FirstOrDefaultAsync(w => w.TenantId == tenantId.Value && w.Id == dto.WarehouseId, ct);
                if (warehouse == null)
                {
                    throw new OismException("NOT_FOUND", "Không tìm thấy kho kiểm kê trong phạm vi doanh nghiệp.", 404);
                }

                if (!warehouse.IsActive)
                {
                    throw new OismException("VALIDATION_ERROR", "Kho kiểm kê đang bị khóa hoặc ngưng hoạt động.", 400);
                }

                if (warehouse.BranchId != dto.BranchId)
                {
                    throw new OismException("VALIDATION_ERROR", "Kho kiểm kê không thuộc chi nhánh kiểm kê đã chọn.", 400);
                }

                // 4. Lấy danh sách SKU và chụp snapshot FRESH dưới khóa
                var countId = Guid.NewGuid();
                var itemsToSnapshot = new List<StockCountItem>();

                if (dto.SkuIds != null && dto.SkuIds.Count > 0)
                {
                    var skus = await _context.Skus
                        .Where(s => s.TenantId == tenantId.Value && dto.SkuIds.Contains(s.Id))
                        .ToListAsync(ct);

                    if (skus.Count != dto.SkuIds.Count)
                    {
                        throw new OismException("NOT_FOUND", "Một hoặc nhiều SKU kiểm kê không tồn tại trong hệ thống.", 404);
                    }

                    var inactiveSku = skus.FirstOrDefault(s => !s.IsActive);
                    if (inactiveSku != null)
                    {
                        throw new OismException("SKU_INACTIVE", $"Biến thể SKU '{inactiveSku.Code}' đang bị khóa hoặc ngưng hoạt động.", 422);
                    }

                    var whBalances = await _context.WarehouseInventoryBalances
                        .Where(w => w.TenantId == tenantId.Value && w.WarehouseId == dto.WarehouseId && dto.SkuIds.Contains(w.SkuId))
                        .ToListAsync(ct);

                    var whBalMap = whBalances.ToDictionary(b => b.SkuId);

                    foreach (var sku in skus)
                    {
                        whBalMap.TryGetValue(sku.Id, out var whBal);
                        itemsToSnapshot.Add(new StockCountItem
                        {
                            Id = Guid.NewGuid(),
                            TenantId = tenantId.Value,
                            StockCountId = countId,
                            SkuId = sku.Id,
                            ExpectedQuantity = whBal?.OnHand ?? 0m,
                            CountedQuantity = null,
                            BalanceSequenceAtSnapshot = whBal?.LastSequence ?? 0L,
                            AdjustmentUnitCost = null,
                            CreatedAt = DateTimeOffset.UtcNow
                        });
                    }
                }
                else
                {
                    // Chế độ ALL: Đọc fresh WarehouseInventoryBalances sau khi đã giữ khóa
                    var whBalances = await _context.WarehouseInventoryBalances
                        .Include(w => w.Sku)
                        .Where(w => w.TenantId == tenantId.Value && w.WarehouseId == dto.WarehouseId)
                        .ToListAsync(ct);

                    if (whBalances.Count == 0)
                    {
                        throw new OismException("EMPTY_WAREHOUSE_STOCK_COUNT", "Kho kiểm kê hiện không có mặt hàng nào để kiểm kê toàn kho.", 400);
                    }

                    // Đối soát tập SKU: Mọi SKU đưa vào snapshot cuối BẮT BUỘC phải được giữ khóa trước đó.
                    // Nếu tập SKU trong kho đã tăng sau discovery (có SKU mới xuất hiện mà chưa nằm trong candidateSkuIds),
                    // ta rollback transaction hiện tại, nạp lại toàn bộ candidateSkuIds mới và thực hiện lại từ đầu.
                    var actualSkuIds = whBalances.Select(w => w.SkuId).Distinct().OrderBy(x => x).ToList();
                    var unheldSkuLocks = actualSkuIds.Except(candidateSkuIds).ToList();
                    if (unheldSkuLocks.Count > 0)
                    {
                        _logger.LogInformation("Phát hiện SKU mới xuất hiện trong kho sau discovery. Rollback và khởi động lại transaction với tập khóa đầy đủ ({Count} SKUs).", actualSkuIds.Count);
                        await tx.RollbackAsync(ct);
                        return (true, (StockCountResponseDto?)null, actualSkuIds);
                    }

                    foreach (var whBal in whBalances)
                    {
                        itemsToSnapshot.Add(new StockCountItem
                        {
                            Id = Guid.NewGuid(),
                            TenantId = tenantId.Value,
                            StockCountId = countId,
                            SkuId = whBal.SkuId,
                            ExpectedQuantity = whBal.OnHand,
                            CountedQuantity = null,
                            BalanceSequenceAtSnapshot = whBal.LastSequence,
                            AdjustmentUnitCost = null,
                            CreatedAt = DateTimeOffset.UtcNow
                        });
                    }
                }

            // 5. Sinh mã kiểm kê an toàn dưới Advisory Lock
            if (_context.IsRelationalDatabase)
            {
                await _context.ExecuteSqlRawAsync(
                    "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/stock_count_number', 0));",
                    new object[] { tenantId.Value.ToString() },
                    ct);
            }

            var now = DateTimeOffset.UtcNow;
            var todayPrefix = $"CNT-{now:yyyyMMdd}-";
            var countToday = await _context.StockCounts
                .CountAsync(c => c.TenantId == tenantId.Value && c.Number.StartsWith(todayPrefix), ct);
            var number = $"{todayPrefix}{(countToday + 1):D5}";

            var stockCount = new StockCount
            {
                Id = countId,
                TenantId = tenantId.Value,
                Number = number,
                RequestKey = dto.RequestKey,
                PayloadSignature = payloadSignature,
                BranchId = dto.BranchId,
                WarehouseId = dto.WarehouseId,
                Status = "Draft",
                CreatedBy = createdByUserId,
                Note = dto.Note?.Trim(),
                CreatedAt = now,
                Items = itemsToSnapshot
            };

            // Nạp lại navigation để hiển thị DTO
            stockCount.Branch = branch;
            stockCount.Warehouse = warehouse;
            var skuIds = stockCount.Items.Select(i => i.SkuId).ToList();
            var skuMap = await _context.Skus
                .Include(s => s.Product)
                .Where(s => s.TenantId == tenantId.Value && skuIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, ct);
            foreach (var item in stockCount.Items)
            {
                if (skuMap.TryGetValue(item.SkuId, out var sku))
                {
                    item.Sku = sku;
                }
            }

            _context.StockCounts.Add(stockCount);
            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

                    return (false, MapToResponseDto(stockCount, isReplay: false), candidateSkuIds);
                }
                finally
                {
                    tx.Dispose();
                }
            });

            if (shouldRetry)
            {
                candidateSkuIds = newCandidates;
                continue;
            }

            return response!;
        }
    }

    public async Task<StockCountResponseDto> RecordCountsAsync(
        Guid stockCountId,
        RecordStockCountBatchRequestDto dto,
        Guid recordedByUserId,
        CancellationToken ct = default)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new OismException("UNAUTHORIZED", "Không xác định được danh tính Tenant.", 401);
        }

        if (dto == null || dto.Items == null || dto.Items.Count == 0)
        {
            throw new OismException("VALIDATION_ERROR", "Danh sách đếm không được rỗng.", 400);
        }

        if (dto.Items.Any(i => i == null))
        {
            throw new OismException("VALIDATION_ERROR", "Danh sách mặt hàng chứa phần tử rỗng (null).", 400);
        }

        if (dto.Items.Any(i => i.SkuId == Guid.Empty))
        {
            throw new OismException("VALIDATION_ERROR", "Mã biến thể SKU không được rỗng.", 400);
        }

        if (dto.Items.GroupBy(x => x.SkuId).Any(g => g.Count() > 1))
        {
            throw new OismException("VALIDATION_ERROR", "Danh sách kiểm đếm không được chứa SKU trùng lặp.", 400);
        }

        foreach (var item in dto.Items)
        {
            if (!item.CountedQuantity.HasValue)
            {
                throw new OismException("VALIDATION_ERROR", "Số lượng đếm thực tế là bắt buộc cho mỗi mặt hàng.", 400);
            }

            if (item.CountedQuantity.Value < 0)
            {
                throw new OismException("VALIDATION_ERROR", "Số lượng đếm thực tế không được âm.", 400);
            }

            if (item.CountedQuantity.Value > MaxQuantityAmount)
            {
                throw new OismException("VALIDATION_ERROR", "Số lượng đếm thực tế vượt quá giới hạn tối đa cho phép.", 400);
            }

            if (decimal.Round(item.CountedQuantity.Value, 3) != item.CountedQuantity.Value)
            {
                throw new OismException("VALIDATION_ERROR", "Số lượng đếm thực tế không được vượt quá 3 chữ số thập phân.", 400);
            }

            if (item.AdjustmentUnitCost.HasValue)
            {
                if (item.AdjustmentUnitCost.Value <= 0)
                {
                    throw new OismException("VALIDATION_ERROR", "Đơn giá điều chỉnh phải lớn hơn 0.", 400);
                }

                if (item.AdjustmentUnitCost.Value > MaxUnitCostAmount)
                {
                    throw new OismException("VALIDATION_ERROR", "Đơn giá điều chỉnh vượt quá giới hạn tối đa cho phép.", 400);
                }

                if (decimal.Round(item.AdjustmentUnitCost.Value, 6) != item.AdjustmentUnitCost.Value)
                {
                    throw new OismException("VALIDATION_ERROR", "Đơn giá điều chỉnh không được vượt quá 6 chữ số thập phân.", 400);
                }
            }
        }

        var hint = await _context.StockCounts
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId.Value && c.Id == stockCountId)
            .Select(c => new { c.BranchId })
            .FirstOrDefaultAsync(ct);

        if (hint == null)
        {
            throw new OismException("NOT_FOUND", "Không tìm thấy đợt kiểm kê trong phạm vi doanh nghiệp.", 404);
        }

        var strategy = _context.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _context.ClearChangeTracker();
            await using var tx = await _context.BeginTransactionAsync(ct);

            // 1. Khóa SKU -> Khóa Document
            if (_context.IsRelationalDatabase)
            {
                var distinctSkus = dto.Items.Select(i => i.SkuId).Distinct().OrderBy(x => x).ToList();
                foreach (var skuId in distinctSkus)
                {
                    await _context.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/' || {1}::text || '/' || {2}::text, 0));",
                        new object[] { tenantId.Value.ToString(), hint.BranchId.ToString(), skuId.ToString() },
                        ct);
                }

                await _context.ExecuteSqlRawAsync(
                    "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/stock_count/' || {1}::text, 0));",
                    new object[] { tenantId.Value.ToString(), stockCountId.ToString() },
                    ct);
            }

            // 2. Nạp lại fresh StockCount dưới khóa
            var stockCount = await _context.StockCounts
                .Include(c => c.Items)
                .ThenInclude(i => i.Sku)
                .ThenInclude(s => s!.Product)
                .Include(c => c.Branch)
                .Include(c => c.Warehouse)
                .FirstOrDefaultAsync(c => c.TenantId == tenantId.Value && c.Id == stockCountId, ct);

            if (stockCount == null)
            {
                throw new OismException("NOT_FOUND", "Không tìm thấy đợt kiểm kê trong phạm vi doanh nghiệp.", 404);
            }

            if (stockCount.Status != "Draft" && stockCount.Status != "Counting")
            {
                throw new OismException("INVALID_STATE_TRANSITION", $"Không thể ghi số đếm cho đợt kiểm kê ở trạng thái '{stockCount.Status}'. Chỉ cho phép ghi số đếm ở trạng thái Draft hoặc Counting.", 409);
            }

            // 3. Cập nhật số đếm thực tế
            foreach (var item in dto.Items)
            {
                var existingItem = stockCount.Items.FirstOrDefault(i => i.SkuId == item.SkuId);
                if (existingItem == null)
                {
                    throw new OismException("INVALID_COUNT_ITEM", $"SKU '{item.SkuId}' không thuộc phạm vi đợt kiểm kê này.", 400);
                }

                existingItem.CountedQuantity = item.CountedQuantity!.Value;
                if (item.AdjustmentUnitCost.HasValue)
                {
                    existingItem.AdjustmentUnitCost = item.AdjustmentUnitCost.Value;
                }
                existingItem.UpdatedAt = DateTimeOffset.UtcNow;
            }

            if (stockCount.Status == "Draft")
            {
                stockCount.Status = "Counting";
            }
            stockCount.UpdatedAt = DateTimeOffset.UtcNow;

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return MapToResponseDto(stockCount, isReplay: false);
        });
    }

    public async Task<StockCountResponseDto> ConfirmStockCountAsync(
        Guid stockCountId,
        ConfirmStockCountRequestDto? dto,
        Guid confirmedByUserId,
        CancellationToken ct = default)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new OismException("UNAUTHORIZED", "Không xác định được danh tính Tenant.", 401);
        }

        if (dto?.SkuAdjustmentCosts != null)
        {
            foreach (var (skuId, cost) in dto.SkuAdjustmentCosts)
            {
                if (skuId == Guid.Empty)
                {
                    throw new OismException("VALIDATION_ERROR", "Mã SKU trong danh sách đơn giá bổ sung không được rỗng.", 400);
                }

                if (cost < 0)
                {
                    throw new OismException("VALIDATION_ERROR", "Đơn giá điều chỉnh bổ sung không được âm.", 400);
                }

                if (cost > MaxUnitCostAmount)
                {
                    throw new OismException("VALIDATION_ERROR", "Đơn giá điều chỉnh bổ sung vượt quá giới hạn tối đa cho phép.", 400);
                }

                if (decimal.Round(cost, 6) != cost)
                {
                    throw new OismException("VALIDATION_ERROR", "Đơn giá điều chỉnh bổ sung không được vượt quá 6 chữ số thập phân.", 400);
                }
            }
        }

        var hint = await _context.StockCounts
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId.Value && c.Id == stockCountId)
            .Select(c => new { c.BranchId, c.Status, SkuIds = c.Items.Select(i => i.SkuId).ToList() })
            .FirstOrDefaultAsync(ct);

        if (hint == null)
        {
            throw new OismException("NOT_FOUND", "Không tìm thấy đợt kiểm kê trong phạm vi doanh nghiệp.", 404);
        }

        List<Func<Task>> pendingNotifications = new();
        var strategy = _context.CreateExecutionStrategy();
        var result = await strategy.ExecuteAsync(async () =>
        {
            pendingNotifications.Clear();
            _context.ClearChangeTracker();
            await using var tx = await _context.BeginTransactionAsync(ct);

            // 1. Phân cấp khóa: SKU khóa trước -> Document khóa sau
            if (_context.IsRelationalDatabase)
            {
                var sortedSkus = hint.SkuIds.Distinct().OrderBy(x => x).ToList();
                foreach (var skuId in sortedSkus)
                {
                    await _context.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/' || {1}::text || '/' || {2}::text, 0));",
                        new object[] { tenantId.Value.ToString(), hint.BranchId.ToString(), skuId.ToString() },
                        ct);
                }

                await _context.ExecuteSqlRawAsync(
                    "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/stock_count/' || {1}::text, 0));",
                    new object[] { tenantId.Value.ToString(), stockCountId.ToString() },
                    ct);
            }

            // 2. Nạp lại fresh StockCount dưới khóa
            var stockCount = await _context.StockCounts
                .Include(c => c.Items)
                .ThenInclude(i => i.Sku)
                .ThenInclude(s => s!.Product)
                .Include(c => c.Branch)
                .Include(c => c.Warehouse)
                .FirstOrDefaultAsync(c => c.TenantId == tenantId.Value && c.Id == stockCountId, ct);

            if (stockCount == null)
            {
                throw new OismException("NOT_FOUND", "Không tìm thấy đợt kiểm kê trong phạm vi doanh nghiệp.", 404);
            }

            if (stockCount.Status != "Counting")
            {
                throw new OismException("INVALID_STATE_TRANSITION", $"Đợt kiểm kê phải ở trạng thái Counting mới được chốt kiểm kê. Trạng thái hiện tại: '{stockCount.Status}'.", 409);
            }

            // Kiểm tra toàn bộ dòng phải có số đếm thực tế
            var uncountedItem = stockCount.Items.FirstOrDefault(i => !i.CountedQuantity.HasValue);
            if (uncountedItem != null)
            {
                throw new OismException("INVALID_STATE_TRANSITION", "Tất cả các mặt hàng phải được nhập số đếm thực tế trước khi chốt kiểm kê.", 409);
            }

            // Kiểm tra map giá bổ sung không chứa SKU ngoài đợt
            if (dto?.SkuAdjustmentCosts != null)
            {
                foreach (var skuId in dto.SkuAdjustmentCosts.Keys)
                {
                    if (!stockCount.Items.Any(i => i.SkuId == skuId))
                    {
                        throw new OismException("VALIDATION_ERROR", $"SKU '{skuId}' trong danh sách đơn giá bổ sung không thuộc đợt kiểm kê này.", 400);
                    }
                }
            }

            // 3. Kiểm tra tiền điều kiện: Sequence Drift, Reserved Stock & WAC Cost
            var skuIds = stockCount.Items.Select(i => i.SkuId).Distinct().ToList();
            var whBalances = await _context.WarehouseInventoryBalances
                .Where(w => w.TenantId == tenantId.Value && w.WarehouseId == stockCount.WarehouseId && skuIds.Contains(w.SkuId))
                .ToListAsync(ct);
            var whBalMap = whBalances.ToDictionary(b => b.SkuId);

            var branchBalances = await _context.InventoryBalances
                .Where(b => b.TenantId == tenantId.Value && b.BranchId == stockCount.BranchId && skuIds.Contains(b.SkuId))
                .ToListAsync(ct);
            var branchBalMap = branchBalances.ToDictionary(b => b.SkuId);
            var skuCodes = await _context.ProductVariants.Where(s => s.TenantId == tenantId.Value && skuIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.SKU, ct);

            // 3.1. Đối soát Sequence Drift
            foreach (var item in stockCount.Items)
            {
                whBalMap.TryGetValue(item.SkuId, out var whBal);
                var currentWarehouseSeq = whBal?.LastSequence ?? 0L;

                if (currentWarehouseSeq != item.BalanceSequenceAtSnapshot)
                {
                    throw new OismException("STOCK_COUNT_SEQUENCE_DRIFT",
                        $"Phát hiện giao dịch phát sinh xen giữa tại kho kiểm kê cho SKU '{item.SkuId}'. " +
                        $"Sequence tại thời điểm snapshot: {item.BalanceSequenceAtSnapshot}, sequence hiện tại: {currentWarehouseSeq}.", 409);
                }
            }

            // 3.2. Đối soát Reserved Stock và tính toán Delta
            var itemPlan = new List<(StockCountItem Item, decimal Delta, decimal UnitCost, decimal OldBranchOnHand, decimal OldBranchWac)>();

            foreach (var item in stockCount.Items)
            {
                var countedQty = item.CountedQuantity!.Value;
                whBalMap.TryGetValue(item.SkuId, out var whBal);
                var whReserved = whBal?.Reserved ?? 0m;

                if (countedQty < whReserved)
                {
                    throw new OismException("INSUFFICIENT_STOCK",
                        $"Số lượng đếm thực tế ({countedQty:G29}) thấp hơn số lượng đang giữ chỗ ({whReserved:G29}) của kho cho SKU '{item.SkuId}'.", 409);
                }

                var delta = countedQty - item.ExpectedQuantity;

                branchBalMap.TryGetValue(item.SkuId, out var branchBal);
                var branchOnHand = branchBal?.OnHand ?? 0m;
                var branchReserved = branchBal?.Reserved ?? 0m;
                var newBranchOnHand = branchOnHand + delta;

                if (newBranchOnHand < branchReserved)
                {
                    throw new OismException("INSUFFICIENT_STOCK",
                        $"Tổng tồn chi nhánh sau điều chỉnh ({newBranchOnHand:G29}) thấp hơn tổng giữ chỗ ({branchReserved:G29}) cho SKU '{item.SkuId}'.", 409);
                }

                // Chặn tràn số tính toán trước khi lưu (T4B-F04)
                if (newBranchOnHand > MaxQuantityAmount)
                {
                    throw new OismException("VALIDATION_ERROR",
                        $"Tổng tồn chi nhánh sau điều chỉnh ({newBranchOnHand:G29}) vượt quá giới hạn tối đa cho phép ({MaxQuantityAmount:G29}) cho SKU '{item.SkuId}'.", 400);
                }

                if (newBranchOnHand < 0m)
                {
                    throw new OismException("VALIDATION_ERROR",
                        $"Tổng tồn chi nhánh sau điều chỉnh ({newBranchOnHand:G29}) không được âm cho SKU '{item.SkuId}'.", 400);
                }

                decimal unitCost = 0m;
                var branchWac = branchBal?.AverageCost ?? 0m;

                if (delta > 0)
                {
                    // Hàng thừa: Nếu chi nhánh đã có WAC > 0, dùng WAC hiện tại (giá client không được ghi đè)
                    if (branchWac > 0m)
                    {
                        unitCost = branchWac;
                    }
                    else
                    {
                        // Chưa có WAC (WAC == 0 hoặc null): bắt buộc cung cấp đơn giá điều chỉnh dương
                        decimal? explicitCost = null;
                        if (dto?.SkuAdjustmentCosts != null && dto.SkuAdjustmentCosts.TryGetValue(item.SkuId, out var extraCost))
                        {
                            explicitCost = extraCost;
                        }
                        else if (item.AdjustmentUnitCost.HasValue && item.AdjustmentUnitCost.Value > 0m)
                        {
                            explicitCost = item.AdjustmentUnitCost.Value;
                        }

                        if (!explicitCost.HasValue || explicitCost.Value <= 0m)
                        {
                            throw new OismException("ADJUSTMENT_COST_REQUIRED",
                                $"Hàng thừa cho SKU '{item.SkuId}' chưa có giá vốn bình quân (WAC). Bắt buộc cung cấp đơn giá điều chỉnh dương.", 422);
                        }

                        unitCost = explicitCost.Value;
                    }

                    // Kiểm tra tràn số WAC (T4B-F04)
                    try
                    {
                        var totalOldValue = checked(branchOnHand * branchWac);
                        var totalImportValue = checked(delta * unitCost);
                        var totalStock = branchOnHand + delta;
                        var derivedWac = (totalOldValue + totalImportValue) / totalStock;

                        if (derivedWac > MaxUnitCostAmount)
                        {
                            throw new OismException("VALIDATION_ERROR",
                                $"Giá vốn bình quân (WAC) sau điều chỉnh ({derivedWac:G29}) vượt quá giới hạn tối đa cho phép cho SKU '{item.SkuId}'.", 400);
                        }
                    }
                    catch (OverflowException)
                    {
                        throw new OismException("VALIDATION_ERROR",
                            $"Giá trị tồn kho tính toán vượt quá giới hạn số học cho SKU '{item.SkuId}'.", 400);
                    }
                }
                else if (delta < 0)
                {
                    // Hàng thiếu: Ledger OUT dùng WAC hiện tại, WAC không đổi
                    unitCost = branchWac;
                }

                // Kiểm tra tràn số TotalCost số học CLR (T4B-F04)
                if (delta != 0m)
                {
                    try
                    {
                        _ = checked(Math.Abs(delta) * unitCost);
                    }
                    catch (OverflowException)
                    {
                        throw new OismException("VALIDATION_ERROR",
                            $"Tổng giá trị biến động vượt quá giới hạn số học cho SKU '{item.SkuId}'.", 400);
                    }
                }

                itemPlan.Add((item, delta, unitCost, branchOnHand, branchWac));
            }

            // 4. Tạo chứng từ InventoryAdjustment liên kết duy nhất với StockCount
            if (_context.IsRelationalDatabase)
            {
                await _context.ExecuteSqlRawAsync(
                    "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/inventory_adjustment_number', 0));",
                    new object[] { tenantId.Value.ToString() },
                    ct);
            }

            var now = DateTimeOffset.UtcNow;
            var adjPrefix = $"ADJ-{now:yyyyMMdd}-";
            var adjCountToday = await _context.InventoryAdjustments
                .CountAsync(a => a.TenantId == tenantId.Value && a.Number.StartsWith(adjPrefix), ct);
            var adjNumber = $"{adjPrefix}{(adjCountToday + 1):D5}";

            var adjustment = new InventoryAdjustment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId.Value,
                Number = adjNumber,
                BranchId = stockCount.BranchId,
                WarehouseId = stockCount.WarehouseId,
                Reason = !string.IsNullOrWhiteSpace(dto?.Note)
                    ? dto.Note.Trim()
                    : $"Điều chỉnh cân bằng theo đợt kiểm kê {stockCount.Number}",
                Status = "Confirmed",
                CreatedBy = confirmedByUserId,
                ConfirmedBy = confirmedByUserId,
                ConfirmedAt = now,
                CreatedAt = now
            };

            // 5. Cập nhật tồn kho 2 cấp, WAC và Ledger (chỉ ghi các dòng có Delta != 0)
            foreach (var (item, delta, unitCost, oldBranchOnHand, oldBranchWac) in itemPlan)
            {
                if (delta == 0m)
                {
                    // Delta == 0: Không tạo AdjustmentItem, không ghi ledger, không tăng sequence hoặc đổi WAC
                    continue;
                }

                var adjItem = new InventoryAdjustmentItem
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId.Value,
                    AdjustmentId = adjustment.Id,
                    SkuId = item.SkuId,
                    QuantityDelta = delta,
                    UnitCost = decimal.Round(unitCost, 6),
                    CreatedAt = now
                };
                adjustment.Items.Add(adjItem);

                // Cập nhật Branch Balance
                if (!branchBalMap.TryGetValue(item.SkuId, out var branchBal))
                {
                    branchBal = new InventoryBalance
                    {
                        TenantId = tenantId.Value,
                        BranchId = stockCount.BranchId,
                        SkuId = item.SkuId,
                        OnHand = 0m,
                        Reserved = 0m,
                        AverageCost = 0m,
                        LastSequence = 0L,
                        CreatedAt = now
                    };
                    _context.InventoryBalances.Add(branchBal);
                    branchBalMap[item.SkuId] = branchBal;
                }

                branchBal.OnHand += delta;

                if (delta > 0m)
                {
                    var newWac = _wacCalculator.CalculateWac(oldBranchOnHand, oldBranchWac, delta, unitCost);
                    branchBal.AverageCost = decimal.Round(newWac, 6);
                }

                var newSequenceNo = branchBal.LastSequence + 1;
                branchBal.LastSequence = newSequenceNo;
                branchBal.UpdatedAt = now;
                if (_realtimeNotifier != null)
                {
                    var skuIdCapture = item.SkuId;
                    var skuCodeCapture = skuCodes.TryGetValue(item.SkuId, out var code) ? code : item.SkuId.ToString();
                    var prevAvail = oldBranchOnHand - branchBal.Reserved;
                    var newAvail = branchBal.OnHand - branchBal.Reserved;
                    var reorderPointCapture = branchBal.ReorderPoint;
                    var isLowStockCapture = prevAvail > branchBal.ReorderPoint && newAvail <= branchBal.ReorderPoint;
                    pendingNotifications.Add(() => _realtimeNotifier.NotifyStockChangedAsync(tenantId.Value, stockCount.BranchId, skuIdCapture, newAvail, ct));
                    if (isLowStockCapture)
                    {
                        pendingNotifications.Add(() => _realtimeNotifier.NotifyLowStockAlertAsync(tenantId.Value, stockCount.BranchId, skuIdCapture, skuCodeCapture, newAvail, reorderPointCapture, ct));
                    }
                }

                // Cập nhật Warehouse Balance
                if (!whBalMap.TryGetValue(item.SkuId, out var whBal))
                {
                    whBal = new WarehouseInventoryBalance
                    {
                        TenantId = tenantId.Value,
                        WarehouseId = stockCount.WarehouseId,
                        BranchId = stockCount.BranchId,
                        SkuId = item.SkuId,
                        OnHand = 0m,
                        Reserved = 0m,
                        LastSequence = 0L,
                        CreatedAt = now
                    };
                    _context.WarehouseInventoryBalances.Add(whBal);
                    whBalMap[item.SkuId] = whBal;
                }

                whBal.OnHand += delta;
                whBal.LastSequence = newSequenceNo;
                whBal.UpdatedAt = now;

                // Ghi sổ cái bất biến (Append-Only Ledger)
                var txRecord = new InventoryTransaction
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId.Value,
                    BranchId = stockCount.BranchId,
                    WarehouseId = stockCount.WarehouseId,
                    SkuId = item.SkuId,
                    Type = delta > 0m ? "IN" : "OUT",
                    Quantity = Math.Abs(delta),
                    UnitCost = decimal.Round(unitCost, 6),
                    BalanceAfter = branchBal.OnHand,
                    SequenceNo = newSequenceNo,
                    AdjustmentItemId = adjItem.Id,
                    CreatedAt = now
                };
                _context.InventoryTransactions.Add(txRecord);
            }

            // 6. Cập nhật trạng thái StockCount
            stockCount.Status = "Confirmed";
            stockCount.ConfirmedBy = confirmedByUserId;
            stockCount.ConfirmedAt = now;
            stockCount.GeneratedAdjustmentId = adjustment.Id;
            stockCount.UpdatedAt = now;

            _context.InventoryAdjustments.Add(adjustment);
            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return MapToResponseDto(stockCount, isReplay: false);
        });

        if (pendingNotifications.Count > 0)
        {
            foreach (var notifyAction in pendingNotifications)
            {
                try
                {
                    await notifyAction();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lá»—i khi phÃ¡t sá»± kiá»‡n Realtime sau confirm kiá»ƒm kÃª {CountId}.", stockCountId);
                }
            }
        }

        return result;
    }

    public async Task<StockCountResponseDto> CancelStockCountAsync(
        Guid stockCountId,
        string? reason,
        Guid cancelledByUserId,
        CancellationToken ct = default)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new OismException("UNAUTHORIZED", "Không xác định được danh tính Tenant.", 401);
        }

        var hint = await _context.StockCounts
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId.Value && c.Id == stockCountId)
            .Select(c => new { c.Status })
            .FirstOrDefaultAsync(ct);

        if (hint == null)
        {
            throw new OismException("NOT_FOUND", "Không tìm thấy đợt kiểm kê trong phạm vi doanh nghiệp.", 404);
        }

        var strategy = _context.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _context.ClearChangeTracker();
            await using var tx = await _context.BeginTransactionAsync(ct);

            if (_context.IsRelationalDatabase)
            {
                await _context.ExecuteSqlRawAsync(
                    "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/stock_count/' || {1}::text, 0));",
                    new object[] { tenantId.Value.ToString(), stockCountId.ToString() },
                    ct);
            }

            var stockCount = await _context.StockCounts
                .Include(c => c.Items)
                .ThenInclude(i => i.Sku)
                .ThenInclude(s => s!.Product)
                .Include(c => c.Branch)
                .Include(c => c.Warehouse)
                .FirstOrDefaultAsync(c => c.TenantId == tenantId.Value && c.Id == stockCountId, ct);

            if (stockCount == null)
            {
                throw new OismException("NOT_FOUND", "Không tìm thấy đợt kiểm kê trong phạm vi doanh nghiệp.", 404);
            }

            if (stockCount.Status == "Confirmed" || stockCount.Status == "Cancelled")
            {
                throw new OismException("INVALID_STATE_TRANSITION",
                    $"Không thể hủy đợt kiểm kê ở trạng thái '{stockCount.Status}'. Chỉ cho phép hủy đợt kiểm kê ở trạng thái Draft hoặc Counting.", 409);
            }

            stockCount.Status = "Cancelled";
            stockCount.UpdatedAt = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                stockCount.Note = string.IsNullOrWhiteSpace(stockCount.Note)
                    ? $"Lý do hủy: {reason.Trim()}"
                    : $"{stockCount.Note} | Lý do hủy: {reason.Trim()}";
            }

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return MapToResponseDto(stockCount, isReplay: false);
        });
    }

    public async Task<StockCountResponseDto?> GetStockCountByIdAsync(Guid stockCountId, CancellationToken ct = default)
    {
        var tenantId = _tenantService.TenantId;
        if (tenantId == Guid.Empty)
        {
            throw new OismException("UNAUTHORIZED", "Không xác định được danh tính Tenant.", 401);
        }

        var count = await _context.StockCounts
            .Include(c => c.Branch)
            .Include(c => c.Warehouse)
            .Include(c => c.Items)
                .ThenInclude(i => i.Sku)
                    .ThenInclude(s => s!.Product)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == stockCountId, ct);

        return count == null ? null : MapToResponseDto(count, false);
    }

    public async Task<PagedResultDto<StockCountResponseDto>> GetStockCountsAsync(StockCountFilterRequestDto filter, CancellationToken ct = default)
    {
        var tenantId = _tenantService.TenantId;
        if (tenantId == Guid.Empty)
        {
            throw new OismException("UNAUTHORIZED", "Không xác định được danh tính Tenant.", 401);
        }

        var query = _context.StockCounts
            .Include(c => c.Branch)
            .Include(c => c.Warehouse)
            .Include(c => c.Items)
                .ThenInclude(i => i.Sku)
                    .ThenInclude(s => s!.Product)
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId);

        if (filter.BranchId.HasValue && filter.BranchId.Value != Guid.Empty)
        {
            query = query.Where(c => c.BranchId == filter.BranchId.Value);
        }

        if (filter.WarehouseId.HasValue && filter.WarehouseId.Value != Guid.Empty)
        {
            query = query.Where(c => c.WarehouseId == filter.WarehouseId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var normalizedStatus = filter.Status.Trim();
            query = query.Where(c => c.Status == normalizedStatus);
        }

        var totalCount = await query.CountAsync(ct);

        var pageNumber = filter.PageNumber < 1 ? 1 : filter.PageNumber;
        var pageSize = filter.PageSize < 1 ? 20 : (filter.PageSize > 100 ? 100 : filter.PageSize);

        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResultDto<StockCountResponseDto>
        {
            Items = items.Select(c => MapToResponseDto(c, false)).ToList(),
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    private static string ComputeSha256Hash(string rawData)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawData));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static StockCountResponseDto MapToResponseDto(StockCount count, bool isReplay)
    {
        return new StockCountResponseDto
        {
            Id = count.Id,
            Number = count.Number,
            RequestKey = count.RequestKey,
            BranchId = count.BranchId,
            BranchName = count.Branch?.Name,
            WarehouseId = count.WarehouseId,
            WarehouseName = count.Warehouse?.Name,
            Status = count.Status,
            CreatedBy = count.CreatedBy,
            ConfirmedBy = count.ConfirmedBy,
            ConfirmedAt = count.ConfirmedAt,
            Note = count.Note,
            CreatedAt = count.CreatedAt,
            GeneratedAdjustmentId = count.GeneratedAdjustmentId,
            IsReplay = isReplay,
            Items = count.Items.Select(i => new StockCountItemResponseDto
            {
                Id = i.Id,
                SkuId = i.SkuId,
                SkuCode = i.Sku?.Code ?? string.Empty,
                ProductName = i.Sku?.Product?.Name ?? string.Empty,
                ExpectedQuantity = i.ExpectedQuantity,
                CountedQuantity = i.CountedQuantity,
                BalanceSequenceAtSnapshot = i.BalanceSequenceAtSnapshot,
                AdjustmentUnitCost = i.AdjustmentUnitCost
            }).ToList()
        };
    }
}
