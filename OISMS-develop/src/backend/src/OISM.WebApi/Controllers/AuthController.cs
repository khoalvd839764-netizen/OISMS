using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

        // 1. Kiểm tra Email đã dùng chưa (BẮT BUỘC dùng IgnoreQueryFilters để quét toàn hệ thống)
        var emailNormalized = request.Email.Trim().ToLower();
        var emailExists = await _context.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Email.ToLower() == emailNormalized);

        if (emailExists)
        {
            return Conflict(new { message = "Email này đã được sử dụng bởi một tài khoản khác trong hệ thống." });
        }

        // 2. Chuẩn hóa TenantCode và SĐT
        var codeNormalized = request.TenantCode.Trim().ToUpper();
        string? phoneNormalized = null;
        if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            var rawPhone = request.PhoneNumber.Trim();
            if (rawPhone.StartsWith("0"))
            {
                phoneNormalized = "+84" + rawPhone.Substring(1);
            }
            else if (!rawPhone.StartsWith("+"))
            {
                phoneNormalized = "+" + rawPhone;
            }
            else
            {
                phoneNormalized = rawPhone;
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
            });

            return StatusCode(201, response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi trong quá trình đăng ký cửa hàng: " + ex.Message });
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
    /// Làm mới Access Token bằng Refresh Token (Token Rotation).
    /// </summary>
    [HttpPost("refresh-token")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return BadRequest(new { message = "Refresh token không được để trống." });
        }

        var tokenHash = Domain.Entities.RefreshToken.HashToken(request.RefreshToken);
        var existingToken = await _context.RefreshTokens
            .IgnoreQueryFilters()
            .Include(r => r.User)
                .ThenInclude(u => u!.Tenant)
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash);

        if (existingToken == null || !existingToken.IsActive)
        {
            return Unauthorized(new { message = "Refresh token không hợp lệ hoặc đã hết hạn/bị thu hồi." });
        }

        var user = existingToken.User;
        if (user == null || !user.IsActive)
        {
            return Unauthorized(new { message = "Tài khoản không tồn tại hoặc đã bị khóa." });
        }

        // Thu hồi token cũ và sinh cặp token mới (Token Rotation)
        existingToken.RevokedAt = DateTimeOffset.UtcNow;

        var roles = new List<string> { user.Role };

        var newAccessToken = _jwtTokenService.GenerateAccessToken(user, roles);
        var newRefreshToken = _jwtTokenService.GenerateRefreshToken(user.TenantId, user.Id);
        existingToken.ReplacedByToken = newRefreshToken.Token;

        using (_context.OpenPrivilegedScope())
        {
            _context.RefreshTokens.Add(newRefreshToken);
            await _context.SaveChangesAsync();
        }

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
