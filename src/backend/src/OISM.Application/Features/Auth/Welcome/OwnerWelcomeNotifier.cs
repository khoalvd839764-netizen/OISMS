using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.PasswordReset;

namespace OISM.Application.Features.Auth.Welcome;

public sealed record OwnerWelcomeRecipient(Guid UserId, string Email, string DisplayName, string TenantName);

public interface IOwnerWelcomeNotifier
{
    /// <summary>
    /// Xếp mail chào mừng Chủ shop vào hàng đợi. Chỉ gọi sau khi transaction đăng ký đã commit.
    /// Không bao giờ ném lỗi: tài khoản đã tạo xong thì lỗi gửi mail không được làm hỏng phản hồi đăng ký.
    /// </summary>
    void Notify(OwnerWelcomeRecipient recipient);
}

/// <summary>
/// Gửi mail chào mừng cho tài khoản Owner tự đăng ký (Upgrade 11). Staff và Cashier do Owner tạo không nhận thư này.
/// Link đăng nhập lấy từ cấu hình, không dựng từ header Host hay dữ liệu client gửi lên.
/// </summary>
public sealed class OwnerWelcomeNotifier : IOwnerWelcomeNotifier
{
    private readonly IEmailSender _emailSender;
    private readonly PasswordResetOptions _options;
    private readonly ILogger<OwnerWelcomeNotifier> _logger;

    public OwnerWelcomeNotifier(
        IEmailSender emailSender,
        IOptions<PasswordResetOptions> options,
        ILogger<OwnerWelcomeNotifier> logger)
    {
        _emailSender = emailSender;
        _options = options.Value;
        _logger = logger;
    }

    public void Notify(OwnerWelcomeRecipient recipient)
    {
        try
        {
            if (!_emailSender.IsConfigured)
            {
                return;
            }

            if (!Uri.TryCreate(_options.AdminBaseUrl, UriKind.Absolute, out var baseUri)
                || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
            {
                _logger.LogWarning("Bỏ qua mail chào mừng cho người dùng {UserId} vì PasswordReset:AdminBaseUrl không hợp lệ.", recipient.UserId);
                return;
            }

            var loginUrl = $"{_options.AdminBaseUrl!.TrimEnd('/')}/login";
            var email = OwnerWelcomeEmail.Build(recipient.Email, recipient.DisplayName, recipient.TenantName, loginUrl);
            if (!_emailSender.TryQueue(email))
            {
                _logger.LogWarning("Không xếp được mail chào mừng vào hàng đợi cho người dùng {UserId}.", recipient.UserId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không tạo được mail chào mừng cho người dùng {UserId}.", recipient.UserId);
        }
    }
}
