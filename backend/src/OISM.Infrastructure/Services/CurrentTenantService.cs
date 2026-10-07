using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OISM.Application.Common.Interfaces;

namespace OISM.Infrastructure.Services;

/// <summary>
/// Service triển khai ICurrentTenantService để trích xuất TenantId của request hiện tại.
/// Cơ chế ưu tiên:
/// 1. Claims từ vé JWT Token (khi người dùng đã đăng nhập và được xác thực).
/// 2. Header "X-Tenant-Id" của HTTP Request (dùng cho các API trước khi login, webhook hoặc môi trường dev/test).
/// </summary>
public class CurrentTenantService : ICurrentTenantService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentTenantService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? TenantId
    {
        get
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                return null;
            }

            // 1. Ưu tiên đọc từ JWT Claims (đã được xác thực qua Authentication Middleware)
            var user = httpContext.User;
            if (user?.Identity?.IsAuthenticated == true)
            {
                // Thử tìm theo claim "tenant_id" hoặc "TenantId"
                var tenantClaim = user.FindFirst("tenant_id")?.Value 
                                  ?? user.FindFirst("TenantId")?.Value;

                if (Guid.TryParse(tenantClaim, out var claimTenantId))
                {
                    return claimTenantId;
                }
            }

            // 2. Fallback: Đọc từ HTTP Header (ví dụ: X-Tenant-Id)
            if (httpContext.Request.Headers.TryGetValue("X-Tenant-Id", out var headerValues))
            {
                var headerString = headerValues.ToString();
                if (Guid.TryParse(headerString, out var headerTenantId))
                {
                    return headerTenantId;
                }
            }

            return null;
        }
    }
}
