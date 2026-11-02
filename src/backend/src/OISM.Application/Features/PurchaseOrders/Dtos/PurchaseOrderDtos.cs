using System.ComponentModel.DataAnnotations;

namespace OISM.Application.Features.PurchaseOrders.Dtos;

public class PurchaseOrderItemDto
{
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public string SkuName { get; set; } = string.Empty;
    public decimal QuantityOrdered { get; set; }
    public decimal QuantityReceived { get; set; } = 0;
    public decimal UnitCost { get; set; }
    public decimal Subtotal => QuantityOrdered * UnitCost;
}

public class CreatePurchaseOrderItemDto
{
    [Required]
    public Guid SkuId { get; set; }

    [Range(0.001, 1000000, ErrorMessage = "Số lượng đặt phải lớn hơn 0.")]
    public decimal Quantity { get; set; }

    [Range(0, 1000000000, ErrorMessage = "Đơn giá không được âm.")]
    public decimal UnitCost { get; set; }
}

public class PurchaseOrderDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string PoNumber { get; set; } = string.Empty;
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft"; // Draft, Ordered, PartiallyReceived, Completed, Cancelled
    public decimal TotalAmount { get; set; }
    public string? Note { get; set; }
    public DateTime OrderedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpectedDeliveryDate { get; set; }
    public List<PurchaseOrderItemDto> Items { get; set; } = new();
}

public class CreatePurchaseOrderDto
{
    [Required]
    public Guid SupplierId { get; set; }

    [Required]
    public Guid BranchId { get; set; }

    public DateTime? ExpectedDeliveryDate { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    [MinLength(1, ErrorMessage = "Phiếu đặt mua phải có ít nhất 1 sản phẩm.")]
    public List<CreatePurchaseOrderItemDto> Items { get; set; } = new();
}

public class UpdatePurchaseOrderStatusDto
{
    [Required]
    public string Status { get; set; } = "Ordered"; // Draft, Ordered, Completed, Cancelled

    public string? Note { get; set; }
}
