using System.ComponentModel.DataAnnotations;

namespace OISM.Application.Features.Inventory.Dtos;

public class CreateGoodsReceiptDto
{
    [Required(ErrorMessage = "Chi nhánh là bắt buộc.")]
    public Guid BranchId { get; set; }

    [Required(ErrorMessage = "Kho nhập là bắt buộc.")]
    public Guid WarehouseId { get; set; }

    [Required(ErrorMessage = "Nhà cung cấp là bắt buộc.")]
    public Guid SupplierId { get; set; }

    [MaxLength(50)]
    public string? Number { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }

    [Required(ErrorMessage = "Phiếu nhập phải có ít nhất một dòng mặt hàng.")]
    [MinLength(1, ErrorMessage = "Phiếu nhập phải có ít nhất một dòng mặt hàng.")]
    public List<CreateGoodsReceiptItemDto> Items { get; set; } = new();
}

public class CreateGoodsReceiptItemDto
{
    [Required(ErrorMessage = "Mặt hàng SKU là bắt buộc.")]
    public Guid SkuId { get; set; }

    [Range(0.001, 100000000, ErrorMessage = "Số lượng nhập phải lớn hơn 0.")]
    public decimal Quantity { get; set; }

    [Range(0, 1000000000000, ErrorMessage = "Đơn giá nhập không được âm.")]
    public decimal UnitPrice { get; set; }
}

public class GoodsReceiptResponseDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }
    public decimal TotalAmount { get; set; }
    public Guid CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public Guid? ConfirmedBy { get; set; }
    public string? ConfirmedByName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public List<GoodsReceiptItemResponseDto> Items { get; set; } = new();
}

public class GoodsReceiptItemResponseDto
{
    public Guid Id { get; set; }
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal => Quantity * UnitPrice;
}

public class GoodsReceiptFilterDto
{
    public Guid? BranchId { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? SupplierId { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? FromDate { get; set; }
    public DateTimeOffset? ToDate { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int PageIndex { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
}
