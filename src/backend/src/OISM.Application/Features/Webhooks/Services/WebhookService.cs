using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Orders.Services;
using OISM.Application.Features.Pricing.Dtos;
using OISM.Application.Features.Pricing.Services;
using OISM.Application.Features.Webhooks.Dtos;
using OISM.Domain.Entities;

namespace OISM.Application.Features.Webhooks.Services;

/// <summary>
/// Triển khai dịch vụ tiếp nhận đơn hàng Webhook từ các sàn TMĐT (Shopee, TikTok Shop, Lazada).
/// 
/// Nguyên tắc bảo mật & tích hợp (FR-SIM-01, FR-ORD-01):
/// 1. Xác thực qua X-Webhook-Secret hoặc Bearer JWT (Owner/Staff). Tuyệt đối không chấp nhận tenantId từ request body.
/// 2. Ánh xạ chi nhánh fulfillment từ bảng shop_integrations. Kiểm tra chi nhánh và tích hợp phải IsActive.
/// 3. Khóa chống trùng (deduplication key) (tenant_id, channel, external_shop_id, external_order_id) và chữ ký Canonical SHA-256.
///    - Cùng key & cùng payload: Trả HTTP 200 OK (Idempotent replay).
///    - Cùng key & khác payload: Trả HTTP 409 Conflict (WEBHOOK_PAYLOAD_MISMATCH).
///    - Kênh sai lệch URL vs Body: Trả HTTP 400 Bad Request (CHANNEL_MISMATCH).
/// 4. Giữ chỗ tồn kho bằng cùng lõi Order/Reservation với đơn Admin, nhận đơn giá tin cậy từ sàn TMĐT.
/// </summary>
public class WebhookService : IWebhookService
{
    private readonly IAppDbContext _dbContext;
    private readonly IOrderService _orderService;
    private readonly ICurrentTenantService _currentTenantService;
    private readonly ILogger<WebhookService> _logger;

    /// <summary>Giới hạn số SKU trả cho app giả lập, đủ cho shop demo và không tải cả catalog lớn.</summary>
    public const int MaxCatalogItems = 500;

    public WebhookService(
        IAppDbContext dbContext,
        IOrderService orderService,
        ICurrentTenantService currentTenantService,
        ILogger<WebhookService> logger)
    {
        _dbContext = dbContext;
        _orderService = orderService;
        _currentTenantService = currentTenantService;
        _logger = logger;
    }

