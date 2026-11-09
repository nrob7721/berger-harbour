using System.Net;

namespace BergerHarbour.Application.Notifications;

/// <summary>A placeholder's value: plain text for the subject, HTML for the body (values are HTML-encoded).</summary>
public sealed record EmailValue(string Plain, string Html)
{
    public static readonly EmailValue Empty = new(string.Empty, string.Empty);

    public static EmailValue Text(string? value) => new(value ?? string.Empty, WebUtility.HtmlEncode(value ?? string.Empty));

    /// <summary>A value with ready-made markup whose parts the caller has already encoded.</summary>
    public static EmailValue Markup(string plain, string html) => new(plain, html);
}
