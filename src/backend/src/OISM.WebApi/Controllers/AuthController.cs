using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OISM.Application.Common.Exceptions;
using OISM.Application.Features.Auth.CurrentUser;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.RefreshToken;
using OISM.Application.Features.Auth.RegisterTenant;
using OISM.Application.Features.Auth.Services;

namespace OISM.WebApi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Đăng ký Cửa hàng (Tenant) mới kèm tài khoản Chủ shop (Owner) ban đầu (FR-AUTH-01).
    /// </summary>
    [HttpPost("register-tenant")]
    public async Task<IActionResult> RegisterTenant([FromBody] RegisterTenantRequest request, CancellationToken ct)
    {
        var validator = new RegisterTenantValidator();
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        try
        {
            var response = await _authService.RegisterTenantAsync(request, ct);
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
        catch (Exception)
        {
            return StatusCode(500, new { message = "Đã xảy ra lỗi trong quá trình xử lý đăng ký." });
        }
    }

    /// <summary>
    /// Đăng nhập hệ thống bằng Email và Mật khẩu (FR-AUTH-02).
    /// Trả về JWT Access Token (hạn 60p) và Refresh Token (hạn 7 ngày).
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var validator = new LoginValidator();
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return BadRequest(new { errors = validationResult.Errors.Select(e => e.ErrorMessage) });
        }

        try
        {
            var response = await _authService.LoginAsync(request, ct);
            return Ok(response);
        }
        catch (OismException ex) when (ex.StatusCode == 401)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Làm mới Access Token bằng Refresh Token (Token Rotation nguyên tử, chống race condition trên PostgreSQL).
    /// </summary>
    [HttpPost("refresh-token")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return BadRequest(new { message = "Refresh token không được để trống." });
        }

        try
        {
            var response = await _authService.RefreshTokenAsync(request, ct);
            return Ok(response);
        }
        catch (OismException ex) when (ex.StatusCode == 401)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (OismException ex) when (ex.StatusCode == 400)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Đăng xuất tài khoản (Thu hồi Refresh Token).
    /// </summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        await _authService.RevokeTokenAsync(request, ct);
        return Ok(new { message = "Đăng xuất thành công." });
    }

    /// <summary>
    /// Lấy thông tin tài khoản hiện tại của người dùng đang đăng nhập (FR-AUTH-02 / me).
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var userIdClaim = User.FindFirst("userId")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { message = "Không xác định được phiên người dùng." });
        }

        var response = await _authService.GetCurrentUserAsync(userId, ct);
        if (response == null)
        {
            return NotFound(new { message = "Không tìm thấy thông tin người dùng." });
        }

        return Ok(response);
    }
}
