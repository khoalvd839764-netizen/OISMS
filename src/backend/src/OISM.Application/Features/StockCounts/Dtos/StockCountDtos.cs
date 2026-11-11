using OISM.Domain.Common;
using System.ComponentModel.DataAnnotations;
using OISM.Application.Common.Attributes;
using OISM.Application.Features.Orders.Dtos;

namespace OISM.Application.Features.StockCounts.Dtos;

public class CreateStockCountRequestDto
{
    [Required(ErrorMessage = "Khóa RequestKey là bắt buộc.")]
    [NotEmptyGuid(ErrorMessage = "Khóa RequestKey không được rỗng (Guid.Empty).")]
    public Guid RequestKey { get; set; }

    [Required(ErrorMessage = "Chi nhánh kiểm kê là bắt buộc.")]
    [NotEmptyGuid(ErrorMessage = "Chi nhánh kiểm kê không được rỗng (Guid.Empty).")]
    public Guid BranchId { get; set; }

    [Required(ErrorMessage = "Kho kiểm kê là bắt buộc.")]
    [NotEmptyGuid(ErrorMessage = "Kho kiểm kê không được rỗng (Guid.Empty).")]
    public Guid WarehouseId { get; set; }

    /// <summary>
    /// Danh sách SKU cần kiểm kê. Nếu null hoặc rỗng, hệ thống sẽ snapshot toàn bộ SKU đang có trong kho.
    /// </summary>
    public List<Guid>? SkuIds { get; set; }

    /// <summary>
    /// Lý do kiểm kê đột xuất (bắt buộc khi tạo phiếu kiểm kê trực tiếp/đột xuất).
    /// </summary>
    public string? Reason { get; set; }

    public string? Note { get; set; }
}

public class RecordStockCountItemRequestDto
{
    [Required(ErrorMessage = "Mã SKU là bắt buộc.")]
    [NotEmptyGuid(ErrorMessage = "Mã SKU không được rỗng (Guid.Empty).")]
    public Guid SkuId { get; set; }

    [Required(ErrorMessage = "Số lượng đếm thực tế là bắt buộc.")]
    [Range(0, 999999999999999.999, ErrorMessage = "Số lượng đếm thực tế phải từ 0 đến 999,999,999,999,999.999.")]
    public decimal? CountedQuantity { get; set; }

    /// <summary>
    /// Đơn giá điều chỉnh bắt buộc khi phát hiện kiểm kê thừa (CountedQuantity > ExpectedQuantity) đối với SKU chưa từng có giá vốn WAC trong kho (WAC = null hoặc 0).
    /// Tuyệt đối không cho phép tự động gán 0; bắt buộc nhập đơn giá dương để tính toán giá vốn hợp lệ.
    /// Độ chính xác numeric(20,6).
    /// </summary>
    [Range(0.000001, 999999999999.999999, ErrorMessage = "Đơn giá điều chỉnh phải lớn hơn 0.")]
    public decimal? AdjustmentUnitCost { get; set; }
}

public class RecordStockCountBatchRequestDto
{
    [Required(ErrorMessage = "Danh sách đếm không được rỗng.")]
    [MinLength(1, ErrorMessage = "Cần ít nhất 1 dòng kiểm đếm.")]
    public List<RecordStockCountItemRequestDto> Items { get; set; } = new();
}

public class ConfirmStockCountRequestDto
{
    public string? Note { get; set; }

    /// <summary>
    /// Bản đồ ánh xạ SkuId -> AdjustmentUnitCost bổ sung nếu dòng kiểm kê thừa chưa có đơn giá lúc nhập phiếu đếm.
    /// </summary>
    public Dictionary<Guid, decimal>? SkuAdjustmentCosts { get; set; }
}

public class CancelStockCountRequestDto
{
    public string? Reason { get; set; }
}

public class StockCountItemResponseDto
{
    public Guid Id { get; set; }
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal ExpectedQuantity { get; set; }
    public decimal? CountedQuantity { get; set; }
    public decimal? DifferenceQuantity => CountedQuantity.HasValue ? CountedQuantity.Value - ExpectedQuantity : null;
    public long BalanceSequenceAtSnapshot { get; set; }
    public decimal? AdjustmentUnitCost { get; set; }
    public Guid? CountedBy { get; set; }
    public DateTimeOffset? CountedAt { get; set; }
    public DateTimeOffset? SnapshotAt { get; set; }
    public bool IsDrifted { get; set; }
}

public class StockCountResponseDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public Guid? RequestKey { get; set; }
    public Guid BranchId { get; set; }
    public string? BranchName { get; set; }
    public Guid WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public string Status { get; set; } = StockCountStatus.Draft; // 'Draft', 'Counting', 'PendingApproval', 'Confirmed', 'Cancelled'
    public string CountType { get; set; } = "AdHoc";
    public string? AdHocReason { get; set; }
    public Guid? PeriodId { get; set; }
    public Guid? SubmittedBy { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? ConfirmedBy { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Guid? GeneratedAdjustmentId { get; set; }
    public bool IsReplay { get; set; }
    public List<StockCountItemResponseDto> Items { get; set; } = new();
    public List<StockCountApprovalResponseDto>? Approvals { get; set; }
}

public class StockCountFilterRequestDto
{
    public Guid? BranchId { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? Status { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class SubmitStockCountRequestDto
{
    public string? Note { get; set; }
}

public class RecountDriftedRequestDto
{
    [Required(ErrorMessage = "Danh sách SKU kiểm đếm lại là bắt buộc.")]
    [MinLength(1, ErrorMessage = "Cần ít nhất 1 SKU để kiểm đếm lại.")]
    public List<Guid> SkuIds { get; set; } = new();
}

public class RecountStockCountRequestDto : RecountDriftedRequestDto
{
}

public class RequestRecountRequestDto
{
    [Required(ErrorMessage = "Danh sách SKU yêu cầu kiểm đếm lại là bắt buộc.")]
    [MinLength(1, ErrorMessage = "Cần ít nhất 1 SKU để yêu cầu kiểm đếm lại.")]
    public List<Guid> SkuIds { get; set; } = new();

    [Required(ErrorMessage = "Lý do yêu cầu kiểm đếm lại là bắt buộc.")]
    public string Reason { get; set; } = string.Empty;
}

public class StockCountDifferenceEntry
{
    public Guid SkuId { get; set; }
    public string SkuCode { get; set; } = string.Empty;
    public decimal ExpectedQuantity { get; set; }
    public decimal? CountedQuantity { get; set; }
    public decimal? Delta => CountedQuantity.HasValue ? CountedQuantity.Value - ExpectedQuantity : null;
}

public class StockCountApprovalResponseDto
{
    public Guid Id { get; set; }
    public Guid StockCountId { get; set; }
    public string Action { get; set; } = string.Empty;
    public Guid ActorUserId { get; set; }
    public string? ActorName { get; set; }
    public string? Reason { get; set; }
    public List<StockCountDifferenceEntry> Differences { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
}
