using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Features_Auth.RegisterTenant;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;

namespace OISM.WebApi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;

    public AuthController(AppDbContext context)
    {
        _context = context;
    }

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
}