using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;

namespace OISM.IntegrationTests;

public static class PriceSeed
{
    public static async Task SyncFromLegacyColumnsAsync(IAppDbContext db, CancellationToken ct = default)
    {
        using var scope = (db as AppDbContext)?.OpenPrivilegedScope();

        var skus = await db.ProductVariants
            .IgnoreQueryFilters()
            .Include(v => v.SkuUnits)
            .ToListAsync(ct);

        if (skus.Count == 0) return;

        var tenantGroups = skus.GroupBy(s => s.TenantId);

        foreach (var group in tenantGroups)
        {
            var tenantId = group.Key;

            // 1. Ensure default price lists: retail & wholesale
            var retailList = await db.PriceLists
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Code == "retail", ct);
            if (retailList == null)
            {
                retailList = new PriceList
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Code = "retail",
                    Name = "Giá lẻ",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
                };
                db.PriceLists.Add(retailList);
            }

            var wholesaleList = await db.PriceLists
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Code == "wholesale", ct);
            if (wholesaleList == null)
            {
                wholesaleList = new PriceList
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Code = "wholesale",
                    Name = "Giá sỉ",
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
                };
                db.PriceLists.Add(wholesaleList);
            }

            // 2. Ensure default unit exists
            var defaultUnit = await db.Units
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Code == "cai", ct);
            if (defaultUnit == null)
            {
                defaultUnit = new Unit
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Code = "cai",
                    Name = "Cái",
                    Symbol = "cái",
                    MeasureGroup = "Count",
                    DecimalPlaces = 0,
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
                };
                db.Units.Add(defaultUnit);
            }

            // 3. Ensure Opening request
            var openingReq = await db.PriceChangeRequests
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Kind == "Opening", ct);
            if (openingReq == null)
            {
                openingReq = new PriceChangeRequest
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    RequestKey = Guid.NewGuid(),
                    Kind = "Opening",
                    Status = "Approved",
                    Reason = "Giá đầu kỳ",
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                    ReviewedAt = DateTimeOffset.UtcNow.AddDays(-1)
                };
                db.PriceChangeRequests.Add(openingReq);
            }

            await db.SaveChangesAsync(ct);

            // 4. For each SKU, ensure base unit, sku_units, and prices
            foreach (var sku in group)
            {
                if (!sku.BaseUnitId.HasValue)
                {
                    sku.BaseUnitId = defaultUnit.Id;
                    if (string.IsNullOrWhiteSpace(sku.Unit))
                    {
                        sku.Unit = "cai";
                    }
                }

                var baseSkuUnit = sku.SkuUnits?.FirstOrDefault(u => u.UnitId == sku.BaseUnitId.Value);
                if (baseSkuUnit == null)
                {
                    baseSkuUnit = new SkuUnit
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        SkuId = sku.Id,
                        UnitId = sku.BaseUnitId.Value,
                        FactorToBase = 1m,
                        CanPurchase = true,
                        CanSell = true,
                        IsDefaultPurchase = true,
                        IsDefaultSale = true,
                        QuantityDecimalPlaces = 0,
                        IsActive = true,
                        CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                        Version = 1
                    };
                    db.SkuUnits.Add(baseSkuUnit);
                    sku.SkuUnits ??= new List<SkuUnit>();
                    sku.SkuUnits.Add(baseSkuUnit);
                }

                // Retail price for base unit
                var hasRetail = await db.SkuUnitPrices
                    .IgnoreQueryFilters()
                    .AnyAsync(p => p.TenantId == tenantId && p.PriceListId == retailList.Id && p.SkuId == sku.Id && p.UnitId == sku.BaseUnitId.Value && p.CancelledAt == null, ct);
                if (!hasRetail && sku.RetailPrice >= 0)
                {
                    var reqItem = new PriceChangeRequestItem
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        RequestId = openingReq.Id,
                        PriceListId = retailList.Id,
                        SkuId = sku.Id,
                        UnitId = sku.BaseUnitId.Value,
                        NewPrice = sku.RetailPrice,
                        CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
                    };
                    db.PriceChangeRequestItems.Add(reqItem);
                    db.SkuUnitPrices.Add(new SkuUnitPrice
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        PriceListId = retailList.Id,
                        SkuId = sku.Id,
                        UnitId = sku.BaseUnitId.Value,
                        Price = sku.RetailPrice,
                        ValidFrom = DateTimeOffset.UtcNow.AddDays(-1),
                        SourceItemId = reqItem.Id,
                        CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
                    });
                }

                // Wholesale price for base unit
                if (sku.WholesalePrice > 0)
                {
                    var hasWholesale = await db.SkuUnitPrices
                        .IgnoreQueryFilters()
                        .AnyAsync(p => p.TenantId == tenantId && p.PriceListId == wholesaleList.Id && p.SkuId == sku.Id && p.UnitId == sku.BaseUnitId.Value && p.CancelledAt == null, ct);
                    if (!hasWholesale)
                    {
                        var reqItem = new PriceChangeRequestItem
                        {
                            Id = Guid.NewGuid(),
                            TenantId = tenantId,
                            RequestId = openingReq.Id,
                            PriceListId = wholesaleList.Id,
                            SkuId = sku.Id,
                            UnitId = sku.BaseUnitId.Value,
                            NewPrice = sku.WholesalePrice,
                            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
                        };
                        db.PriceChangeRequestItems.Add(reqItem);
                        db.SkuUnitPrices.Add(new SkuUnitPrice
                        {
                            Id = Guid.NewGuid(),
                            TenantId = tenantId,
                            PriceListId = wholesaleList.Id,
                            SkuId = sku.Id,
                            UnitId = sku.BaseUnitId.Value,
                            Price = sku.WholesalePrice,
                            ValidFrom = DateTimeOffset.UtcNow.AddDays(-1),
                            SourceItemId = reqItem.Id,
                            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
                        });
                    }
                }

                // Conversion units
                if (sku.SkuUnits != null)
                {
                    foreach (var u in sku.SkuUnits.Where(u => u.UnitId != sku.BaseUnitId.Value))
                    {
                        if (u.SalePrice.HasValue)
                        {
                            var hasConv = await db.SkuUnitPrices
                                .IgnoreQueryFilters()
                                .AnyAsync(p => p.TenantId == tenantId && p.PriceListId == retailList.Id && p.SkuId == sku.Id && p.UnitId == u.UnitId && p.CancelledAt == null, ct);
                            if (!hasConv)
                            {
                                var reqItem = new PriceChangeRequestItem
                                {
                                    Id = Guid.NewGuid(),
                                    TenantId = tenantId,
                                    RequestId = openingReq.Id,
                                    PriceListId = retailList.Id,
                                    SkuId = sku.Id,
                                    UnitId = u.UnitId,
                                    NewPrice = u.SalePrice.Value,
                                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
                                };
                                db.PriceChangeRequestItems.Add(reqItem);
                                db.SkuUnitPrices.Add(new SkuUnitPrice
                                {
                                    Id = Guid.NewGuid(),
                                    TenantId = tenantId,
                                    PriceListId = retailList.Id,
                                    SkuId = sku.Id,
                                    UnitId = u.UnitId,
                                    Price = u.SalePrice.Value,
                                    ValidFrom = DateTimeOffset.UtcNow.AddDays(-1),
                                    SourceItemId = reqItem.Id,
                                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
                                });
                            }
                        }
                    }
                }
            }

            await db.SaveChangesAsync(ct);
        }
    }
}
