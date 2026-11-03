using FluentValidation;
using OISM.Domain.Entities;

namespace OISM.Application.Features.Catalog.Units;

public static class DefaultUnits
{
    public static readonly (string Code, string Name, string Symbol, string MeasureGroup, short DecimalPlaces)[] Items =
    [
        ("kg", "Kilôgam", "kg", MeasureGroups.Mass, 3),
        ("g", "Gam", "g", MeasureGroups.Mass, 3),
        ("tan", "Tấn", "tấn", MeasureGroups.Mass, 3),
        ("lit", "Lít", "l", MeasureGroups.Volume, 3),
        ("ml", "Mililít", "ml", MeasureGroups.Volume, 3),
        ("m", "Mét", "m", MeasureGroups.Length, 3),
        ("piece", "Cái", "cái", MeasureGroups.Count, 3),
        ("bao", "Bao", "bao", MeasureGroups.Packaging, 0),
        ("thung", "Thùng", "thùng", MeasureGroups.Packaging, 0),
        ("hop", "Hộp", "hộp", MeasureGroups.Packaging, 0),
        ("chai", "Chai", "chai", MeasureGroups.Packaging, 0)
    ];
}

public class CreateUnitRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string MeasureGroup { get; set; } = MeasureGroups.Count;
    public short DecimalPlaces { get; set; } = 3;
}

public class UpdateUnitRequest
{
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? Symbol { get; set; }
    public string? MeasureGroup { get; set; }
    public short? DecimalPlaces { get; set; }
    public bool? IsActive { get; set; }
}

public class UnitResponse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string MeasureGroup { get; set; } = string.Empty;
    public short DecimalPlaces { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class CreateSkuUnitRequest
{
    public Guid UnitId { get; set; }
    public decimal FactorToBase { get; set; } = 1m;
    public bool CanPurchase { get; set; } = true;
    public bool CanSell { get; set; } = true;
    public bool IsDefaultPurchase { get; set; } = false;
    public bool IsDefaultSale { get; set; } = false;
    public string? Barcode { get; set; }
    public short QuantityDecimalPlaces { get; set; } = 3;
    public decimal? SalePrice { get; set; }
}

public class UpdateSkuUnitRequest
{
    public decimal? FactorToBase { get; set; }
    public bool? CanPurchase { get; set; }
    public bool? CanSell { get; set; }
    public bool? IsDefaultPurchase { get; set; }
    public bool? IsDefaultSale { get; set; }
    public string? Barcode { get; set; }
    public short? QuantityDecimalPlaces { get; set; }
    public decimal? SalePrice { get; set; }
    public bool? IsActive { get; set; }
}

public class SkuUnitResponse
{
    public Guid Id { get; set; }
    public Guid SkuId { get; set; }
    public Guid UnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string UnitName { get; set; } = string.Empty;
    public string UnitSymbol { get; set; } = string.Empty;
    public string MeasureGroup { get; set; } = string.Empty;
    public decimal FactorToBase { get; set; }
    public bool CanPurchase { get; set; }
    public bool CanSell { get; set; }
    public bool IsDefaultPurchase { get; set; }
    public bool IsDefaultSale { get; set; }
    public string? Barcode { get; set; }
    public short QuantityDecimalPlaces { get; set; }
    public decimal? SalePrice { get; set; }
    public int Version { get; set; }
    public bool IsActive { get; set; }
    public bool IsBase { get; set; }
}

public class SkuUnitFactorHistoryResponse
{
    public Guid Id { get; set; }
    public Guid SkuUnitId { get; set; }
    public decimal? OldFactor { get; set; }
    public decimal NewFactor { get; set; }
    public int? OldVersion { get; set; }
    public int NewVersion { get; set; }
    public Guid? ChangedBy { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
}

public class CreateUnitValidator : AbstractValidator<CreateUnitRequest>
{
    public CreateUnitValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Mã đơn vị không được để trống.")
            .MaximumLength(30).WithMessage("Mã đơn vị không quá 30 ký tự.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên đơn vị không được để trống.")
            .MaximumLength(100).WithMessage("Tên đơn vị không quá 100 ký tự.");

        RuleFor(x => x.Symbol)
            .NotEmpty().WithMessage("Ký hiệu đơn vị không được để trống.")
            .MaximumLength(20).WithMessage("Ký hiệu đơn vị không quá 20 ký tự.");

        RuleFor(x => x.MeasureGroup)
            .Must(MeasureGroups.IsValid)
            .WithMessage("Nhóm đo lường không hợp lệ. Cho phép: Mass, Volume, Length, Count, Packaging.");

        RuleFor(x => x.DecimalPlaces)
            .InclusiveBetween((short)0, (short)3)
            .WithMessage("Số chữ số thập phân cho phép từ 0 đến 3.");
    }
}
