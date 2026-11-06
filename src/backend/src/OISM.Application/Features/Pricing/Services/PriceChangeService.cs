using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Pricing.Dtos;
using OISM.Domain.Common;
using OISM.Domain.Entities;

namespace OISM.Application.Features.Pricing.Services;

public class PriceChangeService : IPriceChangeService
{
    private const decimal MaxMoneyAmount = 999_999_999_999_999_999.99m;
    private const int MaxTextLength = 500;

    private readonly IAppDbContext _dbContext;
    private readonly ICurrentTenantService _currentTenantService;
    private readonly ISqlLockService _sqlLockService;

    public PriceChangeService(
        IAppDbContext dbContext,
        ICurrentTenantService currentTenantService,
        ISqlLockService sqlLockService)
    {
        _dbContext = dbContext;
        _currentTenantService = currentTenantService;
        _sqlLockService = sqlLockService;
    }

    public async Task<PriceChangeRequestResponse> CreateAsync(
        CreatePriceChangeRequest request,
        Guid requestKey,
        PriceActor actor,
        CancellationToken ct = default)
    {
        if (request == null)
        {
            throw new OismException("VALIDATION_ERROR", "Dữ liệu yêu cầu không được để trống.", 400);
        }

        var tenantId = _currentTenantService.TenantId
            ?? throw new OismException("UNAUTHORIZED", "Chưa xác thực thông tin khách thuê.", 401);

        return await _dbContext.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            using var tx = await _dbContext.UnitOfWork.BeginTransactionAsync(ct);

            // 1. Kiểm tra Idempotency dưới khóa/giao dịch
            var existingRequest = await _dbContext.PriceChangeRequests
                .Include(r => r.Items)
                    .ThenInclude(i => i.PriceList)
                .Include(r => r.Items)
                    .ThenInclude(i => i.Sku)
                .Include(r => r.Items)
                    .ThenInclude(i => i.Unit)
                .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.RequestKey == requestKey, ct);

            if (existingRequest != null)
            {
                if (!PayloadMatches(existingRequest, request))
                {
                    throw new IdempotencyPayloadMismatchException("RequestKey đã tồn tại nhưng nội dung phiếu đổi giá không khớp.");
                }

                var replayDto = MapToResponse(existingRequest);
                replayDto.IsReplay = true;
                return replayDto;
            }

            // 2. Kiểm tra dòng phiếu
            ValidateRequestLines(request);

            // 3. Nạp danh sách bảng giá hợp lệ của tenant
            var requestedCodes = request.Items
                .Select(i => i.PriceListCode.Trim().ToLowerInvariant())
                .Distinct()
                .ToList();

            var priceLists = await _dbContext.PriceLists
                .Where(p => p.TenantId == tenantId && p.IsActive)
                .ToListAsync(ct);

            var priceListsByCode = priceLists.ToDictionary(p => p.Code.ToLowerInvariant());

            foreach (var code in requestedCodes)
            {
                if (!priceListsByCode.ContainsKey(code))
                {
                    throw new PriceListNotFoundException(code);
                }
            }

            // 4. Kiểm tra đơn vị thuộc SKU, active và CanSell
            var requestedSkuIds = request.Items.Select(i => i.SkuId).Distinct().ToList();
            var skuUnits = await _dbContext.SkuUnits
                .Where(su => su.TenantId == tenantId && requestedSkuIds.Contains(su.SkuId))
                .ToListAsync(ct);

            foreach (var item in request.Items)
            {
                var su = skuUnits.FirstOrDefault(u => u.SkuId == item.SkuId && u.UnitId == item.UnitId);
                if (su == null || !su.IsActive || !su.CanSell)
                {
                    throw new OismException("UNIT_NOT_ALLOWED", "Đơn vị tính không thuộc SKU, đang bị khóa hoặc không cho phép bán.", 422);
                }
            }

            // 5. Khởi tạo phiếu đổi giá
            var now = DateTimeOffset.UtcNow;
            var status = actor.IsOwner ? PriceChangeRequestStatus.Approved : PriceChangeRequestStatus.Pending;

