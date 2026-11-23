namespace OISM.Application.Common.Interfaces;

/// <summary>
/// Cung cấp thông tin người dùng hiện tại từ Claims của JWT Token đã xác thực.
/// </summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? Role { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
}
