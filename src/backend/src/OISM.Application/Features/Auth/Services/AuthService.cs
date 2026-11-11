using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Helpers;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.CurrentUser;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.RefreshToken;
using OISM.Application.Features.Auth.RegisterTenant;
using OISM.Domain.Entities;
using RefreshTokenEntity = OISM.Domain.Entities.RefreshToken;

namespace OISM.Application.Features.Auth.Services;

/// <summary>
/// Hiện thực hóa nghiệp vụ Xác thực, Quản trị Tenant và Cấp phát Token (FR-AUTH-01, FR-AUTH-02, NFR-SEC-01).
/// </summary>
public class AuthService : IAuthService
{
    private readonly IAppDbContext _context;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISqlLockService _lockService;

    public AuthService(
        IAppDbContext context,
        IJwtTokenService jwtTokenService,
        IPasswordHasher passwordHasher,
        ISqlLockService lockService)
    {
        _context = context;
        _jwtTokenService = jwtTokenService;
        _passwordHasher = passwordHasher;
        _lockService = lockService;
    }

    public async Task<RegisterTenantResponse> RegisterTenantAsync(RegisterTenantRequest request, CancellationToken ct = default)
    {
        var emailNormalized = request.Email.Trim().ToLowerInvariant();
        string? phoneNormalized = null;
        if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            if (!PhoneHelper.TryNormalizePhoneNumber(request.PhoneNumber, out phoneNormalized))
            {
                throw new OismException("INVALID_PHONE", "Định dạng số điện thoại không hợp lệ. Hỗ trợ số điện thoại Việt Nam (ví dụ 0901234567) hoặc chuẩn quốc tế E.164 (ví dụ +84901234567).", 400);
            }
        }

        var codeNormalized = request.TenantCode.Trim().ToUpperInvariant();

