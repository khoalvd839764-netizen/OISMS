using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Features.Branches;
using OISM.Application.Features.Branches.Services;

namespace OISM.WebApi.Controllers;

[ApiController]
[Route("api/branches")]
[Authorize]
public class BranchController : ControllerBase
{
    private readonly IBranchService _branchService;

    public BranchController(IBranchService branchService)
    {
        _branchService = branchService;
    }

    /// <summary>
    /// Lấy danh sách chi nhánh thuộc về Cửa hàng của phiên đăng nhập (FR-AUTH-04).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetBranches(CancellationToken ct)
    {
        var branches = await _branchService.GetBranchesAsync(ct);
        return Ok(branches);
    }

    /// <summary>
    /// Lấy thông tin chi tiết một chi nhánh theo Id.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetBranchById(Guid id, CancellationToken ct)
    {
        var branch = await _branchService.GetBranchByIdAsync(id, ct);
        if (branch == null)
        {
            return NotFound(new { message = "Không tìm thấy chi nhánh." });
        }

        return Ok(branch);
    }

    /// <summary>
    /// Tạo chi nhánh mới (Chỉ dành cho Owner - FR-AUTH-04, BE2-08).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> CreateBranch([FromBody] CreateBranchRequest request, CancellationToken ct)
    {
        var validator = new CreateBranchValidator();
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        var response = await _branchService.CreateBranchAsync(request, ct);
        return StatusCode(201, response);
    }

    /// <summary>
    /// Cập nhật thông tin chi nhánh (Chỉ dành cho Owner - BE2-08).
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> UpdateBranch(Guid id, [FromBody] UpdateBranchRequest request, CancellationToken ct)
    {
        var validator = new UpdateBranchValidator();
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        var response = await _branchService.UpdateBranchAsync(id, request, ct);
        if (response == null)
        {
            return NotFound(new { message = "Không tìm thấy chi nhánh." });
        }

        return Ok(response);
    }

    /// <summary>
    /// Bật/Tắt trạng thái hoạt động của chi nhánh (FR-AUTH-04).
    /// </summary>
    [HttpPut("{id}/toggle-status")]
    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> ToggleStatus(Guid id, CancellationToken ct)
    {
        var result = await _branchService.ToggleStatusAsync(id, ct);
        if (result == null)
        {
            return NotFound(new { message = "Không tìm thấy chi nhánh." });
        }

        return Ok(new { message = result.Message, isActive = result.IsActive });
    }
}
