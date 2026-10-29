using OISM.Application.Features.Branches;

namespace OISM.Application.Features.Branches.Services;

public record BranchToggleResult(bool IsActive, string Message);

/// <summary>
/// Giao diện xử lý nghiệp vụ Chi nhánh (FR-AUTH-04, BE2-08).
/// </summary>
public interface IBranchService
{
    Task<List<BranchResponse>> GetBranchesAsync(CancellationToken ct = default);
    Task<BranchResponse?> GetBranchByIdAsync(Guid id, CancellationToken ct = default);
    Task<BranchResponse> CreateBranchAsync(CreateBranchRequest request, CancellationToken ct = default);
    Task<BranchResponse?> UpdateBranchAsync(Guid id, UpdateBranchRequest request, CancellationToken ct = default);
    Task<BranchToggleResult?> ToggleStatusAsync(Guid id, CancellationToken ct = default);
}
