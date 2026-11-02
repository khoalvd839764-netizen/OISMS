using System.ComponentModel.DataAnnotations;

namespace OISM.Application.Features.Units.Dtos;

public class UnitDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CreateUnitDto
{
    [Required(ErrorMessage = "Tên đơn vị tính không được để trống.")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "Tên đơn vị tính từ 1 đến 50 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(200, ErrorMessage = "Mô tả không vượt quá 200 ký tự.")]
    public string? Description { get; set; }
}

public class SkuUnitDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SkuId { get; set; }
    public Guid UnitId { get; set; }
    public string UnitName { get; set; } = string.Empty;

    /// <summary>
    /// Hệ số quy đổi so với đơn vị gốc (BaseUnit). Ví dụ: 1 Thùng = 24 Lon (ConversionRate = 24).
    /// </summary>
    public decimal ConversionRate { get; set; } = 1;

    public string? Barcode { get; set; }
    public bool IsBaseUnit { get; set; } = false;
    public decimal? CostPrice { get; set; }
    public decimal? SellingPrice { get; set; }
}

public class CreateSkuUnitDto
{
    [Required]
    public Guid UnitId { get; set; }

    [Range(0.001, 1000000, ErrorMessage = "Hệ số quy đổi phải lớn hơn 0.")]
    public decimal ConversionRate { get; set; } = 1;

    public string? Barcode { get; set; }
    public bool IsBaseUnit { get; set; } = false;
    public decimal? CostPrice { get; set; }
    public decimal? SellingPrice { get; set; }
}
