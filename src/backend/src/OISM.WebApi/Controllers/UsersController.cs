using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.Users;
using OISM.Application.Features.Users.Services;
using OISM.WebApi.Http;

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
            throw new OismException("USER_NOT_FOUND", "Không tìm thấy thông tin nhân viên.", 404);
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
            throw new OismException("VALIDATION_ERROR", string.Join(" ", validationResult.Errors.Select(e => e.ErrorMessage)), 400);
        }

        var response = await _userService.CreateUserAsync(request, ct);
        return StatusCode(201, response);
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
            throw new OismException("VALIDATION_ERROR", string.Join(" ", validationResult.Errors.Select(e => e.ErrorMessage)), 400);
        }

        var response = await _userService.UpdateUserAsync(id, request, User.GetUserId() ?? Guid.Empty, ct);
        return Ok(response);
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
        var response = await _userService.UpdateUserStatusAsync(id, request.IsActive, User.GetUserId() ?? Guid.Empty, ct);
        return Ok(response);
    }
}
