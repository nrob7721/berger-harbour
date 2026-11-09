using BergerHarbour.Domain.Settings;

namespace BergerHarbour.Application.Settings.Dtos;

/// <summary>An editable email template.</summary>
public sealed record EmailTemplateDto(
    EmailTemplateKey Key,
    string Subject,
    string HtmlBody,
    int Version);
