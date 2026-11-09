using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BergerHarbour.Domain.Shared;

/// <summary>
/// <c>LASTNAME-YYMMDDX</c>: the cleaned last name, the stay's start date as yyMMdd, and a per-prefix suffix from 0.
/// Assigned once and never changed.
/// </summary>
public sealed partial record BookingReference
{
    private const int MaxNameLength = 20;
    private const string Fallback = "GUEST";

    private BookingReference(string value)
    {
        Value = value;
    }

    public string Value { get; }

    /// <summary>The <c>LASTNAME-YYMMDD</c> prefix the suffix counter is kept for.</summary>
    public static string PrefixFor(string lastName, DateOnly startDate) =>
        $"{NamePart(lastName)}-{startDate.ToString("yyMMdd", CultureInfo.InvariantCulture)}";

    public static BookingReference Create(string prefix, int suffix)
    {
        if (suffix < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(suffix));
        }

        return Parse(prefix + suffix.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Rehydrates a stored reference.</summary>
    public static BookingReference Parse(string value)
    {
        if (!Format().IsMatch(value))
        {
            throw new FormatException($"'{value}' is not a booking reference.");
        }

        return new BookingReference(value);
    }

    /// <summary>Upper-case A–Z only, accents removed, at most 20 characters; <c>GUEST</c> when nothing is left.</summary>
    public static string NamePart(string lastName)
    {
        var decomposed = (lastName ?? string.Empty).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var upper = char.ToUpperInvariant(c);
            if (upper is >= 'A' and <= 'Z')
            {
                builder.Append(upper);
            }
        }

        var name = builder.ToString();
        if (name.Length > MaxNameLength)
        {
            name = name[..MaxNameLength];
        }

        return name.Length == 0 ? Fallback : name;
    }

    public bool Matches(string search) => Value.Equals(search.Trim(), StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Value;

    [GeneratedRegex("^[A-Z]{1,20}-[0-9]{6}[0-9]+$")]
    private static partial Regex Format();
}
