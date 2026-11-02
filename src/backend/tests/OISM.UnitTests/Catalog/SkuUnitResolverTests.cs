using FluentAssertions;
using OISM.Application.Common.Exceptions;
using OISM.Domain.Entities;
using OISM.Application.Features.Catalog.Units.Services;
using Xunit;

namespace OISM.UnitTests.Catalog;

public class SkuUnitResolverTests
{
    private readonly ProductVariant _sampleSku;
    private readonly Unit _kgUnit;
    private readonly Unit _baoUnit;
    private readonly Unit _tanUnit;
    private readonly SkuUnit _kgSkuUnit;
    private readonly SkuUnit _baoSkuUnit;
    private readonly SkuUnit _tanSkuUnit;
    private readonly SkuUnit _inactiveSkuUnit;
    private readonly List<SkuUnit> _allSkuUnits;

    public SkuUnitResolverTests()
    {
        var tenantId = Guid.NewGuid();
        var skuId = Guid.NewGuid();

        _kgUnit = new Unit
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "kg",
            Name = "Kilôgam",
            Symbol = "kg",
            MeasureGroup = MeasureGroups.Mass,
            DecimalPlaces = 3,
            IsActive = true
        };

        _baoUnit = new Unit
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "bao",
            Name = "Bao",
            Symbol = "bao",
            MeasureGroup = MeasureGroups.Packaging,
            DecimalPlaces = 0,
            IsActive = true
        };

        _tanUnit = new Unit
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "tan",
            Name = "Tấn",
            Symbol = "tấn",
            MeasureGroup = MeasureGroups.Mass,
            DecimalPlaces = 4,
            IsActive = true
        };

        var inactiveUnit = new Unit
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "thung",
            Name = "Thùng",
            Symbol = "thùng",
            MeasureGroup = MeasureGroups.Packaging,
            DecimalPlaces = 0,
            IsActive = false
        };

        _sampleSku = new ProductVariant
        {
            Id = skuId,
            TenantId = tenantId,
            SKU = "RICE-001",
            Barcode = "893000000001",
            RetailPrice = 20000m,
            BaseUnitId = _kgUnit.Id,
            BaseUnit = _kgUnit,
            Unit = "kg",
            IsActive = true
        };

        _kgSkuUnit = new SkuUnit
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SkuId = skuId,
            UnitId = _kgUnit.Id,
            Unit = _kgUnit,
            FactorToBase = 1m,
            CanPurchase = true,
            CanSell = true,
            IsDefaultPurchase = true,
            IsDefaultSale = true,
            QuantityDecimalPlaces = 3,
            Version = 1,
            IsActive = true
        };

        _baoSkuUnit = new SkuUnit
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SkuId = skuId,
            UnitId = _baoUnit.Id,
            Unit = _baoUnit,
            FactorToBase = 25m,
            CanPurchase = true,
            CanSell = true,
            QuantityDecimalPlaces = 0,
            Version = 1,
            IsActive = true
        };

        _tanSkuUnit = new SkuUnit
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SkuId = skuId,
            UnitId = _tanUnit.Id,
            Unit = _tanUnit,
            FactorToBase = 1000m,
            CanPurchase = true,
            CanSell = true,
            QuantityDecimalPlaces = 4,
            Version = 1,
            IsActive = true
        };

        _inactiveSkuUnit = new SkuUnit
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SkuId = skuId,
            UnitId = inactiveUnit.Id,
            Unit = inactiveUnit,
            FactorToBase = 50m,
            CanPurchase = true,
            CanSell = true,
            QuantityDecimalPlaces = 0,
            Version = 1,
            IsActive = false
        };

        _allSkuUnits = new List<SkuUnit> { _kgSkuUnit, _baoSkuUnit, _tanSkuUnit, _inactiveSkuUnit };
    }

    [Fact]
    public void AC08_NullUnitId_ResolvesToBaseUnit()
    {
        // Act
        var resolved = SkuUnitResolver.ResolveForSale(_sampleSku, _allSkuUnits, null);

        // Assert
        resolved.Should().NotBeNull();
        resolved.IsBase.Should().BeTrue();
        resolved.UnitId.Should().Be(_kgUnit.Id);
        resolved.UnitName.Should().Be("Kilôgam");
        resolved.Factor.Should().Be(1m);
        resolved.DecimalPlaces.Should().Be(3);
        resolved.SalePrice.Should().Be(20000m);
    }

    [Fact]
    public void AC08_UnactivatedUnit_Throws_UNIT_NOT_ALLOWED()
    {
        // Act
        var actInactive = () => SkuUnitResolver.ResolveForSale(_sampleSku, _allSkuUnits, _inactiveSkuUnit.UnitId);
        var actNotFound = () => SkuUnitResolver.ResolveForSale(_sampleSku, _allSkuUnits, Guid.NewGuid());

        // Assert
        var exInactive = actInactive.Should().Throw<OismException>().Which;
        exInactive.ErrorCode.Should().Be("UNIT_NOT_ALLOWED");
        exInactive.StatusCode.Should().Be(422);

        var exNotFound = actNotFound.Should().Throw<OismException>().Which;
        exNotFound.ErrorCode.Should().Be("UNIT_NOT_ALLOWED");
        exNotFound.StatusCode.Should().Be(422);
    }

    [Fact]
    public void AC08_UnpermittedSaleOrPurchase_Throws_UNIT_NOT_ALLOWED()
    {
        // Arrange
        var purchaseOnlyUnit = new SkuUnit
        {
            Id = Guid.NewGuid(),
            TenantId = _sampleSku.TenantId,
            SkuId = _sampleSku.Id,
            UnitId = Guid.NewGuid(),
            FactorToBase = 10m,
            CanPurchase = true,
            CanSell = false,
            IsActive = true,
            QuantityDecimalPlaces = 1
        };
        var units = new List<SkuUnit>(_allSkuUnits) { purchaseOnlyUnit };

        // Act & Assert
        var actSale = () => SkuUnitResolver.ResolveForSale(_sampleSku, units, purchaseOnlyUnit.UnitId);
        var exSale = actSale.Should().Throw<OismException>().Which;
        exSale.ErrorCode.Should().Be("UNIT_NOT_ALLOWED");
        exSale.StatusCode.Should().Be(422);

        var actPurchase = () => SkuUnitResolver.ResolveForPurchase(_sampleSku, units, purchaseOnlyUnit.UnitId);
        actPurchase.Should().NotThrow();
    }

    [Fact]
    public void AC08_ValidQuantity_2_Point_5_Kg_Succeeds()
    {
        // Arrange
        var resolved = SkuUnitResolver.ResolveForSale(_sampleSku, _allSkuUnits, _kgUnit.Id);

        // Act
        var baseQuantity = SkuUnitResolver.ComputeBaseQuantity(2.5m, resolved);

        // Assert
        baseQuantity.Should().Be(2.5m);
    }

    [Fact]
    public void AC08_FractionalQuantity_1_Point_5_Bao_Throws_INVALID_UNIT_QUANTITY()
    {
        // Arrange (Bao có 0 chữ số lẻ)
        var resolved = SkuUnitResolver.ResolveForSale(_sampleSku, _allSkuUnits, _baoUnit.Id);

        // Act & Assert
        var act = () => SkuUnitResolver.ComputeBaseQuantity(1.5m, resolved);
        var ex = act.Should().Throw<OismException>().Which;
        ex.ErrorCode.Should().Be("INVALID_UNIT_QUANTITY");
        ex.StatusCode.Should().Be(400);
    }

    [Fact]
    public void AC08_SmallFraction_0_0001_Tan_Multiplied_By_1000_Equals_0_1_Succeeds()
    {
        // Arrange (Tấn hệ số 1000, 4 số lẻ)
        var resolved = SkuUnitResolver.ResolveForSale(_sampleSku, _allSkuUnits, _tanUnit.Id);

        // Act
        var baseQuantity = SkuUnitResolver.ComputeBaseQuantity(0.0001m, resolved);

        // Assert (0.0001 * 1000 = 0.1 kg)
        baseQuantity.Should().Be(0.1m);
    }

    [Fact]
    public void AC08_BaseQuantity_Exceeding_3_Decimals_Rejected_Without_Rounding()
    {
        // Arrange: đơn vị có factor = 1.0001m, decimalPlaces = 1
        var oddUnit = new SkuUnit
        {
            Id = Guid.NewGuid(),
            TenantId = _sampleSku.TenantId,
            SkuId = _sampleSku.Id,
            UnitId = Guid.NewGuid(),
            FactorToBase = 1.0001m,
            QuantityDecimalPlaces = 1,
            CanPurchase = true,
            CanSell = true,
            IsActive = true
        };
        var resolved = new ResolvedSkuUnit(
            UnitId: oddUnit.UnitId,
            UnitName: "Odd",
            Factor: oddUnit.FactorToBase,
            DecimalPlaces: oddUnit.QuantityDecimalPlaces,
            SalePrice: null,
            IsBase: false,
            Version: 1);

        // Act: 1.1 * 1.0001 = 1.10011 (5 chữ số lẻ > 3)
        var act = () => SkuUnitResolver.ComputeBaseQuantity(1.1m, resolved);

        // Assert
        var ex = act.Should().Throw<OismException>().Which;
        ex.ErrorCode.Should().Be("INVALID_UNIT_QUANTITY");
        ex.StatusCode.Should().Be(400);
    }

    [Fact]
    public void AC08_Quantity_Zero_Or_Negative_Throws_INVALID_UNIT_QUANTITY()
    {
        var resolved = SkuUnitResolver.ResolveForSale(_sampleSku, _allSkuUnits, null);

        var actZero = () => SkuUnitResolver.ComputeBaseQuantity(0m, resolved);
        actZero.Should().Throw<OismException>().Which.ErrorCode.Should().Be("INVALID_UNIT_QUANTITY");

        var actNeg = () => SkuUnitResolver.ComputeBaseQuantity(-1m, resolved);
        actNeg.Should().Throw<OismException>().Which.ErrorCode.Should().Be("INVALID_UNIT_QUANTITY");
    }

    [Fact]
    public void F05_WhenPackagingUnitWithFactor1PrecedesKg_ResolvesToKgBaseUnit()
    {
        // Arrange: đơn vị "goi" (Gói, Factor = 1, DecimalPlaces = 0) đứng trước "kg" (Factor = 1, DecimalPlaces = 3)
        var goiUnit = new Unit
        {
            Id = Guid.NewGuid(),
            TenantId = _sampleSku.TenantId,
            Code = "goi",
            Name = "Gói",
            Symbol = "gói",
            MeasureGroup = MeasureGroups.Packaging,
            DecimalPlaces = 0,
            IsActive = true
        };
        var goiSkuUnit = new SkuUnit
        {
            Id = Guid.NewGuid(),
            TenantId = _sampleSku.TenantId,
            SkuId = _sampleSku.Id,
            UnitId = goiUnit.Id,
            Unit = goiUnit,
            FactorToBase = 1m,
            CanPurchase = true,
            CanSell = true,
            QuantityDecimalPlaces = 0,
            Version = 1,
            IsActive = true
        };

        // Danh sách với goiSkuUnit đứng trước kgSkuUnit
        var unitsWithGoiFirst = new List<SkuUnit> { goiSkuUnit, _kgSkuUnit, _baoSkuUnit, _tanSkuUnit };

        // Act - null unitId phải resolve về BaseUnitId (_kgUnit.Id), không phải gói
        var resolvedSale = SkuUnitResolver.ResolveForSale(_sampleSku, unitsWithGoiFirst, null);
        var resolvedPurchase = SkuUnitResolver.ResolveForPurchase(_sampleSku, unitsWithGoiFirst, null);

        // Assert
        resolvedSale.UnitId.Should().Be(_kgUnit.Id);
        resolvedSale.UnitName.Should().Be("Kilôgam");
        resolvedSale.DecimalPlaces.Should().Be(3);
        resolvedSale.Factor.Should().Be(1m);
        resolvedSale.IsBase.Should().BeTrue();

        resolvedPurchase.UnitId.Should().Be(_kgUnit.Id);
        resolvedPurchase.UnitName.Should().Be("Kilôgam");
        resolvedPurchase.DecimalPlaces.Should().Be(3);
        resolvedPurchase.Factor.Should().Be(1m);
        resolvedPurchase.IsBase.Should().BeTrue();
    }

    [Fact]
    public void Review_CoBaseUnitIdNhungThieuDongGoc_KhongLayDonViHeSo1Khac()
    {
        var baseUnitId = Guid.NewGuid();
        var sku = new ProductVariant { BaseUnitId = baseUnitId, Unit = "kg", RetailPrice = 1000m };
        var units = new List<SkuUnit>
        {
            new()
            {
                UnitId = Guid.NewGuid(),
                FactorToBase = 1m,
                QuantityDecimalPlaces = 0,
                IsActive = true,
                CanSell = true,
                Unit = new Unit { Code = "goi", Name = "Gói" }
            }
        };

        var resolved = SkuUnitResolver.ResolveForSale(sku, units, null);

        resolved.UnitId.Should().Be(baseUnitId);
        resolved.UnitName.Should().Be("kg");
        resolved.DecimalPlaces.Should().Be(3);
    }
}
