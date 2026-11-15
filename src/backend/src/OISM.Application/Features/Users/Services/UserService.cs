using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Helpers;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.PasswordReset;
using OISM.Domain.Entities;

namespace OISM.Application.Features.Users.Services;

/// <summary>
/// Hiện thực hóa nghiệp vụ Quản trị Nhân sự & Phân quyền RBAC (CS-02).
/// Đảm bảo tính cô lập dữ liệu Đa người thuê (NFR-TENANT-01) và an toàn tài khoản.
/// </summary>
public class UserService : IUserService
{
    private readonly IAppDbContext _context;
    private readonly ICurrentTenantService _currentTenantService;
    private readonly IPasswordHasher _passwordHasher;

    public UserService(
        IAppDbContext context,
        ICurrentTenantService currentTenantService,
        IPasswordHasher passwordHasher)
    {
        _context = context;
        _currentTenantService = currentTenantService;
        _passwordHasher = passwordHasher;
    }

    public async Task<List<UserResponse>> GetUsersAsync(
        string? search = null,
        string? role = null,
        bool? isActive = null,
        CancellationToken ct = default)
    {
        var query = _context.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.Trim().ToLowerInvariant();
            query = query.Where(u =>
                u.DisplayName.ToLower().Contains(searchLower) ||
                (u.Email != null && u.Email.ToLower().Contains(searchLower)) ||
                (u.Phone != null && u.Phone.Contains(searchLower)));
        }

        if (!string.IsNullOrWhiteSpace(role) && role != "all")
        {
            query = query.Where(u => u.Role == role);
        }

        if (isActive.HasValue)
        {
            query = query.Where(u => u.IsActive == isActive.Value);
        }

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync(ct);

