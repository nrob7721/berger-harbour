using System.Globalization;
using Google.Cloud.Firestore;

namespace BergerHarbour.Infrastructure.Firestore;

/// <summary>
/// Storage formats: money as integer cents, dates as <c>yyyy-MM-dd</c> strings (they sort for range queries), times as
/// <c>HH:mm</c>, instants as UTC timestamps.
/// </summary>
internal static class FirestoreValues
{
    public static long Cents(decimal amount) => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

    public static long? Cents(decimal? amount) => amount is { } a ? Cents(a) : null;

    public static decimal Money(long cents) => cents / 100m;

    public static decimal? Money(long? cents) => cents is { } c ? Money(c) : null;

    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateOnly Date(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Time(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static TimeOnly Time(string text) => TimeOnly.ParseExact(text, "HH:mm", CultureInfo.InvariantCulture);

    public static Timestamp Instant(DateTimeOffset instant) => Timestamp.FromDateTimeOffset(instant);

    public static Timestamp? Instant(DateTimeOffset? instant) => instant is { } i ? Timestamp.FromDateTimeOffset(i) : null;

    public static DateTimeOffset Instant(Timestamp timestamp) => timestamp.ToDateTimeOffset();

    public static DateTimeOffset? Instant(Timestamp? timestamp) => timestamp?.ToDateTimeOffset();
}
