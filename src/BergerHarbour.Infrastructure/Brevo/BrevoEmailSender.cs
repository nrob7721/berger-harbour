using System.Net.Http.Json;
using BergerHarbour.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace BergerHarbour.Infrastructure.Brevo;

public sealed class BrevoOptions
{
    /// <summary>BREVO_API_KEY</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string SenderEmail { get; set; } = "bookings@bergerhouseboats.com.au";

    public string SenderName { get; set; } = "Berger Houseboats";
}

/// <summary>Sends transactional email through the Brevo API (POST /v3/smtp/email).</summary>
public sealed class BrevoEmailSender(HttpClient http, IOptions<BrevoOptions> options) : IEmailSender
{
    public const string BaseAddress = "https://api.brevo.com/";

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var o = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, "v3/smtp/email")
        {
            Content = JsonContent.Create(new
            {
                sender = new { email = o.SenderEmail, name = o.SenderName },
                to = new[] { new { email = message.To, name = message.ToName ?? message.To } },
                replyTo = new { email = o.SenderEmail, name = o.SenderName },
                subject = message.Subject,
                htmlContent = message.HtmlBody,
            }),
        };
        request.Headers.Add("api-key", o.ApiKey);
        request.Headers.Add("accept", "application/json");
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Brevo rejected the email ({(int)response.StatusCode}): {body}");
        }
    }
}
