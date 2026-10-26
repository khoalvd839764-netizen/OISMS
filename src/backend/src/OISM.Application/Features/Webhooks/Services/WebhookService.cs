using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Orders.Services;
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
        // 1. Chuẩn hóa và xác thực kênh bán từ URL
        if (string.IsNullOrWhiteSpace(pathChannel))
        {
            throw new OismException("VALIDATION_ERROR", "Kênh bán trên đường dẫn URL không được để trống.", 400);
        }

        var normalizedPathChannel = pathChannel.Trim().ToLowerInvariant() switch
        {
            "shopee" => "Shopee",
            "tiktok" => "TikTok",
            "lazada" => "Lazada",
            _ => null
        };

        if (normalizedPathChannel == null)
        {
            throw new OismException("VALIDATION_ERROR", $"Kênh bán '{pathChannel}' không được hỗ trợ. Chỉ hỗ trợ 'Shopee', 'TikTok', 'Lazada'.", 400);
        }

        // 2. Kiểm tra Payload cơ bản & Validation cấu trúc
        if (dto == null)
        {
            throw new OismException("VALIDATION_ERROR", "Nội dung Webhook payload không được để trống.", 400);
        }

        if (string.IsNullOrWhiteSpace(dto.Channel))
        {
            throw new OismException("VALIDATION_ERROR", "Trường 'channel' trong payload không được để trống.", 400);
        }

        if (!string.Equals(normalizedPathChannel, dto.Channel.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new OismException("CHANNEL_MISMATCH", $"Kênh bán trên đường dẫn URL ('{pathChannel}') không khớp với kênh bán trong nội dung payload ('{dto.Channel}').", 400);
        }

        if (string.IsNullOrWhiteSpace(dto.ExternalShopId))
        {
            throw new OismException("VALIDATION_ERROR", "Mã Shop trên sàn ('externalShopId') là bắt buộc.", 400);
        }

        if (dto.ExternalShopId.Trim().Length > 100)
        {
            throw new OismException("VALIDATION_ERROR", "Mã Shop trên sàn không được vượt quá 100 ký tự.", 400);
        }

        if (string.IsNullOrWhiteSpace(dto.ExternalOrderId))
        {
            throw new OismException("VALIDATION_ERROR", "Mã Đơn hàng trên sàn ('externalOrderId') là bắt buộc.", 400);
        }

        if (dto.ExternalOrderId.Trim().Length > 100)
        {
            throw new OismException("VALIDATION_ERROR", "Mã Đơn hàng trên sàn không được vượt quá 100 ký tự.", 400);
        }

        // T5-F03: OrderTimestamp bắt buộc có giá trị xác định
        if (!dto.OrderTimestamp.HasValue || dto.OrderTimestamp.Value == default)
        {
            throw new OismException("VALIDATION_ERROR", "Thời gian đặt hàng ('orderTimestamp') là bắt buộc và phải có giá trị xác định.", 400);
        }

        // T5-F03: TotalAmount bắt buộc, không âm, scale <= 2, magnitude
        if (!dto.TotalAmount.HasValue)
        {
            throw new OismException("VALIDATION_ERROR", "Tổng tiền đơn hàng ('totalAmount') là bắt buộc.", 400);
        }

        if (dto.TotalAmount.Value < 0)
        {
            throw new OismException("VALIDATION_ERROR", "Tổng tiền đơn hàng không được âm.", 400);
        }

        if (decimal.Round(dto.TotalAmount.Value, 2) != dto.TotalAmount.Value)
        {
            throw new OismException("VALIDATION_ERROR", "Tổng tiền đơn hàng không được vượt quá 2 chữ số thập phân.", 400);
        }

        if (dto.TotalAmount.Value > OrderService.MaxMoneyAmount)
        {
            throw new OismException("VALIDATION_ERROR", "Tổng tiền đơn hàng vượt quá giới hạn tối đa cho phép.", 400);
        }

        // T5-F03: Customer fields length validation
        if (dto.CustomerName != null && dto.CustomerName.Trim().Length > 200)
        {
            throw new OismException("VALIDATION_ERROR", "Tên khách hàng không được vượt quá 200 ký tự.", 400);
        }

        if (dto.CustomerPhone != null && dto.CustomerPhone.Trim().Length > 32)
        {
            throw new OismException("VALIDATION_ERROR", "Số điện thoại không được vượt quá 32 ký tự.", 400);
        }

        if (dto.ShippingAddress != null && dto.ShippingAddress.Trim().Length > 500)
        {
            throw new OismException("VALIDATION_ERROR", "Địa chỉ giao hàng không được vượt quá 500 ký tự.", 400);
        }

        if (dto.Items == null || dto.Items.Count == 0)
        {
            throw new OismException("VALIDATION_ERROR", "Đơn hàng phải có ít nhất 1 sản phẩm.", 400);
        }

        if (dto.Items.Any(i => i == null))
        {
            throw new OismException("VALIDATION_ERROR", "Dòng sản phẩm trong đơn hàng không được mang giá trị null.", 400);
        }

        foreach (var item in dto.Items)
        {
            if (string.IsNullOrWhiteSpace(item.SkuCode))
            {
                throw new OismException("VALIDATION_ERROR", "Mã SKU mặt hàng trong đơn không được để trống.", 400);
            }

            if (item.SkuCode.Trim().Length > 80)
            {
                throw new OismException("VALIDATION_ERROR", $"Mã SKU '{item.SkuCode}' vượt quá 80 ký tự cho phép.", 400);
            }

            if (!item.Quantity.HasValue)
            {
                throw new OismException("VALIDATION_ERROR", $"Số lượng mặt hàng SKU '{item.SkuCode}' là bắt buộc.", 400);
            }

            if (item.Quantity.Value <= 0)
            {
                throw new OismException("VALIDATION_ERROR", $"Số lượng mặt hàng SKU '{item.SkuCode}' phải lớn hơn 0.", 400);
            }

            if (item.Quantity.Value > OrderService.MaxQuantityAmount)
            {
                throw new OismException("VALIDATION_ERROR", $"Số lượng mặt hàng SKU '{item.SkuCode}' vượt quá giới hạn tối đa cho phép.", 400);
            }

            if (decimal.Round(item.Quantity.Value, 3) != item.Quantity.Value)
            {
                throw new OismException("VALIDATION_ERROR", $"Số lượng mặt hàng SKU '{item.SkuCode}' không được vượt quá 3 chữ số thập phân.", 400);
            }

            if (!item.UnitPrice.HasValue)
            {
                throw new OismException("VALIDATION_ERROR", $"Đơn giá của SKU '{item.SkuCode}' là bắt buộc.", 400);
            }

            if (item.UnitPrice.Value < 0)
            {
                throw new OismException("VALIDATION_ERROR", $"Đơn giá của SKU '{item.SkuCode}' không được âm.", 400);
            }

            if (item.UnitPrice.Value > OrderService.MaxMoneyAmount)
            {
                throw new OismException("VALIDATION_ERROR", $"Đơn giá của SKU '{item.SkuCode}' vượt quá giới hạn tối đa cho phép.", 400);
            }

            if (decimal.Round(item.UnitPrice.Value, 2) != item.UnitPrice.Value)
            {
                throw new OismException("VALIDATION_ERROR", $"Đơn giá của SKU '{item.SkuCode}' không được vượt quá 2 chữ số thập phân.", 400);
            }
        }

        // Kiểm tra tính nhất quán đơn giá nếu có dòng trùng SKU
        foreach (var g in dto.Items.GroupBy(i => i.SkuCode.Trim().ToUpperInvariant()))
        {
            if (g.Select(x => Math.Round(x.UnitPrice!.Value, 2)).Distinct().Count() > 1)
            {
                throw new OismException("VALIDATION_ERROR", "Các dòng cùng SKU trong đơn hàng phải có cùng đơn giá.", 400);
            }
        }

        // Tính toán tổng tiền theo cấu trúc dòng hàng phía Server
        decimal calculatedTotal = 0;
        foreach (var item in dto.Items)
        {
            try
            {
                checked
                {
                    var lineTotal = item.Quantity!.Value * item.UnitPrice!.Value;
                    if (lineTotal > OrderService.MaxMoneyAmount)
                    {
                        throw new OismException("VALIDATION_ERROR", "Tổng tiền của dòng hàng vượt quá giới hạn tối đa cho phép.", 400);
                    }
                    calculatedTotal += lineTotal;
                    if (calculatedTotal > OrderService.MaxMoneyAmount)
                    {
                        throw new OismException("VALIDATION_ERROR", "Tổng tiền đơn hàng vượt quá giới hạn tối đa cho phép.", 400);
                    }
                }
            }
            catch (OverflowException)
            {
                throw new OismException("VALIDATION_ERROR", "Tổng tiền đơn hàng vượt quá giới hạn tối đa cho phép.", 400);
            }
        }
        calculatedTotal = Math.Round(calculatedTotal, 2, MidpointRounding.AwayFromZero);

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
            List<ShopIntegration> allIntegrations;
            using (_dbContext.OpenPrivilegedScope())
            {
                allIntegrations = await _dbContext.ShopIntegrations
                    .IgnoreQueryFilters()
                    .ToListAsync(ct);
            }

            var matchingIntegrations = allIntegrations
                .Where(si => WebhookSecretHasher.VerifySecret(secretHeader!, si.SecretHash))
                .ToList();

            if (matchingIntegrations.Count == 0)
            {
                throw new OismException("UNAUTHORIZED", "Webhook Secret không hợp lệ.", 401);
            }

            if (matchingIntegrations.Count > 1)
            {
                throw new OismException("UNAUTHORIZED", "Thông tin chứng thực xung đột giữa các cấu hình tích hợp.", 401);
            }

            var integration = matchingIntegrations[0];

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
        if (Math.Round(calculatedTotal, 2) != Math.Round(dto.TotalAmount.Value, 2))
        {
            throw new OismException("TOTAL_AMOUNT_MISMATCH", $"Tổng tiền đơn hàng không khớp (Server tính: {calculatedTotal:N2}, Payload gửi: {dto.TotalAmount.Value:N2}).", 400);
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
            OrderTimestamp = dto.OrderTimestamp.Value,
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
}
