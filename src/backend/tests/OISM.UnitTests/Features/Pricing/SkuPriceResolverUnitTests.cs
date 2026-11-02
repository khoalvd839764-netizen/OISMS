using FluentAssertions;
using OISM.Application.Features.Pricing.Dtos;
using OISM.Application.Features.Pricing.Services;
using OISM.Domain.Entities;
using Xunit;

namespace OISM.UnitTests.Features.Pricing;

public class SkuPriceResolverUnitTests
{
    [Fact]
    public async Task ResolveAsync_ReturnsCorrectPriceAtGivenValidFrom()
    {
        var (context, _, tenantId) = TestDbContextFactory.CreateInMemoryContext();

        var retailList = new PriceList
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "retail",
            Name = "Giá lẻ",
            IsActive = true
        };
        context.PriceLists.Add(retailList);

        var skuId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var t0 = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddDays(1);
        var t2 = t0.AddDays(2);

        // Price 1: 100,000 at t0
        var p1 = new SkuUnitPrice
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PriceListId = retailList.Id,
            SkuId = skuId,
            UnitId = unitId,
            Price = 100000m,
            ValidFrom = t0,
            SourceItemId = Guid.NewGuid(),
            PriceList = retailList
        };

        // Price 2: 120,000 at t1
        var p2 = new SkuUnitPrice
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PriceListId = retailList.Id,
            SkuId = skuId,
            UnitId = unitId,
            Price = 120000m,
            ValidFrom = t1,
            SourceItemId = Guid.NewGuid(),
            PriceList = retailList
        };

        // Price 3: 150,000 at t2
        var p3 = new SkuUnitPrice
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PriceListId = retailList.Id,
            SkuId = skuId,
            UnitId = unitId,
            Price = 150000m,
            ValidFrom = t2,
            SourceItemId = Guid.NewGuid(),
            PriceList = retailList
        };

        context.SkuUnitPrices.AddRange(p1, p2, p3);
        await context.SaveChangesAsync();

        var resolver = new SkuPriceResolver(context);
        var key = new SkuUnitPriceKey(skuId, unitId);

        // Tra tại thời điểm trước t1 -> Nhận giá p1 (100k)
        var resBeforeT1 = await resolver.ResolveAsync("retail", new[] { key }, t0.AddHours(12));
        resBeforeT1.Should().ContainKey(key);
        resBeforeT1[key].Price.Should().Be(100000m);
        resBeforeT1[key].SkuUnitPriceId.Should().Be(p1.Id);

        // Tra tại thời điểm đúng t1 -> Nhận giá p2 (120k)
        var resAtT1 = await resolver.ResolveAsync("retail", new[] { key }, t1);
        resAtT1[key].Price.Should().Be(120000m);
        resAtT1[key].SkuUnitPriceId.Should().Be(p2.Id);

        // Tra tại thời điểm giữa t1 và t2 -> Nhận giá p2 (120k)
        var resBetweenT1T2 = await resolver.ResolveAsync("retail", new[] { key }, t1.AddHours(5));
        resBetweenT1T2[key].Price.Should().Be(120000m);

        // Tra tại thời điểm sau t2 -> Nhận giá p3 (150k)
        var resAfterT2 = await resolver.ResolveAsync("retail", new[] { key }, t2.AddHours(1));
        resAfterT2[key].Price.Should().Be(150000m);
    }

    [Fact]
    public async Task ResolveAsync_IgnoresCancelledPrices()
    {
        var (context, _, tenantId) = TestDbContextFactory.CreateInMemoryContext();

        var retailList = new PriceList
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "retail",
            Name = "Giá lẻ",
            IsActive = true
        };
        context.PriceLists.Add(retailList);

        var skuId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var t0 = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddDays(1);

        // Price 1: 100k at t0
        var p1 = new SkuUnitPrice
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PriceListId = retailList.Id,
            SkuId = skuId,
            UnitId = unitId,
            Price = 100000m,
            ValidFrom = t0,
            SourceItemId = Guid.NewGuid(),
            PriceList = retailList
        };

        // Price 2: 150k at t1 BUT cancelled!
        var p2 = new SkuUnitPrice
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PriceListId = retailList.Id,
            SkuId = skuId,
            UnitId = unitId,
            Price = 150000m,
            ValidFrom = t1,
            SourceItemId = Guid.NewGuid(),
            PriceList = retailList,
            CancelledAt = t0.AddHours(2),
            CancelledBy = Guid.NewGuid(),
            CancelReason = "Hủy kế hoạch tăng giá"
        };

        context.SkuUnitPrices.AddRange(p1, p2);
        await context.SaveChangesAsync();

        var resolver = new SkuPriceResolver(context);
        var key = new SkuUnitPriceKey(skuId, unitId);

        // Tra tại thời điểm sau t1 -> Phải nhận p1 vì p2 đã bị hủy!
        var res = await resolver.ResolveAsync("retail", new[] { key }, t1.AddHours(1));
        res.Should().ContainKey(key);
        res[key].Price.Should().Be(100000m);
        res[key].SkuUnitPriceId.Should().Be(p1.Id);
    }
}
