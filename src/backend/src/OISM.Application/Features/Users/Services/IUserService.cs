namespace OISM.Application.Features.Users.Services;

/// <summary>
/// Giao diện xử lý nghiệp vụ Quản lý Nhân sự & Phân quyền RBAC theo Tenant (CS-02).
/// </summary>
public interface IUserService
{
    Task<List<UserResponse>> GetUsersAsync(string? search = null, string? role = null, bool? isActive = null, CancellationToken ct = default);
    Task<UserResponse?> GetUserByIdAsync(Guid id, CancellationToken ct = default);
    Task<UserResponse> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<UserResponse> UpdateUserAsync(Guid id, UpdateUserRequest request, Guid currentUserId, CancellationToken ct = default);
    Task<UserResponse> UpdateUserStatusAsync(Guid id, bool isActive, Guid currentUserId, CancellationToken ct = default);
}
