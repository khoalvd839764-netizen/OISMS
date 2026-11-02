using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.Users;
using OISM.Application.Features.Users.Services;

namespace OISM.WebApi.Controllers;

/// <summary>
/// Quản lý danh sách nhân viên, phân quyền RBAC và trạng thái tài khoản theo Tenant (CS-02).
/// Chỉ tài khoản có vai trò 'Owner' (Chủ shop) mới có quyền truy cập các API này.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize(Roles = "Owner")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirst("userId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var uid) ? uid : Guid.Empty;
    }

    /// <summary>
    /// Lấy danh sách nhân viên thuộc Tenant hiện tại kèm bộ lọc tìm kiếm và trạng thái.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetUsers(
        [FromQuery] string? search,
        [FromQuery] string? role,
        [FromQuery] bool? isActive,
        CancellationToken ct)
    {
        var users = await _userService.GetUsersAsync(search, role, isActive, ct);
        return Ok(users);
    }

    /// <summary>
    /// Lấy thông tin chi tiết một nhân viên theo Id.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetUserById([FromRoute] Guid id, CancellationToken ct)
    {
        var user = await _userService.GetUserByIdAsync(id, ct);
        if (user == null)
        {
            return NotFound(new { message = "Không tìm thấy thông tin nhân viên." });
        }

        return Ok(user);
    }

    /// <summary>
    /// Thêm nhân viên mới vào Cửa hàng (Tenant) với vai trò 'Staff' hoặc 'Cashier'.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        var validator = new CreateUserValidator();
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        try
        {
            var response = await _userService.CreateUserAsync(request, ct);
            return StatusCode(201, response);
        }
        catch (OismException ex) when (ex.StatusCode == 409)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (OismException ex) when (ex.StatusCode == 400)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Cập nhật thông tin, vai trò hoặc trạng thái hoạt động của nhân viên.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateUser(
        [FromRoute] Guid id,
        [FromBody] UpdateUserRequest request,
        CancellationToken ct)
    {
        var validator = new UpdateUserValidator();
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        try
        {
            var response = await _userService.UpdateUserAsync(id, request, GetCurrentUserId(), ct);
            return Ok(response);
        }
        catch (OismException ex) when (ex.StatusCode == 404)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (OismException ex) when (ex.StatusCode == 409)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (OismException ex) when (ex.StatusCode == 400)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Bật/Tắt trạng thái hoạt động (Kích hoạt / Tạm dừng) của nhân viên.
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateUserStatus(
        [FromRoute] Guid id,
        [FromBody] UpdateUserStatusRequest request,
        CancellationToken ct)
    {
        try
        {
            var response = await _userService.UpdateUserStatusAsync(id, request.IsActive, GetCurrentUserId(), ct);
            return Ok(response);
        }
        catch (OismException ex) when (ex.StatusCode == 404)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (OismException ex) when (ex.StatusCode == 400)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
