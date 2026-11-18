using System.Net;
using OISM.Application.Common.Email;
using OISM.Application.Common.Interfaces;

namespace OISM.Application.Features.Auth.PasswordReset;

/// <summary>
/// Mẫu thư đặt lại mật khẩu (Upgrade 10). Bố cục bảng và CSS inline vì Gmail bỏ thẻ style và không hỗ trợ flex.
/// Logo gửi kèm qua CID; mọi dữ liệu người dùng đều được mã hóa HTML trước khi chèn.
/// </summary>
public static class PasswordResetEmail
{
    public const string Subject = "OISMS - Đặt lại mật khẩu";

    public static EmailMessage Build(string to, string displayName, string link, int lifetimeMinutes)
    {
        var name = WebUtility.HtmlEncode(displayName);
        var href = WebUtility.HtmlEncode(link);
        const string font = "font-family:Arial,Helvetica,sans-serif;";

        var html = $"""
            <!DOCTYPE html>
            <html lang="vi">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>{Subject}</title>
            </head>
            <body style="margin:0;padding:0;background:{EmailBranding.Canvas};">
            <div style="display:none;max-height:0;overflow:hidden;opacity:0;">Link đặt mật khẩu mới cho tài khoản OISMS của bạn, hiệu lực {lifetimeMinutes} phút.</div>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:{EmailBranding.Canvas};">
              <tr>
                <td align="center" style="padding:32px 16px;">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:560px;background:#ffffff;border:1px solid {EmailBranding.Line};border-radius:12px;">
                    <tr>
                      <td style="padding:28px 32px 20px;border-bottom:1px solid {EmailBranding.Line};">
                        <img src="cid:{EmailBranding.LogoContentId}" width="{EmailBranding.LogoWidth}" height="{EmailBranding.LogoHeight}" alt="OISMS" style="display:block;border:0;outline:none;text-decoration:none;">
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:28px 32px 8px;{font}color:{EmailBranding.Ink};">
                        <h1 style="margin:0 0 16px;font-size:22px;line-height:30px;font-weight:700;color:{EmailBranding.Ink};">Đặt lại mật khẩu</h1>
                        <p style="margin:0 0 12px;font-size:15px;line-height:24px;">Xin chào <strong>{name}</strong>,</p>
                        <p style="margin:0 0 24px;font-size:15px;line-height:24px;">OISMS nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn. Bấm nút dưới đây để đặt mật khẩu mới.</p>
                        <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                          <tr>
                            <td align="center" bgcolor="{EmailBranding.Brand}" style="border-radius:8px;">
                              <a href="{href}" target="_blank" style="display:inline-block;padding:13px 28px;{font}font-size:15px;font-weight:700;color:#ffffff;text-decoration:none;border-radius:8px;">Đặt lại mật khẩu</a>
                            </td>
                          </tr>
                        </table>
                        <p style="margin:20px 0 0;font-size:13px;line-height:20px;color:{EmailBranding.Muted};">Link có hiệu lực trong <strong>{lifetimeMinutes} phút</strong> và chỉ dùng được một lần.</p>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:20px 32px 28px;{font}">
                        <p style="margin:0 0 6px;font-size:13px;line-height:20px;color:{EmailBranding.Muted};">Nút không bấm được? Chép link sau vào trình duyệt:</p>
                        <p style="margin:0 0 20px;font-size:13px;line-height:20px;word-break:break-all;"><a href="{href}" target="_blank" style="color:{EmailBranding.Brand};text-decoration:underline;">{href}</a></p>
                        <p style="margin:0;padding-top:16px;border-top:1px solid {EmailBranding.Line};font-size:13px;line-height:20px;color:{EmailBranding.Muted};">Nếu bạn không yêu cầu, hãy bỏ qua email này. Mật khẩu hiện tại vẫn giữ nguyên. Không chuyển tiếp email này cho người khác.</p>
                      </td>
                    </tr>
                  </table>
                  <p style="margin:16px 0 0;{font}font-size:12px;line-height:18px;color:{EmailBranding.Muted};">OISMS · Hệ thống quản lý tồn kho và bán hàng đa kênh<br>Email tự động, vui lòng không trả lời.</p>
                </td>
              </tr>
            </table>
            </body>
            </html>
            """;

        var text =
            $"Xin chào {displayName},\n\n" +
            $"OISMS nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn.\n" +
            $"Mở link sau để đặt mật khẩu mới (hiệu lực {lifetimeMinutes} phút, dùng một lần):\n{link}\n\n" +
            "Nếu bạn không yêu cầu, hãy bỏ qua email này. Mật khẩu hiện tại vẫn giữ nguyên.\n\n" +
            "OISMS · Email tự động, vui lòng không trả lời.";

        return new EmailMessage(to, Subject, html, text, [EmailBranding.Logo]);
    }
}
