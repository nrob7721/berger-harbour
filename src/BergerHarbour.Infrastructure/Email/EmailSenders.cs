using System.Globalization;
using System.Net;
using System.Text;
using BergerHarbour.Application.Abstractions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace BergerHarbour.Infrastructure.Email;

public enum EmailMode { Smtp, File }

public sealed class EmailOptions
{
    public EmailMode Mode { get; set; } = EmailMode.Smtp;
    /// <summary>info@bergerhouseboats.com.au: SMTP login, From and Reply-To.</summary>
    public string Sender { get; set; } = "";
    public string SenderName { get; set; } = "Berger Houseboats";
    public string SmtpPassword { get; set; } = "";
    public string SmtpHost { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    /// <summary>"[DEV] " in dev, empty in prod.</summary>
    public string SubjectPrefix { get; set; } = "";
    public string OutputDirectory { get; set; } = ".dev-emails";

    public IEnumerable<string> Validate()
    {
        if (Mode != EmailMode.Smtp) yield break;
        if (string.IsNullOrWhiteSpace(Sender)) yield return "EMAIL_SENDER is required when EMAIL_MODE=Smtp.";
        if (string.IsNullOrWhiteSpace(SmtpPassword)) yield return "SMTP_PASSWORD is required when EMAIL_MODE=Smtp.";
    }
}

/// <summary>Gmail SMTP (smtp.gmail.com:587, STARTTLS) with a Google app password.</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var o = options.Value;
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(o.SenderName, o.Sender));
        mime.ReplyTo.Add(new MailboxAddress(o.SenderName, o.Sender));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = o.SubjectPrefix + message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(o.SmtpHost, o.SmtpPort, SecureSocketOptions.StartTls, ct);
        await client.AuthenticateAsync(o.Sender, o.SmtpPassword, ct);
        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(true, ct);
        logger.LogInformation("Email sent: {Subject}", mime.Subject);
    }
}

/// <summary>Local development: writes each email to an .html file instead of sending it.</summary>
public sealed class FileEmailSender(IOptions<EmailOptions> options, ILogger<FileEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var o = options.Value;
        Directory.CreateDirectory(o.OutputDirectory);
        var safe = new string(message.Subject.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        if (safe.Length > 60) safe = safe[..60];
        var path = Path.Combine(o.OutputDirectory,
            $"{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}-{safe}.html");
        var html = new StringBuilder()
            .Append("<!-- To: ").Append(WebUtility.HtmlEncode(message.To)).AppendLine(" -->")
            .Append("<!-- Subject: ").Append(WebUtility.HtmlEncode(o.SubjectPrefix + message.Subject)).AppendLine(" -->")
            .Append(message.HtmlBody).ToString();
        await File.WriteAllTextAsync(path, html, ct);
        logger.LogInformation("Email to {To} written to {Path}: {Subject}", message.To, path, message.Subject);
    }
}
