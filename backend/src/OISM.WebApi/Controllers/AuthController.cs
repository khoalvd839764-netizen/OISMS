using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using OISM.Application.Features_Auth.RegisterTenant;
// (Lưu ý: Nếu chữ AppDbContext bị gạch đỏ, bạn bấm chuột vào nó rồi nhấn Cmd + Dấu chấm (.) để VS Code tự Import thư viện nhé)

namespace OISM.WebApi.Controllers
{
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
            // 1. Kiểm tra Email trùng
            var emailExists = await _context.Users.AnyAsync(u => u.Email == request.Email);
            if (emailExists) return BadRequest("Email này đã được sử dụng!");

            // 2. Bắt đầu Transaction
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // BƯỚC A: Tạo Cửa hàng (Tenant)
                var newTenant = new Tenant { Name = request.TenantName };
                _context.Tenants.Add(newTenant);
                await _context.SaveChangesAsync(); // Lưu để sinh ra TenantId

                // BƯỚC B: Tạo Tài khoản Chủ shop (User)
                var newUser = new User
                {
                    TenantId = newTenant.Id, // Gắn đúng mã shop vừa tạo
                    Email = request.Email,
                    FullName = request.FullName,
                    PhoneNumber = request.PhoneNumber,
                    // Băm mật khẩu để bảo mật
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password) 
                };
                _context.Users.Add(newUser);
                await _context.SaveChangesAsync();

                // BƯỚC C: Gán quyền Owner (Chủ sở hữu)
                var ownerRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "Owner");
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
            catch (System.Exception ex)
            {
                await transaction.RollbackAsync(); // Có lỗi thì hủy hết
                return StatusCode(500, "Lỗi hệ thống: " + ex.Message);
            }
        }
    }
}