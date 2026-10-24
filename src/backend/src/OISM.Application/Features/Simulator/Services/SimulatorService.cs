using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Orders.Services;
using OISM.Application.Features.Simulator.Dtos;

namespace OISM.Application.Features.Simulator.Services;

public class SimulatorService : ISimulatorService
{
    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _currentTenantService;
    private readonly IOrderService _orderService;

    private static readonly HashSet<string> ValidChannels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shopee", "TikTok", "Lazada"
    };

    public SimulatorService(
        IAppDbContext context,
        ICurrentTenantService currentTenantService,
        IOrderService orderService)
    {
        _context = context;
        _currentTenantService = currentTenantService;
        _orderService = orderService;
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

    private static string CanonicalizeChannel(string rawChannel)
    {
        if (string.Equals(rawChannel, "shopee", StringComparison.OrdinalIgnoreCase)) return "Shopee";
        if (string.Equals(rawChannel, "tiktok", StringComparison.OrdinalIgnoreCase)) return "TikTok";
        if (string.Equals(rawChannel, "lazada", StringComparison.OrdinalIgnoreCase)) return "Lazada";
        return rawChannel;
    }

    public async Task<OrderDetailDto> IngestWebhookOrderAsync(
        string channel,
        WebhookOrderPayloadDto payload,
        Guid? userId,
        CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        if (string.IsNullOrWhiteSpace(channel) || !ValidChannels.Contains(channel))
        {
            throw new ArgumentException($"Kênh bán '{channel}' không hợp lệ. Hỗ trợ: Shopee, TikTok, Lazada.");
        }

        var normalizedChannel = CanonicalizeChannel(channel);

        if (payload.Items == null || payload.Items.Count == 0)
        {
            throw new ArgumentException("Đơn hàng webhook sàn phải chứa ít nhất một mặt hàng.");
        }

        // Kiểm tra xem đơn hàng sàn ExternalOrderId đã tồn tại chưa (Idempotency)
        var existingOrder = await _context.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.TenantId == tenantId &&
                                      o.Channel == normalizedChannel &&
                                      o.ExternalOrderId == payload.ExternalOrderId.Trim(), ct);

        if (existingOrder != null)
        {
            var detail = await _orderService.GetOrderByIdAsync(existingOrder.Id, ct);
            if (detail != null)
            {
                return detail;
            }
        }

        // Tìm kiếm các SKU trong gian hàng theo SkuCode hoặc Barcode
        var skuCodes = payload.Items.Select(i => i.SkuCode.Trim().ToUpperInvariant()).Distinct().ToList();

        var skus = await _context.Skus
            .Where(s => s.TenantId == tenantId &&
                        (skuCodes.Contains(s.SKU) || (s.Barcode != null && skuCodes.Contains(s.Barcode))))
            .ToListAsync(ct);

        var skuMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in skus)
        {
            skuMap[s.SKU] = s.Id;
            if (!string.IsNullOrWhiteSpace(s.Barcode))
            {
                skuMap[s.Barcode] = s.Id;
            }
        }

        var orderItemsReq = new List<CreateOrderItemRequestDto>();
        foreach (var item in payload.Items)
        {
            var code = item.SkuCode.Trim();
            if (!skuMap.TryGetValue(code, out var skuId))
            {
                throw new KeyNotFoundException($"Không tìm thấy SKU hoặc Barcode '{code}' trong gian hàng.");
            }

            orderItemsReq.Add(new CreateOrderItemRequestDto
            {
                SkuId = skuId,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice
            });
        }

        var createDto = new CreateOrderDto
        {
            Number = $"ORD-{normalizedChannel.ToUpperInvariant()}-{payload.ExternalOrderId.Trim()}",
            BranchId = payload.BranchId,
            Channel = normalizedChannel,
            ExternalShopId = payload.ExternalShopId?.Trim(),
            ExternalOrderId = payload.ExternalOrderId.Trim(),
            CustomerName = payload.CustomerName?.Trim(),
            CustomerPhone = payload.CustomerPhone?.Trim(),
            ShippingAddress = payload.ShippingAddress?.Trim(),
            InitialStatus = "Reserved", // Đơn hàng sàn tự động giữ chỗ theo quy trình FR-ORD-01
            RequestKey = Guid.NewGuid(),
            Items = orderItemsReq
        };

        return await _orderService.CreateOrderAsync(createDto, userId, ct);
    }

    public async Task<SimulationBatchResultDto> RunConcurrencySimulationAsync(
        SimulationBatchRequestDto request,
        Guid? userId,
        CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var normalizedChannel = CanonicalizeChannel(request.Channel);
        var stopwatch = Stopwatch.StartNew();

        var result = new SimulationBatchResultDto
        {
            Channel = normalizedChannel,
            SkuCode = request.SkuCode,
            TotalRequests = request.ConcurrencyCount
        };

        var orderNumbers = new List<string>();
        var errors = new List<string>();
        int successCount = 0;
        int failedCount = 0;

        for (int i = 1; i <= request.ConcurrencyCount; i++)
        {
            var extId = $"SIM-{DateTime.UtcNow:yyyyMMddHHmmss}-{i:D3}";
            var payload = new WebhookOrderPayloadDto
            {
                ExternalOrderId = extId,
                ExternalShopId = "SHOP-SIMULATOR",
                BranchId = request.BranchId,
                CustomerName = $"Khách hàng test #{i}",
                CustomerPhone = "0901234567",
                ShippingAddress = "123 Đường Test, Quận 1, TP.HCM",
                Items = new List<WebhookOrderItemPayloadDto>
                {
                    new()
                    {
                        SkuCode = request.SkuCode,
                        Quantity = request.QuantityPerOrder,
                        UnitPrice = request.UnitPrice
                    }
                }
            };

            try
            {
                var order = await IngestWebhookOrderAsync(normalizedChannel, payload, userId, ct);
                successCount++;
                orderNumbers.Add(order.Number);
            }
            catch (Exception ex)
            {
                failedCount++;
                errors.Add($"Đơn #{i} thất bại: {ex.Message}");
            }
        }

        stopwatch.Stop();

        result.SuccessCount = successCount;
        result.FailedConflictCount = failedCount;
        result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
        result.CreatedOrderNumbers = orderNumbers;
        result.ErrorMessages = errors.Take(10).ToList();

        return result;
    }
}
