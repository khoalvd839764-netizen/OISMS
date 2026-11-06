using OISM.Application.Features.Orders.Dtos;
using static OISM.Application.Features.Orders.Services.OrderIntakeService;

namespace OISM.Application.Features.Orders.Services;

internal sealed class MarketplaceOrderService(OrderIntakeService intake)
{
    public async Task<OrderResponseDto> CreateMarketplaceReservedOrderAsync(InternalMarketplaceOrderContext context, CancellationToken ct = default)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var coreReq = new InternalOrderCoreRequest(
            TenantId: context.TenantId,
            BranchId: context.BranchId,
            Channel: context.Channel,
            ExternalShopId: context.ExternalShopId,
            ExternalOrderId: context.ExternalOrderId,
            WebhookPayloadSignature: context.WebhookPayloadSignature,
            RequestKey: context.RequestKey,
            CustomerName: context.CustomerName,
            CustomerPhone: context.CustomerPhone,
            ShippingAddress: context.ShippingAddress,
            ExpectedTotalAmount: context.ExpectedTotalAmount,
            CreatedByUserId: context.CreatedByUserId,
            IsTrustedMarketplace: true,
            Items: context.Items.Select(i => new InternalOrderCoreItem(null, i.SkuCode, i.Quantity, i.UnitPrice)).ToList(),
            PriceListCode: "retail");

        return await intake.CreateReservedOrderCoreAsync(coreReq, ct);
    }
}
