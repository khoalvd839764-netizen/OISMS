namespace OISM.Application.Common.Interfaces;

/// <summary>
/// Cung cấp thông tin TenantId hiện tại của phiên làm việc (Request context).
/// </summary>
public interface ICurrentTenantService
{
    Guid? TenantId { get; }
    void SetTenant(Guid tenantId);
}
