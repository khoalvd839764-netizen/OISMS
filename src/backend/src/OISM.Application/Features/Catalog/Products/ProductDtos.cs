using FluentValidation;
using OISM.Application.Common.Helpers;

namespace OISM.Application.Features.Catalog.Products;

public class CreateProductRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? BrandId { get; set; }
    public List<CreateVariantRequest> Variants { get; set; } = new();
}

public class CreateVariantRequest
{
    public string SKU { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public decimal RetailPrice { get; set; }
    public decimal WholesalePrice { get; set; }
    public string? Color { get; set; }
    public string? Size { get; set; }
}

public class UpdateProductRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? BrandId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ProductResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public Guid? BrandId { get; set; }
    public string? BrandName { get; set; }
    public bool IsActive { get; set; }
    public List<ProductVariantResponse> Variants { get; set; } = new();
}

public class ProductVariantResponse
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string SKU { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public decimal RetailPrice { get; set; }
    public decimal WholesalePrice { get; set; }
    public decimal CostPrice { get; set; }
    public string? Color { get; set; }
    public string? Size { get; set; }
    public bool IsActive { get; set; }
}

public class CreateProductValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên sản phẩm không được để trống.")
            .MaximumLength(200).WithMessage("Tên sản phẩm không được quá 200 ký tự.");

        RuleForEach(x => x.Variants).SetValidator(new CreateVariantValidator());
    }
}

public class CreateVariantValidator : AbstractValidator<CreateVariantRequest>
{
    public CreateVariantValidator()
    {
        RuleFor(x => x.SKU)
            .NotEmpty().WithMessage("Mã SKU không được để trống.")
            .MaximumLength(50).WithMessage("Mã SKU không được quá 50 ký tự.")
            .Matches("^[a-zA-Z0-9_-]+$").WithMessage("Mã SKU chỉ được chứa ký tự chữ, số, gạch nối hoặc gạch dưới.");

        RuleFor(x => x.VariantName)
            .NotEmpty().WithMessage("Tên biến thể không được để trống.");

        RuleFor(x => x.RetailPrice)
            .GreaterThanOrEqualTo(0).WithMessage("Giá bán lẻ không được là số âm.");

        RuleFor(x => x.WholesalePrice)
            .GreaterThanOrEqualTo(0).WithMessage("Giá bán buôn không được là số âm.");

        RuleFor(x => x.Barcode)
            .Must(barcode => string.IsNullOrEmpty(barcode) || BarcodeHelper.IsValidEan13(barcode) || barcode.Length >= 4)
            .WithMessage("Mã vạch không đúng định dạng chuẩn EAN-13 (13 chữ số) hoặc Code128.");
    }
}
