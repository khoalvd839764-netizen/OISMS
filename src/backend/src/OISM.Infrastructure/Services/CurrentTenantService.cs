using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OISM.Application.Common.Interfaces;

namespace OISM.Infrastructure.Services;

/// <summary>
/// Quản lý và cung cấp TenantId hiện tại của phiên làm việc.
/// Ưu tiên trích xuất từ JWT Claims ("tenantId" hoặc "TenantId"), sau đó kiểm tra Header ("X-Tenant-Id").
/// </summary>
public class CurrentTenantService : ICurrentTenantService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private Guid? _tenantId;

    public CurrentTenantService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? TenantId
    {
        get
        {
            if (_tenantId.HasValue)
            {
                return _tenantId;
            }

            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                return null;
            }

            // 1. Kiểm tra trong Claims của JWT Token
            var tenantClaim = httpContext.User.FindFirst("tenantId")?.Value 
                              ?? httpContext.User.FindFirst("TenantId")?.Value;

            if (!string.IsNullOrEmpty(tenantClaim) && Guid.TryParse(tenantClaim, out var claimGuid))
            {
                _tenantId = claimGuid;
                return _tenantId;
            }

            // 2. Kiểm tra Header X-Tenant-Id (dành cho API test hoặc webhook có chữ ký)
            if (httpContext.Request.Headers.TryGetValue("X-Tenant-Id", out var headerValue) 
                && Guid.TryParse(headerValue, out var headerGuid))
            {
                _tenantId = headerGuid;
                return _tenantId;
            }

            return null;
        }
    }

    public void SetTenant(Guid tenantId)
    {
        _tenantId = tenantId;
    }
}
