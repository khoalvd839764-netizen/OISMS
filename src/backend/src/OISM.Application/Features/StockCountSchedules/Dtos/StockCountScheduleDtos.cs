using System.ComponentModel.DataAnnotations;

namespace OISM.Application.Features.StockCountSchedules.Dtos;

public class StockCountScheduleDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Tần suất kiểm kê: 'Daily', 'Weekly', 'Monthly', 'Quarterly'.
    /// </summary>
    public string Frequency { get; set; } = "Monthly";

    public DateTime NextRunAt { get; set; }
    public string Status { get; set; } = "Active"; // Active, Paused, Completed
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CreateStockCountScheduleDto
{
    [Required]
    public Guid BranchId { get; set; }

    [Required(ErrorMessage = "Tiêu đề lịch kiểm kê không được để trống.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Tiêu đề lịch kiểm kê từ 2 đến 100 ký tự.")]
    public string Title { get; set; } = string.Empty;

    public string Frequency { get; set; } = "Monthly";

    public DateTime? StartDate { get; set; }

    [StringLength(300)]
    public string? Note { get; set; }
}
