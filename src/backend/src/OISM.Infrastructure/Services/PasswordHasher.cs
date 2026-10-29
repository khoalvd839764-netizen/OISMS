using OISM.Application.Common.Interfaces;

namespace OISM.Infrastructure.Services;

/// <summary>
/// Hiện thực hóa dịch vụ băm và xác thực mật khẩu bằng BCrypt theo chuẩn NFR-SEC-01.
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    public string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password);
    }

    public bool VerifyPassword(string password, string passwordHash)
    {
        return BCrypt.Net.BCrypt.Verify(password, passwordHash);
    }
}
