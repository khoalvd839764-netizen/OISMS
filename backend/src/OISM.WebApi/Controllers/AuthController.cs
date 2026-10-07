using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Features_Auth.RegisterTenant;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using OISM.WebApi;

namespace OISM.WebApi.Controllers;

// DTOs cho Login
public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
}

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

    // 1. API ĐĂNG KÝ TENANT
    [HttpPost("register-tenant")]
    public async Task<IActionResult> RegisterTenant([FromBody] RegisterTenantRequest request)
    {
        // 1. Kiểm tra Email trùng (BẮT BUỘC dùng IgnoreQueryFilters theo Quy tắc 1)
        var emailExists = await _context.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Email == request.Email);

        if (emailExists)
        {
            return BadRequest(new { message = "Email này đã được sử dụng!" });
        }

        // Kiểm tra TenantCode trùng (theo Quy tắc 3)
        var tenantCodeFormatted = request.TenantCode.Trim().ToUpper();
        var codeExists = await _context.Tenants.AnyAsync(t => t.Code == tenantCodeFormatted);
        if (codeExists)
        {
            return BadRequest(new { message = "Mã cửa hàng (TenantCode) đã tồn tại!" });
        }

        // 2. Bắt đầu Database Transaction
        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // Bước A: Tạo Cửa hàng (Tenant) với Code viết hoa
            var newTenant = new Tenant
            {
                Name = request.TenantName,
                Code = tenantCodeFormatted,
                IsActive = true
            };
            _context.Tenants.Add(newTenant);
            await _context.SaveChangesAsync(); // Lưu để sinh ra newTenant.Id

            // Bước B: Tạo Tài khoản Chủ shop (User)
            var newUser = new User
            {
                TenantId = newTenant.Id,
                Email = request.Email,
                FullName = request.FullName,
                PhoneNumber = request.PhoneNumber,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                IsActive = true
            };
            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            // Bước C: Gán quyền Owner (theo Quy tắc 3: kiểm tra theo r.Code)
            var ownerRole = await _context.Roles.FirstOrDefaultAsync(r => r.Code == "Owner");
            if (ownerRole != null)
            {
                var userRole = new UserRole
                {
                    UserId = newUser.Id,
                    RoleId = ownerRole.Id
                };
                _context.UserRoles.Add(userRole);
                await _context.SaveChangesAsync();
            }

            // Hoàn tất Transaction
            await transaction.CommitAsync();

            return Ok(new { message = "Đăng ký Cửa hàng thành công!" });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, new { message = "Lỗi hệ thống: " + ex.Message });
        }
    }

    // 2. API ĐĂNG NHẬP (MỚI THÊM)
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        // Kiểm tra User theo Email
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == request.Email);

        if (user == null)
        {
            return Unauthorized(new { message = "Email hoặc mật khẩu không chính xác." });
        }

        // Kiểm tra mật khẩu băm BCrypt
        bool isValidPassword = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);
        if (!isValidPassword)
        {
            return Unauthorized(new { message = "Email hoặc mật khẩu không chính xác." });
        }

        // Lấy Role của User
        var userRole = await _context.UserRoles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ur => ur.UserId == user.Id);

        string roleName = "User";
        if (userRole != null)
        {
            var role = await _context.Roles.FirstOrDefaultAsync(r => r.Id == userRole.RoleId);
            if (role != null)
            {
                roleName = role.Code;
            }
        }

        // Sinh JWT Token
        string token = _jwtTokenService.GenerateToken(user.Id, user.TenantId, user.Email, roleName);

        // Trả về thông tin thành công
        return Ok(new LoginResponse
        {
            AccessToken = token,
            ExpiresIn = 3600,
            FullName = user.FullName,
            Role = roleName,
            TenantId = user.TenantId
        });
    }
}