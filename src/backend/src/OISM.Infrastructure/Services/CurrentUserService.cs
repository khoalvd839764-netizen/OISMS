using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OISM.Application.Common.Interfaces;

namespace OISM.Infrastructure.Services;

/// <summary>
/// Trích xuất danh tính và vai trò người dùng từ HttpContext.User đã được xác thực qua JWT.
/// Tuân thủ quy tắc bảo mật: Không tin cậy bất kỳ thông tin vai trò nào từ header hoặc body client.
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            var subClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                           ?? user.FindFirst("sub")?.Value
                           ?? user.FindFirst("userId")?.Value;

            if (!string.IsNullOrEmpty(subClaim) && Guid.TryParse(subClaim, out var userId))
            {
                return userId;
            }

            return null;
        }
    }

    public string? Role
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            return user.FindFirst(ClaimTypes.Role)?.Value
                   ?? user.FindFirst("role")?.Value;
        }
    }

    public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true;

    public bool IsInRole(string role)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (user.IsInRole(role))
        {
            return true;
        }

        return user.HasClaim(c =>
            (c.Type == ClaimTypes.Role || string.Equals(c.Type, "role", StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(c.Value, role, StringComparison.OrdinalIgnoreCase));
    }
}
