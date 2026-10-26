using OISM.Application.Features.Realtime;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Inventory.Services;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.StockTransfers.Dtos;
using OISM.Domain.Entities;

namespace OISM.Application.Features.StockTransfers.Services;

/// <summary>
/// Triển khai dịch vụ Chuyển kho nội bộ 2 bước (Draft -> InTransit -> Received, Cancel chỉ khi Draft).
/// Đảm bảo Idempotency tuyệt đối theo RequestKey, hạch toán Sổ cái 2 chiều (OUT/IN) và tính toán lại WAC kho đích.
/// Tuân thủ nghiêm ngặt thứ tự khóa tài nguyên: SKU -> RequestKey (nếu có) -> Chứng từ.
/// </summary>
public class StockTransferService : IStockTransferService
{
    private const decimal MaxQuantityAmount = 999_999_999_999_999.999m;

    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IWacCalculator _wacCalculator;
    private readonly IRealtimeNotifier? _realtimeNotifier;
    private readonly ILogger<StockTransferService> _logger;

    public StockTransferService(
        IAppDbContext context,
        ICurrentTenantService tenantService,
        IWacCalculator wacCalculator,
        ILogger<StockTransferService> logger,
        IRealtimeNotifier? realtimeNotifier = null)
    {
        _context = context;
        _tenantService = tenantService;
        _wacCalculator = wacCalculator;
        _logger = logger;
        _realtimeNotifier = realtimeNotifier;
    }

    public async Task<StockTransferResponseDto> CreateTransferAsync(
        CreateStockTransferRequestDto dto,
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

        // 1. Kiểm tra validation cơ bản về cấu trúc dữ liệu
        if (dto.RequestKey == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "RequestKey không được rỗng (Guid.Empty).", 400);
        }

