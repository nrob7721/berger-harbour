using System.Text.RegularExpressions;
using BergerHarbour.Domain.Shared;

namespace BergerHarbour.Domain.Bookings;

/// <summary>Human-readable booking reference BH-YYNNNN: YY = year created, NNNN = per-year sequence.</summary>
public sealed partial record BookingReference
{
    private BookingReference(string value) => Value = value;

    public string Value { get; }

    public static BookingReference Create(int year, long sequence)
    {
        if (sequence < 1)
        {
            throw new DomainValidationException("reference", "The reference sequence starts at 1.");
        }

        return new BookingReference($"BH-{year % 100:00}{sequence:0000}");
    }

    public static BookingReference Parse(string value)
    {
        var normalised = Normalise(value);
        if (!Pattern().IsMatch(normalised))
        {
            throw new DomainValidationException("reference", $"'{value}' is not a booking reference.");
        }

        return new BookingReference(normalised);
    }

    public static bool TryParse(string? value, out BookingReference? reference)
    {
        var normalised = Normalise(value);
        reference = Pattern().IsMatch(normalised) ? new BookingReference(normalised) : null;
        return reference is not null;
    }

    public override string ToString() => Value;

    private static string Normalise(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    [GeneratedRegex(@"^BH-\d{6,}$")]
    private static partial Regex Pattern();
}
