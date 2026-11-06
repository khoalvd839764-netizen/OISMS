using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Catalog.Units.Services;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Pricing.Dtos;
using OISM.Domain.Common;
using OISM.Domain.Entities;
using static OISM.Domain.Common.OrderLimits;

namespace OISM.Application.Features.Orders.Services;

internal static class OrderLinePlanning
{
    internal sealed record OrderLineInput(Guid SkuId, Guid? UnitId, decimal Quantity, decimal UnitPrice);

    internal sealed class OrderLinePlan
    {
        public required Guid SkuId { get; init; }
        public required ResolvedSkuUnit Unit { get; init; }
        public required decimal UnitPrice { get; init; }
        public decimal Quantity { get; set; }
        public decimal BaseQuantity { get; set; }
        public Guid? SkuUnitPriceId { get; set; }
        public required string PriceSource { get; init; }
    }

    /// <summary>
    /// Phân giải đơn vị, gom các dòng cùng (SKU, đơn vị) và tính lượng gốc. Hệ số chỉ lấy từ cấu hình server, không nhận từ client.
    /// </summary>
    internal static List<OrderLinePlan> BuildOrderLines(
        IEnumerable<OrderLineInput> items,
        IReadOnlyDictionary<Guid, ProductVariant> skus,
        bool trustRequestedPrice,
        IReadOnlyDictionary<SkuUnitPriceKey, ResolvedPrice>? resolvedPrices = null)
    {
        var itemsList = items.ToList();
        var resolvedUnits = new Dictionary<OrderLineInput, ResolvedSkuUnit>();

        foreach (var item in itemsList)
        {
            var sku = skus[item.SkuId];
            var resolved = SkuUnitResolver.ResolveForSale(sku, sku.SkuUnits, item.UnitId);
            if (!trustRequestedPrice && !resolved.UnitId.HasValue)
            {
                throw new OismException("UNIT_PRICE_NOT_CONFIGURED", $"Đơn vị của mặt hàng '{sku.SKU}' chưa được cấu hình đơn vị gốc.", 422);
            }
            resolvedUnits[item] = resolved;
        }

        if (!trustRequestedPrice)
        {
            var mismatches = new List<PriceMismatchItem>();

            foreach (var item in itemsList)
            {
                var sku = skus[item.SkuId];
                var resolved = resolvedUnits[item];
                var key = new SkuUnitPriceKey(item.SkuId, resolved.UnitId!.Value);

                if (resolvedPrices == null || !resolvedPrices.TryGetValue(key, out var price))
                {
                    throw new OismException("UNIT_PRICE_NOT_CONFIGURED", $"Đơn vị '{resolved.UnitName}' của mặt hàng '{sku.SKU}' chưa được cấu hình giá bán.", 422);
                }

                if (item.UnitPrice != price.Price)
                {
                    mismatches.Add(new PriceMismatchItem(item.SkuId, sku.SKU, resolved.UnitId, price.Price));
                }
            }

            if (mismatches.Count > 0)
            {
                var distinctMismatches = mismatches
                    .GroupBy(m => (m.SkuId, m.UnitId))
                    .Select(g => g.First())
                    .ToList();
                throw new PriceMismatchException("Đơn giá của mặt hàng trong giỏ hàng không khớp với giá niêm yết của hệ thống.", distinctMismatches);
            }
        }

        var lines = new List<OrderLinePlan>();
        foreach (var item in itemsList)
        {
            var sku = skus[item.SkuId];
            var resolved = resolvedUnits[item];

            Guid? skuUnitPriceId = null;
            string priceSource = "Marketplace";
            decimal finalUnitPrice = item.UnitPrice;

            if (!trustRequestedPrice)
            {
                var key = new SkuUnitPriceKey(item.SkuId, resolved.UnitId!.Value);
                var price = resolvedPrices![key];
                skuUnitPriceId = price.SkuUnitPriceId;
                priceSource = "PriceList";
                finalUnitPrice = price.Price;
            }

            var line = lines.FirstOrDefault(l => l.SkuId == item.SkuId && l.Unit.UnitId == resolved.UnitId);
            if (line == null)
            {
                lines.Add(new OrderLinePlan
                {
                    SkuId = item.SkuId,
                    Unit = resolved,
                    UnitPrice = finalUnitPrice,
                    Quantity = item.Quantity,
                    SkuUnitPriceId = skuUnitPriceId,
                    PriceSource = priceSource
                });
                continue;
            }

            line.Quantity += item.Quantity;
            if (line.Quantity > MaxQuantityAmount)
            {
                throw new OismException("VALIDATION_ERROR", $"Tổng số lượng mặt hàng SKU '{sku.SKU}' vượt quá giới hạn tối đa cho phép của hệ thống.", 400);
            }
        }

        foreach (var line in lines)
        {
            line.BaseQuantity = SkuUnitResolver.ComputeBaseQuantity(line.Quantity, line.Unit);
        }

        return lines;
    }

    internal static Dictionary<Guid, decimal> SumBaseQuantityBySku(IEnumerable<OrderLinePlan> lines)
    {
        var totals = new Dictionary<Guid, decimal>();
        foreach (var line in lines)
        {
            var total = totals.GetValueOrDefault(line.SkuId) + line.BaseQuantity;
            if (total > MaxQuantityAmount)
            {
                throw new OismException("VALIDATION_ERROR", $"Tổng số lượng mặt hàng SKU '{line.SkuId}' vượt quá giới hạn tối đa cho phép của hệ thống.", 400);
            }
            totals[line.SkuId] = total;
        }
        return totals;
    }

    /// <summary>
    /// Chia phân bổ kho của một SKU cho từng dòng đơn; phần đã lấy được trừ khỏi danh sách còn lại.
    /// </summary>
    internal static List<(Warehouse Warehouse, WarehouseInventoryBalance Balance, decimal Quantity)> TakeAllocations(
        List<(Warehouse Warehouse, WarehouseInventoryBalance Balance, decimal Quantity)> remaining,
        decimal quantity)
    {
        var taken = new List<(Warehouse Warehouse, WarehouseInventoryBalance Balance, decimal Quantity)>();
        for (var i = 0; i < remaining.Count && quantity > 0; i++)
        {
            var entry = remaining[i];
            var take = Math.Min(entry.Quantity, quantity);
            if (take <= 0) continue;

            taken.Add((entry.Warehouse, entry.Balance, take));
            remaining[i] = (entry.Warehouse, entry.Balance, entry.Quantity - take);
            quantity -= take;
        }
        return taken;
    }

    internal static Guid? NormalizeUnit(Guid? unitId, Guid skuId, IReadOnlyDictionary<Guid, Guid?> baseUnits)
    {
        return !unitId.HasValue || (baseUnits.TryGetValue(skuId, out var baseUnitId) && unitId == baseUnitId) ? null : unitId;
    }

    internal static async Task<Dictionary<Guid, Guid?>> LoadBaseUnitsAsync(IAppDbContext context, IEnumerable<Guid> skuIds, CancellationToken ct)
    {
        var ids = skuIds.Distinct().ToList();
        return await context.ProductVariants
            .AsNoTracking()
            .Where(v => ids.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.BaseUnitId, ct);
    }
}
