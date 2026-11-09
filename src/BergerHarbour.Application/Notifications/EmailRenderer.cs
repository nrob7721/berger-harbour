using System.Net;
using BergerHarbour.Domain.Settings;

namespace BergerHarbour.Application.Notifications;

/// <summary>A placeholder value as plain text (subjects) and as HTML (bodies).</summary>
public sealed record PlaceholderValue(string Text, string Html)
{
    public static PlaceholderValue Plain(string? text) => new(text ?? "", WebUtility.HtmlEncode(text ?? ""));
    public static readonly PlaceholderValue Empty = new("", "");
}

public static class EmailRenderer
{
    /// <summary>Replaces every known placeholder. Placeholders without a value render empty.</summary>
    public static string Render(string template, IReadOnlyDictionary<string, PlaceholderValue> values, bool html) =>
        EmailPlaceholders.Pattern().Replace(template, m =>
        {
            var name = m.Groups[1].Value;
            return values.TryGetValue(name, out var v) ? (html ? v.Html : v.Text) : "";
        });
}
