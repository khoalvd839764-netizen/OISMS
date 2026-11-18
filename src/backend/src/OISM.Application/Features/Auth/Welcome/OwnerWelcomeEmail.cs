using System.Net;
using OISM.Application.Common.Email;
using OISM.Application.Common.Interfaces;

namespace OISM.Application.Features.Auth.Welcome;

/// <summary>
/// Mẫu thư chào mừng Chủ shop sau khi đăng ký cửa hàng (Upgrade 11). Bố cục bảng và CSS inline vì Gmail bỏ thẻ style và không hỗ trợ flex.
/// Tiêu đề cố định, mọi dữ liệu người dùng đều được mã hóa HTML trước khi chèn. Thư không bao giờ chứa mật khẩu.
/// </summary>
public static class OwnerWelcomeEmail
{
    public const string Subject = "Chào mừng bạn đến với OISMS";

    public static EmailMessage Build(string to, string displayName, string tenantName, string loginUrl)
    {
        var name = WebUtility.HtmlEncode(displayName);
        var shop = WebUtility.HtmlEncode(tenantName);
        var email = WebUtility.HtmlEncode(to);
        var href = WebUtility.HtmlEncode(loginUrl);
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
            <div style="display:none;max-height:0;overflow:hidden;opacity:0;">Cửa hàng {shop} đã sẵn sàng trên OISMS. Đăng nhập để bắt đầu quản lý tồn kho và bán hàng.</div>
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
                        <h1 style="margin:0 0 16px;font-size:22px;line-height:30px;font-weight:700;color:{EmailBranding.Ink};">Chào mừng bạn đến với OISMS</h1>
                        <p style="margin:0 0 12px;font-size:15px;line-height:24px;">Xin chào <strong>{name}</strong>,</p>
                        <p style="margin:0 0 20px;font-size:15px;line-height:24px;">Cửa hàng của bạn đã được tạo thành công. Từ giờ bạn có thể quản lý tồn kho và đơn hàng từ quầy POS đến các sàn thương mại điện tử ở cùng một nơi.</p>
                        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:{EmailBranding.Canvas};border-radius:8px;">
                          <tr>
                            <td style="padding:16px 20px 4px;{font}font-size:13px;line-height:20px;color:{EmailBranding.Muted};">Cửa hàng</td>
                            <td align="right" style="padding:16px 20px 4px;{font}font-size:14px;line-height:20px;font-weight:700;color:{EmailBranding.Ink};">{shop}</td>
                          </tr>
                          <tr>
                            <td style="padding:4px 20px 16px;{font}font-size:13px;line-height:20px;color:{EmailBranding.Muted};">Email đăng nhập</td>
                            <td align="right" style="padding:4px 20px 16px;{font}font-size:14px;line-height:20px;font-weight:700;color:{EmailBranding.Ink};word-break:break-all;">{email}</td>
                          </tr>
                        </table>
                        <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin-top:24px;">
                          <tr>
                            <td align="center" bgcolor="{EmailBranding.Brand}" style="border-radius:8px;">
                              <a href="{href}" target="_blank" style="display:inline-block;padding:13px 28px;{font}font-size:15px;font-weight:700;color:#ffffff;text-decoration:none;border-radius:8px;">Đăng nhập OISMS</a>
                            </td>
                          </tr>
                        </table>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:24px 32px 4px;{font}color:{EmailBranding.Ink};">
                        <h2 style="margin:0 0 12px;font-size:16px;line-height:24px;font-weight:700;color:{EmailBranding.Ink};">Ba bước để bắt đầu</h2>
                        <p style="margin:0 0 8px;font-size:14px;line-height:22px;"><strong style="color:{EmailBranding.Brand};">1.</strong> Tạo chi nhánh và kho hàng đầu tiên.</p>
                        <p style="margin:0 0 8px;font-size:14px;line-height:22px;"><strong style="color:{EmailBranding.Brand};">2.</strong> Thêm sản phẩm và nhập hàng vào kho.</p>
                        <p style="margin:0 0 8px;font-size:14px;line-height:22px;"><strong style="color:{EmailBranding.Brand};">3.</strong> Thêm nhân viên và phân quyền theo vai trò.</p>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:16px 32px 28px;{font}">
                        <p style="margin:0 0 6px;font-size:13px;line-height:20px;color:{EmailBranding.Muted};">Nút không bấm được? Chép link sau vào trình duyệt:</p>
                        <p style="margin:0 0 20px;font-size:13px;line-height:20px;word-break:break-all;"><a href="{href}" target="_blank" style="color:{EmailBranding.Brand};text-decoration:underline;">{href}</a></p>
                        <p style="margin:0;padding-top:16px;border-top:1px solid {EmailBranding.Line};font-size:13px;line-height:20px;color:{EmailBranding.Muted};">Nếu bạn không thực hiện đăng ký này, hãy bỏ qua email này. OISMS không bao giờ yêu cầu bạn cung cấp mật khẩu qua email.</p>
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
            "Cửa hàng của bạn đã được tạo thành công trên OISMS.\n\n" +
            $"Cửa hàng: {tenantName}\n" +
            $"Email đăng nhập: {to}\n\n" +
            $"Đăng nhập tại: {loginUrl}\n\n" +
            "Ba bước để bắt đầu:\n" +
            "1. Tạo chi nhánh và kho hàng đầu tiên.\n" +
            "2. Thêm sản phẩm và nhập hàng vào kho.\n" +
            "3. Thêm nhân viên và phân quyền theo vai trò.\n\n" +
            "Nếu bạn không thực hiện đăng ký này, hãy bỏ qua email này. OISMS không bao giờ yêu cầu bạn cung cấp mật khẩu qua email.\n\n" +
            "OISMS · Email tự động, vui lòng không trả lời.";

        return new EmailMessage(to, Subject, html, text, [EmailBranding.Logo]);
    }
}
