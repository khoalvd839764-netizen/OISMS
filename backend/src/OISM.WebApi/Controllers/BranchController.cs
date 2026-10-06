using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using OISM.Application.Features_Branches;
using OISM.Infrastructure.Persistence;
using OISM.Domain.Entities;

namespace OISM.WebApi.Controllers
{
    [ApiController]
    [Route("api/branches")]
    public class BranchController : ControllerBase
    {
        private readonly AppDbContext _context;

        public BranchController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> CreateBranch([FromBody] CreateBranchRequest request)
        {
            var branch = new Branch
            {
                Name = request.Name,
                Address = request.Address,
                Phone = request.Phone
                // KHÔNG CẦN gán TenantId ở đây, hệ thống của Phương Lê sẽ tự lấy tự gán!
            };

            _context.Branches.Add(branch);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Tạo chi nhánh thành công!" });
        }

        [HttpGet]
        public async Task<IActionResult> GetBranches()
        {
            // KHÔNG CẦN viết lệnh WHERE TenantId = ... 
            // EF Core sẽ tự động bóp lại chỉ lấy chi nhánh của Shop đang đăng nhập
            var branches = await _context.Branches
                .Select(b => new BranchResponse
                {
                    Id = b.Id,
                    Name = b.Name,
                    Address = b.Address,
                    Phone = b.Phone ?? ""
                }).ToListAsync();

            return Ok(branches);
        }
    }
}