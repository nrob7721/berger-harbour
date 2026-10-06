using System.Text.RegularExpressions;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Customers;

/// <summary>An email address, normalised to lower case and trimmed. Customers are matched on this value.</summary>
public sealed partial record EmailAddress
{
    private EmailAddress(string value) => Value = value;

    public string Value { get; }

    public static EmailAddress Create(string? raw, string field = "email")
    {
        var normalised = Normalise(raw);
        if (normalised.Length == 0)
        {
            throw new DomainValidationException(field, "Email is required.");
        }

        if (normalised.Length > 254 || !Pattern().IsMatch(normalised))
        {
            throw new DomainValidationException(field, "Enter a valid email address.");
        }

        return new EmailAddress(normalised);
    }

    public static bool IsValid(string? raw)
    {
        var normalised = Normalise(raw);
        return normalised.Length is > 0 and <= 254 && Pattern().IsMatch(normalised);
    }

    public static string Normalise(string? raw) => (raw ?? string.Empty).Trim().ToLowerInvariant();

    public override string ToString() => Value;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex Pattern();
}
