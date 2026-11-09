namespace BergerHarbour.Application.Settings.Dtos;

/// <summary>Every template, and the placeholders templates may use.</summary>
public sealed record EmailTemplatesResponse(
    IReadOnlyList<EmailTemplateDto> Templates,
    IReadOnlyList<string> Placeholders);
