namespace BergerHarbour.Application.Abstractions;

public sealed record EmailMessage(string To, string? ToName, string Subject, string HtmlBody);

/// <summary>Transactional email (Gmail SMTP in production, a fake that writes to disk/console locally).</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
