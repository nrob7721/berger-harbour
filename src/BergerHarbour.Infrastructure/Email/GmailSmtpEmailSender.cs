using BergerHarbour.Application.Abstractions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace BergerHarbour.Infrastructure.Email;

public sealed class SmtpOptions
{
    /// <summary>EMAIL_SENDER: the Gmail / Google Workspace mailbox. It is the SMTP login, the From and the Reply-To.</summary>
    public string SenderEmail { get; set; } = string.Empty;

    /// <summary>EMAIL_SENDER_NAME</summary>
    public string SenderName { get; set; } = "Berger Houseboats";

    /// <summary>SMTP_PASSWORD: a Google app password for <see cref="SenderEmail"/>.</summary>
    public string Password { get; set; } = string.Empty;

    public string Host { get; set; } = "smtp.gmail.com";

    public int Port { get; set; } = 587;

    public IEnumerable<string> Validate()
    {
        if (!MailboxAddress.TryParse(SenderEmail, out var sender) || !sender.Address.Contains('@'))
        {
            yield return "EMAIL_SENDER must be a valid email address.";
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            yield return "SMTP_PASSWORD is required.";
        }
    }
}

/// <summary>Sends transactional email through Gmail SMTP, logging in with an app password.</summary>
public sealed class GmailSmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var o = options.Value;
        using var mime = Build(o, message);
        using var client = new SmtpClient { Timeout = 20_000 };
        await client.ConnectAsync(o.Host, o.Port, SecureSocketOptions.StartTls, ct);
        await client.AuthenticateAsync(o.SenderEmail, o.Password, ct);
        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(true, ct);
    }

    public static MimeMessage Build(SmtpOptions o, EmailMessage message)
    {
        var sender = new MailboxAddress(o.SenderName, o.SenderEmail);
        var mime = new MimeMessage();
        mime.From.Add(sender);
        mime.ReplyTo.Add(sender);
        mime.To.Add(new MailboxAddress(message.ToName ?? message.To, message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody }.ToMessageBody();
        return mime;
    }
}
