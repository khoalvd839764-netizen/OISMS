using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Helpers;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.CurrentUser;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.RefreshToken;
using OISM.Application.Features.Auth.RegisterTenant;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;

namespace OISM.WebApi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IJwtTokenService _jwtTokenService;

    public AuthController(AppDbContext context, IJwtTokenService jwtTokenService)
    {
        _context = context;
        _jwtTokenService = jwtTokenService;
    }

    /// <summary>
    /// Đăng ký Cửa hàng (Tenant) mới kèm tài khoản Chủ shop (Owner) ban đầu (FR-AUTH-01).
    /// </summary>
    [HttpPost("register-tenant")]
    public async Task<IActionResult> RegisterTenant([FromBody] RegisterTenantRequest request)
    {
        var validator = new RegisterTenantValidator();
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        // 1. Chuẩn hóa Email và SĐT
        var emailNormalized = request.Email.Trim().ToLowerInvariant();
        string? phoneNormalized = null;
        if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            if (!PhoneHelper.TryNormalizePhoneNumber(request.PhoneNumber, out phoneNormalized))
            {
                return BadRequest(new { errors = new[] { "Định dạng số điện thoại không hợp lệ. Hỗ trợ số điện thoại Việt Nam (ví dụ 0901234567) hoặc chuẩn quốc tế E.164 (ví dụ +84901234567)." } });
            }
        }

        var codeNormalized = request.TenantCode.Trim().ToUpperInvariant();

        // 2. Pre-check Email và Phone đã dùng chưa (BẮT BUỘC dùng IgnoreQueryFilters để quét toàn hệ thống)
        var emailExists = await _context.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Email != null && u.Email.ToLower() == emailNormalized);

        if (emailExists)
        {
            return Conflict(new { message = "Email này đã được sử dụng bởi một tài khoản khác trong hệ thống." });
        }

        if (phoneNormalized != null)
        {
            var phoneExists = await _context.Users
                .IgnoreQueryFilters()
                .AnyAsync(u => u.Phone == phoneNormalized);

            if (phoneExists)
            {
                return Conflict(new { message = "Số điện thoại này đã được sử dụng bởi một tài khoản khác trong hệ thống." });
            }
        }

        // 3. Thực thi trong Database Transaction nguyên tử (ACID) với Privileged Scope có kiểm soát và Execution Strategy
        var strategy = _context.Database.CreateExecutionStrategy();
        RegisterTenantResponse? response = null;

        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                using var privilegedScope = _context.OpenPrivilegedScope();
                await using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    // Bước A: Tạo Cửa hàng
                    var tenant = new Tenant
                    {
                        Name = request.TenantName.Trim(),
                        Code = codeNormalized,
                        IsActive = true
                    };
                    _context.Tenants.Add(tenant);
                    await _context.SaveChangesAsync();

                    // Bước B: Tạo Tài khoản Chủ shop với vai trò Owner trực tiếp theo schema v0.5
                    var user = new User
                    {
                        TenantId = tenant.Id,
                        Email = emailNormalized,
                        DisplayName = request.FullName.Trim(),
                        Phone = phoneNormalized,
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                        Role = "Owner",
                        IsActive = true
                    };
                    _context.Users.Add(user);
                    await _context.SaveChangesAsync();

                    // Hoàn tất Transaction
                    await transaction.CommitAsync();

                    response = new RegisterTenantResponse
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
                    await transaction.RollbackAsync();
                    throw;
                }
            });

            return StatusCode(201, response);
        }
        catch (DbUpdateException ex)
        {
            if (ex.InnerException is Npgsql.PostgresException pgEx)
            {
                if (pgEx.SqlState == "23505")
                {
                    if (pgEx.ConstraintName == "uq_users_2" || pgEx.Message.Contains("uq_users_2"))
                    {
                        return Conflict(new { message = "Email này đã được sử dụng bởi một tài khoản khác trong hệ thống." });
                    }
                    if (pgEx.ConstraintName == "uq_users_3" || pgEx.Message.Contains("uq_users_3"))
                    {
                        return Conflict(new { message = "Số điện thoại này đã được sử dụng bởi một tài khoản khác trong hệ thống." });
                    }
                    return Conflict(new { message = "Dữ liệu đăng ký bị trùng lặp trong hệ thống." });
                }
                if (pgEx.SqlState == "23514")
                {
                    return BadRequest(new { message = "Dữ liệu không thỏa mãn ràng buộc hệ thống." });
                }
            }
            return StatusCode(500, new { message = "Đã xảy ra lỗi khi lưu thông tin đăng ký vào cơ sở dữ liệu." });
        }
        catch (Exception)
        {
            return StatusCode(500, new { message = "Đã xảy ra lỗi trong quá trình xử lý đăng ký." });
        }
    }

    /// <summary>
    /// Đăng nhập hệ thống bằng Email và Mật khẩu (FR-AUTH-02).
    /// Trả về JWT Access Token (hạn 60p) và Refresh Token (hạn 7 ngày).
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var validator = new LoginValidator();
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        var emailNormalized = request.Email.Trim().ToLower();

        // Tìm người dùng theo Email (dùng IgnoreQueryFilters vì chưa có tenant context khi đăng nhập)
        var user = await _context.Users
            .IgnoreQueryFilters()
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == emailNormalized);

        if (user == null)
        {
            return Unauthorized(new { message = "Email hoặc mật khẩu không chính xác." });
        }

        // Kiểm tra mật khẩu bằng BCrypt
        bool passwordMatches = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);
        if (!passwordMatches)
        {
            return Unauthorized(new { message = "Email hoặc mật khẩu không chính xác." });
        }

        // Kiểm tra trạng thái kích hoạt của User và Tenant
        if (!user.IsActive)
        {
            return Unauthorized(new { message = "Tài khoản của bạn đã bị khóa." });
        }

        if (user.Tenant != null && !user.Tenant.IsActive)
        {
            return Unauthorized(new { message = "Cửa hàng của bạn hiện đang bị tạm ngưng." });
        }

        // Lấy danh sách Roles từ user.Role
        var roles = new List<string> { user.Role };

        // Sinh JWT Access Token và Refresh Token
        var accessToken = _jwtTokenService.GenerateAccessToken(user, roles);
        var refreshToken = _jwtTokenService.GenerateRefreshToken(user.TenantId, user.Id);

        using (_context.OpenPrivilegedScope())
        {
            _context.RefreshTokens.Add(refreshToken);
            await _context.SaveChangesAsync();
        }

        var response = new AuthResponse
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

        return Ok(response);
    }

    /// <summary>
    /// Làm mới Access Token bằng Refresh Token (Token Rotation nguyên tử, chống race condition trên PostgreSQL).
    /// </summary>
    [HttpPost("refresh-token")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return BadRequest(new { message = "Refresh token không được để trống." });
        }

        var tokenHash = Domain.Entities.RefreshToken.HashToken(request.RefreshToken);

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            using var privilegedScope = _context.OpenPrivilegedScope();
            await using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);

            // Trên PostgreSQL: Khóa bi quan hàng refresh_token bằng FOR UPDATE để bảo đảm rotation nguyên tử
            if (_context.Database.IsNpgsql())
            {
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT id FROM refresh_tokens WHERE token_hash = {tokenHash} FOR UPDATE");
            }

            var existingToken = await _context.RefreshTokens
                .IgnoreQueryFilters()
                .Include(r => r.User)
                    .ThenInclude(u => u!.Tenant)
                .FirstOrDefaultAsync(r => r.TokenHash == tokenHash);

            if (existingToken == null || !existingToken.IsActive)
            {
                await tx.RollbackAsync();
                return Unauthorized(new { message = "Refresh token không hợp lệ hoặc đã hết hạn/bị thu hồi." });
            }

            var user = existingToken.User;
            if (user == null || !user.IsActive)
            {
                await tx.RollbackAsync();
                return Unauthorized(new { message = "Tài khoản không tồn tại hoặc đã bị khóa." });
            }

            if (user.Tenant == null || !user.Tenant.IsActive)
            {
                await tx.RollbackAsync();
                return Unauthorized(new { message = "Cửa hàng của bạn hiện đang bị tạm ngưng." });
            }

            // Thu hồi token cũ và sinh cặp token mới (Token Rotation)
            existingToken.RevokedAt = DateTimeOffset.UtcNow;

            var roles = new List<string> { user.Role };

            var newAccessToken = _jwtTokenService.GenerateAccessToken(user, roles);
            var newRefreshToken = _jwtTokenService.GenerateRefreshToken(user.TenantId, user.Id);
            existingToken.ReplacedByToken = newRefreshToken.Token;

            _context.RefreshTokens.Add(newRefreshToken);
            await _context.SaveChangesAsync();

            await tx.CommitAsync();

            var response = new AuthResponse
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

            return Ok(response);
        });
    }

    /// <summary>
    /// Đăng xuất tài khoản (Thu hồi Refresh Token).
    /// </summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            var tokenHash = Domain.Entities.RefreshToken.HashToken(request.RefreshToken);
            var token = await _context.RefreshTokens
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.TokenHash == tokenHash);

            if (token != null)
            {
                using (_context.OpenPrivilegedScope())
                {
                    token.RevokedAt = DateTimeOffset.UtcNow;
                    await _context.SaveChangesAsync();
                }
            }
        }

        return Ok(new { message = "Đăng xuất thành công." });
    }

    /// <summary>
    /// Lấy thông tin tài khoản hiện tại của người dùng đang đăng nhập (FR-AUTH-02 / me).
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        var userIdClaim = User.FindFirst("userId")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { message = "Không xác định được phiên người dùng." });
        }

        var user = await _context.Users
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return NotFound(new { message = "Không tìm thấy thông tin người dùng." });
        }

        var response = new CurrentUserResponse
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

        return Ok(response);
    }
}
