using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.StockCountSchedules.Dtos;

namespace OISM.Application.Features.StockCountSchedules.Services;

public class StockCountScheduleService : IStockCountScheduleService
{
    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _currentTenantService;

    private static readonly ConcurrentDictionary<Guid, List<StockCountScheduleDto>> _tenantSchedules = new();

    public StockCountScheduleService(IAppDbContext context, ICurrentTenantService currentTenantService)
    {
        _context = context;
        _currentTenantService = currentTenantService;
    }

    private Guid RequireTenantId()
    {
        var tenantId = _currentTenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Yêu cầu xác thực gian hàng hợp lệ.");
        }
        return tenantId.Value;
    }

    public Task<List<StockCountScheduleDto>> GetSchedulesAsync(CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();
        var schedules = _tenantSchedules.GetOrAdd(tenantId, _ => new List<StockCountScheduleDto>());

        lock (schedules)
        {
            return Task.FromResult(schedules.OrderBy(s => s.NextRunAt).ToList());
        }
    }

    public async Task<StockCountScheduleDto> CreateScheduleAsync(CreateStockCountScheduleDto dto, CancellationToken ct = default)
    {
        var tenantId = RequireTenantId();

        var branch = await _context.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == dto.BranchId, ct);

        var startDate = dto.StartDate ?? DateTime.UtcNow.AddDays(7);

        var newSchedule = new StockCountScheduleDto
        {
            Id = Guid.NewGuid(),
            BranchId = dto.BranchId,
            BranchName = branch?.Name ?? "Chi nhánh",
            Title = dto.Title.Trim(),
            Frequency = dto.Frequency,
            NextRunAt = startDate,
            Status = "Active",
            Note = dto.Note,
            CreatedAt = DateTime.UtcNow
        };

        var schedules = _tenantSchedules.GetOrAdd(tenantId, _ => new List<StockCountScheduleDto>());
        lock (schedules)
        {
            schedules.Add(newSchedule);
        }

        return newSchedule;
    }
}
