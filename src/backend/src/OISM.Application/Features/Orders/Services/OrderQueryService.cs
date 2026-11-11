using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Orders.Dtos;
using OISM.Domain.Common;
using OISM.Domain.Entities;
using static OISM.Domain.Common.OrderLimits;

namespace OISM.Application.Features.Orders.Services;

internal sealed class OrderQueryService(IAppDbContext context, ICurrentTenantService currentTenantService)
{
    internal async Task<OrderResponseDto> MapToOrderResponseDtoAsync(Order order, CancellationToken ct)
    {
        var branchName = await context.Branches
            .AsNoTracking()
            .Where(b => b.TenantId == order.TenantId && b.Id == order.BranchId)
            .Select(b => b.Name)
            .FirstOrDefaultAsync(ct);

        var invoiceInfo = await context.SalesInvoices
            .AsNoTracking()
            .Where(s => s.TenantId == order.TenantId && s.OrderId == order.Id)
            .Select(s => new { s.Id, s.Number })
            .FirstOrDefaultAsync(ct);

        var returnedQuantities = await context.SalesReturns
            .AsNoTracking()
            .Where(r => r.TenantId == order.TenantId && r.OrderId == order.Id && r.Status == SalesReturnStatus.Confirmed)
            .SelectMany(r => r.Items)
            .GroupBy(i => i.OrderItemId)
            .Select(g => new { OrderItemId = g.Key, TotalReturned = g.Sum(i => i.Quantity) })
            .ToDictionaryAsync(x => x.OrderItemId, x => x.TotalReturned, ct);

        decimal? totalCogs = null;
        if (order.Items != null && order.Items.Count > 0 && order.Items.All(i => i.CostPrice.HasValue))
        {
            totalCogs = Math.Round(order.Items.Sum(i => i.BaseQuantity * i.CostPrice!.Value), 6, MidpointRounding.AwayFromZero);
            if (totalCogs > MaxMoneyAmount)
            {
                totalCogs = MaxMoneyAmount;
            }
        }

        return new OrderResponseDto
        {
            Id = order.Id,
            Number = order.Number,
            BranchId = order.BranchId,
            BranchName = branchName,
            Channel = order.Channel,
            ExternalShopId = order.ExternalShopId,
            ExternalOrderId = order.ExternalOrderId,
            Status = order.Status,
            CustomerName = order.CustomerName,
            CustomerPhone = order.CustomerPhone,
            ShippingAddress = order.ShippingAddress,
            TotalAmount = order.TotalAmount,
            TotalCogs = totalCogs,
            RequestKey = order.RequestKey,
            CreatedBy = order.CreatedBy,
            ConfirmedBy = order.ConfirmedBy,
            SalesInvoiceId = invoiceInfo?.Id,
            SalesInvoiceNumber = invoiceInfo?.Number,
            ConfirmedAt = order.ConfirmedAt,
            CompletedAt = order.CompletedAt,
            CancelledAt = order.CancelledAt,
            CreatedAt = order.CreatedAt,
            Items = order.Items?.Select(i => new OrderItemResponseDto
            {
                Id = i.Id,
                SkuId = i.SkuId,
                SkuCodeSnapshot = i.SkuCodeSnapshot,
                SkuNameSnapshot = i.SkuNameSnapshot,
                UnitId = i.UnitId,
                UnitName = i.UnitNameSnapshot,
                FactorSnapshot = i.FactorSnapshot,
                BaseQuantity = i.BaseQuantity,
                Quantity = i.Quantity,
                ReturnedQuantity = returnedQuantities.GetValueOrDefault(i.Id, 0m),
                UnitPrice = i.UnitPrice,
                CostPrice = i.CostPrice
            }).ToList() ?? new List<OrderItemResponseDto>(),
            Payment = order.Payment != null ? new PaymentResponseDto
            {
                Id = order.Payment.Id,
                Method = order.Payment.Method,
                Status = order.Payment.Status,
                Amount = order.Payment.Amount,
                CashReceived = order.Payment.CashReceived,
                PaidAt = order.Payment.PaidAt,
                TransferReference = order.Payment.TransferReference,
                ReceiverBankBin = order.Payment.ReceiverBankBin,
                ReceiverAccountNumber = order.Payment.ReceiverAccountNumber,
                ReceiverAccountName = order.Payment.ReceiverAccountName,
                PaymentAccountId = order.Payment.PaymentAccountId
            } : null
        };
    }

    public async Task<OrderResponseDto?> GetOrderByIdAsync(Guid orderId, CancellationToken ct = default)
    {
        var tenantId = currentTenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Yêu cầu xác thực gian hàng (TenantId hợp lệ).");
        }

