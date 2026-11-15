using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace OISM.WebApi.Http;

/// <summary>
/// Tên policy rate limit dùng ở controller. Policy auth chia theo IP thật (sau UseForwardedHeaders).
/// </summary>
internal static class RateLimitPolicies
{
    public const string AiChat = "AiChat";
    public const string AuthLogin = "AuthLogin";
    public const string AuthForgotPassword = "AuthForgotPassword";

    /// <summary>
    /// Giới hạn cửa sổ cố định theo IP. Request không có IP (TestServer) không bị giới hạn;
    /// Kestrel và reverse proxy luôn cung cấp IP.
    /// </summary>
    public static void AddPerIpPolicy(RateLimiterOptions options, string name, int permitLimit, TimeSpan window) =>
        options.AddPolicy(name, context =>
        {
            var ip = context.Connection.RemoteIpAddress;
            return ip == null
                ? RateLimitPartition.GetNoLimiter("no-ip")
                : RateLimitPartition.GetFixedWindowLimiter(ip.ToString(), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = window,
                    QueueLimit = 0
                });
        });
}
