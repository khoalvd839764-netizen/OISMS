using FluentValidation;

namespace OISM.Application.Features.Warehouses;

public class CreateWarehouseRequest
{
    public Guid BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class UpdateWarehouseRequest
{
    public Guid BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class CreateWarehouseValidator : AbstractValidator<CreateWarehouseRequest>
{
    public CreateWarehouseValidator()
    {
        RuleFor(x => x.BranchId)
            .NotEmpty().WithMessage("Chi nhánh (BranchId) không được để trống.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên kho hàng không được để trống.")
            .MaximumLength(200).WithMessage("Tên kho hàng không được vượt quá 200 ký tự.");
    }
}

public class UpdateWarehouseValidator : AbstractValidator<UpdateWarehouseRequest>
{
    public UpdateWarehouseValidator()
    {
        RuleFor(x => x.BranchId)
            .NotEmpty().WithMessage("Chi nhánh (BranchId) không được để trống.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên kho hàng không được để trống.")
            .MaximumLength(200).WithMessage("Tên kho hàng không được vượt quá 200 ký tự.");
    }
}

public class WarehouseResponse
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
