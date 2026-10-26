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

    public async Task<OrderDetailDto> IngestWebhookOrderAsync(
        string channel,
        WebhookOrderPayloadDto payload,
        Guid? userId,
        CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        if (payload == null)
        {
            throw new OismException("VALIDATION_ERROR", "Nội dung Webhook payload không được để trống.", 400);
        }

        // 1. Chuẩn hóa kênh bán từ WebhookCanonicalValidator
        var normalizedChannel = WebhookCanonicalValidator.CanonicalizeChannel(channel);

        // 2. Chuyển đổi sang CanonicalWebhookPayloadDto và chuẩn hóa Timestamp
        // Nếu payload.OrderTimestamp có giá trị: giữ nguyên (bao gồm default(DateTimeOffset) để validator bắt lỗi)
        // Nếu payload.OrderTimestamp là null: tự động gán vào payload để idempotent replay cùng payload instance có signature nhất quán
        payload.OrderTimestamp ??= DateTimeOffset.UtcNow;
        DateTimeOffset? effectiveTimestamp = payload.OrderTimestamp;

        var canonicalDto = new CanonicalWebhookPayloadDto
        {
            Channel = normalizedChannel,
            ExternalShopId = payload.ExternalShopId?.Trim() ?? string.Empty,
            ExternalOrderId = payload.ExternalOrderId?.Trim() ?? string.Empty,
            CustomerName = payload.CustomerName?.Trim(),
            CustomerPhone = payload.CustomerPhone?.Trim(),
            ShippingAddress = payload.ShippingAddress?.Trim(),
            OrderTimestamp = effectiveTimestamp,
            TotalAmount = payload.Items != null ? payload.Items.Sum(i => i.Quantity * i.UnitPrice) : null,
            Items = payload.Items?.Select(i => new CanonicalWebhookItemDto
            {
                SkuCode = i.SkuCode?.Trim() ?? string.Empty,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList() ?? new()
        };

        // 3. Thực thi xác thực chuẩn hóa dùng chung Canonical Validator (BE2-F06, R2-AC-04)
        var calculatedTotal = WebhookCanonicalValidator.ValidateAndCalculateTotal(normalizedChannel, canonicalDto);

        // 4. Đối soát cấu hình ShopIntegration của gian hàng (FR-SIM-01, BE2-F06 - Tuyệt đối không bypass ShopIntegration)
        var tenant = await _context.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct);

        if (tenant != null && !tenant.IsActive)
        {
            throw new OismException("UNAUTHORIZED", "Doanh nghiệp đã ngừng hoạt động.", 401);
        }

        var shopIntegration = await _context.ShopIntegrations
            .FirstOrDefaultAsync(si => si.TenantId == tenantId &&
                                       si.Channel == normalizedChannel &&
                                       si.ExternalShopId == canonicalDto.ExternalShopId, ct);

        if (shopIntegration == null)
        {
            throw new OismException("INTEGRATION_NOT_FOUND", $"Không tìm thấy cấu hình tích hợp gian hàng '{canonicalDto.ExternalShopId}' của kênh '{normalizedChannel}' cho cửa hàng của bạn.", 404);
        }

        if (!shopIntegration.IsActive)
        {
            throw new OismException("UNAUTHORIZED", "Kênh tích hợp gian hàng này đã bị vô hiệu hóa.", 401);
        }

        var targetBranchId = shopIntegration.BranchId;
        if (payload.BranchId != Guid.Empty && payload.BranchId != targetBranchId)
        {
            throw new OismException("BRANCH_MISMATCH", $"Chi nhánh '{payload.BranchId}' không khớp với chi nhánh được cấu hình cho gian hàng '{canonicalDto.ExternalShopId}'.", 400);
        }

        var branch = await _context.Branches
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == targetBranchId, ct);
        if (branch == null || !branch.IsActive)
        {
            throw new OismException("BRANCH_INACTIVE_OR_INVALID", $"Chi nhánh '{targetBranchId}' không tồn tại hoặc đã ngừng hoạt động.", 404);
        }

        // 5. Tính toán chữ ký Canonical SHA-256 dùng chung (R2-AC-05)
        var signature = WebhookCanonicalSignatureHelper.ComputeCanonicalSignature(canonicalDto);

        var marketplaceContext = new InternalMarketplaceOrderContext
        {
            TenantId = tenantId,
            BranchId = targetBranchId,
            Channel = normalizedChannel,
            ExternalShopId = canonicalDto.ExternalShopId,
            ExternalOrderId = canonicalDto.ExternalOrderId,
            WebhookPayloadSignature = signature,
            RequestKey = new Func<Guid>(() => {
                using var md5 = System.Security.Cryptography.MD5.Create();
                var keyBytes = System.Text.Encoding.UTF8.GetBytes(
                    $"T={tenantId}\0C={normalizedChannel}\0S={canonicalDto.ExternalShopId}\0O={canonicalDto.ExternalOrderId}");
                return new Guid(md5.ComputeHash(keyBytes));
            })(),
            CustomerName = canonicalDto.CustomerName,
            CustomerPhone = canonicalDto.CustomerPhone,
            ShippingAddress = canonicalDto.ShippingAddress,
            CreatedByUserId = userId,
            OrderTimestamp = canonicalDto.OrderTimestamp!.Value,
            ExpectedTotalAmount = calculatedTotal,
            Items = canonicalDto.Items.Select(i => new InternalMarketplaceOrderItemContext
            {
                SkuCode = i.SkuCode.Trim(),
                Quantity = i.Quantity!.Value,
                UnitPrice = i.UnitPrice!.Value
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

        var normalizedChannel = WebhookCanonicalValidator.CanonicalizeChannel(request.Channel);
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

        // 1. Kiểm tra Chi nhánh tồn tại và đang hoạt động
        var branch = await _context.Branches
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == request.BranchId, ct);
        if (branch == null || !branch.IsActive)
        {
            throw new OismException("BRANCH_INACTIVE_OR_INVALID", $"Chi nhánh '{request.BranchId}' không tồn tại hoặc đã ngừng hoạt động.", 404);
        }

        // 2. BE2-T01-F02: Không tự tạo ShopIntegration active/secret ngầm (R2-AC-01..03)
        // Bắt buộc ShopIntegration phải được cấu hình tường minh trước đó.
        var simIntegration = await _context.ShopIntegrations
            .FirstOrDefaultAsync(si => si.TenantId == tenantId && si.Channel == normalizedChannel && si.BranchId == request.BranchId && si.IsActive, ct);

        if (simIntegration == null)
        {
            throw new OismException("INTEGRATION_NOT_FOUND", $"Không tìm thấy cấu hình tích hợp gian hàng đang hoạt động cho kênh '{normalizedChannel}' tại chi nhánh '{request.BranchId}'. Cần cấu hình gian hàng trước khi chạy mô phỏng.", 404);
        }

        string effectiveShopId = simIntegration.ExternalShopId;

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
                    OrderTimestamp = DateTimeOffset.UtcNow,
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
                orderNumbers.Add(order.Number);
                successCount++;
            }
            catch (Exception ex)
            {
                failedCount++;
                errors.Add($"Request #{i}: {ex.Message}");
            }
        }

        stopwatch.Stop();

        result.SuccessCount = successCount;
        result.FailedConflictCount = failedCount;
        result.CreatedOrderNumbers = orderNumbers;
        result.ErrorMessages = errors;
        result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;

        return result;
    }
}
