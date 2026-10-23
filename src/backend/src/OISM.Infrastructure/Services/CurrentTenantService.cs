using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OISM.Application.Common.Interfaces;

namespace OISM.Infrastructure.Services;

/// <summary>
/// Quản lý và cung cấp TenantId hiện tại của phiên làm việc.
/// Tuân thủ quy tắc bảo mật NFR-TENANT-01: Chỉ tin cậy JWT Claims từ token đã được ký và xác thực.
/// Tuyệt đối KHÔNG đọc TenantId từ header hoặc body do client tự gửi lên.
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

            // Chỉ trích xuất từ Claims của JWT Token đã được xác thực (User.Identity.IsAuthenticated)
            if (httpContext.User?.Identity?.IsAuthenticated == true)
            {
                var tenantClaim = httpContext.User.FindFirst("tenantId")?.Value 
                                  ?? httpContext.User.FindFirst("TenantId")?.Value;

                if (!string.IsNullOrEmpty(tenantClaim) && Guid.TryParse(tenantClaim, out var claimGuid))
                {
                    _tenantId = claimGuid;
                    return _tenantId;
                }
            }

            // Không tin cậy bất kỳ header nào (như X-Tenant-Id) cho các API nghiệp vụ
            return null;
        }
    }

    public void SetTenant(Guid tenantId)
    {
        _tenantId = tenantId;
    }
}
