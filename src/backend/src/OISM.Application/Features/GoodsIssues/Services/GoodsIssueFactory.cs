using OISM.Domain.Entities;

namespace OISM.Application.Features.GoodsIssues.Services;

/// <summary>
/// Factory tĩnh thuần túy để lập Phiếu xuất kho 02-VT (GoodsIssue + GoodsIssueItem)
/// trong cùng giao dịch CSDL với các dòng bút toán sổ cái kho OUT bán hàng (Upgrade 13 - P1, P5, Q13).
/// </summary>
public static class GoodsIssueFactory
{
    public static List<GoodsIssue> CreateForSalesOut(
        Order order,
        SalesInvoice salesInvoice,
        Branch? branch,
        User? issuedBy,
        IReadOnlyCollection<(InventoryTransaction Transaction, OrderItem OrderItem, Warehouse? Warehouse)> outEntries,
        DateTimeOffset now)
    {
        if (outEntries == null || outEntries.Count == 0)
        {
            return new List<GoodsIssue>();
        }

        // P5, Q13: Mỗi kho một phiếu. Sắp xếp theo thứ tự id kho cố định (WarehouseId tăng dần).
        var warehouseGroups = outEntries
            .GroupBy(e => e.Transaction.WarehouseId)
            .OrderBy(g => g.Key)
            .ToList();

        var isMultiWarehouse = warehouseGroups.Count > 1;
        var branchName = !string.IsNullOrWhiteSpace(salesInvoice.BranchNameSnapshot)
            ? salesInvoice.BranchNameSnapshot
            : (branch?.Name ?? string.Empty);
        var issuedById = issuedBy?.Id ?? salesInvoice.IssuedBy ?? order.ConfirmedBy ?? order.CreatedBy;
        var issuedByName = !string.IsNullOrWhiteSpace(issuedBy?.FullName)
            ? issuedBy.FullName
            : salesInvoice.IssuedByNameSnapshot;

        var result = new List<GoodsIssue>(warehouseGroups.Count);

        for (var idx = 0; idx < warehouseGroups.Count; idx++)
        {
            var group = warehouseGroups[idx];
            var warehouseId = group.Key;
            var warehouseName = group.Select(x => x.Warehouse?.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                ?? string.Empty;

            var issueNumber = isMultiWarehouse
                ? $"PXK-{salesInvoice.Number}-{idx + 1}"
                : $"PXK-{salesInvoice.Number}";

            var goodsIssueId = Guid.NewGuid();
            var goodsIssue = new GoodsIssue
            {
                Id = goodsIssueId,
                TenantId = order.TenantId,
                Number = issueNumber,
                OrderId = order.Id,
                SalesInvoiceId = salesInvoice.Id,
                BranchId = order.BranchId,
                WarehouseId = warehouseId,
                Channel = order.Channel,
                OrderNumberSnapshot = order.Number,
                SalesInvoiceNumberSnapshot = salesInvoice.Number,
                BranchNameSnapshot = branchName,
                WarehouseNameSnapshot = warehouseName,
                CustomerNameSnapshot = order.CustomerName,
                IssuedBy = issuedById,
                IssuedByNameSnapshot = issuedByName,
                IssuedAt = now,
                CreatedAt = now,
                UpdatedAt = null
            };

            foreach (var entry in group.OrderBy(e => e.Transaction.SequenceNo))
            {
                var tx = entry.Transaction;
                var orderItem = entry.OrderItem;
                var factor = orderItem.FactorSnapshot > 0m ? orderItem.FactorSnapshot : 1m;
                var baseQuantity = tx.Quantity;

                decimal quantity;
                if (baseQuantity == orderItem.BaseQuantity && orderItem.Quantity > 0m)
                {
                    quantity = orderItem.Quantity;
                }
                else if (factor == 1m)
                {
                    quantity = baseQuantity;
                }
                else
                {
                    quantity = Math.Round(baseQuantity / factor, 3, MidpointRounding.AwayFromZero);
                    if (quantity <= 0m)
                    {
                        quantity = baseQuantity;
                    }
                }

                goodsIssue.Items.Add(new GoodsIssueItem
                {
                    Id = Guid.NewGuid(),
                    TenantId = order.TenantId,
                    GoodsIssueId = goodsIssueId,
                    InventoryTransactionId = tx.Id,
                    OrderItemId = orderItem.Id,
                    SkuId = tx.SkuId,
                    SkuCodeSnapshot = orderItem.SkuCodeSnapshot,
                    SkuNameSnapshot = orderItem.SkuNameSnapshot,
                    UnitId = orderItem.UnitId,
                    UnitNameSnapshot = orderItem.UnitNameSnapshot ?? string.Empty,
                    FactorSnapshot = factor,
                    Quantity = quantity,
                    BaseQuantity = baseQuantity,
                    UnitCost = tx.UnitCost,
                    CreatedAt = now,
                    UpdatedAt = null
                });
            }

            result.Add(goodsIssue);
        }

        return result;
    }
}