    public async Task<WebhookSimulationResultDto> ProcessWebhookAsync(
        string pathChannel,
        CanonicalWebhookPayloadDto dto,
        string? secretHeader,
        string? authHeader,
        ClaimsPrincipal? user,
        CancellationToken ct = default)
    {
        // 1 & 2. Chuẩn hóa kênh bán và kiểm tra toàn bộ tính hợp lệ của Canonical Payload (BE2-F06)
        var normalizedPathChannel = WebhookCanonicalValidator.CanonicalizeChannel(pathChannel);
        var calculatedTotal = WebhookCanonicalValidator.ValidateAndCalculateTotal(normalizedPathChannel, dto);

        // 3. T5-F02: Xác thực và Ánh xạ Tenant/Branch (Authentication & Credential Resolution)
        Guid resolvedTenantId;
        Guid resolvedBranchId;

        var hasSecret = !string.IsNullOrWhiteSpace(secretHeader);
        var hasBearer = user?.Identity?.IsAuthenticated == true;

        if (!hasSecret && !hasBearer)
        {
            throw new OismException("UNAUTHORIZED", "Yêu cầu xác thực Webhook: Thiếu thông tin chứng thực (X-Webhook-Secret hoặc Bearer JWT).", 401);
        }

        if (hasSecret)
        {
            // Secret Mode: Định danh integration từ secret header trước khi đối soát body shop
            var integration = await FindIntegrationBySecretAsync(secretHeader!, ct);

            // Đối soát channel và shopId với integration đã định danh từ credential
            if (!string.Equals(integration.Channel, normalizedPathChannel, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(integration.ExternalShopId, dto.ExternalShopId.Trim(), StringComparison.Ordinal))
            {
                throw new OismException("UNAUTHORIZED", "Thông tin chứng thực không khớp với kênh bán hoặc mã gian hàng được chỉ định.", 401);
            }

            if (!integration.IsActive)
            {
                throw new OismException("UNAUTHORIZED", "Kênh tích hợp gian hàng này đã bị vô hiệu hóa.", 401);
            }

            // Kiểm tra Tenant.IsActive
            Tenant? tenant;
            using (_dbContext.OpenPrivilegedScope())
            {
                tenant = await _dbContext.Tenants
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(t => t.Id == integration.TenantId, ct);
            }

            if (tenant == null || !tenant.IsActive)
            {
                throw new OismException("UNAUTHORIZED", "Doanh nghiệp đã ngừng hoạt động.", 401);
            }

            // Nếu đồng thời có Bearer JWT, kiểm tra không được xung đột Tenant
            if (hasBearer)
            {
                var jwtTenantClaim = user!.FindFirst("tenantId")?.Value ?? user.FindFirst("tenant_id")?.Value;
                if (Guid.TryParse(jwtTenantClaim, out var jwtTenantId) && jwtTenantId != integration.TenantId)
                {
                    throw new OismException("UNAUTHORIZED", "Thông tin xác thực xung đột giữa Bearer JWT và X-Webhook-Secret.", 401);
                }
            }

            resolvedTenantId = integration.TenantId;
            resolvedBranchId = integration.BranchId;
        }
        else
        {
            // JWT Mode: Chỉ cho phép Owner, Staff (Cashier bị 403)
            if (user!.IsInRole("Cashier"))
            {
                throw new OismException("FORBIDDEN", "Tài khoản Thu ngân (Cashier) không có quyền gọi webhook tiếp nhận đơn.", 403);
            }

            if (!user.IsInRole("Owner") && !user.IsInRole("Staff"))
            {
                throw new OismException("FORBIDDEN", "Bạn không có quyền thực hiện thao tác này.", 403);
            }

            var jwtTenantClaim = user.FindFirst("tenantId")?.Value ?? user.FindFirst("tenant_id")?.Value;
            if (!Guid.TryParse(jwtTenantClaim, out var jwtTenantId) || jwtTenantId == Guid.Empty)
            {
                throw new OismException("UNAUTHORIZED", "Không xác định được TenantId hợp lệ từ Bearer Token.", 401);
            }

            ShopIntegration? integration;
            using (_dbContext.OpenPrivilegedScope())
            {
                integration = await _dbContext.ShopIntegrations
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(si => si.TenantId == jwtTenantId && si.Channel == normalizedPathChannel && si.ExternalShopId == dto.ExternalShopId.Trim(), ct);
            }

            if (integration == null)
            {
                throw new OismException("INTEGRATION_NOT_FOUND", $"Không tìm thấy cấu hình tích hợp gian hàng '{dto.ExternalShopId}' của kênh '{normalizedPathChannel}' cho cửa hàng của bạn.", 404);
            }

            if (!integration.IsActive)
            {
                throw new OismException("UNAUTHORIZED", "Kênh tích hợp gian hàng này đã bị vô hiệu hóa.", 401);
            }

            // Kiểm tra Tenant.IsActive
            Tenant? tenant;
            using (_dbContext.OpenPrivilegedScope())
            {
                tenant = await _dbContext.Tenants
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(t => t.Id == jwtTenantId, ct);
            }

            if (tenant == null || !tenant.IsActive)
            {
                throw new OismException("UNAUTHORIZED", "Doanh nghiệp đã ngừng hoạt động.", 401);
            }

            resolvedTenantId = integration.TenantId;
            resolvedBranchId = integration.BranchId;
        }

        // 4. T5-F04 & T5-F05: Tính toán chữ ký Canonical SHA-256 & Fast Replay Check TRƯỚC các kiểm tra tài nguyên thay đổi
        var signature = WebhookCanonicalSignatureHelper.ComputeCanonicalSignature(dto);

        // Thiết lập Tenant context
        _currentTenantService.SetTenant(resolvedTenantId);

        // Fast Replay Check: Tìm đơn hàng sàn đã tồn tại theo external identity tuple
        Order? existingOrder;
        using (_dbContext.OpenPrivilegedScope())
        {
            existingOrder = await _dbContext.Orders
                .IgnoreQueryFilters()
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.TenantId == resolvedTenantId &&
                                          o.Channel == normalizedPathChannel &&
                                          o.ExternalShopId == dto.ExternalShopId.Trim() &&
                                          o.ExternalOrderId == dto.ExternalOrderId.Trim(), ct);
        }

        if (existingOrder != null)
        {
            if (string.IsNullOrWhiteSpace(existingOrder.WebhookPayloadSignature))
            {
                throw new OismException("WEBHOOK_PAYLOAD_MISMATCH", "Đơn hàng sàn lịch sử không có chữ ký để đối soát payload.", 409);
            }

            if (!string.Equals(existingOrder.WebhookPayloadSignature, signature, StringComparison.Ordinal))
            {
                throw new OismException("WEBHOOK_PAYLOAD_MISMATCH", "Payload không khớp với đơn hàng TMĐT đã tiếp nhận trước đó cùng mã định danh sàn.", 409);
            }

            _logger.LogInformation("Idempotent replay webhook thành công cho đơn {OrderNumber} ({Status}).", existingOrder.Number, existingOrder.Status);

            return new WebhookSimulationResultDto
            {
                Success = true,
                Message = $"Đơn hàng TMĐT đã tồn tại trước đó ({existingOrder.Status}).",
                OrderId = existingOrder.Id,
                OrderNumber = existingOrder.Number,
                Channel = existingOrder.Channel,
                Status = existingOrder.Status,
                ReceivedAt = existingOrder.CreatedAt
            };
        }

        // 5. Đối với Đơn Hàng MỚI (chưa tồn tại): Kiểm tra tổng tiền và trạng thái hoạt động của Chi nhánh
        if (Math.Round(calculatedTotal, 2) != Math.Round(dto.TotalAmount!.Value, 2))
        {
            throw new OismException("TOTAL_AMOUNT_MISMATCH", $"Tổng tiền đơn hàng không khớp (Server tính: {calculatedTotal:N2}, Payload gửi: {dto.TotalAmount!.Value:N2}).", 400);
        }

        Branch? branch;
        using (_dbContext.OpenPrivilegedScope())
        {
            branch = await _dbContext.Branches
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(b => b.TenantId == resolvedTenantId && b.Id == resolvedBranchId, ct);
        }

        if (branch == null || !branch.IsActive)
        {
            throw new OismException("BRANCH_INACTIVE_OR_INVALID", $"Chi nhánh fulfillment '{resolvedBranchId}' không tồn tại hoặc đã ngừng hoạt động.", 404);
        }

        // 6. T5-F04: RequestKey nội bộ không mơ hồ bằng null-byte delimiter & trường gắn tag
        var orderContext = new InternalMarketplaceOrderContext
        {
            TenantId = resolvedTenantId,
            BranchId = resolvedBranchId,
            Channel = normalizedPathChannel,
            ExternalShopId = dto.ExternalShopId.Trim(),
            ExternalOrderId = dto.ExternalOrderId.Trim(),
            WebhookPayloadSignature = signature,
            RequestKey = new Func<Guid>(() => {
                using var md5 = System.Security.Cryptography.MD5.Create();
                var keyBytes = System.Text.Encoding.UTF8.GetBytes(
                    $"T={resolvedTenantId}\0C={normalizedPathChannel}\0S={dto.ExternalShopId.Trim()}\0O={dto.ExternalOrderId.Trim()}");
                return new Guid(md5.ComputeHash(keyBytes));
            })(),
            CustomerName = dto.CustomerName,
            CustomerPhone = dto.CustomerPhone,
            ShippingAddress = dto.ShippingAddress,
            ExpectedTotalAmount = calculatedTotal,
            OrderTimestamp = dto.OrderTimestamp!.Value,
            CreatedByUserId = null,
            Items = dto.Items.Select(i => new InternalMarketplaceOrderItemContext
            {
                SkuCode = i.SkuCode.Trim(),
                Quantity = i.Quantity!.Value,
                UnitPrice = i.UnitPrice!.Value
            }).ToList()
        };

        var createdOrder = await _orderService.CreateMarketplaceReservedOrderAsync(orderContext, ct);

        return new WebhookSimulationResultDto
        {
            Success = true,
            Message = createdOrder.IsReplay
                ? $"Đơn hàng TMĐT đã tồn tại trước đó ({createdOrder.Status})."
                : "Tiếp nhận đơn hàng TMĐT thành công và đã giữ chỗ tồn kho.",
            OrderId = createdOrder.Id,
            OrderNumber = createdOrder.Number,
            Channel = createdOrder.Channel,
            Status = createdOrder.Status,
            ReceivedAt = createdOrder.CreatedAt
        };
    }

