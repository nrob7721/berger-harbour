using System.Globalization;
using System.Text;

namespace BergerHarbour.Domain.Bookings;

/// <summary>LASTNAME-YYMMDDX, e.g. SMITH-2701150. The suffix comes from <c>IBookingReferenceGenerator</c>.</summary>
public static class BookingReference
{
    public const int MaxNameLength = 20;
    public const string Fallback = "GUEST";

    /// <summary>Upper-cased, accents removed, everything but A–Z dropped, at most 20 characters; GUEST when nothing is left.</summary>
    public static string NamePart(string? lastName)
    {
        var decomposed = (lastName ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            var u = char.ToUpperInvariant(c);
            if (u is >= 'A' and <= 'Z') sb.Append(u);
            if (sb.Length == MaxNameLength) break;
        }
        return sb.Length == 0 ? Fallback : sb.ToString();
    }

    /// <summary>LASTNAME-YYMMDD from the booking's start date.</summary>
    public static string Prefix(string? lastName, DateOnly startDate) =>
        $"{NamePart(lastName)}-{startDate.ToString("yyMMdd", CultureInfo.InvariantCulture)}";

    public static string Compose(string prefix, int suffix)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(suffix);
        return prefix + suffix.ToString(CultureInfo.InvariantCulture);
    }
}
