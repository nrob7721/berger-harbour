using System.Globalization;

namespace BergerHarbour.Application.Common;

/// <summary>Australian display formats used in emails and messages.</summary>
public static class Format
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>$1,234.00</summary>
    public static string Money(decimal amount) => amount < 0
        ? "-$" + (-amount).ToString("N2", Invariant)
        : "$" + amount.ToString("N2", Invariant);

    /// <summary>dd/MM/yyyy</summary>
    public static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", Invariant);

    /// <summary>1:00pm</summary>
    public static string Time(TimeOnly time) =>
        time.ToString("h:mmtt", Invariant).ToLowerInvariant();
}
