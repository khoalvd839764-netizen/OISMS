using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OISM.Application.Common.Email;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.PasswordReset;
using OISM.Application.Features.Auth.Welcome;
using Xunit;

namespace OISM.UnitTests;

/// <summary>
/// Upgrade 11: mẫu thư chào mừng Chủ shop và quy tắc xếp thư vào hàng đợi.
/// </summary>
public class OwnerWelcomeUnitTests
{
    private static readonly OwnerWelcomeRecipient Recipient =
        new(Guid.NewGuid(), "owner@shop.local", "Chị Lan", "Cửa hàng Lan");

    private sealed class FakeEmailSender : IEmailSender
    {
        public bool IsConfigured { get; init; } = true;
        public bool QueueResult { get; init; } = true;
        public bool ThrowOnQueue { get; init; }
        public List<EmailMessage> Queued { get; } = new();

        public bool TryQueue(EmailMessage message)
        {
            if (ThrowOnQueue)
            {
                throw new InvalidOperationException("Hàng đợi lỗi.");
            }

            Queued.Add(message);
            return QueueResult;
        }
    }

    private static OwnerWelcomeNotifier CreateNotifier(IEmailSender sender, string? adminBaseUrl = "https://oisms.example/") =>
        new(sender, Options.Create(new PasswordResetOptions { AdminBaseUrl = adminBaseUrl }), NullLogger<OwnerWelcomeNotifier>.Instance);

    [Fact]
    public void Email_Build_EmbedsLogo_ShowsShopInfo_AndKeepsFixedSubject()
    {
        var message = OwnerWelcomeEmail.Build("owner@shop.local", "Chị Lan", "Cửa hàng Lan", "https://oisms.example/login");

        message.To.Should().Be("owner@shop.local");
        message.Subject.Should().Be("Chào mừng bạn đến với OISMS");
        message.HtmlBody.Should().Contain($"src=\"cid:{EmailBranding.LogoContentId}\"");
        message.HtmlBody.Should().Contain("href=\"https://oisms.example/login\"");
        // HtmlEncode đổi ký tự như "à" thành &#224;, ứng dụng mail vẫn hiển thị đúng tiếng Việt.
        message.HtmlBody.Should().Contain(WebUtility.HtmlEncode("Cửa hàng Lan")).And.Contain("owner@shop.local");
        message.TextBody.Should().Contain("https://oisms.example/login").And.Contain("Cửa hàng Lan");
        message.InlineImages.Should().ContainSingle().Which.ContentId.Should().Be(EmailBranding.LogoContentId);
    }

    [Fact]
    public void Email_Build_EncodesEveryUserValue()
    {
        var message = OwnerWelcomeEmail.Build(
            "owner@shop.local", "<b>Lan</b>", "<script>alert(1)</script>", "https://oisms.example/login?a=1&b=2");

        message.HtmlBody.Should().Contain("&lt;b&gt;Lan&lt;/b&gt;");
        message.HtmlBody.Should().Contain("&lt;script&gt;alert(1)&lt;/script&gt;");
        message.HtmlBody.Should().Contain("href=\"https://oisms.example/login?a=1&amp;b=2\"");
        message.HtmlBody.Should().NotContain("<script>").And.NotContain("<b>Lan</b>");
    }

    [Fact]
    public void Notify_QueuesOneMail_WithLoginLinkFromConfiguration()
    {
        var sender = new FakeEmailSender();

        CreateNotifier(sender).Notify(Recipient);

        var mail = sender.Queued.Should().ContainSingle().Subject;
        mail.To.Should().Be(Recipient.Email);
        mail.TextBody.Should().Contain("https://oisms.example/login");
        mail.TextBody.Should().NotContain("oisms.example//login");
    }

    [Fact]
    public void Notify_WhenEmailNotConfigured_QueuesNothing()
    {
        var sender = new FakeEmailSender { IsConfigured = false };

        CreateNotifier(sender).Notify(Recipient);

        sender.Queued.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("oisms.example")]
    [InlineData("ftp://oisms.example")]
    public void Notify_WhenAdminUrlInvalid_QueuesNothing(string? adminBaseUrl)
    {
        var sender = new FakeEmailSender();

        CreateNotifier(sender, adminBaseUrl).Notify(Recipient);

        sender.Queued.Should().BeEmpty();
    }

    [Fact]
    public void Notify_WhenQueueFullOrThrows_DoesNotThrow()
    {
        var full = () => CreateNotifier(new FakeEmailSender { QueueResult = false }).Notify(Recipient);
        var broken = () => CreateNotifier(new FakeEmailSender { ThrowOnQueue = true }).Notify(Recipient);

        full.Should().NotThrow();
        broken.Should().NotThrow("tài khoản đã commit nên lỗi gửi mail không được làm hỏng phản hồi đăng ký");
    }
}