        if (dto.FromBranchId == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "Chi nhánh xuất không được rỗng.", 400);
        }

        if (dto.ToBranchId == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "Chi nhánh nhận không được rỗng.", 400);
        }

        if (dto.FromWarehouseId == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "Kho xuất không được rỗng.", 400);
        }

        if (dto.ToWarehouseId == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "Kho nhận không được rỗng.", 400);
        }

        if (dto.FromWarehouseId == dto.ToWarehouseId)
        {
            throw new OismException("VALIDATION_ERROR", "Kho xuất và kho nhận không được trùng nhau.", 400);
        }

        if (dto.Items == null || dto.Items.Count == 0)
        {
            throw new OismException("VALIDATION_ERROR", "Phiếu chuyển phải có ít nhất 1 sản phẩm.", 400);
        }

        if (dto.Items.Any(i => i == null))
        {
            throw new OismException("VALIDATION_ERROR", "Danh sách mặt hàng chứa phần tử rỗng (null).", 400);
        }

        if (dto.Items.GroupBy(x => x.SkuId).Any(g => g.Count() > 1))
        {
            throw new OismException("VALIDATION_ERROR", "Danh sách sản phẩm chuyển kho không được chứa SKU trùng lặp.", 400);
        }

        foreach (var item in dto.Items)
        {
            if (item.SkuId == Guid.Empty)
            {
                throw new OismException("VALIDATION_ERROR", "Mã biến thể SKU không được rỗng.", 400);
            }

            if (item.Quantity <= 0)
            {
                throw new OismException("VALIDATION_ERROR", "Số lượng chuyển phải lớn hơn 0.", 400);
            }

            if (item.Quantity > MaxQuantityAmount)
            {
                throw new OismException("VALIDATION_ERROR", "Số lượng chuyển vượt quá giới hạn tối đa cho phép.", 400);
            }

            if (decimal.Round(item.Quantity, 3) != item.Quantity)
            {
                throw new OismException("VALIDATION_ERROR", "Số lượng chuyển không được vượt quá 3 chữ số thập phân.", 400);
            }
        }

        var incomingSignature = ComputePayloadSignature(dto);

        // 2. Mở ExecutionStrategy và Transaction
        var strategy = _context.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _context.ClearChangeTracker();
            await using var tx = await _context.BeginTransactionAsync(ct);
            try
            {
                if (_context.IsRelationalDatabase)
                {
                    // Khóa Advisory theo thứ tự: SKU -> RequestKey
                    var sortedSkuIds = dto.Items.Select(i => i.SkuId).Distinct().OrderBy(id => id).ToList();
                    foreach (var skuId in sortedSkuIds)
                    {
                        await _context.ExecuteSqlRawAsync(
                            "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/' || {1}::text || '/' || {2}::text, 0));",
                            new object[] { tenantId.Value.ToString(), dto.FromBranchId.ToString(), skuId.ToString() },
                            ct);
                    }

                    await _context.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/request_key/' || {1}::text, 0));",
                        new object[] { tenantId.Value.ToString(), dto.RequestKey.ToString() },
                        ct);
                }

                // Kiểm tra Idempotency dưới khóa TRƯỚC KHI validate tài nguyên tạo mới
                var existingTransfer = await _context.StockTransfers
                    .Include(t => t.FromBranch)
                    .Include(t => t.ToBranch)
                    .Include(t => t.FromWarehouse)
                    .Include(t => t.ToWarehouse)
                    .Include(t => t.Items)
                        .ThenInclude(i => i.Sku)
                            .ThenInclude(s => s!.Product)
                    .FirstOrDefaultAsync(t => t.TenantId == tenantId.Value && t.RequestKey == dto.RequestKey, ct);

                if (existingTransfer != null)
                {
                    if (existingTransfer.PayloadSignature != incomingSignature)
                    {
                        throw new IdempotencyPayloadMismatchException("RequestKey đã tồn tại nhưng nội dung phiếu chuyển kho không khớp.");
                    }

                    var replayDto = MapToResponse(existingTransfer);
                    replayDto.IsReplay = true;
                    return replayDto;
                }

                // 3. Với yêu cầu tạo mới: Kiểm tra tính hợp lệ và tồn tại của Chi nhánh, Kho, SKU
                var fromBranch = await _context.Branches
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b => b.TenantId == tenantId.Value && b.Id == dto.FromBranchId, ct);
                if (fromBranch == null)
                {
                    throw new OismException("NOT_FOUND", "Chi nhánh xuất không tồn tại.", 404);
                }
                if (!fromBranch.IsActive)
                {
                    throw new OismException("VALIDATION_ERROR", "Chi nhánh xuất đã ngừng hoạt động.", 400);
                }

                var toBranch = await _context.Branches
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b => b.TenantId == tenantId.Value && b.Id == dto.ToBranchId, ct);
                if (toBranch == null)
                {
                    throw new OismException("NOT_FOUND", "Chi nhánh nhận không tồn tại.", 404);
                }
                if (!toBranch.IsActive)
                {
                    throw new OismException("VALIDATION_ERROR", "Chi nhánh nhận đã ngừng hoạt động.", 400);
                }

                var fromWarehouse = await _context.Warehouses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(w => w.TenantId == tenantId.Value && w.Id == dto.FromWarehouseId, ct);
                if (fromWarehouse == null)
                {
                    throw new OismException("NOT_FOUND", "Kho xuất không tồn tại.", 404);
                }
                if (fromWarehouse.BranchId != dto.FromBranchId)
                {
                    throw new OismException("VALIDATION_ERROR", "Kho xuất không thuộc chi nhánh xuất.", 400);
                }
                if (!fromWarehouse.IsActive)
                {
                    throw new OismException("VALIDATION_ERROR", "Kho xuất đã ngừng hoạt động.", 400);
                }

                var toWarehouse = await _context.Warehouses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(w => w.TenantId == tenantId.Value && w.Id == dto.ToWarehouseId, ct);
                if (toWarehouse == null)
                {
                    throw new OismException("NOT_FOUND", "Kho nhận không tồn tại.", 404);
                }
                if (toWarehouse.BranchId != dto.ToBranchId)
                {
                    throw new OismException("VALIDATION_ERROR", "Kho nhận không thuộc chi nhánh nhận.", 400);
                }
                if (!toWarehouse.IsActive)
                {
                    throw new OismException("VALIDATION_ERROR", "Kho nhận đã ngừng hoạt động.", 400);
                }

                var requestedSkuIds = dto.Items.Select(i => i.SkuId).Distinct().ToList();
                var existingSkus = await _context.Skus
                    .AsNoTracking()
                    .Where(s => s.TenantId == tenantId.Value && requestedSkuIds.Contains(s.Id))
                    .ToDictionaryAsync(s => s.Id, ct);

                foreach (var skuId in requestedSkuIds)
                {
                    if (!existingSkus.TryGetValue(skuId, out var sku))
                    {
                        throw new OismException("NOT_FOUND", $"Biến thể SKU '{skuId}' không tồn tại.", 404);
                    }
                    if (!sku.IsActive)
                    {
                        throw new OismException("VALIDATION_ERROR", $"Biến thể SKU '{sku.SKU}' đã ngừng kinh doanh.", 400);
                    }
                }

                // 4. Sinh mã phiếu an toàn concurrency bằng advisory lock cho tenant number
                if (_context.IsRelationalDatabase)
                {
                    await _context.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/stock_transfer_number', 0));",
                        new object[] { tenantId.Value.ToString() },
                        ct);
                }

                var transferNumber = await GenerateTransferNumberAsync(tenantId.Value, ct);

                var now = DateTimeOffset.UtcNow;
                var transfer = new StockTransfer
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId.Value,
                    Number = transferNumber,
                    RequestKey = dto.RequestKey,
                    PayloadSignature = incomingSignature,
                    FromBranchId = dto.FromBranchId,
                    ToBranchId = dto.ToBranchId,
                    FromWarehouseId = dto.FromWarehouseId,
                    ToWarehouseId = dto.ToWarehouseId,
                    Status = "Draft",
                    CreatedBy = createdByUserId,
                    DispatchedAt = null,
                    ReceivedAt = null,
                    ReceivedBy = null,
                    Note = dto.Note,
                    CreatedAt = now
                };

                foreach (var itemDto in dto.Items)
                {
                    transfer.Items.Add(new StockTransferItem
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId.Value,
                        TransferId = transfer.Id,
                        SkuId = itemDto.SkuId,
                        Quantity = itemDto.Quantity,
                        UnitCost = null,
                        CreatedAt = now
                    });
                }

                _context.StockTransfers.Add(transfer);
                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                _logger.LogInformation("Đã tạo phiếu chuyển kho nháp {TransferNumber} (ID: {TransferId}) cho Tenant {TenantId}.",
                    transfer.Number, transfer.Id, tenantId.Value);

                return await GetTransferResponseAsync(transfer.Id, tenantId.Value, ct);
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(ct); } catch { }
                _logger.LogError(ex, "Lỗi khi tạo phiếu chuyển kho: {Message}", ex.Message);
                throw;
            }
        });
    }

    public async Task<StockTransferResponseDto> DispatchTransferAsync(
        Guid transferId,
        DispatchStockTransferRequestDto dto,
        Guid dispatchedByUserId,
        CancellationToken ct = default)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new OismException("UNAUTHORIZED", "Không xác định được danh tính Tenant.", 401);
        }

        List<Func<Task>> pendingNotifications = new();
        var strategy = _context.CreateExecutionStrategy();
        var result = await strategy.ExecuteAsync(async () =>
        {
            pendingNotifications.Clear();
            _context.ClearChangeTracker();
            await using var tx = await _context.BeginTransactionAsync(ct);
            try
            {
                // Đọc metadata gợi ý khóa TRƯỚC KHI khóa để tuân thủ thứ tự: SKU -> RequestKey (nếu có) -> Chứng từ
                var hint = await _context.StockTransfers
                    .AsNoTracking()
                    .Where(t => t.TenantId == tenantId.Value && t.Id == transferId)
                    .Select(t => new { t.FromBranchId, t.RequestKey, SkuIds = t.Items.Select(i => i.SkuId).Distinct().ToList() })
                    .FirstOrDefaultAsync(ct);

                if (_context.IsRelationalDatabase)
                {
                    if (hint != null)
                    {
                        // 1. Khóa SKU theo thứ tự tăng dần tại chi nhánh xuất
                        foreach (var skuId in hint.SkuIds.OrderBy(id => id))
                        {
                            await _context.ExecuteSqlRawAsync(
                                "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/' || {1}::text || '/' || {2}::text, 0));",
                                new object[] { tenantId.Value.ToString(), hint.FromBranchId.ToString(), skuId.ToString() },
                                ct);
                        }

                        // 2. Khóa RequestKey nếu có
                        if (hint.RequestKey.HasValue)
                        {
                            await _context.ExecuteSqlRawAsync(
                                "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/request_key/' || {1}::text, 0));",
                                new object[] { tenantId.Value.ToString(), hint.RequestKey.Value.ToString() },
                                ct);
                        }
                    }

                    // 3. Khóa Advisory theo Transfer
                    await _context.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/transfer/' || {1}::text, 0));",
                        new object[] { tenantId.Value.ToString(), transferId.ToString() },
                        ct);
                }

                // Đọc lại dữ liệu đầy đủ dưới khóa
                var transfer = await _context.StockTransfers
                    .Include(t => t.Items)
                    .FirstOrDefaultAsync(t => t.TenantId == tenantId.Value && t.Id == transferId, ct);

                if (transfer == null)
                {
                    throw new OismException("NOT_FOUND", "Không tìm thấy phiếu chuyển kho.", 404);
                }

                if (transfer.Status != "Draft")
                {
                    throw new OismException("INVALID_STATE_TRANSITION", $"Không thể xuất chuyển phiếu ở trạng thái '{transfer.Status}'. Chỉ cho phép xuất chuyển từ 'Draft'.", 409);
                }

                var fromWarehouse = await _context.Warehouses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(w => w.TenantId == tenantId.Value && w.Id == transfer.FromWarehouseId, ct);
                if (fromWarehouse == null)
                {
                    throw new OismException("NOT_FOUND", "Kho xuất không tồn tại.", 404);
                }
                if (!fromWarehouse.IsActive)
                {
                    throw new OismException("VALIDATION_ERROR", "Kho xuất đã ngừng hoạt động.", 400);
                }

                var sortedItems = transfer.Items.OrderBy(i => i.SkuId).ToList();

                // Đọc danh mục SKU để hiển thị mã lỗi chi tiết
                var skuIds = sortedItems.Select(i => i.SkuId).Distinct().ToList();
                var skuMap = await _context.Skus
                    .AsNoTracking()
                    .Where(s => s.TenantId == tenantId.Value && skuIds.Contains(s.Id))
                    .ToDictionaryAsync(s => s.Id, s => s.SKU, ct);

                var now = DateTimeOffset.UtcNow;

                foreach (var item in sortedItems)
                {
                    var skuCode = skuMap.TryGetValue(item.SkuId, out var code) ? code : item.SkuId.ToString();

                    // 1. Kiểm tra tồn kho tại Kho xuất
                    var wBal = await _context.WarehouseInventoryBalances
                        .FirstOrDefaultAsync(w => w.TenantId == tenantId.Value && w.WarehouseId == transfer.FromWarehouseId && w.SkuId == item.SkuId, ct);

                    var wAvailable = wBal != null ? wBal.OnHand - wBal.Reserved : 0m;
                    if (wAvailable < item.Quantity)
                    {
                        throw new InsufficientStockException(skuCode, Math.Max(0, wAvailable), item.Quantity);
                    }

                    // 2. Kiểm tra tồn kho tại Chi nhánh xuất
                    var bBal = await _context.InventoryBalances
                        .FirstOrDefaultAsync(b => b.TenantId == tenantId.Value && b.BranchId == transfer.FromBranchId && b.SkuId == item.SkuId, ct);

                    var bAvailable = bBal != null ? bBal.OnHand - bBal.Reserved : 0m;
                    if (bAvailable < item.Quantity)
                    {
                        throw new InsufficientStockException(skuCode, Math.Max(0, bAvailable), item.Quantity);
                    }

                    // 3. Trừ OnHand tại kho xuất và chi nhánh xuất
                    var prevAvail = bBal!.OnHand - bBal.Reserved;
                    var newSequence = bBal.LastSequence + 1;
                    bBal.OnHand -= item.Quantity;
                    bBal.LastSequence = newSequence;
                    bBal.UpdatedAt = now;

                    var newAvail = bBal.OnHand - bBal.Reserved;

                    wBal!.OnHand -= item.Quantity;
                    wBal.LastSequence = newSequence;
                    wBal.UpdatedAt = now;

                    if (_realtimeNotifier != null)
                    {
                        var skuIdCapture = item.SkuId;
                        var branchIdCapture = transfer.FromBranchId;
                        var reorderPoint = bBal.ReorderPoint;
                        var skuCodeCapture = skuCode;
                        var tid = tenantId.Value;

                        pendingNotifications.Add(() => _realtimeNotifier.NotifyStockChangedAsync(tid, branchIdCapture, skuIdCapture, newAvail, ct));

                        if (prevAvail > reorderPoint && newAvail <= reorderPoint)
                        {
                            pendingNotifications.Add(() => _realtimeNotifier.NotifyLowStockAlertAsync(tid, branchIdCapture, skuIdCapture, skuCodeCapture, newAvail, reorderPoint, ct));
                        }
                    }

                    // 4. Snapshot WAC từ Chi nhánh xuất vào Item (giữ nguyên độ chính xác 6 số thập phân)
                    var snapshottedWac = Math.Round(bBal.AverageCost, 6, MidpointRounding.AwayFromZero);
                    item.UnitCost = snapshottedWac;

                    // 5. Ghi nhận giao dịch Sổ cái OUT (TransferOut)
                    var txOut = new InventoryTransaction
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId.Value,
                        BranchId = transfer.FromBranchId,
                        WarehouseId = transfer.FromWarehouseId,
                        SkuId = item.SkuId,
                        Type = "OUT",
                        Quantity = item.Quantity,
                        UnitCost = snapshottedWac,
                        BalanceAfter = bBal.OnHand,
                        SequenceNo = newSequence,
                        TransferItemId = item.Id,
                        CreatedAt = now
                    };
                    _context.InventoryTransactions.Add(txOut);
                }

                // 6. Cập nhật trạng thái phiếu sang InTransit
                transfer.Status = "InTransit";
                transfer.DispatchedAt = now;
                transfer.UpdatedAt = now;

                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                _logger.LogInformation("Đã xuất chuyển phiếu {TransferNumber} (ID: {TransferId}) sang InTransit.",
                    transfer.Number, transfer.Id);

                return await GetTransferResponseAsync(transfer.Id, tenantId.Value, ct);
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(ct); } catch { }
                _logger.LogError(ex, "Lỗi khi xuất chuyển kho: {Message}", ex.Message);
                throw;
            }
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
                    _logger.LogError(ex, "Lá»—i khi phÃ¡t sá»± kiá»‡n Realtime sau xuáº¥t chuyá»ƒn kho {TransferId}.", transferId);
                }
            }
        }

        return result;
    }

    public async Task<StockTransferResponseDto> ReceiveTransferAsync(
        Guid transferId,
        ReceiveStockTransferRequestDto dto,
        Guid receivedByUserId,
        CancellationToken ct = default)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new OismException("UNAUTHORIZED", "Không xác định được danh tính Tenant.", 401);
        }

        List<Func<Task>> pendingNotifications = new();
        var strategy = _context.CreateExecutionStrategy();
        var result = await strategy.ExecuteAsync(async () =>
        {
            pendingNotifications.Clear();
            _context.ClearChangeTracker();
            await using var tx = await _context.BeginTransactionAsync(ct);
            try
            {
                // Đọc metadata gợi ý khóa TRƯỚC KHI khóa để tuân thủ thứ tự: SKU -> RequestKey (nếu có) -> Chứng từ
                var hint = await _context.StockTransfers
                    .AsNoTracking()
                    .Where(t => t.TenantId == tenantId.Value && t.Id == transferId)
                    .Select(t => new { t.ToBranchId, t.RequestKey, SkuIds = t.Items.Select(i => i.SkuId).Distinct().ToList() })
                    .FirstOrDefaultAsync(ct);

                if (_context.IsRelationalDatabase)
                {
                    if (hint != null)
                    {
                        // 1. Khóa SKU theo thứ tự tăng dần tại chi nhánh đích
                        foreach (var skuId in hint.SkuIds.OrderBy(id => id))
                        {
                            await _context.ExecuteSqlRawAsync(
                                "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/' || {1}::text || '/' || {2}::text, 0));",
                                new object[] { tenantId.Value.ToString(), hint.ToBranchId.ToString(), skuId.ToString() },
                                ct);
                        }

                        // 2. Khóa RequestKey nếu có
                        if (hint.RequestKey.HasValue)
                        {
                            await _context.ExecuteSqlRawAsync(
                                "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/request_key/' || {1}::text, 0));",
                                new object[] { tenantId.Value.ToString(), hint.RequestKey.Value.ToString() },
                                ct);
                        }
                    }

                    // 3. Khóa Advisory theo Transfer
                    await _context.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/transfer/' || {1}::text, 0));",
                        new object[] { tenantId.Value.ToString(), transferId.ToString() },
                        ct);
                }

                // Đọc lại dữ liệu đầy đủ dưới khóa
                var transfer = await _context.StockTransfers
                    .Include(t => t.Items)
                    .FirstOrDefaultAsync(t => t.TenantId == tenantId.Value && t.Id == transferId, ct);

                if (transfer == null)
                {
                    throw new OismException("NOT_FOUND", "Không tìm thấy phiếu chuyển kho.", 404);
                }

                if (transfer.Status != "InTransit")
                {
                    throw new OismException("INVALID_STATE_TRANSITION", $"Không thể tiếp nhận phiếu ở trạng thái '{transfer.Status}'. Chỉ cho phép tiếp nhận từ 'InTransit'.", 409);
                }

                var toWarehouse = await _context.Warehouses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(w => w.TenantId == tenantId.Value && w.Id == transfer.ToWarehouseId, ct);
                if (toWarehouse == null)
                {
                    throw new OismException("NOT_FOUND", "Kho nhận không tồn tại.", 404);
                }
                if (!toWarehouse.IsActive)
                {
                    throw new OismException("VALIDATION_ERROR", "Kho nhận đã ngừng hoạt động.", 400);
                }

                // T4A-F03: Kiểm tra tính toàn vẹn snapshot UnitCost trước khi thực hiện bất kỳ mutation nào
                foreach (var item in transfer.Items)
                {
                    if (!item.UnitCost.HasValue)
                    {
                        throw new OismException("INVALID_STATE_TRANSITION",
                            $"Mặt hàng '{item.SkuId}' trong phiếu chuyển kho thiếu đơn giá vốn snapshot (UnitCost is null). Không thể nhập kho nhận.", 409);
                    }
                }

                var sortedItems = transfer.Items.OrderBy(i => i.SkuId).ToList();
                var now = DateTimeOffset.UtcNow;

                foreach (var item in sortedItems)
                {
                    var transferUnitCost = item.UnitCost!.Value;

                    // 1. Tìm hoặc khởi tạo số dư Chi nhánh đích
                    var bBal = await _context.InventoryBalances
                        .FirstOrDefaultAsync(b => b.TenantId == tenantId.Value && b.BranchId == transfer.ToBranchId && b.SkuId == item.SkuId, ct);

                    decimal newWac;
                    long newSequence;
                    if (bBal == null)
                    {
                        newWac = Math.Round(transferUnitCost, 6, MidpointRounding.AwayFromZero);
                        newSequence = 1;
                        bBal = new InventoryBalance
                        {
                            TenantId = tenantId.Value,
                            BranchId = transfer.ToBranchId,
                            SkuId = item.SkuId,
                            OnHand = item.Quantity,
                            Reserved = 0m,
                            AverageCost = newWac,
                            LastSequence = newSequence,
                            CreatedAt = now,
                            UpdatedAt = now
                        };
                        _context.InventoryBalances.Add(bBal);
                    }
                    else
                    {
                        var destOnHandBefore = bBal.OnHand;
                        var destAvgCostBefore = bBal.AverageCost;
                        newWac = _wacCalculator.CalculateWac(destOnHandBefore, destAvgCostBefore, item.Quantity, transferUnitCost);
                        newSequence = bBal.LastSequence + 1;

                        bBal.OnHand += item.Quantity;
                        bBal.AverageCost = newWac;
                        bBal.LastSequence = newSequence;
                        bBal.UpdatedAt = now;
                    }

                    // 2. Tìm hoặc khởi tạo số dư Kho đích
                    var wBal = await _context.WarehouseInventoryBalances
                        .FirstOrDefaultAsync(w => w.TenantId == tenantId.Value && w.WarehouseId == transfer.ToWarehouseId && w.SkuId == item.SkuId, ct);

                    if (wBal == null)
                    {
                        wBal = new WarehouseInventoryBalance
                        {
                            TenantId = tenantId.Value,
                            WarehouseId = transfer.ToWarehouseId,
                            BranchId = transfer.ToBranchId,
                            SkuId = item.SkuId,
                            OnHand = item.Quantity,
                            Reserved = 0m,
                            LastSequence = newSequence,
                            CreatedAt = now,
                            UpdatedAt = now
                        };
                        _context.WarehouseInventoryBalances.Add(wBal);
                    }
                    else
                    {
                        wBal.OnHand += item.Quantity;
                        wBal.LastSequence = newSequence;
                        wBal.UpdatedAt = now;
                    }

                    if (_realtimeNotifier != null)
                    {
                        var skuIdCapture = item.SkuId;
                        var toBranchId = transfer.ToBranchId;
                        var tid = tenantId.Value;
                        var newAvail = bBal.OnHand - bBal.Reserved;
                        pendingNotifications.Add(() => _realtimeNotifier.NotifyStockChangedAsync(tid, toBranchId, skuIdCapture, newAvail, ct));
                    }

                    // 3. Ghi nhận giao dịch Sổ cái IN (TransferIn)
                    var txIn = new InventoryTransaction
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId.Value,
                        BranchId = transfer.ToBranchId,
                        WarehouseId = transfer.ToWarehouseId,
                        SkuId = item.SkuId,
                        Type = "IN",
                        Quantity = item.Quantity,
                        UnitCost = Math.Round(transferUnitCost, 6, MidpointRounding.AwayFromZero),
                        BalanceAfter = bBal.OnHand,
                        SequenceNo = newSequence,
                        TransferItemId = item.Id,
                        CreatedAt = now
                    };
                    _context.InventoryTransactions.Add(txIn);
                }

                // 4. Cập nhật trạng thái phiếu sang Received
                transfer.Status = "Received";
                transfer.ReceivedAt = now;
                transfer.ReceivedBy = receivedByUserId;
                transfer.UpdatedAt = now;

                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                _logger.LogInformation("Đã tiếp nhận thành công phiếu {TransferNumber} (ID: {TransferId}) sang Received.",
                    transfer.Number, transfer.Id);

                return await GetTransferResponseAsync(transfer.Id, tenantId.Value, ct);
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(ct); } catch { }
                _logger.LogError(ex, "Lỗi khi tiếp nhận kho: {Message}", ex.Message);
                throw;
            }
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
                    _logger.LogError(ex, "Lá»—i khi phÃ¡t sá»± kiá»‡n Realtime sau tiáº¿p nháº­n chuyá»ƒn kho {TransferId}.", transferId);
                }
            }
        }

        return result;
    }

    public async Task<StockTransferResponseDto> CancelTransferAsync(
        Guid transferId,
        CancelStockTransferRequestDto? dto,
        Guid cancelledByUserId,
        CancellationToken ct = default)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new OismException("UNAUTHORIZED", "Không xác định được danh tính Tenant.", 401);
        }

        var strategy = _context.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _context.ClearChangeTracker();
            await using var tx = await _context.BeginTransactionAsync(ct);
            try
            {
                // Đọc metadata gợi ý khóa TRƯỚC KHI khóa để tuân thủ thứ tự: SKU -> RequestKey (nếu có) -> Chứng từ
                var hint = await _context.StockTransfers
                    .AsNoTracking()
                    .Where(t => t.TenantId == tenantId.Value && t.Id == transferId)
                    .Select(t => new { t.FromBranchId, t.RequestKey, SkuIds = t.Items.Select(i => i.SkuId).Distinct().ToList() })
                    .FirstOrDefaultAsync(ct);

                if (_context.IsRelationalDatabase)
                {
                    if (hint != null)
                    {
                        foreach (var skuId in hint.SkuIds.OrderBy(id => id))
                        {
                            await _context.ExecuteSqlRawAsync(
                                "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/' || {1}::text || '/' || {2}::text, 0));",
                                new object[] { tenantId.Value.ToString(), hint.FromBranchId.ToString(), skuId.ToString() },
                                ct);
                        }

                        if (hint.RequestKey.HasValue)
                        {
                            await _context.ExecuteSqlRawAsync(
                                "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/request_key/' || {1}::text, 0));",
                                new object[] { tenantId.Value.ToString(), hint.RequestKey.Value.ToString() },
                                ct);
                        }
                    }

                    await _context.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock(hashtextextended({0}::text || '/transfer/' || {1}::text, 0));",
                        new object[] { tenantId.Value.ToString(), transferId.ToString() },
                        ct);
                }

                var transfer = await _context.StockTransfers
                    .FirstOrDefaultAsync(t => t.TenantId == tenantId.Value && t.Id == transferId, ct);

                if (transfer == null)
                {
                    throw new OismException("NOT_FOUND", "Không tìm thấy phiếu chuyển kho.", 404);
                }

                if (transfer.Status != "Draft")
                {
                    throw new OismException("INVALID_STATE_TRANSITION", $"Không thể hủy phiếu chuyển kho ở trạng thái '{transfer.Status}'. Chỉ cho phép hủy từ 'Draft'.", 409);
                }

                var now = DateTimeOffset.UtcNow;
                transfer.Status = "Cancelled";
                if (!string.IsNullOrWhiteSpace(dto?.Reason))
                {
                    var cleanReason = dto.Reason.Trim();
                    transfer.Note = string.IsNullOrWhiteSpace(transfer.Note)
                        ? $"Lý do hủy: {cleanReason}"
                        : $"{transfer.Note} (Lý do hủy: {cleanReason})";
                }
                transfer.UpdatedAt = now;

                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                _logger.LogInformation("Đã hủy phiếu chuyển kho {TransferNumber} (ID: {TransferId}) sang Cancelled.",
                    transfer.Number, transfer.Id);

                return await GetTransferResponseAsync(transfer.Id, tenantId.Value, ct);
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(ct); } catch { }
                _logger.LogError(ex, "Lỗi khi hủy phiếu chuyển kho: {Message}", ex.Message);
                throw;
            }
        });
    }

    public Task<StockTransferResponseDto> CancelTransferAsync(
        Guid transferId,
        string? reason,
        Guid cancelledByUserId,
        CancellationToken ct = default)
    {
        return CancelTransferAsync(transferId, new CancelStockTransferRequestDto { Reason = reason }, cancelledByUserId, ct);
    }

    public Task<StockTransferResponseDto?> GetTransferByIdAsync(Guid transferId, CancellationToken ct = default)
    {
        throw new NotImplementedException("Endpoint truy vấn phiếu chuyển kho thuộc phạm vi BE2 (Nhựt).");
    }

    public Task<PagedResultDto<StockTransferResponseDto>> GetTransfersAsync(StockTransferFilterRequestDto filter, CancellationToken ct = default)
    {
        throw new NotImplementedException("Endpoint truy vấn phiếu chuyển kho thuộc phạm vi BE2 (Nhựt).");
    }

    private static string ComputePayloadSignature(CreateStockTransferRequestDto dto)
    {
        var normalizedNote = (dto.Note ?? string.Empty).Trim();
        var sortedItems = dto.Items
            .Where(i => i != null)
            .OrderBy(i => i.SkuId)
            .Select(i => $"{i.SkuId:D}:{i.Quantity:0.###}");
        var rawPayload = $"{dto.FromBranchId:D}|{dto.ToBranchId:D}|{dto.FromWarehouseId:D}|{dto.ToWarehouseId:D}|{normalizedNote}|{string.Join(",", sortedItems)}";

        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawPayload));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private async Task<string> GenerateTransferNumberAsync(Guid tenantId, CancellationToken ct)
    {
        var dateStr = DateTime.UtcNow.ToString("yyyyMMdd");
        var prefix = $"TRF-{dateStr}-";

        var maxNumber = await _context.StockTransfers
            .IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.Number.StartsWith(prefix))
            .OrderByDescending(t => t.Number)
            .Select(t => t.Number)
            .FirstOrDefaultAsync(ct);

        int nextSeq = 1;
        if (!string.IsNullOrEmpty(maxNumber) && maxNumber.Length >= prefix.Length)
        {
            var suffix = maxNumber[prefix.Length..];
            if (int.TryParse(suffix, out var currentSeq))
            {
                nextSeq = currentSeq + 1;
            }
        }

        var candidate = $"{prefix}{nextSeq:D5}";
        while (await _context.StockTransfers.IgnoreQueryFilters().AnyAsync(t => t.TenantId == tenantId && t.Number == candidate, ct))
        {
            nextSeq++;
            candidate = $"{prefix}{nextSeq:D5}";
        }

        return candidate;
    }

    private async Task<StockTransferResponseDto> GetTransferResponseAsync(Guid transferId, Guid tenantId, CancellationToken ct, bool isReplay = false)
    {
        var transfer = await _context.StockTransfers
            .Include(t => t.FromBranch)
            .Include(t => t.ToBranch)
            .Include(t => t.FromWarehouse)
            .Include(t => t.ToWarehouse)
            .Include(t => t.Items)
                .ThenInclude(i => i.Sku)
                    .ThenInclude(s => s!.Product)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == transferId, ct);

        if (transfer == null)
        {
            throw new OismException("NOT_FOUND", "Không tìm thấy phiếu chuyển kho.", 404);
        }

        var res = MapToResponse(transfer);
        res.IsReplay = isReplay;
        return res;
    }

    private static StockTransferResponseDto MapToResponse(StockTransfer transfer)
    {
        return new StockTransferResponseDto
        {
            Id = transfer.Id,
            Number = transfer.Number,
            RequestKey = transfer.RequestKey,
            FromBranchId = transfer.FromBranchId,
            FromBranchName = transfer.FromBranch?.Name,
            ToBranchId = transfer.ToBranchId,
            ToBranchName = transfer.ToBranch?.Name,
            FromWarehouseId = transfer.FromWarehouseId,
            FromWarehouseName = transfer.FromWarehouse?.Name,
            ToWarehouseId = transfer.ToWarehouseId,
            ToWarehouseName = transfer.ToWarehouse?.Name,
            Status = transfer.Status,
            CreatedBy = transfer.CreatedBy,
            DispatchedAt = transfer.DispatchedAt,
            ReceivedAt = transfer.ReceivedAt,
            ReceivedBy = transfer.ReceivedBy,
            Note = transfer.Note,
            CreatedAt = transfer.CreatedAt,
            Items = transfer.Items.Select(i => new StockTransferItemResponseDto
            {
                Id = i.Id,
                SkuId = i.SkuId,
                SkuCode = i.Sku?.Code ?? i.Sku?.SKU ?? string.Empty,
                ProductName = i.Sku?.Product?.Name ?? string.Empty,
                Quantity = i.Quantity,
                UnitCost = i.UnitCost
            }).ToList()
        };
    }
}
