using System.Globalization;
using System.Text.RegularExpressions;
using PhoneNumbers;

namespace BergerHarbour.Domain.Shared;

/// <summary>
/// The one email rule for the whole system (§7.1), mirrored by <c>web/shared/src/utils/email.ts</c>: after trimming, at most 254
/// characters; exactly one <c>@</c>; a non-empty local part without spaces; a domain of two or more dot-separated labels of letters,
/// digits and hyphens, none empty or starting or ending with <c>-</c>; a top-level domain of at least 2 letters.
/// </summary>
public static partial class EmailAddress
{
    public const string InvalidMessage = "Enter a valid email address.";

    [GeneratedRegex(@"^[A-Za-z0-9-]+$")]
    private static partial Regex Label();

    [GeneratedRegex(@"^[A-Za-z]{2,}$")]
    private static partial Regex TopLevelDomain();

    /// <summary>Lower-cased and trimmed.</summary>
    public static string Normalise(string? email) => (email ?? "").Trim().ToLowerInvariant();

    public static bool IsValid(string? email)
    {
        var e = (email ?? "").Trim();
        if (e.Length is 0 or > 254) return false;
        var parts = e.Split('@');
        if (parts.Length != 2) return false;
        var (local, domain) = (parts[0], parts[1]);
        if (local.Length == 0 || local.Any(char.IsWhiteSpace)) return false;
        var labels = domain.Split('.');
        if (labels.Length < 2) return false;
        if (labels.Any(l => !Label().IsMatch(l) || l.StartsWith('-') || l.EndsWith('-'))) return false;
        return TopLevelDomain().IsMatch(labels[^1]);
    }

    /// <summary>Validates and normalises, or throws a field error.</summary>
    public static string Require(string? email, string field = "email")
    {
        if (string.IsNullOrWhiteSpace(email)) throw new DomainValidationException(field, "Email is required.");
        if (!IsValid(email)) throw new DomainValidationException(field, InvalidMessage);
        return Normalise(email);
    }
}

/// <summary>Customer phone numbers: mobile numbers only, stored in E.164.</summary>
public static class MobileNumber
{
    public const string InvalidMessage = "Enter a valid mobile number for the selected country.";
    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    public static bool TryNormalise(string? input, out string e164)
    {
        e164 = "";
        if (string.IsNullOrWhiteSpace(input)) return false;
        try
        {
            var number = Util.Parse(input.Trim(), "AU");
            if (!Util.IsValidNumber(number)) return false;
            var type = Util.GetNumberType(number);
            if (type is not (PhoneNumberType.MOBILE or PhoneNumberType.FIXED_LINE_OR_MOBILE)) return false;
            e164 = Util.Format(number, PhoneNumberFormat.E164);
            return true;
        }
        catch (NumberParseException)
        {
            return false;
        }
    }

    public static string Require(string? input, string field)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new DomainValidationException(field, "Mobile is required.");
        return TryNormalise(input, out var e164) ? e164 : throw new DomainValidationException(field, InvalidMessage);
    }

    /// <summary>Null when empty; otherwise a valid mobile or a field error.</summary>
    public static string? Optional(string? input, string field) =>
        string.IsNullOrWhiteSpace(input) ? null : Require(input, field);

    /// <summary>National format for Australian numbers, international for others.</summary>
    public static string Display(string? e164)
    {
        if (string.IsNullOrWhiteSpace(e164)) return "";
        try
        {
            var n = Util.Parse(e164, "AU");
            return Util.Format(n, n.CountryCode == 61 ? PhoneNumberFormat.NATIONAL : PhoneNumberFormat.INTERNATIONAL);
        }
        catch (NumberParseException)
        {
            return e164;
        }
    }
}

public static class Money
{
    public static long ToCents(decimal amount) => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
    public static decimal FromCents(long cents) => cents / 100m;
    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    /// <summary>$1,234.00</summary>
    public static string Format(decimal amount) =>
        (amount < 0 ? "-$" : "$") + Math.Abs(amount).ToString("#,##0.00", CultureInfo.InvariantCulture);
}

public static class Text
{
    public static string Required(string? value, string field, string label, int maxLength = 200)
    {
        var v = (value ?? "").Trim();
        if (v.Length == 0) throw new DomainValidationException(field, $"{label} is required.");
        if (v.Length > maxLength) throw new DomainValidationException(field, $"{label} must be {maxLength} characters or fewer.");
        return v;
    }

    public static string? Optional(string? value, string field, string label, int maxLength = 4000)
    {
        var v = (value ?? "").Trim();
        if (v.Length > maxLength) throw new DomainValidationException(field, $"{label} must be {maxLength} characters or fewer.");
        return v.Length == 0 ? null : v;
    }
}
