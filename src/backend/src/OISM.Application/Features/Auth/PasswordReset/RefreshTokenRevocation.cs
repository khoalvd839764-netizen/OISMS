using Microsoft.EntityFrameworkCore;
using OISM.Application.Common.Interfaces;

namespace OISM.Application.Features.Auth.PasswordReset;

/// <summary>
/// Thu hồi mọi refresh token còn hiệu lực của một người dùng. Chỉ đánh dấu, chưa SaveChanges,
/// để bên gọi lưu cùng thay đổi mật khẩu trong một lần ghi.
/// </summary>
public static class RefreshTokenRevocation
{
    public static async Task<int> MarkAllRevokedAsync(IAppDbContext context, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var tokens = await context.RefreshTokens
            .IgnoreQueryFilters()
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .ToListAsync(ct);

        foreach (var token in tokens)
        {
            token.RevokedAt = now;
        }

        return tokens.Count;
    }
}
