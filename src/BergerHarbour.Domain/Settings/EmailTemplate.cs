using System.Text.RegularExpressions;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Settings;

/// <summary>An editable email: subject and HTML body with <c>{{Placeholder}}</c>s. Unknown placeholders are rejected.</summary>
public sealed partial class EmailTemplate : AggregateRoot
{
    private EmailTemplate(EmailTemplateKey key, string subject, string htmlBody)
    {
        Key = key;
        Subject = subject;
        HtmlBody = htmlBody;
    }

    public EmailTemplateKey Key { get; }

    public string Subject { get; private set; }

    public string HtmlBody { get; private set; }

    public static EmailTemplate Create(EmailTemplateKey key, string? subject, string? htmlBody, IReadOnlySet<string> knownPlaceholders)
    {
        var template = new EmailTemplate(key, string.Empty, string.Empty);
        template.Update(subject, htmlBody, knownPlaceholders);
        return template;
    }

    public static EmailTemplate Rehydrate(EmailTemplateKey key, string subject, string htmlBody, int version)
    {
        var template = new EmailTemplate(key, subject, htmlBody);
        template.SetPersistedVersion(version);
        return template;
    }

    public static IEnumerable<string> PlaceholdersIn(string text) =>
        Placeholder().Matches(text).Select(m => m.Groups[1].Value);

    public void Update(string? subject, string? htmlBody, IReadOnlySet<string> knownPlaceholders)
    {
        var errors = new ValidationErrors();
        var s = (subject ?? string.Empty).Trim();
        var body = htmlBody ?? string.Empty;
        if (s.Length == 0)
        {
            errors.Add("subject", "Subject is required.");
        }
        else if (s.Length > 200)
        {
            errors.Add("subject", "Subject must be 200 characters or fewer.");
        }

        if (body.Trim().Length == 0)
        {
            errors.Add("htmlBody", "Body is required.");
        }
        else if (body.Length > 50_000)
        {
            errors.Add("htmlBody", "Body must be 50000 characters or fewer.");
        }

        CheckPlaceholders(errors, "subject", s, knownPlaceholders);
        CheckPlaceholders(errors, "htmlBody", body, knownPlaceholders);
        errors.ThrowIfAny();
        Subject = s;
        HtmlBody = body;
    }

    private static void CheckPlaceholders(ValidationErrors errors, string field, string text, IReadOnlySet<string> known)
    {
        var unknown = PlaceholdersIn(text).Where(p => !known.Contains(p)).Distinct().ToList();
        if (unknown.Count > 0)
        {
            errors.Add(field, $"Unknown placeholder{(unknown.Count > 1 ? "s" : string.Empty)}: {string.Join(", ", unknown.Select(p => "{{" + p + "}}"))}.");
        }
    }

    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}")]
    private static partial Regex Placeholder();
}
