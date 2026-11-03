using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Helpers;
using OISM.Application.Common.Interfaces;
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

        return await query
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new UserResponse
            {
                Id = u.Id,
                TenantId = u.TenantId,
                Email = u.Email,
                DisplayName = u.DisplayName,
                Phone = u.Phone,
                Role = u.Role,
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt,
                UpdatedAt = u.UpdatedAt
            })
            .ToListAsync(ct);
    }

    public async Task<UserResponse?> GetUserByIdAsync(Guid id, CancellationToken ct = default)
    {
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user == null)
        {
            return null;
        }

        return new UserResponse
        {
            Id = user.Id,
            TenantId = user.TenantId,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Phone = user.Phone,
            Role = user.Role,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
    }

    public async Task<UserResponse> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var tenantId = _currentTenantService.TenantId 
            ?? throw new OismException("UNAUTHORIZED", "Yêu cầu đăng nhập.", 401);

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
        await SaveUserChangesAsync(ct);

        return new UserResponse
        {
            Id = user.Id,
            TenantId = user.TenantId,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Phone = user.Phone,
            Role = user.Role,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
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
        if (user.Role != "Owner")
        {
            user.Role = request.Role;
        }

        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await SaveUserChangesAsync(ct);

        return new UserResponse
        {
            Id = user.Id,
            TenantId = user.TenantId,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Phone = user.Phone,
            Role = user.Role,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
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

        await _context.SaveChangesAsync(ct);

        return new UserResponse
        {
            Id = user.Id,
            TenantId = user.TenantId,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Phone = user.Phone,
            Role = user.Role,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
    }

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
