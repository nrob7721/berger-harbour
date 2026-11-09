namespace BergerHarbour.Application.Notifications;

/// <summary>A template with its placeholders replaced.</summary>
public sealed record RenderedEmail(string Subject, string HtmlBody);
