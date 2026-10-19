namespace OISM.Application.Features.Auth.RegisterTenant;

public class RegisterTenantResponse
{
    public Guid TenantId { get; set; }
    public string TenantCode { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Message { get; set; } = "Đăng ký Cửa hàng thành công!";
}
