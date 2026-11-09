using BergerHarbour.Application.Abstractions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace BergerHarbour.Infrastructure.Email;

/// <summary>Gmail SMTP (smtp.gmail.com:587, STARTTLS) as the business mailbox, authenticated with a Google app password.</summary>
public sealed class GmailSmtpEmailSender : IEmailSender
{
    private readonly EmailOptions options;
    private readonly ILogger<GmailSmtpEmailSender> logger;

    public GmailSmtpEmailSender(IOptions<EmailOptions> options, ILogger<GmailSmtpEmailSender> logger)
    {
        this.options = options.Value;
        this.logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var mime = new MimeMessage();
        var from = new MailboxAddress(options.SenderName, options.Sender);
        mime.From.Add(from);
        mime.ReplyTo.Add(from);
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = options.SubjectPrefix + message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody }.ToMessageBody();

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(options.SmtpHost, options.SmtpPort, SecureSocketOptions.StartTls, ct);
        await smtp.AuthenticateAsync(options.Sender, options.SmtpPassword, ct);
        await smtp.SendAsync(mime, ct);
        await smtp.DisconnectAsync(true, ct);
        logger.LogInformation("Email sent: {Subject}", mime.Subject);
    }
}
