using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using OISM.Application.Common.Attributes;
using OISM.Application.Features.Orders.Dtos;

namespace OISM.Application.Features.StockTransfers.Dtos;

public class StockTransferItemRequestDto
{
    [Required(ErrorMessage = "Mã biến thể SKU là bắt buộc.")]
    [NotEmptyGuid(ErrorMessage = "Mã biến thể SKU không được rỗng (Guid.Empty).")]
    public Guid SkuId { get; set; }

    [Range(typeof(decimal), "0.001", "999999999999999.999", ErrorMessage = "Số lượng chuyển phải lớn hơn 0 và không vượt quá giới hạn tối đa.")]
    public decimal Quantity { get; set; }
}

public class CreateStockTransferRequestDto
{
    [Required(ErrorMessage = "RequestKey là bắt buộc.")]
    [NotEmptyGuid(ErrorMessage = "RequestKey không được rỗng (Guid.Empty).")]
    public Guid RequestKey { get; set; }

    [Required(ErrorMessage = "Chi nhánh xuất là bắt buộc.")]
    [NotEmptyGuid(ErrorMessage = "Chi nhánh xuất không được rỗng (Guid.Empty).")]
    public Guid FromBranchId { get; set; }

    [Required(ErrorMessage = "Chi nhánh nhận là bắt buộc.")]
    [NotEmptyGuid(ErrorMessage = "Chi nhánh nhận không được rỗng (Guid.Empty).")]
    public Guid ToBranchId { get; set; }

    [Required(ErrorMessage = "Kho xuất là bắt buộc.")]
    [NotEmptyGuid(ErrorMessage = "Kho xuất không được rỗng (Guid.Empty).")]
    public Guid FromWarehouseId { get; set; }

    [Required(ErrorMessage = "Kho nhận là bắt buộc.")]
    [NotEmptyGuid(ErrorMessage = "Kho nhận không được rỗng (Guid.Empty).")]
    public Guid ToWarehouseId { get; set; }

    public string? Note { get; set; }

    [Required(ErrorMessage = "Danh sách hàng chuyển không được rỗng.")]
    [MinLength(1, ErrorMessage = "Phiếu chuyển phải có ít nhất 1 sản phẩm.")]
    public List<StockTransferItemRequestDto> Items { get; set; } = new();
}

public class DispatchStockTransferRequestDto
{
    public string? Note { get; set; }
}

public class ReceiveStockTransferRequestDto
{
    public string? Note { get; set; }
}

public class CancelStockTransferRequestDto
{
    public string? Reason { get; set; }
}

public class StockTransferItemResponseDto
{
    public Guid Id { get; set; }
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal? UnitCost { get; set; } // Giá vốn WAC tại thời điểm xuất gửi
}

public class StockTransferResponseDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public Guid? RequestKey { get; set; }
    public Guid FromBranchId { get; set; }
    public string? FromBranchName { get; set; }
    public Guid ToBranchId { get; set; }
    public string? ToBranchName { get; set; }
    public Guid FromWarehouseId { get; set; }
    public string? FromWarehouseName { get; set; }
    public Guid ToWarehouseId { get; set; }
    public string? ToWarehouseName { get; set; }
    public string Status { get; set; } = "Draft"; // 'Draft', 'InTransit', 'Received', 'Cancelled'
    public Guid CreatedBy { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public Guid? ReceivedBy { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<StockTransferItemResponseDto> Items { get; set; } = new();

    [JsonIgnore]
    public bool IsReplay { get; set; }
}

public class StockTransferFilterRequestDto
{
    public Guid? BranchId { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? Status { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
