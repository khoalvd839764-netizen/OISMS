namespace OISM.Application.Common.Interfaces;

/// <summary>
/// Ảnh nhúng trong thư, HTML tham chiếu bằng <c>src="cid:{ContentId}"</c>.
/// Nhúng thay vì link ngoài để Gmail hiện được cả khi backend chạy local.
/// </summary>
public sealed record EmailInlineImage(string ContentId, string MediaType, byte[] Content);

public sealed record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string TextBody,
    IReadOnlyList<EmailInlineImage>? InlineImages = null);

/// <summary>
/// Gửi email hệ thống (Upgrade 10). Bản cài đặt đưa thư vào hàng đợi và gửi ở nền,
/// nên lời gọi trả về ngay và thời gian phản hồi API không lộ việc có gửi mail hay không.
/// </summary>
public interface IEmailSender
{
    /// <summary>Đã cấu hình kênh gửi (SMTP, hoặc ghi log ở Development) hay chưa.</summary>
    bool IsConfigured { get; }

    /// <summary>Đưa thư vào hàng đợi. Trả false nếu hàng đợi đầy hoặc chưa cấu hình.</summary>
    bool TryQueue(EmailMessage message);
}
