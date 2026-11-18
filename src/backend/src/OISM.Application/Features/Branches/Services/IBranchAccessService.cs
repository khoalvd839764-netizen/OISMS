namespace OISM.Application.Features.Branches.Services;

/// <summary>
/// Kiểm tra và truy vấn danh sách chi nhánh được phân công cho nhân viên (CS-02).
/// </summary>
public interface IBranchAccessService
{
    Task<IReadOnlyList<Guid>> GetAssignedBranchIdsAsync(Guid userId, CancellationToken ct = default);
    Task EnsureCashierBranchAllowedAsync(Guid? userId, bool isCashier, Guid branchId, CancellationToken ct = default);
}
