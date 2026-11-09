using BergerHarbour.Application.Notifications;
using BergerHarbour.Application.Settings.Dtos;
using BergerHarbour.Domain.Settings;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Application.Settings;

/// <summary>Email templates (<c>EmailTemplatesController</c>). Unknown placeholders are rejected.</summary>
public sealed class EmailTemplateService
{
    private readonly IEmailTemplateRepository templates;
    private readonly BookingNotifier notifier;

    public EmailTemplateService(IEmailTemplateRepository templates, BookingNotifier notifier)
    {
        this.templates = templates;
        this.notifier = notifier;
    }

    public async Task<EmailTemplatesResponse> ListAsync(CancellationToken ct = default) =>
        new((await templates.ListAsync(ct)).OrderBy(t => t.Key).Select(ToDto).ToList(), EmailPlaceholders.All);

    public async Task<EmailTemplateDto> UpdateAsync(EmailTemplateKey key, SaveEmailTemplateRequest request, CancellationToken ct = default)
    {
        var template = await templates.GetAsync(key, ct) ?? throw new NotFoundException("We couldn't find that template.");
        template.EnsureVersion(request.Version);
        template.Update(request.Subject, request.HtmlBody, EmailPlaceholders.Known);
        await templates.SaveAsync(template, ct);
        return ToDto(template);
    }

    /// <summary>Sends the saved template with sample values to the staff alert address.</summary>
    public Task SendTestAsync(EmailTemplateKey key, CancellationToken ct = default) => notifier.SendTestAsync(key, ct);

    private static EmailTemplateDto ToDto(EmailTemplate t) => new(t.Key, t.Subject, t.HtmlBody, t.Version);
}