    public async Task<WebhookIdentityDto> GetIdentityAsync(string? secretHeader, CancellationToken ct = default)
    {
        var (integration, branch) = await ResolveUsableIntegrationAsync(secretHeader, ct);

        // Không trả TenantId, BranchId hay tên doanh nghiệp: người giữ secret chỉ cần biết đang bắn vào gian hàng nào.
        return new WebhookIdentityDto
        {
            Channel = integration.Channel,
            ExternalShopId = integration.ExternalShopId,
            BranchName = branch?.Name ?? string.Empty,
            BranchIsActive = branch?.IsActive == true
        };
    }

    public async Task<IReadOnlyList<WebhookCatalogItemDto>> GetCatalogAsync(string? secretHeader, CancellationToken ct = default)
    {
        var (integration, _) = await ResolveUsableIntegrationAsync(secretHeader, ct);
        var tenantId = integration.TenantId;

        // Luồng chưa có JWT: tenant lấy từ kết nối đã xác thực bằng secret, giống luồng nhận đơn.
        // Truy vấn vẫn lọc TenantId tường minh để không phụ thuộc riêng vào global filter.
        _currentTenantService.SetTenant(tenantId);

        var skus = await _dbContext.ProductVariants
            .AsNoTracking()
            .Where(v => v.TenantId == tenantId && v.IsActive && v.Product != null && v.Product.IsActive)
            .OrderBy(v => v.Product!.Name)
            .ThenBy(v => v.SKU)
            .Take(MaxCatalogItems)
            .Select(v => new
            {
                v.Id,
                v.SKU,
                ProductName = v.Product!.Name,
                v.AttributesJson,
                v.BaseUnitId,
                BaseUnitName = v.BaseUnit != null ? v.BaseUnit.Name : null,
                v.Unit,
                v.RetailPrice
            })
            .ToListAsync(ct);

        if (skus.Count == 0)
        {
            return [];
        }

        var skuIds = skus.Select(s => s.Id).ToList();
        var balances = await _dbContext.InventoryBalances
            .AsNoTracking()
            .Where(b => b.TenantId == tenantId && b.BranchId == integration.BranchId && skuIds.Contains(b.SkuId))
            .Select(b => new { b.SkuId, b.OnHand, b.Reserved })
            .ToListAsync(ct);
        var availableBySku = balances
            .GroupBy(b => b.SkuId)
            .ToDictionary(g => g.Key, g => g.Sum(b => b.OnHand - b.Reserved));

        // Giá bán lẻ hiện hành theo đơn vị gốc, vì webhook không gửi đơn vị nên backend hiểu số lượng theo đơn vị gốc.
        var priceKeys = skus
            .Where(s => s.BaseUnitId.HasValue)
            .Select(s => new SkuUnitPriceKey(s.Id, s.BaseUnitId!.Value))
            .ToList();
        var prices = await new SkuPriceResolver(_dbContext).ResolveAsync("retail", priceKeys, DateTimeOffset.UtcNow, ct);

        // Không trả giá vốn: người giữ secret chỉ đóng vai sàn, chỉ cần mã, tên, giá bán và tồn có thể bán.
        return skus.Select(s =>
        {
            decimal? price = null;
            if (s.BaseUnitId.HasValue && prices.TryGetValue(new SkuUnitPriceKey(s.Id, s.BaseUnitId.Value), out var resolved))
            {
                price = resolved.Price;
            }
            else if (s.RetailPrice > 0)
            {
                price = s.RetailPrice;
            }

            var variant = new ProductVariant { AttributesJson = s.AttributesJson };
            var available = availableBySku.TryGetValue(s.Id, out var a) ? a : 0m;
            return new WebhookCatalogItemDto
            {
                SkuCode = s.SKU,
                ProductName = s.ProductName,
                VariantName = string.IsNullOrWhiteSpace(variant.VariantName) ? null : variant.VariantName,
                UnitName = s.BaseUnitName ?? s.Unit,
                UnitPrice = price,
                Available = available < 0 ? 0 : available
            };
        }).ToList();
    }

