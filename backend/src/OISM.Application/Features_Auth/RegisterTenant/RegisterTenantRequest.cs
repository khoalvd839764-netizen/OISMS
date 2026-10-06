namespace OISM.Application.Features_Auth.RegisterTenant
{
    public class RegisterTenantRequest
    {
        public string TenantName { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public string FullName { get; set; }
        public string PhoneNumber { get; set; }
    }
}