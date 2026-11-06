using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Pricing.Services;
using OISM.Application.Features.Realtime;
using OISM.Application.Features.Realtime.Dtos;
using OISM.Application.Features.SalesInvoices.Services;
using OISM.Domain.Common;
using OISM.Domain.Entities;

namespace OISM.Application.Features.Orders.Services;

/// <summary>
/// Điều phối vòng đời đơn hàng và giữ hợp đồng IOrderService cho các caller hiện có.
/// </summary>
public partial class OrderService : IOrderService, IPosCheckoutService
{
    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _currentTenantService;
    private readonly ILogger<OrderService> _logger;
    private readonly PosCheckoutService _checkout;
    private readonly OrderIntakeService _intake;
    private readonly MarketplaceOrderService _marketplace;
    private readonly OrderQueryService _query;

    public const decimal MaxMoneyAmount = OrderLimits.MaxMoneyAmount;
    public const decimal MaxQuantityAmount = OrderLimits.MaxQuantityAmount;

    private readonly IRealtimeNotifier? _realtimeNotifier;

    public OrderService(
        IAppDbContext context,
        ICurrentTenantService currentTenantService,
        ISkuPriceResolver skuPriceResolver,
        ILogger<OrderService>? logger = null,
        IRealtimeNotifier? realtimeNotifier = null)
    {
        _context = context;
        _currentTenantService = currentTenantService;
        _logger = logger ?? NullLogger<OrderService>.Instance;
        _realtimeNotifier = realtimeNotifier;
        _query = new OrderQueryService(context, currentTenantService);
        _checkout = new PosCheckoutService(context, currentTenantService, skuPriceResolver, _logger, realtimeNotifier);
        _intake = new OrderIntakeService(context, currentTenantService, skuPriceResolver, _query, _logger, realtimeNotifier);
        _marketplace = new MarketplaceOrderService(_intake);
    }

    public OrderService(
        IAppDbContext context,
        ICurrentTenantService currentTenantService,
        ILogger<OrderService>? logger = null,
        IRealtimeNotifier? realtimeNotifier = null)
        : this(context, currentTenantService, new SkuPriceResolver(context), logger, realtimeNotifier)
    {
    }

    public Task<PosCheckoutResponseDto> CheckoutPosAsync(PosCheckoutRequestDto dto, Guid? cashierUserId, CancellationToken ct = default) =>
        _checkout.CheckoutPosAsync(dto, cashierUserId, ct);

    public Task<OrderResponseDto> CreateOrderAsync(CreateOrderRequestDto dto, Guid? createdByUserId, CancellationToken ct = default) =>
        _intake.CreateOrderAsync(dto, createdByUserId, ct);

    public Task<OrderResponseDto> CreateMarketplaceReservedOrderAsync(InternalMarketplaceOrderContext context, CancellationToken ct = default) =>
        _marketplace.CreateMarketplaceReservedOrderAsync(context, ct);

    public Task<OrderResponseDto?> GetOrderByIdAsync(Guid orderId, CancellationToken ct = default) =>
        _query.GetOrderByIdAsync(orderId, ct);

    public Task<PagedResultDto<OrderResponseDto>> GetOrdersAsync(OrderFilterRequestDto filter, CancellationToken ct = default) =>
        _query.GetOrdersAsync(filter, ct);

    public Task<PagedResult<OrderSummaryDto>> GetOrdersAsync(OrderFilterDto filter, CancellationToken ct = default) =>
        _query.GetOrdersAsync(filter, ct);

    public Task<OrderDetailDto?> GetOrderDetailByIdAsync(Guid id, CancellationToken ct = default) =>
        _query.GetOrderDetailByIdAsync(id, ct);

    public async Task<OrderResponseDto> ConfirmOrderAsync(Guid orderId, ConfirmOrderRequestDto dto, Guid? confirmedByUserId, CancellationToken ct = default)
    {
        var tenantId = _currentTenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Không xác định được TenantId hợp lệ từ ngữ cảnh xác thực.");
        }

