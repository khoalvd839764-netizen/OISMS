using System.Net;
using System.Net.Mail;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OISM.Application.Common.Interfaces;

namespace OISM.Infrastructure.Services.Email;

/// <summary>
/// Cấu hình SMTP (section "Email"). Bản demo dùng Gmail SMTP cổng 587 (STARTTLS) với App Password.
/// Mật khẩu chỉ đặt qua biến môi trường, không ghi vào appsettings.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? User { get; set; }
    public string? Password { get; set; }
    public string? From { get; set; }
    public string FromName { get; set; } = "OISMS";

    public bool HasSmtp =>
        !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(User) &&
        !string.IsNullOrWhiteSpace(Password) &&
        !string.IsNullOrWhiteSpace(From);
}

/// <summary>Kênh gửi thật sự một thư.</summary>
public interface IEmailTransport
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

/// <summary>
/// Chọn kênh gửi: kênh đăng ký sẵn trong DI (test thay bằng bản giả), rồi SMTP nếu đủ cấu hình,
/// rồi ghi log nếu là Development. Không có kênh nào thì quên mật khẩu bị tắt.
/// </summary>
public sealed class EmailTransportResolver
{
    public EmailTransportResolver(
        IEnumerable<IEmailTransport> registered,
        IOptions<EmailOptions> options,
        IHostEnvironment environment,
        ILoggerFactory loggerFactory)
    {
        Transport = registered.LastOrDefault()
            ?? (options.Value.HasSmtp ? new SmtpEmailTransport(options)
                : environment.IsDevelopment() ? new LogEmailTransport(loggerFactory.CreateLogger<LogEmailTransport>())
                : null);

        if (Transport == null)
        {
            loggerFactory.CreateLogger<EmailTransportResolver>()
                .LogWarning("Chưa cấu hình SMTP (Email:Host, User, Password, From). Tính năng quên mật khẩu bị tắt.");
        }
    }

    public IEmailTransport? Transport { get; }
}

public sealed class SmtpEmailTransport : IEmailTransport
{
    private readonly EmailOptions _options;

    public SmtpEmailTransport(IOptions<EmailOptions> options) => _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(_options.User, _options.Password),
            Timeout = 30_000
        };

        using var mail = new MailMessage
        {
            From = new MailAddress(_options.From!, _options.FromName),
            Subject = message.Subject,
            Body = message.TextBody,
            IsBodyHtml = false
        };
        mail.To.Add(message.To);
        mail.AlternateViews.Add(CreateHtmlView(message));

        await client.SendMailAsync(mail, ct);
    }

    /// <summary>
    /// Phần HTML kèm ảnh nhúng (multipart/related). Ảnh gắn vào view chứ không vào Attachments
    /// để ứng dụng mail hiện tại chỗ thẻ img, không hiện thành tệp đính kèm.
    /// </summary>
    public static AlternateView CreateHtmlView(EmailMessage message)
    {
        var view = AlternateView.CreateAlternateViewFromString(message.HtmlBody, System.Text.Encoding.UTF8, "text/html");
        foreach (var image in message.InlineImages ?? [])
        {
            // MemoryStream thuộc về LinkedResource, được giải phóng cùng MailMessage.
            view.LinkedResources.Add(new LinkedResource(new MemoryStream(image.Content), image.MediaType)
            {
                ContentId = image.ContentId,
                TransferEncoding = System.Net.Mime.TransferEncoding.Base64
            });
        }

        return view;
    }
}

/// <summary>Chỉ dùng ở Development: ghi nội dung thư (kể cả link) ra log thay vì gửi.</summary>
public sealed class LogEmailTransport : IEmailTransport
{
    private readonly ILogger<LogEmailTransport> _logger;

    public LogEmailTransport(ILogger<LogEmailTransport> logger) => _logger = logger;

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        _logger.LogInformation("[DEV EMAIL] To: {To} | Subject: {Subject}\n{Body}", message.To, message.Subject, message.TextBody);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Hàng đợi thư trong bộ nhớ. API chỉ xếp thư rồi trả về ngay, nên thời gian phản hồi
/// không phụ thuộc vào việc có gửi mail hay không. Mất thư khi backend khởi động lại là chấp nhận được.
/// </summary>
public sealed class QueuedEmailSender : IEmailSender
{
    private readonly Channel<EmailMessage> _channel = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public QueuedEmailSender(EmailTransportResolver resolver) => IsConfigured = resolver.Transport != null;

    public bool IsConfigured { get; }

    internal ChannelReader<EmailMessage> Reader => _channel.Reader;

    public bool TryQueue(EmailMessage message) => IsConfigured && _channel.Writer.TryWrite(message);
}

public sealed class EmailDispatchService : BackgroundService
{
    private readonly QueuedEmailSender _queue;
    private readonly EmailTransportResolver _resolver;
    private readonly ILogger<EmailDispatchService> _logger;

    public EmailDispatchService(QueuedEmailSender queue, EmailTransportResolver resolver, ILogger<EmailDispatchService> logger)
    {
        _queue = queue;
        _resolver = resolver;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var transport = _resolver.Transport;
        if (transport == null)
        {
            return;
        }

        await foreach (var message in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await transport.SendAsync(message, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Không ghi nội dung thư vì có link đặt lại mật khẩu.
                _logger.LogError(ex, "Gửi email \"{Subject}\" thất bại.", message.Subject);
            }
        }
    }
}
