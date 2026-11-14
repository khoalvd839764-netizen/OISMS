using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;

namespace OISM.Application.Features.Branches.Services;

/// <summary>
/// Hiện thực hóa quy tắc phân công chi nhánh (CS-02):
/// - Chưa có dòng phân công nào => được phép mọi chi nhánh (tương thích tài khoản cũ).
/// - Chỉ áp giới hạn thực thi cho vai trò Cashier khi gọi POS checkout / lấy danh sách chi nhánh.
/// </summary>
public class BranchAccessService : IBranchAccessService
{
    private readonly IAppDbContext _context;

    public BranchAccessService(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<Guid>> GetAssignedBranchIdsAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            return Array.Empty<Guid>();
        }

        return await _context.UserBranches
            .AsNoTracking()
            .Where(ub => ub.UserId == userId)
            .Select(ub => ub.BranchId)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task EnsureCashierBranchAllowedAsync(Guid? userId, bool isCashier, Guid branchId, CancellationToken ct = default)
    {
        if (!isCashier || !userId.HasValue || userId.Value == Guid.Empty)
        {
            return;
        }

        var assignedBranchIds = await GetAssignedBranchIdsAsync(userId.Value, ct);
        if (assignedBranchIds.Count == 0)
        {
            return;
        }

        if (!assignedBranchIds.Contains(branchId))
        {
            throw new OismException(
                "BRANCH_NOT_ASSIGNED",
                "Bạn không được phân công làm việc tại chi nhánh này.",
                403);
        }
    }
}
