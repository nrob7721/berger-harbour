using System.Text.RegularExpressions;
using BergerHarbour.Domain.Settings;

namespace BergerHarbour.Application.Notifications;

/// <summary>Replaces <c>{{Placeholder}}</c>s. Placeholders that don't apply to a message render empty.</summary>
public sealed partial class EmailRenderer
{
    public RenderedEmail Render(EmailTemplate template, IReadOnlyDictionary<string, EmailValue> values) =>
        new(
            Placeholder().Replace(template.Subject, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v.Plain : string.Empty).Trim(),
            Placeholder().Replace(template.HtmlBody, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v.Html : string.Empty));

    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}")]
    private static partial Regex Placeholder();
}
