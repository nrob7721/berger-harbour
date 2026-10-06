namespace BergerHarbour.Domain.Shared;

/// <summary>
/// A booking's dates as the half-open range [StartDate, EndDate). StartDate is the check-in day (from 1:00pm),
/// EndDate is the check-out day (by 8:00am). The nights occupied are StartDate … EndDate−1.
/// </summary>
public sealed record Stay
{
    public static readonly TimeOnly CheckInTime = new(13, 0);
    public static readonly TimeOnly CheckOutTime = new(8, 0);

    public Stay(DateOnly startDate, DateOnly endDate)
    {
        if (startDate >= endDate)
        {
            throw new DomainValidationException("endDate", "The end date must be after the start date.");
        }

        StartDate = startDate;
        EndDate = endDate;
    }

    public DateOnly StartDate { get; }

    public DateOnly EndDate { get; }

    public int Nights => EndDate.DayNumber - StartDate.DayNumber;

    public DateOnly LastNight => EndDate.AddDays(-1);

    /// <summary>Booking vs booking: a.Start &lt; b.End &amp;&amp; b.Start &lt; a.End. Same-day changeover does not overlap.</summary>
    public bool Overlaps(Stay other) => StartDate < other.EndDate && other.StartDate < EndDate;

    /// <summary>Booking vs inclusive night range [F, L]: Start &lt;= L &amp;&amp; F &lt; End.</summary>
    public bool Overlaps(NightRange nights) => StartDate <= nights.LastNight && nights.FirstNight < EndDate;

    public override string ToString() => $"{StartDate:yyyy-MM-dd} → {EndDate:yyyy-MM-dd}";
}

/// <summary>An inclusive range of nights [FirstNight, LastNight], used by blocked periods and unavailabilities.</summary>
public sealed record NightRange
{
    public NightRange(DateOnly firstNight, DateOnly lastNight)
    {
        if (firstNight > lastNight)
        {
            throw new DomainValidationException("lastNight", "The last night cannot be before the first night.");
        }

        FirstNight = firstNight;
        LastNight = lastNight;
    }

    public DateOnly FirstNight { get; }

    public DateOnly LastNight { get; }

    public bool Overlaps(NightRange other) => FirstNight <= other.LastNight && other.FirstNight <= LastNight;

    public bool Contains(DateOnly night) => night >= FirstNight && night <= LastNight;

    /// <summary>The same nights expressed as a half-open stay.</summary>
    public Stay ToStay() => new(FirstNight, LastNight.AddDays(1));
}