        if (orderId == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "Mã đơn hàng không được để trống (Guid.Empty).", 400);
        }

        return await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            _context.UnitOfWork.ResetTracking();

            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);
            try
            {
                // 1. Pre-read để lấy metadata cần thiết cho việc khóa
                var meta = await _context.Orders
                    .Where(o => o.TenantId == tenantId.Value && o.Id == orderId)
                    .Select(o => new
                    {
                        o.BranchId,
                        o.RequestKey,
                        SkuIds = o.Items.Select(i => i.SkuId).ToList()
                    })
                    .FirstOrDefaultAsync(ct);

                if (meta == null)
                {
                    throw new OismException("ORDER_NOT_FOUND", $"Không tìm thấy đơn hàng với mã '{orderId}'.", 404);
                }

                var sortedSkuIds = meta.SkuIds.Distinct().OrderBy(id => id).ToList();

                // 2. Lấy khóa theo thứ tự phân cấp: SKU Locks -> RequestKey Lock -> Order Lock
                await _context.Locks.LockSkuStockAsync(tenantId.Value, meta.BranchId, sortedSkuIds, ct);

                await _context.Locks.LockRequestKeyAsync(tenantId.Value, meta.RequestKey, ct);

                await _context.Locks.LockOrderAsync(tenantId.Value, orderId, ct);

                // 3. Đọc dữ liệu đơn hàng dưới khóa
                var order = await _context.Orders
                    .Include(o => o.Items)
                    .Include(o => o.Reservations)
                    .Include(o => o.Payment)
                    .FirstOrDefaultAsync(o => o.TenantId == tenantId.Value && o.Id == orderId, ct);

                if (order == null)
                {
                    throw new OismException("ORDER_NOT_FOUND", $"Không tìm thấy đơn hàng với mã '{orderId}'.", 404);
                }

                if (string.Equals(order.Channel, "POS", StringComparison.OrdinalIgnoreCase))
                {
                    throw new OismException("INVALID_STATE_TRANSITION", "Đơn hàng tại quầy POS không hỗ trợ xác nhận lại qua quy trình trực tuyến.", 409);
                }

                if (!order.CanConfirm)
                {
                    throw new OismException("INVALID_STATE_TRANSITION", $"Không thể xác nhận đơn hàng ở trạng thái '{order.Status}'. Đơn hàng phải ở trạng thái 'Reserved'.", 409);
                }

                var now = DateTimeOffset.UtcNow;

                // 4. Kiểm tra toàn bộ phiếu giữ chỗ (Reservations)
                if (order.Reservations == null || order.Reservations.Count == 0)
                {
                    throw new OismException("RESERVATION_EXPIRED", "Đơn hàng không có phiếu giữ chỗ hợp lệ.", 409);
                }

                foreach (var res in order.Reservations)
                {
                    if (!res.IsActiveAt(now))
                    {
                        throw new OismException("RESERVATION_EXPIRED", "Phiếu giữ chỗ của đơn hàng đã hết hạn hoặc không còn hiệu lực. Vui lòng tạo lại đơn hàng mới.", 409);
                    }
                }

                // 4b. Kiểm tra tính toàn vẹn và mức độ phân bổ giữ chỗ cho từng dòng sản phẩm (T3-F03)
                var reservationsByItem = order.Reservations.GroupBy(r => r.OrderItemId).ToDictionary(g => g.Key, g => g.ToList());
                var orderItemIds = order.Items.Select(i => i.Id).ToHashSet();

                if (order.Reservations.Any(r => !orderItemIds.Contains(r.OrderItemId)))
                {
                    throw new OismException("INVALID_STATE_TRANSITION", "Tồn tại phiếu giữ chỗ không khớp với các dòng sản phẩm của đơn hàng.", 409);
                }

                foreach (var item in order.Items)
                {
                    if (!reservationsByItem.TryGetValue(item.Id, out var itemResList) || itemResList.Count == 0)
                    {
                        throw new OismException("INVALID_STATE_TRANSITION", $"Dòng đơn hàng SKU '{item.SkuCodeSnapshot}' chưa có phiếu giữ chỗ tồn kho.", 409);
                    }

                    foreach (var res in itemResList)
                    {
                        if (res.SkuId != item.SkuId || res.BranchId != order.BranchId)
                        {
                            throw new OismException("INVALID_STATE_TRANSITION", $"Phiếu giữ chỗ của SKU '{item.SkuCodeSnapshot}' không khớp thông tin SKU hoặc Chi nhánh của đơn hàng.", 409);
                        }
                    }

                    var totalAllocated = itemResList.Sum(r => r.Quantity);
                    if (totalAllocated != item.BaseQuantity)
                    {
                        throw new OismException("INVALID_STATE_TRANSITION", $"Số lượng giữ chỗ ({totalAllocated:G29}) không khớp với lượng gốc yêu cầu ({item.BaseQuantity:G29}) của dòng đơn hàng SKU '{item.SkuCodeSnapshot}'.", 409);
                    }
                }

                // 5. Tải số dư tồn kho chi nhánh và các kho liên quan
                var branchBalances = await _context.InventoryBalances
                    .Where(b => b.TenantId == tenantId.Value && b.BranchId == order.BranchId && sortedSkuIds.Contains(b.SkuId))
                    .ToDictionaryAsync(b => b.SkuId, ct);

                var resWarehouseIds = order.Reservations.Select(r => r.WarehouseId).Distinct().ToList();
                var whBalances = await _context.WarehouseInventoryBalances
                    .Where(wb => wb.TenantId == tenantId.Value && wb.BranchId == order.BranchId && resWarehouseIds.Contains(wb.WarehouseId) && sortedSkuIds.Contains(wb.SkuId))
                    .ToListAsync(ct);

                // 6. Snapshot WAC CostPrice vào các dòng đơn hàng (order_items) theo đúng precision numeric(20,6) (T3-F02)
                var snapshotWacBySku = new Dictionary<Guid, decimal>();
                foreach (var item in order.Items)
                {
                    if (!branchBalances.TryGetValue(item.SkuId, out var bBal))
                    {
                        throw new OismException("INSUFFICIENT_STOCK", $"Không tìm thấy số dư tồn kho chi nhánh cho SKU '{item.SkuCodeSnapshot}'.", 409);
                    }

                    var snapshotWac = Math.Round(bBal.AverageCost, 6, MidpointRounding.AwayFromZero);
                    item.CostPrice = snapshotWac;
                    snapshotWacBySku[item.SkuId] = snapshotWac;
                }

                // 7. Chuyển Reservation sang Consumed, trừ OnHand & Reserved, ghi Sổ cái OUT
                foreach (var res in order.Reservations)
                {
                    var whBal = whBalances.FirstOrDefault(wb => wb.WarehouseId == res.WarehouseId && wb.SkuId == res.SkuId);
                    if (whBal == null)
                    {
                        throw new OismException("INSUFFICIENT_STOCK", $"Không tìm thấy số dư tồn kho tại kho '{res.WarehouseId}' cho SKU '{res.SkuId}'.", 409);
                    }

                    var bBal = branchBalances[res.SkuId];

                    // Trừ tồn vật lý và giảm tồn giữ chỗ ở cả 2 cấp
                    whBal.OnHand -= res.Quantity;
                    whBal.Reserved -= res.Quantity;

                    var nextSeq = bBal.LastSequence + 1;
                    bBal.LastSequence = nextSeq;
                    whBal.LastSequence = nextSeq;

                    whBal.UpdatedAt = now;

                    bBal.OnHand -= res.Quantity;
                    bBal.Reserved -= res.Quantity;
                    bBal.UpdatedAt = now;

                    // Chuyển reservation sang Consumed
                    res.Consume(now);

                    // Ghi bút toán Sổ cái OUT với đúng precision numeric(20,6) snapshot (T3-F02)
                    var unitCostSnapshot = snapshotWacBySku.TryGetValue(res.SkuId, out var wac)
                        ? wac
                        : Math.Round(bBal.AverageCost, 6, MidpointRounding.AwayFromZero);

                    var ledger = new InventoryTransaction
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId.Value,
                        BranchId = order.BranchId,
                        WarehouseId = res.WarehouseId,
                        SkuId = res.SkuId,
                        Type = "OUT",
                        Quantity = res.Quantity,
                        UnitCost = unitCostSnapshot,
                        BalanceAfter = bBal.OnHand,
                        SequenceNo = nextSeq,
                        OrderItemId = res.OrderItemId,
                        CreatedAt = now
                    };
                    _context.InventoryTransactions.Add(ledger);
                }

                // 8. Cập nhật trạng thái đơn hàng sang Confirmed
                order.Confirm(confirmedByUserId, now);

                var branch = await _context.Branches.AsNoTracking()
                    .FirstOrDefaultAsync(b => b.TenantId == tenantId.Value && b.Id == order.BranchId, ct);
                var confirmedByUser = confirmedByUserId.HasValue
                    ? await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.TenantId == tenantId.Value && u.Id == confirmedByUserId.Value, ct)
                    : null;
                var paymentMethod = order.Payment?.Method;

                var invoice = SalesInvoiceFactory.Create(order, branch, confirmedByUser, paymentMethod, now);
                _context.SalesInvoices.Add(invoice);

                await _context.SaveChangesAsync(ct);

                await _context.Locks.SetConstraintsImmediateAsync(ct);

                await tx.CommitAsync(ct);

                _logger.LogInformation("Xác nhận đơn hàng online {OrderNumber} thành công. Đã snapshot giá vốn và xuất kho.", order.Number);

                return await _query.MapToOrderResponseDtoAsync(order, ct);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _logger.LogError(ex, "Xác nhận đơn hàng {OrderId} thất bại. Đã rollback giao dịch.", orderId);
                throw;
            }
        });
    }

    public async Task<OrderResponseDto> CompleteOrderAsync(Guid orderId, CompleteOrderRequestDto dto, Guid? completedByUserId, CancellationToken ct = default)
    {
        var tenantId = _currentTenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Không xác định được TenantId hợp lệ từ ngữ cảnh xác thực.");
        }

        if (orderId == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "Mã đơn hàng không được để trống (Guid.Empty).", 400);
        }

        return await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            _context.UnitOfWork.ResetTracking();

            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);
            try
            {
                // 1. Khóa theo OrderId
                await _context.Locks.LockOrderAsync(tenantId.Value, orderId, ct);

                // 2. Đọc đơn hàng dưới khóa
                var order = await _context.Orders
                    .Include(o => o.Items)
                    .Include(o => o.Payment)
                    .FirstOrDefaultAsync(o => o.TenantId == tenantId.Value && o.Id == orderId, ct);

                if (order == null)
                {
                    throw new OismException("ORDER_NOT_FOUND", $"Không tìm thấy đơn hàng với mã '{orderId}'.", 404);
                }

                if (string.Equals(order.Channel, "POS", StringComparison.OrdinalIgnoreCase))
                {
                    throw new OismException("INVALID_STATE_TRANSITION", "Đơn hàng tại quầy POS không hỗ trợ hoàn tất lại qua quy trình trực tuyến.", 409);
                }

                // Idempotent replay: Nếu đơn đã Completed, giữ nguyên CompletedAt và trả 200 OK
                if (order.Status == OrderStatus.Completed)
                {
                    _logger.LogInformation("Idempotent replay hoàn tất đơn hàng online {OrderNumber}.", order.Number);
                    await tx.CommitAsync(ct);
                    return await _query.MapToOrderResponseDtoAsync(order, ct);
                }

                if (!order.CanComplete)
                {
                    throw new OismException("INVALID_STATE_TRANSITION", $"Không thể hoàn tất đơn hàng ở trạng thái '{order.Status}'. Đơn hàng phải ở trạng thái 'Confirmed'.", 409);
                }

                var now = DateTimeOffset.UtcNow;
                order.Complete(now);

                // Tuyệt đối không thay đổi tồn kho, không ghi ledger OUT lần thứ hai
                await _context.SaveChangesAsync(ct);

                await _context.Locks.SetConstraintsImmediateAsync(ct);

                await tx.CommitAsync(ct);

                _logger.LogInformation("Hoàn tất đơn hàng online {OrderNumber} thành công.", order.Number);
                return await _query.MapToOrderResponseDtoAsync(order, ct);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _logger.LogError(ex, "Hoàn tất đơn hàng {OrderId} thất bại. Đã rollback giao dịch.", orderId);
                throw;
            }
        });
    }

    public async Task<OrderResponseDto> CancelOrderAsync(Guid orderId, CancelOrderRequestDto dto, Guid? cancelledByUserId, CancellationToken ct = default)
    {
        var tenantId = _currentTenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Không xác định được TenantId hợp lệ từ ngữ cảnh xác thực.");
        }

        if (orderId == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "Mã đơn hàng không được để trống (Guid.Empty).", 400);
        }

        if (dto == null || string.IsNullOrWhiteSpace(dto.Reason))
        {
            throw new OismException("VALIDATION_ERROR", "Lý do hủy đơn không được để trống.", 400);
        }

        List<Func<Task>> pendingNotifications = new();
        var result = await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            pendingNotifications.Clear();
            _context.UnitOfWork.ResetTracking();

            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);
            try
            {
                // 1. Pre-read để lấy metadata khóa
                var meta = await _context.Orders
                    .Where(o => o.TenantId == tenantId.Value && o.Id == orderId)
                    .Select(o => new
                    {
                        o.BranchId,
                        o.RequestKey,
                        ItemSkuIds = o.Items.Select(i => i.SkuId).ToList(),
                        ResSkuIds = o.Reservations.Select(r => r.SkuId).ToList()
                    })
                    .FirstOrDefaultAsync(ct);

                if (meta == null)
                {
                    throw new OismException("ORDER_NOT_FOUND", $"Không tìm thấy đơn hàng với mã '{orderId}'.", 404);
                }

                var sortedSkuIds = meta.ItemSkuIds.Concat(meta.ResSkuIds).Distinct().OrderBy(id => id).ToList();

                // 2. Khóa advisory locks: SKU Locks -> RequestKey Lock -> Order Lock
                await _context.Locks.LockSkuStockAsync(tenantId.Value, meta.BranchId, sortedSkuIds, ct);

                await _context.Locks.LockRequestKeyAsync(tenantId.Value, meta.RequestKey, ct);

                await _context.Locks.LockOrderAsync(tenantId.Value, orderId, ct);

                // 3. Đọc dữ liệu đơn hàng dưới khóa
                var order = await _context.Orders
                    .Include(o => o.Items)
                    .Include(o => o.Reservations)
                    .Include(o => o.Payment)
                    .FirstOrDefaultAsync(o => o.TenantId == tenantId.Value && o.Id == orderId, ct);

                if (order == null)
                {
                    throw new OismException("ORDER_NOT_FOUND", $"Không tìm thấy đơn hàng với mã '{orderId}'.", 404);
                }

                if (!order.IsCancellableStatus)
                {
                    throw new OismException("INVALID_STATE_TRANSITION", $"Không thể hủy đơn hàng ở trạng thái '{order.Status}'. Chỉ cho phép hủy đơn ở trạng thái 'Draft' hoặc 'Reserved'.", 409);
                }

                if (!order.CanCancel)
                {
                    throw new OismException("INVALID_STATE_TRANSITION", "Đơn hàng tại quầy POS đã thanh toán không hỗ trợ hủy qua quy trình trực tuyến.", 409);
                }

                var now = DateTimeOffset.UtcNow;

                // 4. Giải phóng toàn bộ phiếu giữ chỗ Active của đơn hàng (kể cả đã quá hạn TTL)
                if (order.Reservations != null && order.Reservations.Count > 0)
                {
                    var activeReservations = order.Reservations.Where(r => r.Status == ReservationStatus.Active).ToList();
                    if (activeReservations.Count > 0)
                    {
                        var branchBalances = await _context.InventoryBalances
                            .Where(b => b.TenantId == tenantId.Value && b.BranchId == order.BranchId && sortedSkuIds.Contains(b.SkuId))
                            .ToDictionaryAsync(b => b.SkuId, ct);

                        var resWarehouseIds = activeReservations.Select(r => r.WarehouseId).Distinct().ToList();
                        var whBalances = await _context.WarehouseInventoryBalances
                            .Where(wb => wb.TenantId == tenantId.Value && wb.BranchId == order.BranchId && resWarehouseIds.Contains(wb.WarehouseId) && sortedSkuIds.Contains(wb.SkuId))
                            .ToListAsync(ct);

                        foreach (var res in activeReservations)
                        {
                            var whBal = whBalances.FirstOrDefault(wb => wb.WarehouseId == res.WarehouseId && wb.SkuId == res.SkuId);
                            if (whBal != null)
                            {
                                whBal.Reserved -= res.Quantity;
                                whBal.UpdatedAt = now;
                            }

                            if (branchBalances.TryGetValue(res.SkuId, out var bBal))
                            {
                                bBal.Reserved -= res.Quantity;
                                bBal.UpdatedAt = now;
                            }

                            res.Release();
                        }
                    }
                }

                // 5. Cập nhật trạng thái đơn hàng sang Cancelled
                order.Cancel(now);

                // Không thay đổi OnHand, không ghi ledger OUT
                await _context.SaveChangesAsync(ct);

                await _context.Locks.SetConstraintsImmediateAsync(ct);

                await tx.CommitAsync(ct);

                _logger.LogInformation("Hủy đơn hàng {OrderNumber} thành công. Đã giải phóng toàn bộ tồn giữ chỗ.", order.Number);

                
                if (_realtimeNotifier != null && order.Reservations != null)
                {
                    try
                    {
                        var releasedSkus = order.Reservations
                            .Where(r => r.Status == ReservationStatus.Released)
                            .GroupBy(r => r.SkuId)
                            .ToList();

                        if (releasedSkus.Count > 0)
                        {
                            var branchBalances = await _context.InventoryBalances
                                .AsNoTracking()
                                .Where(b => b.TenantId == tenantId.Value && b.BranchId == order.BranchId && releasedSkus.Select(g => g.Key).Contains(b.SkuId))
                                .ToDictionaryAsync(b => b.SkuId, ct);

                            foreach (var g in releasedSkus)
                            {
                                if (branchBalances.TryGetValue(g.Key, out var bBal))
                                {
                                    var newAvail = bBal.OnHand - bBal.Reserved;
                                    var skuIdCapture = g.Key;
                                    var branchIdCapture = order.BranchId;
                                    var tidCapture = tenantId.Value;
                                    pendingNotifications.Add(() => _realtimeNotifier.NotifyStockChangedAsync(tidCapture, branchIdCapture, skuIdCapture, newAvail, ct));
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi khi chuẩn bị sự kiện Realtime sau commit hủy đơn {OrderNumber}.", order.Number);
                    }
                }

                return await _query.MapToOrderResponseDtoAsync(order, ct);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _logger.LogError(ex, "Hủy đơn hàng {OrderId} thất bại. Đã rollback giao dịch.", orderId);
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
                    _logger.LogError(ex, "Lỗi khi phát sự kiện Realtime sau commit hủy đơn {OrderId}.", orderId);
                }
            }
        }

        return result;
    }

    public async Task<OrderDetailDto> CreateOrderAsync(CreateOrderDto dto, Guid? userId, CancellationToken ct = default)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        if (dto.Items == null || dto.Items.Count == 0)
        {
            throw new ArgumentException("Đơn hàng phải chứa ít nhất một mặt hàng.");
        }

        var req = new CreateOrderRequestDto
        {
            BranchId = dto.BranchId,
            Channel = dto.Channel,
            CustomerName = dto.CustomerName,
            CustomerPhone = dto.CustomerPhone,
            ShippingAddress = dto.ShippingAddress,
            RequestKey = (dto.RequestKey.HasValue && dto.RequestKey.Value != Guid.Empty) ? dto.RequestKey.Value : Guid.NewGuid(),
            Items = dto.Items.Select(i => new OrderItemRequestDto
            {
                SkuId = i.SkuId,
                UnitId = i.UnitId,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList()
        };

        var response = await CreateOrderAsync(req, userId, ct);
        var detail = await GetOrderDetailByIdAsync(response.Id, ct);
        return detail ?? throw new InvalidOperationException("Không thể tải lại chi tiết đơn hàng sau khi tạo.");
    }

    public async Task<OrderDetailDto> ConfirmOrderAsync(Guid id, Guid? userId, CancellationToken ct = default)
    {
        var response = await ConfirmOrderAsync(id, new ConfirmOrderRequestDto(), userId, ct);
        var detail = await GetOrderDetailByIdAsync(response.Id, ct);
        return detail ?? throw new InvalidOperationException("Không thể tải lại chi tiết đơn hàng sau khi xác nhận.");
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

    public async Task<OrderDetailDto> ProcessOrderAsync(Guid id, Guid? userId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var order = await _context.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id && o.TenantId == tenantId, ct);

        if (order == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy Đơn hàng với ID '{id}'.");
        }

        if (order.Status == OrderStatus.Cancelled || order.Status == OrderStatus.Completed)
        {
            throw new OismException("INVALID_STATE_TRANSITION", $"Không thể xử lý đóng gói đối với đơn hàng ở trạng thái '{order.Status}'.", 409);
        }

        if (order.Status == OrderStatus.Confirmed)
        {
            var existing = await GetOrderDetailByIdAsync(id, ct);
            return existing ?? throw new InvalidOperationException("Không thể tải chi tiết đơn hàng.");
        }

        if (!order.CanConfirm)
        {
            throw new OismException("INVALID_STATE_TRANSITION", $"Đơn hàng ở trạng thái '{order.Status}' không thể chuyển sang xử lý đóng gói. Yêu cầu trạng thái 'Reserved'.", 409);
        }

        try
        {
            var response = await ConfirmOrderAsync(id, new ConfirmOrderRequestDto(), userId, ct);
            var detail = await GetOrderDetailByIdAsync(response.Id, ct);
            return detail ?? throw new InvalidOperationException("Không thể tải lại chi tiết đơn hàng sau khi xử lý.");
        }
        catch (OismException ex) when (ex.ErrorCode == "INVALID_STATE_TRANSITION")
        {
            var reloaded = await _context.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id && o.TenantId == tenantId, ct);
            if (reloaded?.Status == OrderStatus.Confirmed)
            {
                var detail = await GetOrderDetailByIdAsync(id, ct);
                if (detail != null)
                {
                    return detail;
                }
            }
            throw;
        }
    }

    public async Task<OrderDetailDto> CompleteOrderAsync(Guid id, Guid? userId, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == id && o.TenantId == tenantId, ct);

        if (order == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy Đơn hàng với ID '{id}'.");
        }

        var response = await CompleteOrderAsync(id, new CompleteOrderRequestDto(), userId, ct);
        var detail = await GetOrderDetailByIdAsync(response.Id, ct);
        return detail ?? throw new InvalidOperationException("Không thể tải lại chi tiết đơn hàng sau khi hoàn tất.");
    }

    public async Task<OrderDetailDto> CancelOrderAsync(Guid id, Guid? userId, CancellationToken ct = default)
    {
        var response = await CancelOrderAsync(id, new CancelOrderRequestDto { Reason = "Huỷ đơn theo yêu cầu" }, userId, ct);
        var detail = await GetOrderDetailByIdAsync(response.Id, ct);
        return detail ?? throw new InvalidOperationException("Không thể tải lại chi tiết đơn hàng sau khi hủy.");
    }
}
