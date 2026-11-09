using System.Globalization;

namespace BergerHarbour.Domain.Shared;

public enum PeriodType { Midweek, Weekend, Week, LongWeekend, Custom }

/// <summary>A stay as a half-open range of dates [Start, End): nights Start … End−1.</summary>
public readonly record struct StayRange
{
    public DateOnly Start { get; }
    public DateOnly End { get; }

    public StayRange(DateOnly start, DateOnly end)
    {
        if (start >= end) throw new DomainValidationException("endDate", "The end date must be after the start date.");
        Start = start;
        End = end;
    }

    public int Nights => End.DayNumber - Start.DayNumber;
    public DateOnly LastNight => End.AddDays(-1);

    /// <summary>Booking vs booking: a.Start &lt; b.End &amp;&amp; b.Start &lt; a.End.</summary>
    public bool Overlaps(StayRange other) => Start < other.End && other.Start < End;

    /// <summary>Booking vs inclusive nights [F, L]: Start &lt;= L &amp;&amp; F &lt; End.</summary>
    public bool Overlaps(NightRange nights) => Start <= nights.LastNight && nights.FirstNight < End;

    public bool IncludesNight(DateOnly night) => Start <= night && night < End;

    public IEnumerable<DateOnly> EachNight()
    {
        for (var d = Start; d < End; d = d.AddDays(1)) yield return d;
    }

    public override string ToString() => $"{Dates.Format(Start)} → {Dates.Format(End)}";
}

/// <summary>An inclusive range of nights [FirstNight, LastNight].</summary>
public readonly record struct NightRange
{
    public DateOnly FirstNight { get; }
    public DateOnly LastNight { get; }

    public NightRange(DateOnly firstNight, DateOnly lastNight)
    {
        if (firstNight > lastNight)
            throw new DomainValidationException("lastNight", "The last night can't be before the first night.");
        FirstNight = firstNight;
        LastNight = lastNight;
    }

    public bool Overlaps(NightRange other) => FirstNight <= other.LastNight && other.FirstNight <= LastNight;

    /// <summary>The same nights as a stay [FirstNight, LastNight + 1).</summary>
    public StayRange AsStay() => new(FirstNight, LastNight.AddDays(1));
}

public static class Dates
{
    public static string Format(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    public static string FormatWithWeekday(DateOnly d) => d.ToString("ddd dd/MM/yyyy", CultureInfo.InvariantCulture);
    public static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateOnly ParseIso(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>11am, 11:30am, 8am, 12pm.</summary>
    public static string FormatTime(TimeOnly t)
    {
        var hour12 = t.Hour % 12 == 0 ? 12 : t.Hour % 12;
        var suffix = t.Hour < 12 ? "am" : "pm";
        return t.Minute == 0 ? $"{hour12}{suffix}" : $"{hour12}:{t.Minute:00}{suffix}";
    }

    public static string FormatTimeOfDay(TimeOnly t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);
    public static TimeOnly ParseTime(string s) => TimeOnly.ParseExact(s, "HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>Everything about Australia/Sydney local time.</summary>
public static class SydneyTime
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");

    public static readonly TimeOnly StandardCheckIn = new(11, 0);
    public static readonly TimeOnly StandardCheckInWindowEnd = new(14, 0);
    public static readonly TimeOnly StandardCheckOut = new(8, 0);

    public static DateTimeOffset ToLocal(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Zone);

    public static DateOnly DateOf(DateTimeOffset instant) => DateOnly.FromDateTime(ToLocal(instant).DateTime);

    public static DateOnly Today(DateTimeOffset now) => DateOf(now);

    /// <summary>The UTC instant of a Sydney local date and time.</summary>
    public static DateTimeOffset ToInstant(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (Zone.IsInvalidTime(local)) local = local.AddHours(1); // skipped hour at a DST start
        return new DateTimeOffset(local, Zone.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <summary>The standard check-in instant (11:00) of a stay that starts on <paramref name="startDate"/>.</summary>
    public static DateTimeOffset CheckInInstant(DateOnly startDate) => ToInstant(startDate, StandardCheckIn);

    /// <summary>"AEST/NSW" or "AEDT/NSW", whichever applies on that date.</summary>
    public static string ZoneLabel(DateOnly date) =>
        Zone.IsDaylightSavingTime(date.ToDateTime(new TimeOnly(12, 0))) ? "AEDT/NSW" : "AEST/NSW";

    /// <summary>dd/MM/yyyy h:mma in Sydney time.</summary>
    public static string FormatTimestamp(DateTimeOffset instant)
    {
        var local = ToLocal(instant);
        return $"{Dates.Format(DateOnly.FromDateTime(local.DateTime))} {Dates.FormatTime(TimeOnly.FromDateTime(local.DateTime))}";
    }
}
