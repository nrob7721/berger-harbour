namespace BergerHarbour.Application.Settings.Dtos;

/// <summary>Edit a template.</summary>
public sealed record SaveEmailTemplateRequest(
    string Subject,
    string HtmlBody,
    int Version);
