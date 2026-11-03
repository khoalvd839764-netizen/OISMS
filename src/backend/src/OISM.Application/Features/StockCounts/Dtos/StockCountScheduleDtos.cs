using System.ComponentModel.DataAnnotations;
using OISM.Application.Common.Attributes;

namespace OISM.Application.Features.StockCounts.Dtos;

public class CreateStockCountScheduleRequestDto
{
    [Required(ErrorMessage = "Chi nhánh kiểm kê là bắt buộc.")]
    [NotEmptyGuid(ErrorMessage = "Chi nhánh kiểm kê không được rỗng (Guid.Empty).")]
    public Guid BranchId { get; set; }

    [Required(ErrorMessage = "Kho kiểm kê là bắt buộc.")]
    [NotEmptyGuid(ErrorMessage = "Kho kiểm kê không được rỗng (Guid.Empty).")]
    public Guid WarehouseId { get; set; }

    [Required(ErrorMessage = "Loại kỳ kiểm kê là bắt buộc.")]
    [RegularExpression("^(MidMonth|EndOfMonth)$", ErrorMessage = "Loại kỳ chỉ chấp nhận 'MidMonth' hoặc 'EndOfMonth'.")]
    public string PeriodType { get; set; } = string.Empty;

    [Range(1, 28, ErrorMessage = "Ngày kiểm kê giữa tháng phải từ 1 đến 28.")]
    public short? DayOfMonth { get; set; }

    public TimeOnly? LocalTime { get; set; }

    [Range(1, 14, ErrorMessage = "Hạn hoàn tất kiểm kê phải từ 1 đến 14 ngày.")]
    public short? DueDays { get; set; }

    [Required(ErrorMessage = "Người phụ trách là bắt buộc.")]
    [NotEmptyGuid(ErrorMessage = "Người phụ trách không được rỗng (Guid.Empty).")]
    public Guid AssigneeUserId { get; set; }

    public bool? IsActive { get; set; } = true;
}

public class UpdateStockCountScheduleRequestDto
{
    [Range(1, 28, ErrorMessage = "Ngày kiểm kê giữa tháng phải từ 1 đến 28.")]
    public short? DayOfMonth { get; set; }

    public TimeOnly? LocalTime { get; set; }

    [Range(1, 14, ErrorMessage = "Hạn hoàn tất kiểm kê phải từ 1 đến 14 ngày.")]
    public short? DueDays { get; set; }

    public Guid? AssigneeUserId { get; set; }

    public bool? IsActive { get; set; }
}

public class StockCountScheduleFilterRequestDto
{
    public Guid? BranchId { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? PeriodType { get; set; }
    public bool? IsActive { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class StockCountScheduleResponseDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public string? BranchName { get; set; }
    public Guid WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public string PeriodType { get; set; } = string.Empty;
    public short? DayOfMonth { get; set; }
    public TimeOnly LocalTime { get; set; }
    public short DueDays { get; set; }
    public Guid AssigneeUserId { get; set; }
    public string? AssigneeName { get; set; }
    public bool IsActive { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
