using System.Text.RegularExpressions;

namespace BergerHarbour.Domain.Shared;

/// <summary>
/// The one email rule (§7.1), stored normalised (trimmed, lower-case): at most 254 characters, exactly one <c>@</c>, a
/// non-empty local part without spaces, and a domain of two or more labels whose last label is letters only (≥ 2).
/// </summary>
public sealed partial record EmailAddress
{
    public const string Error = "Enter a valid email address.";

    private EmailAddress(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static EmailAddress Create(string? raw, string field = "email", string requiredMessage = "Email is required.")
    {
        var email = (raw ?? string.Empty).Trim();
        if (email.Length == 0)
        {
            throw new DomainValidationException(field, requiredMessage);
        }

        if (!IsValid(email))
        {
            throw new DomainValidationException(field, Error);
        }

        return new EmailAddress(email.ToLowerInvariant());
    }

    /// <summary>An optional email: blank gives null.</summary>
    public static EmailAddress? CreateOptional(string? raw, string field) =>
        string.IsNullOrWhiteSpace(raw) ? null : Create(raw, field);

    public static bool IsValid(string? raw)
    {
        var email = (raw ?? string.Empty).Trim();
        if (email.Length == 0 || email.Length > 254)
        {
            return false;
        }

        var parts = email.Split('@');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[0].Any(char.IsWhiteSpace))
        {
            return false;
        }

        var labels = parts[1].Split('.');
        return labels.Length >= 2
            && labels.All(l => Label().IsMatch(l) && !l.StartsWith('-') && !l.EndsWith('-'))
            && TopLevelDomain().IsMatch(labels[^1]);
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[A-Za-z0-9-]+$")]
    private static partial Regex Label();

    [GeneratedRegex("^[A-Za-z]{2,}$")]
    private static partial Regex TopLevelDomain();
}