    /// <summary>
    /// Cùng điều kiện như luồng nhận đơn: secret sai, kết nối tắt hoặc doanh nghiệp ngừng hoạt động đều trả 401.
    /// Chi nhánh tắt vẫn trả về để giả lập báo cho người dùng, đơn gửi lên mới bị từ chối.
    /// </summary>
    private async Task<(ShopIntegration Integration, Branch? Branch)> ResolveUsableIntegrationAsync(string? secretHeader, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(secretHeader))
        {
            throw new OismException("UNAUTHORIZED", "Thiếu header X-Webhook-Secret.", 401);
        }

        var integration = await FindIntegrationBySecretAsync(secretHeader, ct);

        if (!integration.IsActive)
        {
            throw new OismException("UNAUTHORIZED", "Kênh tích hợp gian hàng này đã bị vô hiệu hóa.", 401);
        }

        Tenant? tenant;
        Branch? branch;
        using (_dbContext.OpenPrivilegedScope())
        {
            tenant = await _dbContext.Tenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == integration.TenantId, ct);
            branch = await _dbContext.Branches
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.TenantId == integration.TenantId && b.Id == integration.BranchId, ct);
        }

        if (tenant == null || !tenant.IsActive)
        {
            throw new OismException("UNAUTHORIZED", "Doanh nghiệp đã ngừng hoạt động.", 401);
        }

        return (integration, branch);
    }

    /// <summary>
    /// secret_hash là SHA-256 tất định và có ràng buộc UNIQUE, nên tra thẳng theo hash
    /// thay vì tải toàn bộ kết nối của mọi tenant.
    /// </summary>
    private async Task<ShopIntegration> FindIntegrationBySecretAsync(string secret, CancellationToken ct)
    {
        var secretHash = WebhookSecretHasher.HashSecret(secret);
        List<ShopIntegration> matchingIntegrations;
        using (_dbContext.OpenPrivilegedScope())
        {
            matchingIntegrations = await _dbContext.ShopIntegrations
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(si => si.SecretHash == secretHash)
                .Take(2)
                .ToListAsync(ct);
        }

        if (matchingIntegrations.Count == 0)
        {
            throw new OismException("UNAUTHORIZED", "Webhook Secret không hợp lệ.", 401);
        }

        if (matchingIntegrations.Count > 1)
        {
            throw new OismException("UNAUTHORIZED", "Thông tin chứng thực xung đột giữa các cấu hình tích hợp.", 401);
        }

        return matchingIntegrations[0];
    }
}
