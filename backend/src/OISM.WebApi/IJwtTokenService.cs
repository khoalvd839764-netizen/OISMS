namespace OISM.WebApi;

public interface IJwtTokenService
{
    string GenerateToken(Guid userId, Guid tenantId, string email, string role);
}