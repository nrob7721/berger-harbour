namespace BergerHarbour.Domain.Shared;

/// <summary>Inclusive nights <c>[FirstNight, LastNight]</c>, used by blocked periods and unavailabilities.</summary>
public readonly record struct NightRange
{
    private NightRange(DateOnly firstNight, DateOnly lastNight)
    {
        FirstNight = firstNight;
        LastNight = lastNight;
    }

    public DateOnly FirstNight { get; }

    public DateOnly LastNight { get; }

    /// <summary>The stay that covers these nights, from FirstNight to the morning after LastNight.</summary>
    public StayRange AsStay => StayRange.Create(FirstNight, LastNight.AddDays(1));

    public static NightRange Create(DateOnly firstNight, DateOnly lastNight, string field = "lastNight")
    {
        if (lastNight < firstNight)
        {
            throw new DomainValidationException(field, "The last night can't be before the first night.");
        }

        return new NightRange(firstNight, lastNight);
    }

    public bool Overlaps(StayRange stay) => stay.Overlaps(this);

    public bool Overlaps(NightRange other) => FirstNight <= other.LastNight && other.FirstNight <= LastNight;
}
