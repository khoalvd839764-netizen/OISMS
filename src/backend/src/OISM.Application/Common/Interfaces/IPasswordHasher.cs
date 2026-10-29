namespace OISM.Application.Common.Interfaces;

/// <summary>
/// Giao diện dịch vụ băm và xác thực mật khẩu tuân thủ NFR-SEC-01 và Clean Architecture.
/// </summary>
public interface IPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string passwordHash);
}
