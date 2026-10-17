using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OISM.Application.Features.Branches;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;

namespace OISM.WebApi.Controllers;

[ApiController]
[Route("api/branches")]
[Authorize]
public class BranchController : ControllerBase
{
    private readonly AppDbContext _context;

    public BranchController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Lấy danh sách chi nhánh/kho hàng thuộc về Cửa hàng của phiên đăng nhập (FR-AUTH-04).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetBranches()
    {
        var branches = await _context.Branches
            .Select(b => new BranchResponse
            {
                Id = b.Id,
                Name = b.Name,
                Address = b.Address,
                Phone = b.Phone,
                IsActive = b.IsActive
            })
            .ToListAsync();

        return Ok(branches);
    }

    /// <summary>
    /// Tạo chi nhánh / kho hàng mới (Chỉ dành cho Owner - FR-AUTH-04).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> CreateBranch([FromBody] CreateBranchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Tên chi nhánh không được để trống." });
        }

        var branch = new Branch
        {
            Name = request.Name.Trim(),
            Address = request.Address?.Trim(),
            Phone = request.Phone?.Trim(),
            IsActive = true
            // TenantId được tự động gán bởi AppDbContext.ApplyAuditAndTenantInfo()
        };

        _context.Branches.Add(branch);
        await _context.SaveChangesAsync();

        var response = new BranchResponse
        {
            Id = branch.Id,
            Name = branch.Name,
            Address = branch.Address,
            Phone = branch.Phone,
            IsActive = branch.IsActive
        };

        return StatusCode(201, response);
    }

    /// <summary>
    /// Bật/Tắt trạng thái hoạt động của chi nhánh/kho hàng (FR-AUTH-04).
    /// </summary>
    [HttpPut("{id}/toggle-status")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> ToggleStatus(Guid id)
    {
        var branch = await _context.Branches.FirstOrDefaultAsync(b => b.Id == id);
        if (branch == null)
        {
            return NotFound(new { message = "Không tìm thấy chi nhánh." });
        }

        branch.IsActive = !branch.IsActive;
        await _context.SaveChangesAsync();

        return Ok(new { message = $"Đã {(branch.IsActive ? "kích hoạt" : "ngưng hoạt động")} chi nhánh thành công.", isActive = branch.IsActive });
    }
}
