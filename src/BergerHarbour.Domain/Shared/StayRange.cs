namespace BergerHarbour.Domain.Shared;

/// <summary>A stay <c>[Start, End)</c>: check-in day to check-out day. The nights occupied are Start … End−1.</summary>
public readonly record struct StayRange
{
    private StayRange(DateOnly start, DateOnly end)
    {
        Start = start;
        End = end;
    }

    public DateOnly Start { get; }

    public DateOnly End { get; }

    public int Nights => End.DayNumber - Start.DayNumber;

    public DateOnly LastNight => End.AddDays(-1);

    public static StayRange Create(DateOnly start, DateOnly end, string field = "endDate")
    {
        if (end <= start)
        {
            throw new DomainValidationException(field, "The end date must be after the start date.");
        }

        return new StayRange(start, end);
    }

    /// <summary>Booking vs booking: <c>a.Start &lt; b.End &amp;&amp; b.Start &lt; a.End</c>. Same-day changeover does not overlap.</summary>
    public bool Overlaps(StayRange other) => Start < other.End && other.Start < End;

    /// <summary>Booking vs inclusive nights <c>[F, L]</c>: <c>Start &lt;= L &amp;&amp; F &lt; End</c>.</summary>
    public bool Overlaps(NightRange nights) => Start <= nights.LastNight && nights.FirstNight < End;

    public bool IncludesNight(DateOnly night) => Start <= night && night < End;

    public IEnumerable<DateOnly> EachNight()
    {
        for (var night = Start; night < End; night = night.AddDays(1))
        {
            yield return night;
        }
    }
}
