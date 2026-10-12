using PhoneNumbers;

namespace BergerHarbour.Domain.Shared;

/// <summary>
/// Phone numbers are validated with libphonenumber and stored in E.164 (e.g. +61412345678). Bookings may come from
/// overseas; a number without a country code is read as Australian.
/// </summary>
public static class PhoneNumber
{
    public const string DefaultRegion = "AU";

    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    /// <summary>The E.164 form of a required number. Throws when it is missing or not valid for its country.</summary>
    public static string Normalise(string? raw, string field, string label) =>
        NormaliseOptional(raw, field, label) ?? throw new DomainValidationException(field, $"{label} is required.");

    /// <summary>The E.164 form of an optional number, or null when it is empty.</summary>
    public static string? NormaliseOptional(string? raw, string field, string label)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return TryParse(raw, out var number)
            ? Util.Format(number, PhoneNumberFormat.E164)
            : throw new DomainValidationException(field, $"Enter a valid {label.ToLowerInvariant()}, including the country code for overseas numbers.");
    }

    public static bool IsValid(string? raw) => TryParse(raw, out _);

    /// <summary>National format for Australian numbers, international format for others; unparsable values as stored.</summary>
    public static string Display(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return string.Empty;
        }

        if (!TryParse(stored, out var number))
        {
            return stored;
        }

        return Util.GetRegionCodeForNumber(number) == DefaultRegion
            ? Util.Format(number, PhoneNumberFormat.NATIONAL)
            : Util.Format(number, PhoneNumberFormat.INTERNATIONAL);
    }

    private static bool TryParse(string? raw, out PhoneNumbers.PhoneNumber number)
    {
        number = null!;
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 30)
        {
            return false;
        }

        try
        {
            number = Util.Parse(value, DefaultRegion);
            return Util.IsValidNumber(number);
        }
        catch (NumberParseException)
        {
            return false;
        }
    }
}
