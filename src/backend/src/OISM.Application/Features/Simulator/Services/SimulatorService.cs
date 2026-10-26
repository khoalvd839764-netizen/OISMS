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

        var signature = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"{tenantId}:{normalizedChannel}:{payload.ExternalShopId?.Trim()}:{payload.ExternalOrderId.Trim()}:{payload.Items.Sum(i => i.Quantity * i.UnitPrice)}")
            )
        ).ToLowerInvariant();

        var marketplaceContext = new InternalMarketplaceOrderContext
        {
            TenantId = tenantId,
            BranchId = payload.BranchId,
            Channel = normalizedChannel,
            ExternalShopId = !string.IsNullOrWhiteSpace(payload.ExternalShopId) ? payload.ExternalShopId.Trim() : "DEFAULT_SHOP",
            ExternalOrderId = payload.ExternalOrderId.Trim(),
            WebhookPayloadSignature = signature,
            RequestKey = Guid.NewGuid(),
            CustomerName = payload.CustomerName?.Trim(),
            CustomerPhone = payload.CustomerPhone?.Trim(),
            ShippingAddress = payload.ShippingAddress?.Trim(),
            CreatedByUserId = userId,
            OrderTimestamp = DateTimeOffset.UtcNow,
            ExpectedTotalAmount = payload.Items.Sum(i => i.Quantity * i.UnitPrice),
            Items = payload.Items.Select(i => new InternalMarketplaceOrderItemContext
            {
                SkuCode = i.SkuCode.Trim(),
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList()
        };

        var orderResponse = await _orderService.CreateMarketplaceReservedOrderAsync(marketplaceContext, ct);
        var detail = await _orderService.GetOrderDetailByIdAsync(orderResponse.Id, ct);
        return detail!;
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
            try
            {
                var payload = new WebhookOrderPayloadDto
                {
                    ExternalOrderId = $"SIM-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}-{i}",
                    ExternalShopId = "SIM-SHOP-01",
                    BranchId = request.BranchId,
                    CustomerName = $"Sim Customer {i}",
                    CustomerPhone = "0900000000",
                    ShippingAddress = "Sim Address",
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

                var order = await IngestWebhookOrderAsync(normalizedChannel, payload, userId, ct);
                successCount++;
                orderNumbers.Add(order.Number);
            }
            catch (Exception ex)
            {
                failedCount++;
                errors.Add($"Req #{i}: {ex.Message}");
            }
        }

        stopwatch.Stop();

        result.SuccessCount = successCount;
        result.FailedConflictCount = failedCount;
        result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
        result.CreatedOrderNumbers = orderNumbers;
        result.ErrorMessages = errors;

        return result;
    }
}
