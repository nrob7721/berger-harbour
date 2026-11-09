using System.Globalization;
using System.Net;
using BergerHarbour.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Infrastructure.Email;

/// <summary>Local development: writes each email to an HTML file (or the console), never sends it.</summary>
public sealed class FileEmailSender : IEmailSender
{
    private readonly EmailOptions options;
    private readonly ILogger<FileEmailSender> logger;

    public FileEmailSender(IOptions<EmailOptions> options, ILogger<FileEmailSender> logger)
    {
        this.options = options.Value;
        this.logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var subject = options.SubjectPrefix + message.Subject;
        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
        {
            logger.LogInformation("Email to {To}: {Subject}\n{Body}", message.To, subject, message.HtmlBody);
            return;
        }

        Directory.CreateDirectory(options.OutputDirectory);
        var name = $"{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}-{Guid.NewGuid().ToString("N")[..6]}.html";
        var html = $"<!doctype html><meta charset=\"utf-8\"><title>{WebUtility.HtmlEncode(subject)}</title>" +
            $"<p><strong>To:</strong> {WebUtility.HtmlEncode(message.To)}<br><strong>Subject:</strong> {WebUtility.HtmlEncode(subject)}</p><hr>{message.HtmlBody}";
        await File.WriteAllTextAsync(Path.Combine(options.OutputDirectory, name), html, ct);
        logger.LogInformation("Email to {To} written to {File}: {Subject}", message.To, name, subject);
    }
}
