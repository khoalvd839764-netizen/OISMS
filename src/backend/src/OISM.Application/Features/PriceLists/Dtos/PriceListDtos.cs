using System.ComponentModel.DataAnnotations;

namespace OISM.Application.Features.PriceLists.Dtos;

public class PriceListDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Channel { get; set; } = "Retail"; // Retail, Wholesale, Shopee, TikTok, Lazada
    public bool IsActive { get; set; } = true;
    public int VersionsCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CreatePriceListDto
{
    [Required(ErrorMessage = "Mã bảng giá không được để trống.")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Mã bảng giá từ 2 đến 50 ký tự.")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên bảng giá không được để trống.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Tên bảng giá từ 2 đến 100 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Description { get; set; }

    public string Channel { get; set; } = "Retail";
}

public class PriceVersionItemDto
{
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

public class PriceVersionDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PriceListId { get; set; }
    public string VersionName { get; set; } = string.Empty;
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public string Status { get; set; } = "Active"; // Active, Draft, Expired
    public List<PriceVersionItemDto> Items { get; set; } = new();
}

public class CreatePriceVersionDto
{
    [Required(ErrorMessage = "Tên phiên bản giá không được để trống.")]
    public string VersionName { get; set; } = string.Empty;

    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;
    public DateTime? EffectiveTo { get; set; }

    public List<PriceVersionItemDto> Items { get; set; } = new();
}
