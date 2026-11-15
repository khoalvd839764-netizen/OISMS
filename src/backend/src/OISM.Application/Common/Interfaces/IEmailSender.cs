namespace OISM.Application.Common.Interfaces;

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);

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
