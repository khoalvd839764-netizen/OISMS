namespace OISM.Application.Features_Auth.RegisterTenant;

public class RegisterTenantRequest
{
    public string TenantName { get; set; } = string.Empty;
    public string TenantCode { get; set; } = string.Empty; // Bắt buộc theo schema DB
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
}