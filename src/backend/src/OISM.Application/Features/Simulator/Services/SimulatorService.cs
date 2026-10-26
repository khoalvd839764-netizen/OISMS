using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Orders.Services;
using OISM.Application.Features.Simulator.Dtos;
using OISM.Application.Features.Webhooks.Dtos;
using OISM.Application.Features.Webhooks.Services;
using OISM.Domain.Entities;

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
            throw new OismException("VALIDATION_ERROR", $"Kênh bán '{channel}' không hợp lệ. Hỗ trợ: Shopee, TikTok, Lazada.", 400);
        }

        var normalizedChannel = CanonicalizeChannel(channel);

        if (payload == null)
        {
            throw new OismException("VALIDATION_ERROR", "Nội dung Webhook payload không được để trống.", 400);
        }

        if (string.IsNullOrWhiteSpace(payload.ExternalShopId))
        {
            throw new OismException("VALIDATION_ERROR", "Mã Shop trên sàn ('externalShopId') là bắt buộc.", 400);
        }

        if (string.IsNullOrWhiteSpace(payload.ExternalOrderId))
        {
            throw new OismException("VALIDATION_ERROR", "Mã Đơn hàng trên sàn ('externalOrderId') là bắt buộc.", 400);
        }

        if (payload.Items == null || payload.Items.Count == 0)
        {
            throw new OismException("VALIDATION_ERROR", "Đơn hàng webhook sàn phải chứa ít nhất một mặt hàng.", 400);
        }

        // Đối soát cấu hình ShopIntegration của gian hàng (FR-SIM-01, BE2-F06 - Tuyệt đối không bypass ShopIntegration)
        var shopIntegration = await _context.ShopIntegrations
            .FirstOrDefaultAsync(si => si.TenantId == tenantId &&
                                       si.Channel == normalizedChannel &&
                                       si.ExternalShopId == payload.ExternalShopId.Trim(), ct);

        if (shopIntegration == null)
        {
            throw new OismException("INTEGRATION_NOT_FOUND", $"Không tìm thấy cấu hình tích hợp gian hàng '{payload.ExternalShopId}' của kênh '{normalizedChannel}' cho cửa hàng của bạn.", 404);
        }

        if (!shopIntegration.IsActive)
        {
            throw new OismException("UNAUTHORIZED", "Kênh tích hợp gian hàng này đã bị vô hiệu hóa.", 401);
        }

        var targetBranchId = shopIntegration.BranchId;
        if (payload.BranchId != Guid.Empty && payload.BranchId != targetBranchId)
        {
            throw new OismException("BRANCH_MISMATCH", $"Chi nhánh '{payload.BranchId}' không khớp với chi nhánh được cấu hình cho gian hàng '{payload.ExternalShopId}'.", 400);
        }

        var branch = await _context.Branches
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == targetBranchId, ct);
        if (branch == null || !branch.IsActive)
        {
            throw new OismException("BRANCH_INACTIVE_OR_INVALID", $"Chi nhánh '{targetBranchId}' không tồn tại hoặc đã ngừng hoạt động.", 404);
        }

        // Tính toán chữ ký Canonical Payload SHA-256 theo thuật toán chuẩn của BE1 (bao gồm Channel, Shop, Order, Items, Sku, Qty, Price)
        var canonicalDto = new CanonicalWebhookPayloadDto
        {
            Channel = normalizedChannel,
            ExternalShopId = payload.ExternalShopId.Trim(),
            ExternalOrderId = payload.ExternalOrderId.Trim(),
            CustomerName = payload.CustomerName?.Trim(),
            CustomerPhone = payload.CustomerPhone?.Trim(),
            ShippingAddress = payload.ShippingAddress?.Trim(),
            OrderTimestamp = payload.OrderTimestamp ?? DateTimeOffset.UnixEpoch,
            TotalAmount = payload.Items.Sum(i => i.Quantity * i.UnitPrice),
            Items = payload.Items.Select(i => new CanonicalWebhookItemDto
            {
                SkuCode = i.SkuCode.Trim(),
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList()
        };

        var signature = WebhookCanonicalSignatureHelper.ComputeCanonicalSignature(canonicalDto);

        var marketplaceContext = new InternalMarketplaceOrderContext
        {
            TenantId = tenantId,
            BranchId = targetBranchId,
            Channel = normalizedChannel,
            ExternalShopId = payload.ExternalShopId.Trim(),
            ExternalOrderId = payload.ExternalOrderId.Trim(),
            WebhookPayloadSignature = signature,
            RequestKey = Guid.NewGuid(),
            CustomerName = payload.CustomerName?.Trim(),
            CustomerPhone = payload.CustomerPhone?.Trim(),
            ShippingAddress = payload.ShippingAddress?.Trim(),
            CreatedByUserId = userId,
            OrderTimestamp = canonicalDto.OrderTimestamp ?? (payload.OrderTimestamp ?? DateTimeOffset.UnixEpoch),
            ExpectedTotalAmount = canonicalDto.TotalAmount ?? payload.Items.Sum(i => i.Quantity * i.UnitPrice),
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

        // Tự động đảm bảo ShopIntegration tồn tại cho runner batch mô phỏng
        var simIntegration = await _context.ShopIntegrations
            .FirstOrDefaultAsync(si => si.TenantId == tenantId && si.Channel == normalizedChannel && si.BranchId == request.BranchId && si.IsActive, ct);

        string effectiveShopId = simIntegration?.ExternalShopId ?? "SIM-SHOP-01";
        if (simIntegration == null)
        {
            simIntegration = new ShopIntegration
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Channel = normalizedChannel,
                ExternalShopId = effectiveShopId,
                BranchId = request.BranchId,
                IsActive = true,
                SecretHash = WebhookSecretHasher.HashSecret("sim-batch-secret-key")
            };
            _context.ShopIntegrations.Add(simIntegration);
            await _context.SaveChangesAsync(ct);
        }

        for (int i = 1; i <= request.ConcurrencyCount; i++)
        {
            try
            {
                var payload = new WebhookOrderPayloadDto
                {
                    ExternalOrderId = $"SIM-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}-{i}",
                    ExternalShopId = effectiveShopId,
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
