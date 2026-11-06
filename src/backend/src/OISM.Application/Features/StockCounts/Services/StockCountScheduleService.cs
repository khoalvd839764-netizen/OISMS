using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.StockCounts.Dtos;
using OISM.Domain.Common;
using OISM.Domain.Entities;

namespace OISM.Application.Features.StockCounts.Services;

public class StockCountScheduleService : IStockCountScheduleService
{
    private static readonly TimeSpan StoreOffset = TimeSpan.FromHours(7);

    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ILogger<StockCountScheduleService> _logger;

    public StockCountScheduleService(
        IAppDbContext context,
        ICurrentTenantService tenantService,
        ILogger<StockCountScheduleService> logger)
    {
        _context = context;
        _tenantService = tenantService;
        _logger = logger;
    }

    public async Task<PagedResultDto<StockCountScheduleResponseDto>> GetSchedulesAsync(
        StockCountScheduleFilterRequestDto filter,
        CancellationToken ct = default)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new OismException("UNAUTHORIZED", "Không xác định được danh tính Tenant.", 401);
        }

        var query = _context.StockCountSchedules
            .Include(s => s.Branch)
            .Include(s => s.Warehouse)
            .Include(s => s.AssigneeUser)
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId.Value);

        if (filter.BranchId.HasValue && filter.BranchId.Value != Guid.Empty)
        {
            query = query.Where(s => s.BranchId == filter.BranchId.Value);
        }

        if (filter.WarehouseId.HasValue && filter.WarehouseId.Value != Guid.Empty)
        {
            query = query.Where(s => s.WarehouseId == filter.WarehouseId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.PeriodType))
        {
            var pt = filter.PeriodType.Trim();
            query = query.Where(s => s.PeriodType == pt);
        }

        if (filter.IsActive.HasValue)
        {
            query = query.Where(s => s.IsActive == filter.IsActive.Value);
        }

        var totalCount = await query.CountAsync(ct);
        var pageNumber = filter.PageNumber < 1 ? 1 : filter.PageNumber;
        var pageSize = filter.PageSize < 1 ? 20 : (filter.PageSize > 100 ? 100 : filter.PageSize);

        var items = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResultDto<StockCountScheduleResponseDto>
        {
            Items = items.Select(MapToScheduleResponseDto).ToList(),
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    public async Task<StockCountScheduleResponseDto> CreateScheduleAsync(
        CreateStockCountScheduleRequestDto dto,
        Guid userId,
        CancellationToken ct = default)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new OismException("UNAUTHORIZED", "Không xác định được danh tính Tenant.", 401);
        }

        if (dto == null)
        {
            throw new OismException("VALIDATION_ERROR", "Dữ liệu yêu cầu không được để trống.", 400);
        }

        if (dto.BranchId == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "Chi nhánh kiểm kê không được rỗng.", 400);
        }

        if (dto.WarehouseId == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "Kho kiểm kê không được rỗng.", 400);
        }

        if (dto.AssigneeUserId == Guid.Empty)
        {
            throw new OismException("VALIDATION_ERROR", "Người phụ trách không được rỗng.", 400);
        }

        var periodType = dto.PeriodType?.Trim() ?? string.Empty;
        if (periodType != "MidMonth" && periodType != "EndOfMonth")
        {
            throw new OismException("VALIDATION_ERROR", "Loại kỳ chỉ chấp nhận 'MidMonth' hoặc 'EndOfMonth'.", 400);
        }

        short? dayOfMonth = null;
        if (periodType == "MidMonth")
        {
            dayOfMonth = dto.DayOfMonth ?? 15;
            if (dayOfMonth < 1 || dayOfMonth > 28)
            {
                throw new OismException("VALIDATION_ERROR", "Ngày kiểm kê giữa tháng phải từ 1 đến 28.", 400);
            }
        }
        else
        {
            dayOfMonth = null;
        }

        var localTime = dto.LocalTime ?? new TimeOnly(8, 0);
        var dueDays = dto.DueDays ?? 3;
        if (dueDays < 1 || dueDays > 14)
        {
            throw new OismException("VALIDATION_ERROR", "Hạn hoàn tất kiểm kê phải từ 1 đến 14 ngày.", 400);
        }

        return await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            _context.UnitOfWork.ResetTracking();
            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);

            await _context.Locks.LockStockCountScheduleAsync(tenantId.Value, dto.WarehouseId, periodType, ct);

            await _context.Locks.LockStockCountPeriodGenerationAsync(tenantId.Value, ct);

            // Kiểm tra trùng lặp (warehouse, period_type)
            var exists = await _context.StockCountSchedules
                .AnyAsync(s => s.TenantId == tenantId.Value && s.WarehouseId == dto.WarehouseId && s.PeriodType == periodType, ct);
            if (exists)
            {
                throw new OismException("STOCK_COUNT_SCHEDULE_DUPLICATE", $"Kho đã được cấu hình lịch kiểm kê loại '{periodType}'.", 409);
            }

            // Kiểm tra chi nhánh và kho
            var branch = await _context.Branches
                .FirstOrDefaultAsync(b => b.TenantId == tenantId.Value && b.Id == dto.BranchId, ct);
            if (branch == null)
            {
                throw new OismException("NOT_FOUND", "Không tìm thấy chi nhánh kiểm kê trong phạm vi doanh nghiệp.", 404);
            }
            if (!branch.IsActive)
            {
                throw new OismException("VALIDATION_ERROR", "Chi nhánh kiểm kê đang bị khóa hoặc ngưng hoạt động.", 400);
            }

            var warehouse = await _context.Warehouses
                .FirstOrDefaultAsync(w => w.TenantId == tenantId.Value && w.Id == dto.WarehouseId, ct);
            if (warehouse == null)
            {
                throw new OismException("NOT_FOUND", "Không tìm thấy kho kiểm kê trong phạm vi doanh nghiệp.", 404);
            }
            if (!warehouse.IsActive)
            {
                throw new OismException("VALIDATION_ERROR", "Kho kiểm kê đang bị khóa hoặc ngưng hoạt động.", 400);
            }
            if (warehouse.BranchId != dto.BranchId)
            {
                throw new OismException("VALIDATION_ERROR", "Kho kiểm kê không thuộc chi nhánh kiểm kê đã chọn.", 400);
            }

            // Kiểm tra người phụ trách
            var assignee = await _context.Users
                .FirstOrDefaultAsync(u => u.TenantId == tenantId.Value && u.Id == dto.AssigneeUserId, ct);
            if (assignee == null || !assignee.IsActive)
            {
                throw new OismException("VALIDATION_ERROR", "Người phụ trách không hợp lệ hoặc đang bị khóa.", 400);
            }
            if (assignee.Role != "Owner" && assignee.Role != "Staff")
            {
                throw new OismException("VALIDATION_ERROR", "Người phụ trách phải có vai trò Owner hoặc Staff.", 400);
            }

            var now = DateTimeOffset.UtcNow;
            var schedule = new StockCountSchedule
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId.Value,
                BranchId = dto.BranchId,
                WarehouseId = dto.WarehouseId,
                PeriodType = periodType,
                DayOfMonth = dayOfMonth,
                LocalTime = localTime,
                DueDays = dueDays,
                AssigneeUserId = dto.AssigneeUserId,
                IsActive = dto.IsActive ?? true,
                CreatedBy = userId,
                CreatedAt = now
            };

            schedule.Branch = branch;
            schedule.Warehouse = warehouse;
            schedule.AssigneeUser = assignee;

            _context.StockCountSchedules.Add(schedule);
            await _context.SaveChangesAsync(ct);

            // Sinh kỳ ngay trong cùng transaction nếu lịch đang bật
            if (schedule.IsActive)
            {
                await GeneratePeriodsForScheduleInternalAsync(schedule, now, ct);
                await _context.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);

            return MapToScheduleResponseDto(schedule);
        });
    }

    public async Task<StockCountScheduleResponseDto> UpdateScheduleAsync(
        Guid id,
        UpdateStockCountScheduleRequestDto dto,
        Guid userId,
        CancellationToken ct = default)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new OismException("UNAUTHORIZED", "Không xác định được danh tính Tenant.", 401);
        }

        var hint = await _context.StockCountSchedules
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId.Value && s.Id == id)
            .Select(s => new { s.WarehouseId, s.PeriodType })
            .FirstOrDefaultAsync(ct);

        if (hint == null)
        {
            throw new OismException("NOT_FOUND", "Không tìm thấy lịch kiểm kê trong phạm vi doanh nghiệp.", 404);
        }

        return await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            _context.UnitOfWork.ResetTracking();
            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);

            await _context.Locks.LockStockCountScheduleAsync(tenantId.Value, hint.WarehouseId, hint.PeriodType, ct);

            await _context.Locks.LockStockCountPeriodGenerationAsync(tenantId.Value, ct);

            var schedule = await _context.StockCountSchedules
                .Include(s => s.Branch)
                .Include(s => s.Warehouse)
                .Include(s => s.AssigneeUser)
                .FirstOrDefaultAsync(s => s.TenantId == tenantId.Value && s.Id == id, ct);

            if (schedule == null)
            {
                throw new OismException("NOT_FOUND", "Không tìm thấy lịch kiểm kê trong phạm vi doanh nghiệp.", 404);
            }

            if (dto.AssigneeUserId.HasValue && dto.AssigneeUserId.Value != schedule.AssigneeUserId)
            {
                var assignee = await _context.Users
                    .FirstOrDefaultAsync(u => u.TenantId == tenantId.Value && u.Id == dto.AssigneeUserId.Value, ct);
                if (assignee == null || !assignee.IsActive)
                {
                    throw new OismException("VALIDATION_ERROR", "Người phụ trách không hợp lệ hoặc đang bị khóa.", 400);
                }
                if (assignee.Role != "Owner" && assignee.Role != "Staff")
                {
                    throw new OismException("VALIDATION_ERROR", "Người phụ trách phải có vai trò Owner hoặc Staff.", 400);
                }
                schedule.AssigneeUserId = dto.AssigneeUserId.Value;
                schedule.AssigneeUser = assignee;
            }

            if (dto.DayOfMonth.HasValue)
            {
                if (string.Equals(schedule.PeriodType, "MidMonth", StringComparison.OrdinalIgnoreCase))
                {
                    if (dto.DayOfMonth.Value < 1 || dto.DayOfMonth.Value > 28)
                    {
                        throw new OismException("VALIDATION_ERROR", "Ngày kiểm kê giữa tháng phải từ 1 đến 28.", 400);
                    }
                    schedule.DayOfMonth = dto.DayOfMonth.Value;
                }
                else
                {
                    schedule.DayOfMonth = null;
                }
            }

            if (dto.LocalTime.HasValue)
            {
                schedule.LocalTime = dto.LocalTime.Value;
            }

            if (dto.DueDays.HasValue)
            {
                if (dto.DueDays.Value < 1 || dto.DueDays.Value > 14)
                {
                    throw new OismException("VALIDATION_ERROR", "Hạn hoàn tất kiểm kê phải từ 1 đến 14 ngày.", 400);
                }
                schedule.DueDays = dto.DueDays.Value;
            }

            var now = DateTimeOffset.UtcNow;
            var wasActive = schedule.IsActive;
            if (dto.IsActive.HasValue)
            {
                schedule.IsActive = dto.IsActive.Value;
            }

            schedule.UpdatedBy = userId;
            schedule.UpdatedAt = now;

            // Sửa lịch: các kỳ Planned có scheduled_at > now được tính lại giờ, hạn, người phụ trách.
            // Tắt lịch: các kỳ đó chuyển Cancelled. Kỳ đang làm hoặc đã xong không đổi.
            var futurePlannedPeriods = await _context.StockCountPeriods
                .Where(p => p.TenantId == tenantId.Value && p.ScheduleId == schedule.Id && p.Status == StockCountPeriodStatus.Planned && p.ScheduledAt > now)
                .ToListAsync(ct);

            if (!schedule.IsActive)
            {
                foreach (var period in futurePlannedPeriods)
                {
                    period.Cancel(now);
                }
            }
            else
            {
                foreach (var period in futurePlannedPeriods)
                {
                    // Lấy năm và tháng theo giờ Store (+07:00) từ ScheduledAt hiện tại của kỳ
                    var periodStoreTime = period.ScheduledAt.ToOffset(StoreOffset);
                    var (periodKey, schedAtUtc, dueAtUtc) = StockCountPeriodCalculator.Compute(
                        schedule.PeriodType,
                        schedule.DayOfMonth,
                        schedule.LocalTime,
                        schedule.DueDays,
                        periodStoreTime.Year,
                        periodStoreTime.Month);

                    period.PeriodKey = periodKey;
                    period.ScheduledAt = schedAtUtc;
                    period.DueAt = dueAtUtc;
                    period.AssigneeUserId = schedule.AssigneeUserId;
                    period.UpdatedAt = now;
                }

                // Đồng thời sinh thêm kỳ tháng này / tháng sau nếu chưa có
                await GeneratePeriodsForScheduleInternalAsync(schedule, now, ct);
            }

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return MapToScheduleResponseDto(schedule);
        });
    }

    public async Task<PagedResultDto<StockCountPeriodResponseDto>> GetPeriodsAsync(
        StockCountPeriodFilterRequestDto filter,
        CancellationToken ct = default)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new OismException("UNAUTHORIZED", "Không xác định được danh tính Tenant.", 401);
        }

        var query = _context.StockCountPeriods
            .Include(p => p.Branch)
            .Include(p => p.Warehouse)
            .Include(p => p.AssigneeUser)
            .Include(p => p.StockCounts)
                .ThenInclude(c => c.Items)
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId.Value);

        if (filter.BranchId.HasValue && filter.BranchId.Value != Guid.Empty)
        {
            query = query.Where(p => p.BranchId == filter.BranchId.Value);
        }

        if (filter.WarehouseId.HasValue && filter.WarehouseId.Value != Guid.Empty)
        {
            query = query.Where(p => p.WarehouseId == filter.WarehouseId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var st = filter.Status.Trim();
            query = query.Where(p => p.Status == st);
        }

        if (filter.From.HasValue)
        {
            var fromUtc = filter.From.Value.ToUniversalTime();
            query = query.Where(p => p.ScheduledAt >= fromUtc);
        }

        if (filter.To.HasValue)
        {
            var toUtc = filter.To.Value.ToUniversalTime();
            query = query.Where(p => p.ScheduledAt <= toUtc);
        }

        var now = DateTimeOffset.UtcNow;
        if (filter.Overdue.HasValue)
        {
            if (filter.Overdue.Value)
            {
                query = query.Where(p => (p.Status == StockCountPeriodStatus.Planned || p.Status == StockCountPeriodStatus.InProgress) && p.DueAt < now);
            }
            else
            {
                query = query.Where(p => !((p.Status == StockCountPeriodStatus.Planned || p.Status == StockCountPeriodStatus.InProgress) && p.DueAt < now));
            }
        }

        var totalCount = await query.CountAsync(ct);
        var pageNumber = filter.PageNumber < 1 ? 1 : filter.PageNumber;
        var pageSize = filter.PageSize < 1 ? 20 : (filter.PageSize > 100 ? 100 : filter.PageSize);

        var items = await query
            .OrderByDescending(p => p.ScheduledAt)
            .ThenBy(p => p.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResultDto<StockCountPeriodResponseDto>
        {
            Items = items.Select(p => MapToPeriodResponseDto(p, now)).ToList(),
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    public async Task<int> GeneratePeriodsAsync(DateTimeOffset? nowUtc = null, CancellationToken ct = default)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
        {
            throw new OismException("UNAUTHORIZED", "Không xác định được danh tính Tenant.", 401);
        }

        return await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            _context.UnitOfWork.ResetTracking();
            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);

            await _context.Locks.LockStockCountPeriodGenerationAsync(tenantId.Value, ct);

            var now = nowUtc ?? DateTimeOffset.UtcNow;
            var activeSchedules = await _context.StockCountSchedules
                .Where(s => s.TenantId == tenantId.Value && s.IsActive)
                .ToListAsync(ct);

            int totalGeneratedOrUpdated = 0;
            foreach (var schedule in activeSchedules)
            {
                totalGeneratedOrUpdated += await GeneratePeriodsForScheduleInternalAsync(schedule, now, ct);
            }

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return totalGeneratedOrUpdated;
        });
    }

    private async Task<int> GeneratePeriodsForScheduleInternalAsync(
        StockCountSchedule schedule,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var storeNow = now.ToOffset(StoreOffset);
        var currentMonth = (Year: storeNow.Year, Month: storeNow.Month);
        var nextStoreMonth = storeNow.AddMonths(1);
        var nextMonth = (Year: nextStoreMonth.Year, Month: nextStoreMonth.Month);

        var targetMonths = new[] { currentMonth, nextMonth };
        int count = 0;

        foreach (var (year, month) in targetMonths)
        {
            var (periodKey, scheduledAtUtc, dueAtUtc) = StockCountPeriodCalculator.Compute(
                schedule.PeriodType,
                schedule.DayOfMonth,
                schedule.LocalTime,
                schedule.DueDays,
                year,
                month);

            // Bỏ qua kỳ có scheduled_at < schedule.created_at
            if (scheduledAtUtc < schedule.CreatedAt)
            {
                continue;
            }

            var existingPeriod = await _context.StockCountPeriods
                .FirstOrDefaultAsync(p => p.TenantId == schedule.TenantId && p.WarehouseId == schedule.WarehouseId && p.PeriodKey == periodKey, ct);

            if (existingPeriod != null)
            {
                // Đã có period_key: nếu Cancelled và scheduled_at > now thì đưa về Planned với giờ mới; còn lại không đổi
                if (existingPeriod.Status == StockCountPeriodStatus.Cancelled && scheduledAtUtc > now)
                {
                    existingPeriod.Reschedule(scheduledAtUtc, dueAtUtc, schedule.AssigneeUserId, now);
                    count++;
                }
            }
            else
            {
                // Chưa có thì tạo Planned
                var period = new StockCountPeriod
                {
                    Id = Guid.NewGuid(),
                    TenantId = schedule.TenantId,
                    ScheduleId = schedule.Id,
                    BranchId = schedule.BranchId,
                    WarehouseId = schedule.WarehouseId,
                    PeriodType = schedule.PeriodType,
                    PeriodKey = periodKey,
                    ScheduledAt = scheduledAtUtc,
                    DueAt = dueAtUtc,
                    AssigneeUserId = schedule.AssigneeUserId,
                    Status = StockCountPeriodStatus.Planned,
                    CreatedAt = now
                };

                _context.StockCountPeriods.Add(period);
                count++;
            }
        }

        return count;
    }

    private static StockCountScheduleResponseDto MapToScheduleResponseDto(StockCountSchedule schedule)
    {
        return new StockCountScheduleResponseDto
        {
            Id = schedule.Id,
            TenantId = schedule.TenantId,
            BranchId = schedule.BranchId,
            BranchName = schedule.Branch?.Name,
            WarehouseId = schedule.WarehouseId,
            WarehouseName = schedule.Warehouse?.Name,
            PeriodType = schedule.PeriodType,
            DayOfMonth = schedule.DayOfMonth,
            LocalTime = schedule.LocalTime,
            DueDays = schedule.DueDays,
            AssigneeUserId = schedule.AssigneeUserId,
            AssigneeName = schedule.AssigneeUser?.DisplayName,
            IsActive = schedule.IsActive,
            CreatedBy = schedule.CreatedBy,
            UpdatedBy = schedule.UpdatedBy,
            CreatedAt = schedule.CreatedAt,
            UpdatedAt = schedule.UpdatedAt
        };
    }

    private static StockCountPeriodResponseDto MapToPeriodResponseDto(StockCountPeriod period, DateTimeOffset now)
    {
        var isOverdue = (period.Status == StockCountPeriodStatus.Planned || period.Status == StockCountPeriodStatus.InProgress) && period.DueAt < now;
        var activeCount = period.StockCounts.FirstOrDefault(c => c.Status != StockCountStatus.Cancelled);

        var countedUserIds = activeCount?.Items
            .Where(i => i.CountedBy.HasValue)
            .Select(i => i.CountedBy!.Value)
            .Distinct()
            .ToList() ?? new List<Guid>();

        var differenceSkuCount = activeCount?.Items
            .Count(i => i.CountedQuantity.HasValue && i.CountedQuantity.Value != i.ExpectedQuantity) ?? 0;

        return new StockCountPeriodResponseDto
        {
            Id = period.Id,
            TenantId = period.TenantId,
            ScheduleId = period.ScheduleId,
            BranchId = period.BranchId,
            BranchName = period.Branch?.Name,
            WarehouseId = period.WarehouseId,
            WarehouseName = period.Warehouse?.Name,
            PeriodType = period.PeriodType,
            PeriodKey = period.PeriodKey,
            ScheduledAt = period.ScheduledAt,
            DueAt = period.DueAt,
            AssigneeUserId = period.AssigneeUserId,
            AssigneeName = period.AssigneeUser?.DisplayName,
            Status = period.Status,
            CompletedAt = period.CompletedAt,
            CreatedAt = period.CreatedAt,
            UpdatedAt = period.UpdatedAt,
            IsOverdue = isOverdue,
            StockCountId = activeCount?.Id,
            StockCountNumber = activeCount?.Number,
            StockCountStatus = activeCount?.Status,
            CountedByUserIds = countedUserIds,
            ConfirmedBy = activeCount?.ConfirmedBy,
            DifferenceSkuCount = differenceSkuCount
        };
    }
}