        var userIds = users.Select(u => u.Id).ToList();
        var branchMap = userIds.Count == 0
            ? new Dictionary<Guid, List<Guid>>()
            : (await _context.UserBranches
                .AsNoTracking()
                .Where(ub => userIds.Contains(ub.UserId))
                .Select(ub => new { ub.UserId, ub.BranchId })
                .ToListAsync(ct))
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.BranchId).Distinct().ToList());

        return users.Select(u => MapToResponse(
            u,
            u.Role == "Owner" ? new List<Guid>() : branchMap.GetValueOrDefault(u.Id, new List<Guid>())))
            .ToList();
    }

    public async Task<UserResponse?> GetUserByIdAsync(Guid id, CancellationToken ct = default)
    {
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user == null)
        {
            return null;
        }

        var branchIds = user.Role == "Owner"
            ? new List<Guid>()
            : await _context.UserBranches
                .AsNoTracking()
                .Where(ub => ub.UserId == user.Id)
                .Select(ub => ub.BranchId)
                .Distinct()
                .ToListAsync(ct);

        return MapToResponse(user, branchIds);
    }

    public async Task<UserResponse> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var tenantId = _currentTenantService.TenantId 
            ?? throw new OismException("UNAUTHORIZED", "Yêu cầu đăng nhập.", 401);

        var assignedBranchIds = request.Role == "Owner"
            ? new List<Guid>()
            : await ValidateBranchIdsAsync(tenantId, request.BranchIds, ct) ?? new List<Guid>();

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        // Kiểm tra trùng lặp email trên toàn hệ thống (uq_users_2)
        var emailExists = await _context.Users.IgnoreQueryFilters()
            .AnyAsync(u => u.Email == normalizedEmail, ct);
        if (emailExists)
        {
            throw new OismException("EMAIL_ALREADY_EXISTS", "Email này đã được sử dụng bởi một tài khoản khác.", 409);
        }

        // Kiểm tra trùng lặp số điện thoại nếu có cung cấp (uq_users_3)
        string? normalizedPhone = null;
        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            normalizedPhone = NormalizePhoneOrThrow(request.Phone);
            var phoneExists = await _context.Users.IgnoreQueryFilters()
                .AnyAsync(u => u.Phone == normalizedPhone, ct);
            if (phoneExists)
            {
                throw new OismException("PHONE_ALREADY_EXISTS", "Số điện thoại này đã được sử dụng bởi một tài khoản khác.", 409);
            }
        }

        var passwordHash = _passwordHasher.HashPassword(request.Password);

        var user = new User
        {
            TenantId = tenantId,
            DisplayName = request.DisplayName.Trim(),
            Email = normalizedEmail,
            Phone = normalizedPhone,
            PasswordHash = passwordHash,
            Role = request.Role,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.Users.Add(user);

        if (assignedBranchIds.Count > 0)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var branchId in assignedBranchIds)
            {
                _context.UserBranches.Add(new UserBranch
                {
                    TenantId = tenantId,
                    UserId = user.Id,
                    BranchId = branchId,
                    CreatedAt = now
                });
            }
        }

        await SaveUserChangesAsync(ct);

        return MapToResponse(user, assignedBranchIds);
    }

    public async Task<UserResponse> UpdateUserAsync(
        Guid id,
        UpdateUserRequest request,
        Guid currentUserId,
        CancellationToken ct = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user == null)
        {
            throw new OismException("USER_NOT_FOUND", "Không tìm thấy thông tin nhân viên.", 404);
        }

        // Bảo vệ tài khoản: Không được tự khóa hoặc tự hạ quyền tài khoản của chính mình
        if (id == currentUserId)
        {
            if (!request.IsActive)
            {
                throw new OismException("CANNOT_DEACTIVATE_SELF", "Không thể tự vô hiệu hóa tài khoản của chính mình.", 400);
            }
            if (user.Role == "Owner" && request.Role != "Owner")
            {
                throw new OismException("CANNOT_DEMOTE_SELF", "Không thể tự hạ quyền của chính mình.", 400);
            }
        }

        // Vai trò Owner chỉ thuộc về Chủ shop tạo ra khi đăng ký gian hàng
        if (user.Role != "Owner" && request.Role == "Owner")
        {
            throw new OismException("CANNOT_ASSIGN_OWNER", "Không thể gán vai trò Chủ shop cho nhân viên.", 400);
        }

        // Kiểm tra branchIds TRƯỚC khi thay đổi bất kỳ thực thể nào trong context
        var isOwner = user.Role == "Owner";
        List<Guid>? validatedBranchIds = isOwner
            ? new List<Guid>()
            : await ValidateBranchIdsAsync(user.TenantId, request.BranchIds, ct);

        // Kiểm tra trùng số điện thoại nếu có thay đổi
        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            var normalizedPhone = NormalizePhoneOrThrow(request.Phone);
            if (user.Phone != normalizedPhone)
            {
                var phoneExists = await _context.Users.IgnoreQueryFilters()
                    .AnyAsync(u => u.Id != id && u.Phone == normalizedPhone, ct);
                if (phoneExists)
                {
                    throw new OismException("PHONE_ALREADY_EXISTS", "Số điện thoại này đã được sử dụng bởi một tài khoản khác.", 409);
                }
                user.Phone = normalizedPhone;
            }
        }
        else
        {
            user.Phone = null;
        }

        user.DisplayName = request.DisplayName.Trim();

        // Không cho phép đổi vai trò của Chủ shop (Owner)
        if (!isOwner)
        {
            user.Role = request.Role;
        }

        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        List<Guid> finalBranchIds;
        if (isOwner)
        {
            var ownerAssignments = await _context.UserBranches
                .Where(ub => ub.UserId == user.Id)
                .ToListAsync(ct);
            if (ownerAssignments.Count > 0)
            {
                _context.UserBranches.RemoveRange(ownerAssignments);
            }
            finalBranchIds = new List<Guid>();
        }
        else if (validatedBranchIds != null)
        {
            var existingAssignments = await _context.UserBranches
                .Where(ub => ub.UserId == user.Id)
                .ToListAsync(ct);

            var targetSet = validatedBranchIds.ToHashSet();
            var existingSet = existingAssignments.Select(ub => ub.BranchId).ToHashSet();

            var toRemove = existingAssignments.Where(ub => !targetSet.Contains(ub.BranchId)).ToList();
            if (toRemove.Count > 0)
            {
                _context.UserBranches.RemoveRange(toRemove);
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var branchId in validatedBranchIds.Where(bid => !existingSet.Contains(bid)))
            {
                _context.UserBranches.Add(new UserBranch
                {
                    TenantId = user.TenantId,
                    UserId = user.Id,
                    BranchId = branchId,
                    CreatedAt = now
                });
            }

            finalBranchIds = validatedBranchIds;
        }
        else
        {
            finalBranchIds = await _context.UserBranches
                .AsNoTracking()
                .Where(ub => ub.UserId == user.Id)
                .Select(ub => ub.BranchId)
                .Distinct()
                .ToListAsync(ct);
        }

        await SaveUserChangesAsync(ct);

        return MapToResponse(user, finalBranchIds);
    }

    public async Task<UserResponse> UpdateUserStatusAsync(
        Guid id,
        bool isActive,
        Guid currentUserId,
        CancellationToken ct = default)
    {
        if (id == currentUserId && !isActive)
        {
            throw new OismException("CANNOT_DEACTIVATE_SELF", "Không thể tự vô hiệu hóa tài khoản của chính mình.", 400);
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user == null)
        {
            throw new OismException("USER_NOT_FOUND", "Không tìm thấy thông tin nhân viên.", 404);
        }

        user.IsActive = isActive;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        var branchIds = user.Role == "Owner"
            ? new List<Guid>()
            : await _context.UserBranches
                .AsNoTracking()
                .Where(ub => ub.UserId == user.Id)
                .Select(ub => ub.BranchId)
                .Distinct()
                .ToListAsync(ct);

        await _context.SaveChangesAsync(ct);

        return MapToResponse(user, branchIds);
    }

    public async Task ResetPasswordAsync(Guid id, string newPassword, CancellationToken ct = default)
    {
        // Query filter tenant giới hạn trong cửa hàng của Owner: người dùng cửa hàng khác trả 404 như không tồn tại.
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user == null)
        {
            throw new OismException("USER_NOT_FOUND", "Không tìm thấy thông tin nhân viên.", 404);
        }

        if (user.Role == "Owner")
        {
            throw new OismException(
                "CANNOT_RESET_OWNER_PASSWORD",
                "Không thể đặt lại mật khẩu hộ tài khoản Chủ shop. Chủ shop dùng chức năng Quên mật khẩu qua email.",
                403);
        }

        var now = DateTimeOffset.UtcNow;
        user.PasswordHash = _passwordHasher.HashPassword(newPassword);
        user.UpdatedAt = now;
        await RefreshTokenRevocation.MarkAllRevokedAsync(_context, user.Id, now, ct);

        // Một lần SaveChanges là một transaction: đổi mật khẩu và thu hồi phiên cùng thành công hoặc cùng thất bại.
        await _context.SaveChangesAsync(ct);
    }

    private async Task<List<Guid>?> ValidateBranchIdsAsync(
        Guid tenantId,
        List<Guid>? branchIds,
        CancellationToken ct)
    {
        if (branchIds == null)
        {
            return null;
        }

        if (branchIds.Count == 0)
        {
            return new List<Guid>();
        }

        var distinctIds = branchIds.Distinct().ToList();
        if (distinctIds.Contains(Guid.Empty))
        {
            throw new OismException("BRANCH_NOT_FOUND", "Chi nhánh được chọn không tồn tại hoặc không thuộc cửa hàng của bạn.", 400);
        }

        var validCount = await _context.Branches
            .AsNoTracking()
            .Where(b => b.TenantId == tenantId && distinctIds.Contains(b.Id))
            .Select(b => b.Id)
            .Distinct()
            .CountAsync(ct);

        if (validCount != distinctIds.Count)
        {
            throw new OismException("BRANCH_NOT_FOUND", "Chi nhánh được chọn không tồn tại hoặc không thuộc cửa hàng của bạn.", 400);
        }

        return distinctIds;
    }

    private static UserResponse MapToResponse(User user, List<Guid> branchIds) => new()
    {
        Id = user.Id,
        TenantId = user.TenantId,
        Email = user.Email,
        DisplayName = user.DisplayName,
        Phone = user.Phone,
        Role = user.Role,
        IsActive = user.IsActive,
        BranchIds = branchIds,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt
    };

    private static string NormalizePhoneOrThrow(string rawPhone)
    {
        if (!PhoneHelper.TryNormalizePhoneNumber(rawPhone, out var normalizedPhone) || normalizedPhone == null)
        {
            throw new OismException("INVALID_PHONE", "Định dạng số điện thoại không hợp lệ. Hỗ trợ số điện thoại Việt Nam (ví dụ 0901234567) hoặc chuẩn quốc tế E.164 (ví dụ +84901234567).", 400);
        }

        return normalizedPhone;
    }

    /// <summary>
    /// Hai request đồng thời có thể cùng vượt qua bước kiểm tra trùng; ràng buộc unique của DB là chốt chặn cuối.
    /// </summary>
    private async Task SaveUserChangesAsync(CancellationToken ct)
    {
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("uq_users_2") == true)
        {
            throw new OismException("EMAIL_ALREADY_EXISTS", "Email này đã được sử dụng bởi một tài khoản khác.", 409);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("uq_users_3") == true)
        {
            throw new OismException("PHONE_ALREADY_EXISTS", "Số điện thoại này đã được sử dụng bởi một tài khoản khác.", 409);
        }
    }
}
