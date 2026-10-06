using BergerHarbour.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace BergerHarbour.Infrastructure.Email;

/// <summary>Local development: writes each email to a folder as .html and logs it, instead of sending it.</summary>
public sealed class FileEmailSender(string directory, ILogger<FileEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        Directory.CreateDirectory(directory);
        var safeSubject = new string(message.Subject.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        var path = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{safeSubject[..Math.Min(60, safeSubject.Length)]}.html");
        var html = $"<!-- To: {System.Net.WebUtility.HtmlEncode(message.To)} | Subject: {System.Net.WebUtility.HtmlEncode(message.Subject)} -->\n" +
                   $"<p><strong>To:</strong> {System.Net.WebUtility.HtmlEncode(message.To)}<br><strong>Subject:</strong> " +
                   $"{System.Net.WebUtility.HtmlEncode(message.Subject)}</p><hr>\n{message.HtmlBody}";
        await File.WriteAllTextAsync(path, html, ct);
        logger.LogInformation("[fake email] To {To}: {Subject} → {Path}", message.To, message.Subject, path);
    }
}