            var requestEntity = new PriceChangeRequest
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                RequestKey = requestKey,
                Kind = "Change",
                Status = status,
                Reason = request.Reason.Trim(),
                RequestedValidFrom = request.RequestedValidFrom,
                CreatedBy = actor.UserId,
                CreatedAt = now,
                ReviewedBy = actor.IsOwner ? actor.UserId : null,
                ReviewedAt = actor.IsOwner ? now : null,
                ReviewNote = null
            };

            _dbContext.PriceChangeRequests.Add(requestEntity);

            // 6. Tính mốc hiệu lực nếu Owner duyệt ngay
            var validFrom = request.RequestedValidFrom.HasValue && request.RequestedValidFrom.Value > now
                ? request.RequestedValidFrom.Value
                : now;

            foreach (var item in request.Items)
            {
                var priceList = priceListsByCode[item.PriceListCode.Trim().ToLowerInvariant()];
                var itemEntity = new PriceChangeRequestItem
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    RequestId = requestEntity.Id,
                    PriceListId = priceList.Id,
                    SkuId = item.SkuId,
                    UnitId = item.UnitId,
                    NewPrice = item.NewPrice,
                    CreatedAt = now
                };

                _dbContext.PriceChangeRequestItems.Add(itemEntity);

                if (actor.IsOwner)
                {
                    var priceEntity = new SkuUnitPrice
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        PriceListId = priceList.Id,
                        SkuId = item.SkuId,
                        UnitId = item.UnitId,
                        Price = item.NewPrice,
                        ValidFrom = validFrom,
                        SourceItemId = itemEntity.Id,
                        CreatedAt = now
                    };

                    _dbContext.SkuUnitPrices.Add(priceEntity);
                }
            }

            try
            {
                await _dbContext.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (_dbContext.DatabaseErrors.IsUniqueViolation(ex, "ux_sku_unit_prices_active_key"))
            {
                await tx.RollbackAsync(ct);
                throw new PriceValidFromConflictException();
            }
            catch (DbUpdateException ex) when (_dbContext.DatabaseErrors.IsUniqueViolation(ex, "uq_price_change_requests_2"))
            {
                await tx.RollbackAsync(ct);

                // Race condition khi gửi đồng thời: đọc lại và replay
                var reloaded = await _dbContext.PriceChangeRequests
                    .Include(r => r.Items)
                        .ThenInclude(i => i.PriceList)
                    .Include(r => r.Items)
                        .ThenInclude(i => i.Sku)
                    .Include(r => r.Items)
                        .ThenInclude(i => i.Unit)
                    .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.RequestKey == requestKey, ct);

                if (reloaded != null)
                {
                    if (!PayloadMatches(reloaded, request))
                    {
                        throw new IdempotencyPayloadMismatchException("RequestKey đã tồn tại nhưng nội dung phiếu đổi giá không khớp.");
                    }

                    var replayDto = MapToResponse(reloaded);
                    replayDto.IsReplay = true;
                    return replayDto;
                }

                throw;
            }

            return await LoadAndMapResponseAsync(requestEntity.Id, tenantId, ct);
        });
    }

    public async Task<PriceChangeRequestResponse> ApproveAsync(Guid id, Guid? reviewerId, CancellationToken ct = default)
    {
        var tenantId = _currentTenantService.TenantId
            ?? throw new OismException("UNAUTHORIZED", "Chưa xác thực thông tin khách thuê.", 401);

        return await _dbContext.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            using var tx = await _dbContext.UnitOfWork.BeginTransactionAsync(ct);

            await _sqlLockService.LockPriceChangeRequestAsync(tenantId, id, ct);

            var request = await _dbContext.PriceChangeRequests
                .Include(r => r.Items)
                    .ThenInclude(i => i.PriceList)
                .Include(r => r.Items)
                    .ThenInclude(i => i.Sku)
                .Include(r => r.Items)
                    .ThenInclude(i => i.Unit)
                .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);

            if (request == null)
            {
                throw new OismException("NOT_FOUND", "Không tìm thấy phiếu đổi giá.", 404);
            }

            if (!request.IsPending)
            {
                throw new OismException("INVALID_STATE_TRANSITION", $"Phiếu đổi giá đang ở trạng thái '{request.Status}', chỉ cho phép duyệt phiếu 'Pending'.", 409);
            }

            // Kiểm tra đơn vị còn active và bán được, khác thì UNIT_NOT_ALLOWED
            var requestedSkuIds = request.Items.Select(i => i.SkuId).Distinct().ToList();
            var skuUnits = await _dbContext.SkuUnits
                .Where(su => su.TenantId == tenantId && requestedSkuIds.Contains(su.SkuId))
                .ToListAsync(ct);

            foreach (var item in request.Items)
            {
                var su = skuUnits.FirstOrDefault(u => u.SkuId == item.SkuId && u.UnitId == item.UnitId);
                if (su == null || !su.IsActive || !su.CanSell)
                {
                    throw new OismException("UNIT_NOT_ALLOWED", "Đơn vị tính không thuộc SKU, đang bị khóa hoặc không cho phép bán.", 422);
                }
            }

            // Tính valid_from theo quyết định 2 bằng đồng hồ backend
            var now = DateTimeOffset.UtcNow;
            var validFrom = request.RequestedValidFrom.HasValue && request.RequestedValidFrom.Value > now
                ? request.RequestedValidFrom.Value
                : now;

            foreach (var item in request.Items)
            {
                var priceEntity = new SkuUnitPrice
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    PriceListId = item.PriceListId,
                    SkuId = item.SkuId,
                    UnitId = item.UnitId,
                    Price = item.NewPrice,
                    ValidFrom = validFrom,
                    SourceItemId = item.Id,
                    CreatedAt = now
                };

                _dbContext.SkuUnitPrices.Add(priceEntity);
            }

            request.Approve(reviewerId, now);

            try
            {
                await _dbContext.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (_dbContext.DatabaseErrors.IsUniqueViolation(ex, "ux_sku_unit_prices_active_key"))
            {
                await tx.RollbackAsync(ct);
                throw new PriceValidFromConflictException();
            }

            return MapToResponse(request);
        });
    }

    public async Task<PriceChangeRequestResponse> RejectAsync(Guid id, string note, Guid? reviewerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            throw new OismException("VALIDATION_ERROR", "Lý do từ chối không được để trống.", 400);
        }

        if (note.Trim().Length > MaxTextLength)
        {
            throw new OismException("VALIDATION_ERROR", $"Lý do từ chối tối đa {MaxTextLength} ký tự.", 400);
        }

        var tenantId = _currentTenantService.TenantId
            ?? throw new OismException("UNAUTHORIZED", "Chưa xác thực thông tin khách thuê.", 401);

        return await _dbContext.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            using var tx = await _dbContext.UnitOfWork.BeginTransactionAsync(ct);

            await _sqlLockService.LockPriceChangeRequestAsync(tenantId, id, ct);

            var request = await _dbContext.PriceChangeRequests
                .Include(r => r.Items)
                    .ThenInclude(i => i.PriceList)
                .Include(r => r.Items)
                    .ThenInclude(i => i.Sku)
                .Include(r => r.Items)
                    .ThenInclude(i => i.Unit)
                .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, ct);

            if (request == null)
            {
                throw new OismException("NOT_FOUND", "Không tìm thấy phiếu đổi giá.", 404);
            }

            if (!request.IsPending)
            {
                throw new OismException("INVALID_STATE_TRANSITION", $"Phiếu đổi giá đang ở trạng thái '{request.Status}', chỉ cho phép từ chối phiếu 'Pending'.", 409);
            }

            var now = DateTimeOffset.UtcNow;
            request.Reject(reviewerId, note.Trim(), now);

            await _dbContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return MapToResponse(request);
        });
    }

    public async Task CancelScheduledPriceAsync(Guid skuUnitPriceId, string reason, Guid? userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new OismException("VALIDATION_ERROR", "Lý do hủy giá không được để trống.", 400);
        }

        if (reason.Trim().Length > MaxTextLength)
        {
            throw new OismException("VALIDATION_ERROR", $"Lý do hủy giá tối đa {MaxTextLength} ký tự.", 400);
        }

        var tenantId = _currentTenantService.TenantId
            ?? throw new OismException("UNAUTHORIZED", "Chưa xác thực thông tin khách thuê.", 401);

        await _dbContext.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            using var tx = await _dbContext.UnitOfWork.BeginTransactionAsync(ct);

            await _sqlLockService.LockSkuUnitPriceAsync(tenantId, skuUnitPriceId, ct);

            var price = await _dbContext.SkuUnitPrices
                .FirstOrDefaultAsync(p => p.Id == skuUnitPriceId && p.TenantId == tenantId, ct);

            if (price == null)
            {
                throw new OismException("NOT_FOUND", "Không tìm thấy dòng giá.", 404);
            }

            if (price.CancelledAt != null)
            {
                throw new OismException("INVALID_STATE_TRANSITION", "Dòng giá này đã bị hủy trước đó.", 409);
            }

            var now = DateTimeOffset.UtcNow;
            if (price.ValidFrom <= now)
            {
                throw new PriceAlreadyEffectiveException("Không thể hủy giá đã đến thời điểm hoặc đang có hiệu lực.");
            }

            price.CancelledAt = now;
            price.CancelledBy = userId;
            price.CancelReason = reason.Trim();

            await _dbContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    public async Task AddInitialPricesAsync(IReadOnlyCollection<InitialPriceLine> lines, PriceActor actor, CancellationToken ct = default)
    {
        if (lines == null || lines.Count == 0) return;

        var validLines = lines.Where(l => l.Price > 0).ToList();
        if (validLines.Count == 0) return;

        var tenantId = _currentTenantService.TenantId
            ?? throw new OismException("UNAUTHORIZED", "Chưa xác thực thông tin khách thuê.", 401);

        var priceLists = await _dbContext.PriceLists
            .Where(p => p.TenantId == tenantId && p.IsActive)
            .ToListAsync(ct);

        var priceListsByCode = priceLists.ToDictionary(p => p.Code.ToLowerInvariant());

        var now = DateTimeOffset.UtcNow;
        var request = new PriceChangeRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RequestKey = Guid.NewGuid(),
            Kind = "Initial",
            Status = actor.IsOwner ? PriceChangeRequestStatus.Approved : PriceChangeRequestStatus.Pending,
            Reason = "Giá ban đầu",
            RequestedValidFrom = null,
            CreatedBy = actor.UserId,
            CreatedAt = now,
            ReviewedBy = actor.IsOwner ? actor.UserId : null,
            ReviewedAt = actor.IsOwner ? now : null,
            ReviewNote = null
        };

        _dbContext.PriceChangeRequests.Add(request);

        foreach (var line in validLines)
        {
            var code = line.PriceListCode.Trim().ToLowerInvariant();
            if (!priceListsByCode.TryGetValue(code, out var priceList))
            {
                throw new PriceListNotFoundException(code);
            }

            var item = new PriceChangeRequestItem
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                RequestId = request.Id,
                PriceListId = priceList.Id,
                SkuId = line.SkuId,
                UnitId = line.UnitId,
                NewPrice = line.Price,
                CreatedAt = now
            };

            _dbContext.PriceChangeRequestItems.Add(item);

            if (actor.IsOwner)
            {
                var price = new SkuUnitPrice
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    PriceListId = priceList.Id,
                    SkuId = line.SkuId,
                    UnitId = line.UnitId,
                    Price = line.Price,
                    ValidFrom = now,
                    SourceItemId = item.Id,
                    CreatedAt = now
                };

                _dbContext.SkuUnitPrices.Add(price);
            }
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task EnsureTenantDefaultPriceListsAsync(Guid tenantId, CancellationToken ct = default)
    {
        var existing = await _dbContext.PriceLists
            .Where(p => p.TenantId == tenantId)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var added = false;

        if (!existing.Any(p => p.Code == "retail"))
        {
            _dbContext.PriceLists.Add(new PriceList
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Code = "retail",
                Name = "Giá lẻ",
                IsActive = true,
                CreatedAt = now
            });
            added = true;
        }

        if (!existing.Any(p => p.Code == "wholesale"))
        {
            _dbContext.PriceLists.Add(new PriceList
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Code = "wholesale",
                Name = "Giá sỉ",
                IsActive = true,
                CreatedAt = now
            });
            added = true;
        }

        if (added)
        {
            await _dbContext.SaveChangesAsync(ct);
        }
    }

    private static void ValidateRequestLines(CreatePriceChangeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new OismException("VALIDATION_ERROR", "Lý do đổi giá không được để trống.", 400);
        }

        if (request.Reason.Trim().Length > MaxTextLength)
        {
            throw new OismException("VALIDATION_ERROR", $"Lý do đổi giá tối đa {MaxTextLength} ký tự.", 400);
        }

        if (request.Items == null || request.Items.Count == 0)
        {
            throw new OismException("VALIDATION_ERROR", "Phiếu đổi giá phải có ít nhất 1 dòng mặt hàng.", 400);
        }

        if (request.Items.Count > 500)
        {
            throw new OismException("VALIDATION_ERROR", "Phiếu đổi giá không được vượt quá 500 dòng mặt hàng.", 400);
        }

        if (request.RequestedValidFrom.HasValue && request.RequestedValidFrom.Value < DateTimeOffset.UtcNow)
        {
            throw new PriceValidFromInPastException();
        }

        var duplicates = request.Items
            .GroupBy(i => (i.PriceListCode.Trim().ToLowerInvariant(), i.SkuId, i.UnitId))
            .Where(g => g.Count() > 1)
            .ToList();

        if (duplicates.Count > 0)
        {
            throw new OismException("VALIDATION_ERROR", "Không được chứa các dòng trùng lặp bảng giá, SKU và đơn vị tính trong cùng một phiếu.", 400);
        }

        foreach (var item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item.PriceListCode))
            {
                throw new OismException("VALIDATION_ERROR", "Mã bảng giá không được để trống.", 400);
            }

            if (item.SkuId == Guid.Empty)
            {
                throw new OismException("VALIDATION_ERROR", "Mã SKU không hợp lệ.", 400);
            }

            if (item.UnitId == Guid.Empty)
            {
                throw new OismException("VALIDATION_ERROR", "Mã đơn vị tính không hợp lệ.", 400);
            }

            if (item.NewPrice < 0)
            {
                throw new OismException("VALIDATION_ERROR", "Giá mới không được nhỏ hơn 0.", 400);
            }

            if (item.NewPrice > MaxMoneyAmount)
            {
                throw new OismException("VALIDATION_ERROR", "Giá mới vượt quá giới hạn cho phép.", 400);
            }

            if (decimal.Round(item.NewPrice, 2) != item.NewPrice)
            {
                throw new OismException("VALIDATION_ERROR", "Giá mới tối đa 2 chữ số thập phân.", 400);
            }
        }
    }

    private static bool PayloadMatches(PriceChangeRequest existing, CreatePriceChangeRequest incoming)
    {
        if (existing.Reason != incoming.Reason.Trim()) return false;
        if (TruncateToMicroseconds(existing.RequestedValidFrom) != TruncateToMicroseconds(incoming.RequestedValidFrom)) return false;
        if (existing.Items.Count != incoming.Items.Count) return false;

        foreach (var incItem in incoming.Items)
        {
            var incCode = incItem.PriceListCode.Trim().ToLowerInvariant();
            var match = existing.Items.FirstOrDefault(e =>
                e.PriceList != null &&
                e.PriceList.Code.ToLowerInvariant() == incCode &&
                e.SkuId == incItem.SkuId &&
                e.UnitId == incItem.UnitId &&
                e.NewPrice == incItem.NewPrice);

            if (match == null) return false;
        }

        return true;
    }

    // timestamptz chỉ giữ tới micro giây, còn client có thể gửi tới 100 nano giây
    private static DateTimeOffset? TruncateToMicroseconds(DateTimeOffset? value) =>
        value?.AddTicks(-(value.Value.Ticks % 10));

    private async Task<PriceChangeRequestResponse> LoadAndMapResponseAsync(Guid id, Guid tenantId, CancellationToken ct)
    {
        var request = await _dbContext.PriceChangeRequests
            .Include(r => r.Items)
                .ThenInclude(i => i.PriceList)
            .Include(r => r.Items)
                .ThenInclude(i => i.Sku)
            .Include(r => r.Items)
                .ThenInclude(i => i.Unit)
            .FirstAsync(r => r.Id == id && r.TenantId == tenantId, ct);

        return MapToResponse(request);
    }

    private static PriceChangeRequestResponse MapToResponse(PriceChangeRequest r)
    {
        return new PriceChangeRequestResponse
        {
            Id = r.Id,
            RequestKey = r.RequestKey,
            Kind = r.Kind,
            Status = r.Status,
            Reason = r.Reason,
            RequestedValidFrom = r.RequestedValidFrom,
            CreatedBy = r.CreatedBy,
            CreatedAt = r.CreatedAt,
            ReviewedBy = r.ReviewedBy,
            ReviewedAt = r.ReviewedAt,
            ReviewNote = r.ReviewNote,
            IsReplay = false,
            Items = r.Items.Select(i => new PriceChangeRequestItemDto
            {
                Id = i.Id,
                PriceListId = i.PriceListId,
                PriceListCode = i.PriceList?.Code ?? string.Empty,
                SkuId = i.SkuId,
                SkuCode = i.Sku?.Code ?? string.Empty,
                UnitId = i.UnitId,
                UnitName = i.Unit?.Name ?? string.Empty,
                NewPrice = i.NewPrice
            }).ToList()
        };
    }
}