        // Pre-check Email và Phone đã dùng chưa (BẮT BUỘC dùng IgnoreQueryFilters để quét toàn hệ thống)
        var emailExists = await _context.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Email != null && u.Email.ToLower() == emailNormalized, ct);

        if (emailExists)
        {
            throw new OismException("EMAIL_ALREADY_EXISTS", "Email này đã được sử dụng bởi một tài khoản khác trong hệ thống.", 409);
        }

        if (phoneNormalized != null)
        {
            var phoneExists = await _context.Users
                .IgnoreQueryFilters()
                .AnyAsync(u => u.Phone == phoneNormalized, ct);

            if (phoneExists)
            {
                throw new OismException("PHONE_ALREADY_EXISTS", "Số điện thoại này đã được sử dụng bởi một tài khoản khác trong hệ thống.", 409);
            }
        }

        try
        {
            return await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
            {
                using var privilegedScope = _context.OpenPrivilegedScope();
                await using var transaction = await _context.UnitOfWork.BeginTransactionAsync(ct);

                try
                {
                    var tenant = new Tenant
                    {
                        Name = request.TenantName.Trim(),
                        Code = codeNormalized,
                        IsActive = true
                    };
                    _context.Tenants.Add(tenant);
                    await _context.SaveChangesAsync(ct);

                    var user = new User
                    {
                        TenantId = tenant.Id,
                        Email = emailNormalized,
                        DisplayName = request.FullName.Trim(),
                        Phone = phoneNormalized,
                        PasswordHash = _passwordHasher.HashPassword(request.Password),
                        Role = "Owner",
                        IsActive = true
                    };
                    _context.Users.Add(user);
                    await _context.SaveChangesAsync(ct);

                    await transaction.CommitAsync(ct);

                    return new RegisterTenantResponse
                    {
                        TenantId = tenant.Id,
                        TenantCode = tenant.Code,
                        TenantName = tenant.Name,
                        UserId = user.Id,
                        Email = user.Email,
                        Message = "Đăng ký Cửa hàng và tài khoản Chủ shop thành công!"
                    };
                }
                catch
                {
                    await transaction.RollbackAsync(ct);
                    throw;
                }
            });
        }
        catch (DbUpdateException ex)
        {
            if (_context.DatabaseErrors.IsUniqueViolation(ex, "uq_users_2"))
            {
                throw new OismException("EMAIL_ALREADY_EXISTS", "Email này đã được sử dụng bởi một tài khoản khác trong hệ thống.", 409);
            }
            if (_context.DatabaseErrors.IsUniqueViolation(ex, "uq_users_3"))
            {
                throw new OismException("PHONE_ALREADY_EXISTS", "Số điện thoại này đã được sử dụng bởi một tài khoản khác trong hệ thống.", 409);
            }
            if (_context.DatabaseErrors.IsUniqueViolation(ex))
            {
                throw new OismException("CONFLICT", "Dữ liệu đăng ký bị trùng lặp trong hệ thống.", 409);
            }
            if (_context.DatabaseErrors.IsCheckViolation(ex))
            {
                throw new OismException("CONSTRAINT_VIOLATION", "Dữ liệu không thỏa mãn ràng buộc hệ thống.", 400);
            }
            throw;
        }
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var emailNormalized = request.Email.Trim().ToLowerInvariant();

        var user = await _context.Users
            .IgnoreQueryFilters()
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == emailNormalized, ct);

        if (user == null)
        {
            throw new OismException("INVALID_CREDENTIALS", "Email hoặc mật khẩu không chính xác.", 401);
        }

        bool passwordMatches = _passwordHasher.VerifyPassword(request.Password, user.PasswordHash);
        if (!passwordMatches)
        {
            throw new OismException("INVALID_CREDENTIALS", "Email hoặc mật khẩu không chính xác.", 401);
        }

        if (!user.IsActive)
        {
            throw new OismException("USER_INACTIVE", "Tài khoản của bạn đã bị khóa.", 401);
        }

        if (user.Tenant != null && !user.Tenant.IsActive)
        {
            throw new OismException("TENANT_INACTIVE", "Cửa hàng của bạn hiện đang bị tạm ngưng.", 401);
        }

        var roles = new List<string> { user.Role };
        var accessToken = _jwtTokenService.GenerateAccessToken(user, roles);
        var refreshToken = _jwtTokenService.GenerateRefreshToken(user.TenantId, user.Id);

        using (_context.OpenPrivilegedScope())
        {
            _context.RefreshTokens.Add(refreshToken);
            await _context.SaveChangesAsync(ct);
        }

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            TokenType = "Bearer",
            ExpiresIn = 3600,
            User = new UserProfileDto
            {
                Id = user.Id,
                TenantId = user.TenantId,
                TenantCode = user.Tenant?.Code ?? string.Empty,
                TenantName = user.Tenant?.Name ?? string.Empty,
                Email = user.Email,
                FullName = user.FullName,
                PhoneNumber = user.PhoneNumber,
                Roles = roles
            }
        };
    }

    public async Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw new OismException("EMPTY_TOKEN", "Refresh token không được để trống.", 400);
        }

        var tokenHash = RefreshTokenEntity.HashToken(request.RefreshToken);
        return await _context.UnitOfWork.ExecuteWithRetryAsync(async () =>
        {
            using var privilegedScope = _context.OpenPrivilegedScope();
            await using var tx = await _context.UnitOfWork.BeginTransactionAsync(ct);

            try
            {
                await _lockService.LockRefreshTokenAsync(tokenHash, ct);

                var existingToken = await _context.RefreshTokens
                    .IgnoreQueryFilters()
                    .Include(r => r.User)
                        .ThenInclude(u => u!.Tenant)
                    .FirstOrDefaultAsync(r => r.TokenHash == tokenHash, ct);

                if (existingToken == null || !existingToken.IsActive)
                {
                    throw new OismException("INVALID_TOKEN", "Refresh token không hợp lệ hoặc đã hết hạn/bị thu hồi.", 401);
                }

                var user = existingToken.User;
                if (user == null || !user.IsActive)
                {
                    throw new OismException("USER_INACTIVE", "Tài khoản không tồn tại hoặc đã bị khóa.", 401);
                }

                if (user.Tenant == null || !user.Tenant.IsActive)
                {
                    throw new OismException("TENANT_INACTIVE", "Cửa hàng của bạn hiện đang bị tạm ngưng.", 401);
                }

                existingToken.RevokedAt = DateTimeOffset.UtcNow;

                var roles = new List<string> { user.Role };
                var newAccessToken = _jwtTokenService.GenerateAccessToken(user, roles);
                var newRefreshToken = _jwtTokenService.GenerateRefreshToken(user.TenantId, user.Id);
                existingToken.ReplacedByToken = newRefreshToken.Token;

                _context.RefreshTokens.Add(newRefreshToken);
                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return new AuthResponse
                {
                    AccessToken = newAccessToken,
                    RefreshToken = newRefreshToken.Token,
                    TokenType = "Bearer",
                    ExpiresIn = 3600,
                    User = new UserProfileDto
                    {
                        Id = user.Id,
                        TenantId = user.TenantId,
                        TenantCode = user.Tenant?.Code ?? string.Empty,
                        TenantName = user.Tenant?.Name ?? string.Empty,
                        Email = user.Email,
                        FullName = user.FullName,
                        PhoneNumber = user.PhoneNumber,
                        Roles = roles
                    }
                };
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        });
    }

    public async Task RevokeTokenAsync(RefreshTokenRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken)) return;

        var tokenHash = RefreshTokenEntity.HashToken(request.RefreshToken);
        using (_context.OpenPrivilegedScope())
        {
            var token = await _context.RefreshTokens
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.TokenHash == tokenHash, ct);

            if (token != null)
            {
                token.RevokedAt = DateTimeOffset.UtcNow;
                await _context.SaveChangesAsync(ct);
            }
        }
    }

    public async Task<CurrentUserResponse?> GetCurrentUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _context.Users
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user == null)
        {
            return null;
        }

        return new CurrentUserResponse
        {
            Id = user.Id,
            TenantId = user.TenantId,
            TenantCode = user.Tenant?.Code ?? string.Empty,
            TenantName = user.Tenant?.Name ?? string.Empty,
            Email = user.Email,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            Roles = new List<string> { user.Role }
        };
    }
}
