using OISM.Domain.Entities;

namespace OISM.Application.Features.SalesInvoices.Services;

/// <summary>
/// Factory tĩnh thuần túy để tạo SalesInvoice từ thông tin đơn hàng, chi nhánh, người thực hiện.
/// </summary>
public static class SalesInvoiceFactory
{
    public static SalesInvoice Create(
        Order order,
        Branch? branch,
        User? issuedBy,
        string? paymentMethod,
        DateTimeOffset now)
    {
        var hexSuffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var invoiceNumber = $"INV-{now:yyyyMMdd}-{hexSuffix}";

        return new SalesInvoice
        {
            Id = Guid.NewGuid(),
            TenantId = order.TenantId,
            Number = invoiceNumber,
            OrderId = order.Id,
            BranchId = order.BranchId,
            Channel = order.Channel,
            BranchNameSnapshot = branch?.Name ?? string.Empty,
            BranchAddressSnapshot = branch?.Address,
            CustomerNameSnapshot = order.CustomerName,
            CustomerPhoneSnapshot = order.CustomerPhone,
            IssuedBy = issuedBy?.Id,
            IssuedByNameSnapshot = issuedBy?.FullName,
            TotalAmount = order.TotalAmount,
            PaymentMethodSnapshot = paymentMethod,
            IssuedAt = now,
            CreatedAt = now,
            UpdatedAt = null
        };
    }
}
