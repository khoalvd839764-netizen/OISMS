using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Catalog.Units.Services;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Pricing.Dtos;
using OISM.Application.Features.Pricing.Services;
using OISM.Application.Features.Realtime;
using OISM.Application.Features.Realtime.Dtos;
using OISM.Domain.Common;
using OISM.Domain.Entities;
using static OISM.Application.Features.Orders.Services.OrderLinePlanning;
using static OISM.Domain.Common.OrderLimits;

namespace OISM.Application.Features.Orders.Services;

internal sealed class OrderIntakeService(
    IAppDbContext context,
    ICurrentTenantService currentTenantService,
    ISkuPriceResolver skuPriceResolver,
    OrderQueryService query,
    ILogger<OrderService>? logger,
    IRealtimeNotifier? realtimeNotifier)
{
    private readonly IAppDbContext _context = context;
    private readonly ICurrentTenantService _currentTenantService = currentTenantService;
    private readonly ISkuPriceResolver _skuPriceResolver = skuPriceResolver;
    private readonly ILogger<OrderService> _logger = logger ?? NullLogger<OrderService>.Instance;
    private readonly IRealtimeNotifier? _realtimeNotifier = realtimeNotifier;

    public async Task<OrderResponseDto> CreateOrderAsync(CreateOrderRequestDto dto, Guid? createdByUserId, CancellationToken ct = default)
    {
        if (dto == null)
        {
            throw new ArgumentNullException(nameof(dto));
        }

        var tenantId = _currentTenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Không xác định được TenantId hợp lệ từ ngữ cảnh xác thực.");
        }

        var channel = string.IsNullOrWhiteSpace(dto.Channel) ? "Admin" : dto.Channel.Trim();
        if (!string.Equals(channel, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            throw new OismException("VALIDATION_ERROR", $"Endpoint chỉ hỗ trợ tạo đơn hàng kênh 'Admin'. Kênh '{dto.Channel}' không hợp lệ.", 400);
        }

        var coreReq = new InternalOrderCoreRequest(
            TenantId: tenantId.Value,
            BranchId: dto.BranchId,
            Channel: "Admin",
            ExternalShopId: null,
            ExternalOrderId: null,
            WebhookPayloadSignature: null,
            RequestKey: dto.RequestKey,
            CustomerName: dto.CustomerName,
            CustomerPhone: dto.CustomerPhone,
            ShippingAddress: dto.ShippingAddress,
            ExpectedTotalAmount: null,
            CreatedByUserId: createdByUserId,
            IsTrustedMarketplace: false,
            Items: dto.Items?.Select(i => new InternalOrderCoreItem(i.SkuId, i.SkuCode, i.Quantity, i.UnitPrice, i.UnitId)).ToList() ?? new(),
            PriceListCode: !string.IsNullOrWhiteSpace(dto.PriceListCode) ? dto.PriceListCode : "retail");

        return await CreateReservedOrderCoreAsync(coreReq, ct);
    }

    internal sealed record InternalOrderCoreRequest(
        Guid TenantId,
        Guid BranchId,
        string Channel,
        string? ExternalShopId,
        string? ExternalOrderId,
        string? WebhookPayloadSignature,
        Guid RequestKey,
        string? CustomerName,
        string? CustomerPhone,
        string? ShippingAddress,
        decimal? ExpectedTotalAmount,
        Guid? CreatedByUserId,
        bool IsTrustedMarketplace,
        List<InternalOrderCoreItem> Items,
        string PriceListCode = "retail");

    internal sealed record InternalOrderCoreItem(
        Guid? SkuId,
        string? SkuCode,
        decimal Quantity,
        decimal UnitPrice,
        Guid? UnitId = null);

    private static void ValidateAdminOrderReplayMatch(Order existingOrder, InternalOrderCoreRequest req, IReadOnlyDictionary<Guid, Guid?> baseUnits)
    {
        // So sánh Kênh bán
        if (!string.Equals(existingOrder.Channel, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            throw new IdempotencyPayloadMismatchException("RequestKey đã được sử dụng cho một đơn hàng không thuộc kênh Admin.");
        }

        // So sánh Chi nhánh
        if (existingOrder.BranchId != req.BranchId)
        {
            throw new IdempotencyPayloadMismatchException("RequestKey đã được sử dụng với Chi nhánh khác.");
        }

        // So sánh thông tin khách hàng & địa chỉ giao nhận
        var normDtoCustName = string.IsNullOrWhiteSpace(req.CustomerName) ? null : req.CustomerName.Trim();
        var normDtoCustPhone = string.IsNullOrWhiteSpace(req.CustomerPhone) ? null : req.CustomerPhone.Trim();
        var normDtoAddress = string.IsNullOrWhiteSpace(req.ShippingAddress) ? null : req.ShippingAddress.Trim();

        var normOrderCustName = string.IsNullOrWhiteSpace(existingOrder.CustomerName) ? null : existingOrder.CustomerName.Trim();
        var normOrderCustPhone = string.IsNullOrWhiteSpace(existingOrder.CustomerPhone) ? null : existingOrder.CustomerPhone.Trim();
        var normOrderAddress = string.IsNullOrWhiteSpace(existingOrder.ShippingAddress) ? null : existingOrder.ShippingAddress.Trim();

        if (!string.Equals(normOrderCustName, normDtoCustName, StringComparison.Ordinal))
        {
            throw new IdempotencyPayloadMismatchException("RequestKey đã được sử dụng với thông tin Tên khách hàng khác.");
        }
        if (!string.Equals(normOrderCustPhone, normDtoCustPhone, StringComparison.Ordinal))
        {
            throw new IdempotencyPayloadMismatchException("RequestKey đã được sử dụng với thông tin Số điện thoại khách hàng khác.");
        }
        if (!string.Equals(normOrderAddress, normDtoAddress, StringComparison.Ordinal))
        {
            throw new IdempotencyPayloadMismatchException("RequestKey đã được sử dụng với thông tin Địa chỉ giao hàng khác.");
        }

        // So sánh danh mục SKU
        var existingItemSummary = existingOrder.Items
            .GroupBy(i => (i.SkuId, UnitId: NormalizeUnit(i.UnitId, i.SkuId, baseUnits)))
            .ToDictionary(g => g.Key, g => new
            {
                Quantity = g.Sum(x => x.Quantity),
                UnitPrice = Math.Round(g.First().UnitPrice, 2)
            });

        var requestItemSummary = req.Items
            .GroupBy(i => (SkuId: i.SkuId!.Value, UnitId: NormalizeUnit(i.UnitId, i.SkuId!.Value, baseUnits)))
            .ToDictionary(g => g.Key, g => new
            {
                Quantity = g.Sum(x => x.Quantity),
                UnitPrice = Math.Round(g.First().UnitPrice, 2)
            });

        if (existingItemSummary.Count != requestItemSummary.Count)
        {
            throw new IdempotencyPayloadMismatchException("RequestKey đã được sử dụng với danh mục SKU khác (số loại mặt hàng không khớp).");
        }

        foreach (var kv in existingItemSummary)
        {
            if (!requestItemSummary.TryGetValue(kv.Key, out var reqData))
            {
                throw new IdempotencyPayloadMismatchException($"RequestKey đã được sử dụng với danh mục SKU khác (thiếu SKU '{kv.Key.SkuId}').");
            }

            if (reqData.Quantity != kv.Value.Quantity)
            {
                throw new IdempotencyPayloadMismatchException($"RequestKey đã được sử dụng với số lượng mua khác cho SKU '{kv.Key.SkuId}'.");
            }

            if (reqData.UnitPrice != kv.Value.UnitPrice)
            {
                throw new IdempotencyPayloadMismatchException($"RequestKey đã được sử dụng với đơn giá khác cho SKU '{kv.Key.SkuId}'.");
            }
        }

        // So sánh Bảng giá
        var priceListItems = existingOrder.Items
            .Where(i => i.PriceSource == "PriceList" && i.SkuUnitPrice != null)
            .ToList();
        if (priceListItems.Count > 0)
        {
            var existingPriceListCode = priceListItems.First().SkuUnitPrice?.PriceList?.Code;
            var reqPriceListCode = !string.IsNullOrWhiteSpace(req.PriceListCode) ? req.PriceListCode : "retail";
            if (existingPriceListCode != null && !string.Equals(existingPriceListCode, reqPriceListCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new IdempotencyPayloadMismatchException("RequestKey đã được sử dụng với bảng giá khác.");
            }
        }
    }

    internal async Task<OrderResponseDto> CreateReservedOrderCoreAsync(InternalOrderCoreRequest req, CancellationToken ct)
    {
        var tenantId = req.TenantId;

        // 1. Validation
        if (req.BranchId == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "Chi nhánh không được để trống (Guid.Empty).", 400);
        }

        if (req.RequestKey == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "RequestKey (Idempotency Key) không được để trống (Guid.Empty).", 400);
        }

        if (req.Items == null || req.Items.Count == 0)
        {
            throw new OismException("VALIDATION_ERROR", "Đơn hàng phải có ít nhất 1 sản phẩm.", 400);
        }

        if (req.Items.Any(i => i == null))
        {
            throw new OismException("VALIDATION_ERROR", "Dòng sản phẩm trong đơn hàng không được mang giá trị null.", 400);
        }

        if (!req.IsTrustedMarketplace && req.Items.Any(i => !i.SkuId.HasValue || i.SkuId.Value == Guid.Empty))
        {
            throw new OismException("VALIDATION_ERROR", "Mã biến thể SKU trong đơn hàng không được để trống.", 400);
        }

        if (req.IsTrustedMarketplace && req.Items.Any(i => string.IsNullOrWhiteSpace(i.SkuCode)))
        {
            throw new OismException("VALIDATION_ERROR", "Mã SKU mặt hàng trong đơn không được để trống.", 400);
        }

        foreach (var item in req.Items)
        {
            if (item.Quantity <= 0)
            {
                throw new OismException("VALIDATION_ERROR", "Số lượng mặt hàng mua phải lớn hơn 0.", 400);
            }

            if (item.Quantity > MaxQuantityAmount)
            {
                throw new OismException("VALIDATION_ERROR", $"Số lượng mặt hàng SKU '{item.SkuCode}' vượt quá giới hạn tối đa cho phép của hệ thống.", 400);
            }

            if (decimal.Round(item.Quantity, 3) != item.Quantity)
            {
                throw new OismException("VALIDATION_ERROR", $"Số lượng mặt hàng SKU '{item.SkuCode}' không được vượt quá 3 chữ số thập phân.", 400);
            }

            if (item.UnitPrice < 0)
            {
                throw new OismException("VALIDATION_ERROR", $"Đơn giá của SKU '{item.SkuCode}' không được âm.", 400);
            }

            if (item.UnitPrice > MaxMoneyAmount)
            {
                throw new OismException("VALIDATION_ERROR", $"Đơn giá của SKU '{item.SkuCode}' vượt quá giới hạn tối đa cho phép của hệ thống.", 400);
            }

            if (decimal.Round(item.UnitPrice, 2) != item.UnitPrice)
            {
                throw new OismException("VALIDATION_ERROR", $"Đơn giá của SKU '{item.SkuCode}' không được vượt quá 2 chữ số thập phân.", 400);
            }
        }

        // Kiểm tra tính nhất quán đơn giá nếu có dòng trùng SKU
        if (!req.IsTrustedMarketplace)
        {
            foreach (var g in req.Items.GroupBy(i => (i.SkuId, i.UnitId)))
            {
                if (g.Select(x => Math.Round(x.UnitPrice, 2)).Distinct().Count() > 1)
                {
                    throw new OismException("VALIDATION_ERROR", "Các dòng cùng SKU trong đơn hàng phải có cùng đơn giá.", 400);
                }
            }
        }
        else
        {
            foreach (var g in req.Items.GroupBy(i => i.SkuCode!.Trim().ToUpperInvariant()))
            {
                if (g.Select(x => Math.Round(x.UnitPrice, 2)).Distinct().Count() > 1)
                {
                    throw new OismException("VALIDATION_ERROR", "Các dòng cùng SKU trong đơn hàng phải có cùng đơn giá.", 400);
                }
            }
        }

        // 2. Fast Replay Check TRƯỚC khi kiểm tra trạng thái Branch / SKU Active / Catalog RetailPrice
        if (!req.IsTrustedMarketplace)
        {
            var existingOrderByKey = await _context.Orders
                .Include(o => o.Items)
                    .ThenInclude(i => i.SkuUnitPrice!)
                        .ThenInclude(p => p.PriceList)
                .Include(o => o.Payment)
                .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.RequestKey == req.RequestKey, ct);

            if (existingOrderByKey != null)
            {
                ValidateAdminOrderReplayMatch(existingOrderByKey, req, await LoadBaseUnitsAsync(_context, existingOrderByKey.Items.Select(i => i.SkuId).Concat(req.Items.Select(i => i.SkuId!.Value)), ct));
                _logger.LogInformation("Idempotent fast replay cho tạo đơn hàng trực tuyến với RequestKey {RequestKey}. Trả kết quả đơn {OrderNumber}.", req.RequestKey, existingOrderByKey.Number);
                var replayDto = await query.MapToOrderResponseDtoAsync(existingOrderByKey, ct);
                replayDto.IsReplay = true;
                return replayDto;
            }
        }
        else
        {
            var existingMarketplaceOrder = await _context.Orders
                .Include(o => o.Items)
                .Include(o => o.Payment)
                .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Channel == req.Channel && o.ExternalShopId == req.ExternalShopId && o.ExternalOrderId == req.ExternalOrderId, ct);

            if (existingMarketplaceOrder != null)
            {
                if (string.IsNullOrWhiteSpace(existingMarketplaceOrder.WebhookPayloadSignature))
                {
                    throw new OismException("WEBHOOK_PAYLOAD_MISMATCH", "Đơn hàng sàn lịch sử không có chữ ký để đối soát payload.", 409);
                }

                if (!string.Equals(existingMarketplaceOrder.WebhookPayloadSignature, req.WebhookPayloadSignature, StringComparison.Ordinal))
                {
                    throw new OismException("WEBHOOK_PAYLOAD_MISMATCH", "Payload không khớp với đơn hàng TMĐT đã tiếp nhận trước đó cùng mã định danh sàn.", 409);
                }

                var replayDto = await query.MapToOrderResponseDtoAsync(existingMarketplaceOrder, ct);
                replayDto.IsReplay = true;
                return replayDto;
            }
        }

        // 3. Xác thực Chi nhánh fulfillment (kiểm tra đối với đơn hàng MỚI)
        var branch = await _context.Branches
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == req.BranchId, ct);
        if (branch == null || !branch.IsActive)
        {
            throw new OismException("BRANCH_INACTIVE_OR_INVALID", $"Chi nhánh '{req.BranchId}' không tồn tại hoặc đã ngừng hoạt động.", 404);
        }

        // 4. Tra cứu SKU trong Catalog
        Dictionary<Guid, ProductVariant> skus;
        Dictionary<string, ProductVariant> skusByCode = new(StringComparer.OrdinalIgnoreCase);

        if (!req.IsTrustedMarketplace)
        {
            var requestedSkuIds = req.Items.Select(i => i.SkuId!.Value).Distinct().ToList();
            skus = await _context.ProductVariants
                .Include(v => v.Product)
                .Include(v => v.SkuUnits)
                    .ThenInclude(u => u.Unit)
                .Where(v => v.TenantId == tenantId && requestedSkuIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, ct);

            if (skus.Count != requestedSkuIds.Count)
            {
                var missingSkuId = requestedSkuIds.First(id => !skus.ContainsKey(id));
                throw new OismException("SKU_NOT_FOUND", $"Mặt hàng biến thể SKU '{missingSkuId}' không tồn tại trong hệ thống.", 404);
            }
        }
        else
        {
            var requestedSkuCodes = req.Items.Select(i => i.SkuCode!.Trim().ToUpperInvariant()).Distinct().ToList();
            var skuList = await _context.ProductVariants
                .Include(v => v.Product)
                .Include(v => v.SkuUnits)
                    .ThenInclude(u => u.Unit)
                .Where(v => v.TenantId == tenantId && requestedSkuCodes.Contains(v.SKU.ToUpper()))
                .ToListAsync(ct);

            skusByCode = skuList.ToDictionary(v => v.SKU.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase);

            if (skusByCode.Count != requestedSkuCodes.Count)
            {
                var missingCode = requestedSkuCodes.First(c => !skusByCode.ContainsKey(c));
                throw new OismException("SKU_NOT_FOUND", $"Mặt hàng biến thể SKU '{missingCode}' không tồn tại trong hệ thống.", 422);
            }

            skus = skusByCode.Values.ToDictionary(v => v.Id);
        }

        foreach (var sku in skus.Values)
        {
            if (!sku.IsActive || sku.Product == null || !sku.Product.IsActive)
            {
                throw new OismException("SKU_INACTIVE", $"Mặt hàng SKU '{sku.SKU}' hoặc sản phẩm liên quan đang ngừng hoạt động.", 422);
            }
        }

        var priceListCode = !string.IsNullOrWhiteSpace(req.PriceListCode) ? req.PriceListCode.Trim().ToLowerInvariant() : "retail";
        IReadOnlyDictionary<SkuUnitPriceKey, ResolvedPrice>? resolvedPrices = null;
        if (!req.IsTrustedMarketplace)
        {
            if (priceListCode != "retail" && priceListCode != "wholesale")
            {
                throw new PriceListNotFoundException(priceListCode);
            }

            var priceKeys = new HashSet<SkuUnitPriceKey>();
            foreach (var item in req.Items)
            {
                var skuId = item.SkuId!.Value;
                var sku = skus[skuId];
                var resolved = SkuUnitResolver.ResolveForSale(sku, sku.SkuUnits, item.UnitId);
                if (!resolved.UnitId.HasValue)
                {
                    throw new OismException("UNIT_PRICE_NOT_CONFIGURED", $"Đơn vị của mặt hàng '{sku.SKU}' chưa được cấu hình đơn vị gốc.", 422);
                }
                priceKeys.Add(new SkuUnitPriceKey(skuId, resolved.UnitId.Value));
            }

            resolvedPrices = await _skuPriceResolver.ResolveAsync(priceListCode, priceKeys, DateTimeOffset.UtcNow, ct);
        }

        // Phân giải đơn vị, gom dòng theo (SKU, đơn vị); Admin kiểm giá theo niêm yết của đúng đơn vị, sàn TMĐT giữ giá từ payload
        var orderLines = BuildOrderLines(
            req.Items.Select(i => new OrderLineInput(
                !req.IsTrustedMarketplace ? i.SkuId!.Value : skusByCode[i.SkuCode!.Trim().ToUpperInvariant()].Id,
                i.UnitId,
                i.Quantity,
                i.UnitPrice)),
            skus,
            trustRequestedPrice: req.IsTrustedMarketplace,
            resolvedPrices: resolvedPrices);
        var groupedItems = SumBaseQuantityBySku(orderLines);

        // Tính tổng tiền đơn hàng phía Server
        decimal totalAmount = 0m;
        foreach (var line in orderLines)
        {
            var unitPrice = line.UnitPrice;
            var qty = line.Quantity;
            try
            {
                checked
                {
                    var lineTotal = qty * unitPrice;
                    if (lineTotal > MaxMoneyAmount)
                    {
                        throw new OismException("VALIDATION_ERROR", "Tổng tiền của dòng hàng vượt quá giới hạn tối đa cho phép của hệ thống.", 400);
                    }
                    totalAmount += lineTotal;
                    if (totalAmount > MaxMoneyAmount)
                    {
                        throw new OismException("VALIDATION_ERROR", "Tổng tiền đơn hàng vượt quá giới hạn tối đa cho phép của hệ thống.", 400);
                    }
                }
            }
            catch (OverflowException)
            {
                throw new OismException("VALIDATION_ERROR", "Tổng tiền đơn hàng vượt quá giới hạn tối đa cho phép của hệ thống.", 400);
            }
        }
        totalAmount = Math.Round(totalAmount, 2, MidpointRounding.AwayFromZero);
        if (totalAmount > MaxMoneyAmount)
        {
            throw new OismException("VALIDATION_ERROR", "Tổng tiền đơn hàng vượt quá giới hạn tối đa cho phép của hệ thống.", 400);
        }

        if (req.IsTrustedMarketplace && req.ExpectedTotalAmount.HasValue)
        {
            if (Math.Round(totalAmount, 2) != Math.Round(req.ExpectedTotalAmount.Value, 2))
            {
                throw new OismException("TOTAL_AMOUNT_MISMATCH", $"Tổng tiền đơn hàng không khớp (Server tính: {totalAmount:N2}, Payload gửi: {req.ExpectedTotalAmount.Value:N2}).", 400);
            }
        }

        // 3. Sắp xếp danh sách SkuId tăng dần để triệt tiêu deadlocks khi khóa tuần tự
        var sortedSkuIds = skus.Values.Select(v => v.Id).Distinct().OrderBy(id => id).ToList();

        // 4. Bao bọc trong ExecutionStrategy
        List<Func<Task>> pendingNotifications = new();
        var executionResult = await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            pendingNotifications.Clear();
            _context.UnitOfWork.ResetTracking();

            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);
            try
            {
                // 5. Lấy PostgreSQL Advisory Locks theo thứ tự xác định: SKU Locks -> RequestKey / External Order Lock
                await _context.Locks.LockSkuStockAsync(tenantId, req.BranchId, sortedSkuIds, ct);

                if (!req.IsTrustedMarketplace)
                {
                    await _context.Locks.LockRequestKeyAsync(tenantId, req.RequestKey, ct);
                }
                else
                {
                    await _context.Locks.LockExternalOrderAsync(tenantId, req.Channel, req.ExternalShopId ?? string.Empty, req.ExternalOrderId ?? string.Empty, ct);
                }

                // 6. Kiểm tra Idempotency dưới khóa
                if (!req.IsTrustedMarketplace)
                {
                    var existingOrder = await _context.Orders
                        .Include(o => o.Items)
                            .ThenInclude(i => i.SkuUnitPrice!)
                                .ThenInclude(p => p.PriceList)
                        .Include(o => o.Payment)
                        .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.RequestKey == req.RequestKey, ct);

                    if (existingOrder != null)
                    {
                        ValidateAdminOrderReplayMatch(existingOrder, req, await LoadBaseUnitsAsync(_context, existingOrder.Items.Select(i => i.SkuId).Concat(req.Items.Select(i => i.SkuId!.Value)), ct));
                        _logger.LogInformation("Idempotent replay dưới khóa cho tạo đơn hàng trực tuyến với RequestKey {RequestKey}. Trả kết quả đơn {OrderNumber}.", req.RequestKey, existingOrder.Number);
                        await tx.CommitAsync(ct);
                        var replayDto = await query.MapToOrderResponseDtoAsync(existingOrder, ct);
                        replayDto.IsReplay = true;
                        return replayDto;
                    }
                }
                else
                {
                    var existingOrder = await _context.Orders
                        .Include(o => o.Items)
                        .Include(o => o.Payment)
                        .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Channel == req.Channel && o.ExternalShopId == req.ExternalShopId && o.ExternalOrderId == req.ExternalOrderId, ct);

                    if (existingOrder != null)
                    {
                        if (string.IsNullOrWhiteSpace(existingOrder.WebhookPayloadSignature))
                        {
                            throw new OismException("WEBHOOK_PAYLOAD_MISMATCH", "Đơn hàng sàn lịch sử không có chữ ký để đối soát payload.", 409);
                        }

                        if (!string.Equals(existingOrder.WebhookPayloadSignature, req.WebhookPayloadSignature, StringComparison.Ordinal))
                        {
                            throw new OismException("WEBHOOK_PAYLOAD_MISMATCH", "Payload không khớp với đơn hàng TMĐT đã tiếp nhận trước đó cùng mã định danh sàn.", 409);
                        }

                        await tx.CommitAsync(ct);
                        var replayDto = await query.MapToOrderResponseDtoAsync(existingOrder, ct);
                        replayDto.IsReplay = true;
                        return replayDto;
                    }
                }

                // 6b. Đọc lại giá dưới khóa SKU: giá mới có thể đã có hiệu lực trong lúc đơn đợi khóa
                if (!req.IsTrustedMarketplace)
                {
                    var lockedPrices = await _skuPriceResolver.ResolveAsync(
                        priceListCode,
                        orderLines.Select(l => new SkuUnitPriceKey(l.SkuId, l.Unit.UnitId!.Value)).ToHashSet(),
                        DateTimeOffset.UtcNow,
                        ct);

                    var changedPrices = new List<PriceMismatchItem>();
                    foreach (var line in orderLines)
                    {
                        if (!lockedPrices.TryGetValue(new SkuUnitPriceKey(line.SkuId, line.Unit.UnitId!.Value), out var lockedPrice))
                        {
                            throw new OismException("UNIT_PRICE_NOT_CONFIGURED", $"Đơn vị '{line.Unit.UnitName}' của mặt hàng '{skus[line.SkuId].SKU}' chưa được cấu hình giá bán.", 422);
                        }

                        if (lockedPrice.Price != line.UnitPrice)
                        {
                            changedPrices.Add(new PriceMismatchItem(line.SkuId, skus[line.SkuId].SKU, line.Unit.UnitId, lockedPrice.Price));
                            continue;
                        }

                        line.SkuUnitPriceId = lockedPrice.SkuUnitPriceId;
                    }

                    if (changedPrices.Count > 0)
                    {
                        throw new PriceMismatchException("Đơn giá của mặt hàng trong giỏ hàng không khớp với giá niêm yết của hệ thống.", changedPrices);
                    }
                }

                // 7. Xác thực Chi nhánh
                var branch = await _context.Branches.FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == req.BranchId, ct);
                if (branch == null || !branch.IsActive)
                {
                    throw new OismException("BRANCH_INACTIVE_OR_INVALID", $"Chi nhánh '{req.BranchId}' không tồn tại hoặc đã ngừng hoạt động.", 404);
                }

                // 8. Kiểm tra tồn khả dụng tại Chi nhánh (InventoryBalances)
                var branchBalances = await _context.InventoryBalances
                    .Where(b => b.TenantId == tenantId && b.BranchId == req.BranchId && sortedSkuIds.Contains(b.SkuId))
                    .ToDictionaryAsync(b => b.SkuId, ct);

                foreach (var kv in groupedItems)
                {
                    var skuId = kv.Key;
                    var requestedQty = kv.Value;

                    if (!branchBalances.TryGetValue(skuId, out var bBal))
                    {
                        throw new InsufficientStockException(skus[skuId].SKU, 0, requestedQty);
                    }

                    var available = Math.Max(0, bBal.OnHand - bBal.Reserved);
                    if (available < requestedQty)
                    {
                        throw new InsufficientStockException(skus[skuId].SKU, available, requestedQty);
                    }
                }

                // 9. Phân bổ kho theo chính sách: Tự động phân bổ tồn khả dụng qua các kho hoạt động thuộc chi nhánh
                var activeWarehouses = await _context.Warehouses
                    .Where(w => w.TenantId == tenantId && w.BranchId == req.BranchId && w.IsActive)
                    .OrderBy(w => w.CreatedAt)
                    .ToListAsync(ct);

                if (activeWarehouses.Count == 0)
                {
                    throw new OismException("NO_ACTIVE_WAREHOUSE", $"Chi nhánh '{branch.Name}' không có kho hàng nào đang hoạt động để giữ chỗ hàng.", 409);
                }

                var warehouseIds = activeWarehouses.Select(w => w.Id).ToList();
                var whBalances = await _context.WarehouseInventoryBalances
                    .Where(wb => wb.TenantId == tenantId && wb.BranchId == req.BranchId && warehouseIds.Contains(wb.WarehouseId) && sortedSkuIds.Contains(wb.SkuId))
                    .ToListAsync(ct);

                var allocationPlan = new Dictionary<Guid, List<(Warehouse Warehouse, WarehouseInventoryBalance Balance, decimal Quantity)>>();

                foreach (var kv in groupedItems)
                {
                    var skuId = kv.Key;
                    var requestedQty = kv.Value;

                    var skuWhBalances = whBalances.Where(wb => wb.SkuId == skuId).ToList();
                    var skuWhAvailable = skuWhBalances.Sum(wb => Math.Max(0, wb.OnHand - wb.Reserved));

                    if (skuWhAvailable < requestedQty)
                    {
                        throw new InsufficientStockException(skus[skuId].SKU, skuWhAvailable, requestedQty);
                    }

                    var skuAllocations = new List<(Warehouse, WarehouseInventoryBalance, decimal)>();

                    // Ưu tiên kho có sẵn đủ số lượng yêu cầu
                    var singleWh = skuWhBalances.FirstOrDefault(wb => Math.Max(0, wb.OnHand - wb.Reserved) >= requestedQty);
                    if (singleWh != null)
                    {
                        var wh = activeWarehouses.First(w => w.Id == singleWh.WarehouseId);
                        skuAllocations.Add((wh, singleWh, requestedQty));
                    }
                    else
                    {
                        // Phân bổ lần lượt qua các kho có tồn khả dụng
                        decimal remaining = requestedQty;
                        foreach (var wb in skuWhBalances.Where(wb => Math.Max(0, wb.OnHand - wb.Reserved) > 0).OrderByDescending(wb => wb.OnHand - wb.Reserved))
                        {
                            var avail = Math.Max(0, wb.OnHand - wb.Reserved);
                            var take = Math.Min(remaining, avail);
                            var wh = activeWarehouses.First(w => w.Id == wb.WarehouseId);
                            skuAllocations.Add((wh, wb, take));
                            remaining -= take;
                            if (remaining <= 0) break;
                        }

                        if (remaining > 0)
                        {
                            throw new InsufficientStockException(skus[skuId].SKU, skuWhAvailable, requestedQty);
                        }
                    }

                    allocationPlan[skuId] = skuAllocations;
                }

                // 10. Tạo Đơn hàng ở trạng thái Reserved
                var now = DateTimeOffset.UtcNow;
                var orderId = Guid.NewGuid();
                var prefix = req.Channel switch
                {
                    "Admin" => "ORD",
                    "Shopee" => "SHP",
                    "TikTok" => "TTK",
                    "Lazada" => "LZD",
                    _ => "ORD"
                };
                var orderNumber = $"{prefix}-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..20];

                var order = new Order
                {
                    Id = orderId,
                    TenantId = tenantId,
                    Number = orderNumber,
                    BranchId = req.BranchId,
                    Channel = req.Channel,
                    ExternalShopId = req.Channel == "Admin" || req.Channel == "POS" ? null : req.ExternalShopId,
                    ExternalOrderId = req.Channel == "Admin" || req.Channel == "POS" ? null : req.ExternalOrderId,
                    WebhookPayloadSignature = req.WebhookPayloadSignature,
                    CustomerName = req.CustomerName,
                    CustomerPhone = req.CustomerPhone,
                    ShippingAddress = req.ShippingAddress,
                    TotalAmount = totalAmount,
                    RequestKey = req.RequestKey,
                    CreatedBy = req.CreatedByUserId,
                    CreatedAt = now
                };
                order.Reserve();
                _context.Orders.Add(order);

                var createdOrderItems = new List<OrderItem>();
                var createdReservations = new List<Reservation>();

                foreach (var line in orderLines)
                {
                    var skuId = line.SkuId;
                    var sku = skus[skuId];
                    var bBal = branchBalances[skuId];
                    var unitPrice = line.UnitPrice;

                    var orderItem = new OrderItem
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        OrderId = orderId,
                        SkuId = skuId,
                        SkuCodeSnapshot = sku.SKU,
                        SkuNameSnapshot = string.IsNullOrWhiteSpace(sku.VariantName) ? sku.Product?.Name ?? sku.SKU : $"{sku.Product?.Name ?? sku.SKU} - {sku.VariantName}",
                        UnitId = line.Unit.UnitId,
                        UnitNameSnapshot = line.Unit.UnitName,
                        FactorSnapshot = line.Unit.Factor,
                        Quantity = line.Quantity,
                        UnitPrice = Math.Round(unitPrice, 2, MidpointRounding.AwayFromZero),
                        CostPrice = null, // CostPrice chỉ được snapshot khi Confirmed/Completed
                        SkuUnitPriceId = line.SkuUnitPriceId,
                        PriceSource = line.PriceSource,
                        CreatedAt = now
                    };
                    _context.OrderItems.Add(orderItem);
                    createdOrderItems.Add(orderItem);

                    var allocs = TakeAllocations(allocationPlan[skuId], line.BaseQuantity);
                    foreach (var alloc in allocs)
                    {
                        // Tăng Reserved ở cấp kho và cấp chi nhánh; OnHand không đổi
                        alloc.Balance.Reserved += alloc.Quantity;
                        alloc.Balance.UpdatedAt = now;

                        bBal.Reserved += alloc.Quantity;
                        bBal.UpdatedAt = now;

                        var reservation = new Reservation
                        {
                            Id = Guid.NewGuid(),
                            TenantId = tenantId,
                            OrderId = orderId,
                            OrderItemId = orderItem.Id,
                            BranchId = req.BranchId,
                            WarehouseId = alloc.Warehouse.Id,
                            SkuId = skuId,
                            Quantity = alloc.Quantity,
                            Status = ReservationStatus.Active,
                            ExpiresAt = now.AddMinutes(15),
                            CreatedAt = now
                        };
                        _context.Reservations.Add(reservation);
                        createdReservations.Add(reservation);
                    }
                }

                // Tuyệt đối không ghi Sổ cái (Ledger) và không tạo Payment tại bước tạo đơn Reserved
                await _context.SaveChangesAsync(ct);

                await _context.Locks.SetConstraintsImmediateAsync(ct);

                await tx.CommitAsync(ct);

                order.Items = createdOrderItems;
                order.Reservations = createdReservations;

                _logger.LogInformation("Tạo đơn hàng trực tuyến {OrderNumber} thành công ở trạng thái Reserved.", order.Number);

                // 11. Phát thông báo thời gian thực sau khi Transaction commit thành công
                // 11. Thu tháº­p thÃ´ng bÃ¡o thá»i gian thá»±c sau khi Transaction commit thÃ nh cÃ´ng
                if (_realtimeNotifier != null)
                {
                    var orderSummary = new OrderSummaryRealtimeDto
                    {
                        OrderId = order.Id,
                        OrderNumber = order.Number,
                        BranchId = order.BranchId,
                        BranchName = branch.Name,
                        Channel = order.Channel,
                        Status = order.Status,
                        TotalAmount = order.TotalAmount,
                        TotalItems = order.Items.Count,
                        CustomerName = order.CustomerName,
                        CreatedAt = order.CreatedAt
                    };

                    pendingNotifications.Add(() => _realtimeNotifier.NotifyNewOrderAsync(tenantId, orderSummary, ct));

                    foreach (var kv in groupedItems)
                    {
                        var skuId = kv.Key;
                        var bBal = branchBalances[skuId];
                        var sku = skus[skuId];
                        var prevAvail = bBal.OnHand - (bBal.Reserved - kv.Value);
                        var newAvail = bBal.OnHand - bBal.Reserved;

                        pendingNotifications.Add(() => _realtimeNotifier.NotifyStockChangedAsync(tenantId, req.BranchId, skuId, newAvail, ct));

                        if (prevAvail > bBal.ReorderPoint && newAvail <= bBal.ReorderPoint)
                        {
                            pendingNotifications.Add(() => _realtimeNotifier.NotifyLowStockAlertAsync(tenantId, req.BranchId, skuId, sku.SKU, newAvail, bBal.ReorderPoint, ct));
                        }
                    }
                }
                return await query.MapToOrderResponseDtoAsync(order, ct);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _logger.LogError(ex, "Tạo đơn hàng online thất bại cho RequestKey {RequestKey}. Đã rollback giao dịch.", req.RequestKey);
                throw;
            }
        });

        // 12. PhÃ¡t thÃ´ng bÃ¡o thá»i gian thá»±c sau khi Execution Strategy thÃ nh cÃ´ng toÃ n diá»‡n
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
                    _logger.LogError(ex, "Lá»—i khi phÃ¡t sá»± kiá»‡n Realtime sau commit táº¡o Ä‘Æ¡n thÃ nh cÃ´ng.");
                }
            }
        }

        return executionResult;
    }
}
