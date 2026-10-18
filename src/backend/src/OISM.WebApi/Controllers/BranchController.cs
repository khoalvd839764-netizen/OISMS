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
    /// Lấy danh sách chi nhánh thuộc về Cửa hàng của phiên đăng nhập (FR-AUTH-04).
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
    /// Lấy thông tin chi tiết một chi nhánh theo Id.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetBranchById(Guid id)
    {
        var branch = await _context.Branches.FirstOrDefaultAsync(b => b.Id == id);
        if (branch == null)
        {
            return NotFound(new { message = "Không tìm thấy chi nhánh." });
        }

        return Ok(new BranchResponse
        {
            Id = branch.Id,
            Name = branch.Name,
            Address = branch.Address,
            Phone = branch.Phone,
            IsActive = branch.IsActive
        });
    }

    /// <summary>
    /// Tạo chi nhánh mới (Chỉ dành cho Owner - FR-AUTH-04, BE2-08).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> CreateBranch([FromBody] CreateBranchRequest request)
    {
        var validator = new CreateBranchValidator();
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
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
    /// Cập nhật thông tin chi nhánh (Chỉ dành cho Owner - BE2-08).
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> UpdateBranch(Guid id, [FromBody] UpdateBranchRequest request)
    {
        var validator = new UpdateBranchValidator();
        var validationResult = await validator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        var branch = await _context.Branches.FirstOrDefaultAsync(b => b.Id == id);
        if (branch == null)
        {
            return NotFound(new { message = "Không tìm thấy chi nhánh." });
        }

        branch.Name = request.Name.Trim();
        branch.Address = request.Address?.Trim();
        branch.Phone = request.Phone?.Trim();
        branch.IsActive = request.IsActive;

        await _context.SaveChangesAsync();

        return Ok(new BranchResponse
        {
            Id = branch.Id,
            Name = branch.Name,
            Address = branch.Address,
            Phone = branch.Phone,
            IsActive = branch.IsActive
        });
    }

    /// <summary>
    /// Bật/Tắt trạng thái hoạt động của chi nhánh (FR-AUTH-04).
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
