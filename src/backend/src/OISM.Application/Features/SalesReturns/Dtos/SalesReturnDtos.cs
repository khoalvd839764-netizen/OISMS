using System.ComponentModel.DataAnnotations;

namespace OISM.Application.Features.SalesReturns.Dtos;

public class ReturnableItemDto
{
    public Guid OrderItemId { get; set; }
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public string SkuName { get; set; } = string.Empty;
    public Guid? UnitId { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal FactorSnapshot { get; set; }
    public decimal SoldQuantity { get; set; }
    public decimal ReturnedQuantity { get; set; }
    public decimal ReturnableQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal OriginalUnitCost { get; set; }
}

public class CreateSalesReturnDto
{
    public Guid? RequestKey { get; set; }

    [Required(ErrorMessage = "Mã đơn hàng gốc là bắt buộc.")]
    public Guid OrderId { get; set; }

    [Required(ErrorMessage = "Kho nhận là bắt buộc.")]
    public Guid WarehouseId { get; set; }

    [Required(ErrorMessage = "Lý do trả hàng là bắt buộc.")]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Note { get; set; }

    [Required(ErrorMessage = "Phiếu trả hàng phải có ít nhất một dòng mặt hàng.")]
    [MinLength(1, ErrorMessage = "Phiếu trả hàng phải có ít nhất một dòng mặt hàng.")]
    public List<CreateSalesReturnItemDto> Items { get; set; } = new();
}

public class CreateSalesReturnItemDto
{
    [Required(ErrorMessage = "Mã dòng đơn hàng là bắt buộc.")]
    public Guid OrderItemId { get; set; }

    [Range(0.001, 100000000, ErrorMessage = "Số lượng trả phải lớn hơn 0.")]
    public decimal Quantity { get; set; }

    public decimal? RefundAmount { get; set; }
}

public class ConfirmSalesReturnRequestDto
{
    public string? RefundMethod { get; set; }
}

public class SalesReturnResponseDto
{
    public Guid Id { get; set; }
    public Guid RequestKey { get; set; }
    public string Number { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string OrderChannel { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? Note { get; set; }
    public decimal TotalRefundAmount { get; set; }
    public string? RefundMethod { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public Guid? ConfirmedBy { get; set; }
    public string? ConfirmedByName { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public List<SalesReturnItemResponseDto> Items { get; set; } = new();
}

public class SalesReturnItemResponseDto
{
    public Guid Id { get; set; }
    public Guid SalesReturnId { get; set; }
    public Guid OrderId { get; set; }
    public Guid OrderItemId { get; set; }
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public string SkuName { get; set; } = string.Empty;
    public Guid? UnitId { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal FactorSnapshot { get; set; }
    public decimal Quantity { get; set; }
    public decimal BaseQuantity { get; set; }
    public decimal RefundAmount { get; set; }
    public decimal OriginalUnitCost { get; set; }
}

public class SalesReturnFilterDto
{
    public Guid? BranchId { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? OrderId { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? FromDate { get; set; }
    public DateTimeOffset? ToDate { get; set; }
    public string? SearchTerm { get; set; }
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