        var order = await context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.Payment)
            .FirstOrDefaultAsync(o => o.TenantId == tenantId.Value && o.Id == orderId, ct);

        if (order == null)
        {
            return null;
        }

        return await MapToOrderResponseDtoAsync(order, ct);
    }

    public Task<PagedResultDto<OrderResponseDto>> GetOrdersAsync(OrderFilterRequestDto filter, CancellationToken ct = default)
    {
        throw new NotImplementedException("Chức năng tra cứu danh sách đơn hàng thuộc phạm vi phân công của Nhựt (BE2) sẽ hoàn thiện tại T02.");
    }

    public async Task<PagedResult<OrderSummaryDto>> GetOrdersAsync(OrderFilterDto filter, CancellationToken ct = default)
    {
        var tenantId = currentTenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Yêu cầu xác thực gian hàng (TenantId hợp lệ).");
        }

        var query = context.Orders
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var statusTrim = filter.Status.Trim();
            query = query.Where(o => o.Status == statusTrim);
        }

        if (!string.IsNullOrWhiteSpace(filter.Channel))
        {
            var channelTrim = filter.Channel.Trim();
            query = query.Where(o => o.Channel == channelTrim);
        }

        if (filter.BranchId.HasValue && filter.BranchId.Value != Guid.Empty)
        {
            query = query.Where(o => o.BranchId == filter.BranchId.Value);
        }

        if (filter.FromDate.HasValue)
        {
            query = query.Where(o => o.CreatedAt >= filter.FromDate.Value);
        }

        if (filter.ToDate.HasValue)
        {
            query = query.Where(o => o.CreatedAt <= filter.ToDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLower();
            query = query.Where(o =>
                o.Number.ToLower().Contains(search) ||
                (o.CustomerName != null && o.CustomerName.ToLower().Contains(search)) ||
                (o.CustomerPhone != null && o.CustomerPhone.Contains(search)) ||
                (o.ExternalOrderId != null && o.ExternalOrderId.ToLower().Contains(search)));
        }

        var totalCount = await query.CountAsync(ct);

        var pageIndex = Math.Max(1, filter.PageIndex);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new OrderSummaryDto
            {
                Id = o.Id,
                Number = o.Number,
                BranchId = o.BranchId,
                BranchName = o.Branch != null ? o.Branch.Name : null,
                Channel = o.Channel,
                ExternalShopId = o.ExternalShopId,
                ExternalOrderId = o.ExternalOrderId,
                Status = o.Status,
                CustomerName = o.CustomerName,
                CustomerPhone = o.CustomerPhone,
                TotalAmount = o.TotalAmount,
                ItemCount = o.Items.Count,
                CreatedAt = o.CreatedAt,
                ConfirmedAt = o.ConfirmedAt,
                CompletedAt = o.CompletedAt,
                CancelledAt = o.CancelledAt
            })
            .ToListAsync(ct);

        return new PagedResult<OrderSummaryDto>
        {
            Items = orders,
            PageIndex = pageIndex,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<OrderDetailDto?> GetOrderDetailByIdAsync(Guid id, CancellationToken ct = default)
    {
        var tenantId = currentTenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Yêu cầu xác thực gian hàng (TenantId hợp lệ).");
        }

        var order = await context.Orders
            .AsNoTracking()
            .Include(o => o.Branch)
            .Include(o => o.Items)
                .ThenInclude(i => i.Sku)
                    .ThenInclude(s => s!.Product)
            .FirstOrDefaultAsync(o => o.Id == id && o.TenantId == tenantId.Value, ct);

        if (order == null)
        {
            return null;
        }

        var invoice = await context.SalesInvoices
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId.Value && s.OrderId == order.Id)
            .Select(s => new { s.Id, s.Number })
            .FirstOrDefaultAsync(ct);

        var detail = MapToDetailDto(order);
        detail.ConfirmedBy = order.ConfirmedBy;
        detail.SalesInvoiceId = invoice?.Id;
        detail.SalesInvoiceNumber = invoice?.Number;
        return detail;
    }

    private static OrderDetailDto MapToDetailDto(Order order, string? branchName = null)
    {
        return new OrderDetailDto
        {
            Id = order.Id,
            Number = order.Number,
            BranchId = order.BranchId,
            BranchName = branchName ?? order.Branch?.Name,
            Channel = order.Channel,
            ExternalShopId = order.ExternalShopId,
            ExternalOrderId = order.ExternalOrderId,
            Status = order.Status,
            CustomerName = order.CustomerName,
            CustomerPhone = order.CustomerPhone,
            ShippingAddress = order.ShippingAddress,
            TotalAmount = order.TotalAmount,
            RequestKey = order.RequestKey,
            ItemCount = order.Items?.Count ?? 0,
            CreatedAt = order.CreatedAt,
            ConfirmedBy = order.ConfirmedBy,
            ConfirmedAt = order.ConfirmedAt,
            CompletedAt = order.CompletedAt,
            CancelledAt = order.CancelledAt,
            Items = order.Items?.Select(i => new OrderItemDto
            {
                Id = i.Id,
                SkuId = i.SkuId,
                SkuCode = i.SkuCodeSnapshot ?? i.Sku?.SKU ?? string.Empty,
                SkuName = i.SkuNameSnapshot ?? i.Sku?.Product?.Name ?? string.Empty,
                UnitId = i.UnitId,
                UnitName = i.UnitNameSnapshot,
                FactorSnapshot = i.FactorSnapshot,
                BaseQuantity = i.BaseQuantity,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice,
                CostPrice = i.CostPrice
            }).ToList() ?? new List<OrderItemDto>()
        };
    }
}
