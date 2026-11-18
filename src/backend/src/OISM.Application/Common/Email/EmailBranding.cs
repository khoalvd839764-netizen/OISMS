using OISM.Application.Common.Interfaces;

namespace OISM.Application.Common.Email;

/// <summary>
/// Nhận diện dùng chung cho email hệ thống: logo nhúng và bảng màu theo web Admin.
/// Logo vẽ từ <c>oisms-logo.svg</c> (rsvg-convert -z 3), nhúng vào assembly để build Docker không phải chép file.
/// Gmail không hiện SVG và chặn ảnh base64, nên logo là PNG gửi kèm qua CID.
/// </summary>
public static class EmailBranding
{
    public const string LogoContentId = "oisms-logo";

    /// <summary>Kích thước hiển thị; file PNG gấp 3 lần để nét trên màn hình mật độ cao.</summary>
    public const int LogoWidth = 128;
    public const int LogoHeight = 40;

    public const string Brand = "#1769e0";
    public const string Ink = "#172033";
    public const string Muted = "#667085";
    public const string Line = "#e6eaf0";
    public const string Canvas = "#f6f7f9";

    private const string LogoResourceName = "OISM.Application.Common.Email.oisms-logo.png";

    private static readonly Lazy<EmailInlineImage> LogoImage = new(LoadLogo);

    public static EmailInlineImage Logo => LogoImage.Value;

    private static EmailInlineImage LoadLogo()
    {
        using var stream = typeof(EmailBranding).Assembly.GetManifestResourceStream(LogoResourceName)
            ?? throw new InvalidOperationException($"Thiếu tài nguyên nhúng {LogoResourceName}.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return new EmailInlineImage(LogoContentId, "image/png", buffer.ToArray());
    }
}
